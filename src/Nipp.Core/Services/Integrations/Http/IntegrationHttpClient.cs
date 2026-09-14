using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Secrets;

namespace Nipp.Core.Services.Integrations.Http;

/// <summary>
/// Wie eine Anfrage ausgegangen ist.
/// </summary>
public enum HttpOutcome
{
    /// <summary>Antwort da und lesbar.</summary>
    Success,

    /// <summary>Die Quelle sagt ausdrücklich: nichts gefunden (404).</summary>
    NotFound,

    /// <summary>Die Zeitgrenze der Quelle ist abgelaufen.</summary>
    Timeout,

    /// <summary>Netz, Anmeldung, Serverfehler, unlesbare Antwort.</summary>
    Error,

    /// <summary>Die Quelle wurde nicht gefragt — abgebrochen, oder ein Geheimnis fehlt.</summary>
    Skipped,
}

/// <summary>
/// Das Ergebnis einer Anfrage.
/// </summary>
/// <param name="Outcome">Wie es ausging.</param>
/// <param name="StatusCode">HTTP-Status, soweit einer ankam.</param>
/// <param name="Body">Die gelesene Antwort, oder <c>null</c>.</param>
/// <param name="Message">
/// Ein fertiger Satz für die Oberfläche nach §15 — was war und was zu tun ist.
/// Ohne Rufnummer, ohne Adresse, ohne Antwortinhalt.
/// </param>
/// <param name="Elapsed">Wie lange es gedauert hat.</param>
public sealed record HttpCallResult(
    HttpOutcome Outcome,
    HttpStatusCode? StatusCode,
    JsonNode? Body,
    string? Message,
    TimeSpan Elapsed)
{
    public bool IsUsable => Outcome is HttpOutcome.Success or HttpOutcome.NotFound;
}

/// <summary>
/// Der gemeinsame HTTP-Zugang aller Integrationen (§21.1: eine Infrastruktur,
/// nicht zwei).
///
/// Er trägt vier Zusagen, die überall gleich gelten sollen:
/// <list type="bullet">
///   <item><b>Zeitgrenze je Quelle.</b> Sie ist ein Versprechen an den Benutzer, nicht eine Vorsichtsmassnahme.</item>
///   <item><b>Grössengrenze beim Lesen</b>, nicht danach — sonst läge eine riesige Antwort längst im Speicher.</item>
///   <item><b>Anmeldung aus dem SecretStore</b>, nie aus der Konfigurationsdatei.</item>
///   <item><b>Protokoll ohne Rufnummer, Adresse, Kopfzeile und Antwortinhalt</b> (§21.2).</item>
/// </list>
///
/// <b>Ein Client für alle Quellen.</b> Ein <c>HttpClient</c> je Anfrage
/// erschöpft die Sockets; einer je Quelle wäre Verwaltung ohne Nutzen. Die
/// Zeitgrenze steht deshalb nicht am Client, sondern an jeder Anfrage — sie
/// ist je Quelle verschieden.
/// </summary>
public sealed class IntegrationHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly IntegrationSecrets _secrets;
    private readonly ILogger<IntegrationHttpClient> _logger;

    /// <summary>
    /// Der Schutzschalter je Quelle (§21.2). Ein Server, der weg ist, bleibt
    /// es meist eine Weile — ihn bei jedem Anruf erneut zu fragen kostet je
    /// Anruf die volle Zeitgrenze.
    /// </summary>
    private readonly IntegrationHealth _health;

    private bool _disposed;

    public IntegrationHttpClient(
        IntegrationSecrets secrets,
        ILogger<IntegrationHttpClient> logger)
        : this(secrets, logger, handler: null)
    {
    }

    /// <param name="handler">
    /// Der Nachrichtenkanal. <c>null</c> heisst: der übliche.
    ///
    /// <b>Der Parameter existiert für die Tests</b> — dieselbe Begründung wie
    /// beim Pfad in <c>SettingsService</c>. Ohne ihn liessen sich Zeitgrenze,
    /// Grössengrenze und Statusbehandlung nur gegen einen echten Server
    /// prüfen, und dann prüfte man den Server.
    /// </param>
    public IntegrationHttpClient(
        IntegrationSecrets secrets,
        ILogger<IntegrationHttpClient> logger,
        HttpMessageHandler? handler,
        TimeProvider? time = null)
    {
        _secrets = secrets;
        _logger = logger;
        _health = new IntegrationHealth(time);

        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                // Verbindungen nicht ewig halten: ein DNS-Wechsel beim Kunden
                // soll ankommen, ohne dass nipp neu gestartet wird.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,

                // Strenger als ein Browser, und zwar bewusst: .NET entfernt beim
                // Folgen einer Weiterleitung nur die Authorization-Kopfzeile.
                // Ein API-Key in einer eigenen Kopfzeile (in: header) und alles
                // aus connection.Headers gingen mit — auch an einen fremden
                // Host, und ein 302 vom Server oder einem Proxy hätte gereicht.
                // Eine Umleitung ist hier ohnehin ein Konfigurationsfehler:
                // die Basisadresse gehört richtig eingetragen.
                AllowAutoRedirect = false,
            })
            : new HttpClient(handler);

        // Keine Zeitgrenze am Client: sie steht je Anfrage, weil sie je Quelle
        // verschieden ist. Ohne diese Zeile gälte zusätzlich die Vorgabe von
        // 100 Sekunden, und ein Abbruch wäre nicht mehr zuzuordnen.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("nipp/1.0");
    }

    /// <summary>
    /// Führt eine Anfrage aus. <b>Wirft nicht</b> — jeder Fehler wird zu einem
    /// <see cref="HttpCallResult"/> mit Meldung.
    /// </summary>
    /// <param name="source">Die Quelle, für Zeitgrenze, Anmeldung und Meldungen.</param>
    /// <param name="template">Die übersetzte Anfrage.</param>
    /// <param name="scope">Woher die Werte der Vorlagen kommen.</param>
    /// <param name="timeout">Zeitgrenze; ohne Angabe die der Verbindung.</param>
    /// <param name="cancellationToken">Abbruch von aussen — etwa weil das Gespräch endete.</param>
    public async Task<HttpCallResult> SendAsync(
        DataSourceDefinition source,
        HttpRequestTemplate template,
        IExpressionScope scope,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);

        var connection = source.Http;

        if (connection is null)
        {
            return Skipped(source, "Für diese Quelle ist keine Verbindung eingerichtet.");
        }

        if (_secrets.FindMissing(connection.Auth.SecretRefs) is { Count: > 0 })
        {
            // Mit leerem Schlüssel zu fragen ergäbe 401 und sähe für den
            // Benutzer aus wie ein kaputter Server. §15: sagen, was fehlt und
            // was zu tun ist.
            return Skipped(
                source,
                $"Die Zugangsdaten für {source.DisplayName} fehlen. Sie werden in den "
                    + "Einstellungen unter «Integrationen» eingetragen.");
        }

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            return Skipped(source, $"Die Adresse von {source.DisplayName} ist unbrauchbar. Sie steht in den "
            + "Einstellungen unter «Integrationen», bei «Basisadresse».");
        }

        // Die https-Pflicht steht auch im Validator (§21.4) — hier steht sie ein
        // zweites Mal, mit Absicht. Der Validator wirkt nur über
        // IntegrationConfigStore.UsableSources; der Testabruf aus den
        // Einstellungen nimmt dagegen jede eingetragene Quelle, auch eine
        // fehlerhafte, und schickte dabei Bearer-Token oder API-Key im
        // Klartext über http. Ein Geheimnis darf nicht davon abhängen, welcher
        // Aufrufer die Prüfung vorher gemacht hat.
        if (baseUrl.Scheme == Uri.UriSchemeHttp && !connection.AllowInsecureHttp)
        {
            return Skipped(
                source,
                $"{source.DisplayName} ist über http eingetragen. Zugangsdaten würden dabei "
                    + "unverschlüsselt übertragen. Adresse auf https umstellen — oder, wenn das "
                    + "Netz das wirklich trägt, in der Konfiguration 'allowInsecureHttp' setzen.");
        }

        if (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp)
        {
            return Skipped(
                source,
                $"Die Adresse von {source.DisplayName} nutzt das Schema '{baseUrl.Scheme}'. "
                    + "Erwartet wird https.");
        }

        if (!_health.IsAvailable(source.Id))
        {
            // Die Quelle hat mehrfach nicht geantwortet oder ausdruecklich um
            // eine Pause gebeten. Sie jetzt zu fragen kostet die volle
            // Zeitgrenze fuer ein absehbares Ergebnis.
            var rest = _health.RemainingBreak(source.Id);

            return Skipped(
                source,
                rest is { } wait
                    ? $"{source.DisplayName} wird gerade nicht gefragt — naechster Versuch in "
                        + $"{wait.TotalSeconds:F0} s."
                    : $"{source.DisplayName} wird gerade nicht gefragt.");
        }

        var limit = TimeSpan.FromMilliseconds(
            Math.Max(1, timeout?.TotalMilliseconds ?? connection.TimeoutMs));

        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        // Eigene Zeitgrenze je Anfrage, verbunden mit dem Abbruch von aussen.
        // Nur so lässt sich unterscheiden, ob die Quelle zu langsam war oder
        // das Gespräch vorbei ist — und das ist der Unterschied zwischen einer
        // Meldung und Schweigen.
        using var timeoutSource = new CancellationTokenSource(limit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            using var request = template.Build(baseUrl, scope);

            Authenticate(request, connection);

            foreach (var (name, value) in connection.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            // Weiterleitungen werden nicht verfolgt (siehe AllowAutoRedirect im
            // Konstruktor). Ohne diesen Zweig käme eine 302-Antwort als leerer
            // Rumpf beim Mapping an und ergäbe „keine Daten gefunden" — die
            // eigentliche Ursache stünde nirgends.
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                _health.ReportFailure(source.Id);
                ConnectorLog.SourceFailed(_logger, source.Id, "Redirect");

                return new HttpCallResult(
                    HttpOutcome.Error,
                    response.StatusCode,
                    null,
                    $"{source.DisplayName} leitet um. nipp folgt dem nicht, weil die Zugangsdaten "
                        + "sonst an das Ziel der Umleitung gingen. Die Basisadresse so eintragen, "
                        + "wie der Server sie am Ende erwartet.",
                    Elapsed(started));
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _health.ReportRateLimited(source.Id, IntegrationHealth.RetryAfterOf(response));
            }
            else if ((int)response.StatusCode >= 500)
            {
                _health.ReportFailure(source.Id);
            }
            else
            {
                // Auch ein 401 ist eine Antwort: der Server steht, die
                // Zugangsdaten stimmen nicht. Ihn dafuer zu pausieren wuerde
                // die Ursache verdecken.
                _health.ReportSuccess(source.Id);
            }

            return await ReadResponseAsync(source, connection, response, started, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Von aussen abgebrochen: das Gespräch ist vorbei oder die Suche
            // überholt. Keine Meldung — es gibt nichts zu berichten.
            return new HttpCallResult(HttpOutcome.Skipped, null, null, null, Elapsed(started));
        }
        catch (OperationCanceledException)
        {
            _health.ReportFailure(source.Id);
            ConnectorLog.SourceTimedOut(_logger, source.Id, (int)limit.TotalMilliseconds);

            return new HttpCallResult(
                HttpOutcome.Timeout,
                null,
                null,
                $"{source.DisplayName} antwortet nicht (mehr als {limit.TotalSeconds:0.#} s).",
                Elapsed(started));
        }
        catch (HttpRequestException ex)
        {
            _health.ReportFailure(source.Id);
            ConnectorLog.SourceFailed(_logger, source.Id, ex.GetType().Name);

            return new HttpCallResult(
                HttpOutcome.Error,
                ex.StatusCode,
                null,
                $"{source.DisplayName} ist nicht erreichbar. Netzwerk und Adresse prüfen.",
                Elapsed(started));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException or ArgumentException)
        {
            // Eine Anfrage, die sich nicht bauen lässt — etwa weil eine Vorlage
            // eine unbrauchbare Adresse ergibt. Konfigurationsfehler, kein
            // Grund, den Anruf zu stören.
            ConnectorLog.SourceFailed(_logger, source.Id, ex.GetType().Name);

            return new HttpCallResult(
                HttpOutcome.Error,
                null,
                null,
                $"Die Anfrage an {source.DisplayName} liess sich nicht bilden. Die Einstellungen "
                    + "dieser Quelle prüfen.",
                Elapsed(started));
        }
    }

    /// <summary>
    /// Liest die Antwort — mit Grössengrenze <b>während</b> des Lesens.
    /// </summary>
    private async Task<HttpCallResult> ReadResponseAsync(
        DataSourceDefinition source,
        HttpConnection connection,
        HttpResponseMessage response,
        long started,
        CancellationToken cancellationToken)
    {
        var status = response.StatusCode;

        if (status == HttpStatusCode.NotFound)
        {
            // Kein Fehler, sondern eine Antwort: zu dieser Nummer ist dort
            // nichts bekannt.
            ConnectorLog.SourceAnswered(_logger, source.Id, (int)status, (int)Elapsed(started).TotalMilliseconds);

            return new HttpCallResult(HttpOutcome.NotFound, status, null, null, Elapsed(started));
        }

        if (!response.IsSuccessStatusCode)
        {
            ConnectorLog.SourceRejected(_logger, source.Id, (int)status);

            return new HttpCallResult(
                HttpOutcome.Error,
                status,
                null,
                DescribeStatus(source.DisplayName, status),
                Elapsed(started));
        }

        var limit = Math.Max(1024, connection.MaxResponseBytes);

        // Schon die angekündigte Länge prüfen: eine zu grosse Antwort muss
        // nicht erst gelesen werden.
        if (response.Content.Headers.ContentLength is { } announced && announced > limit)
        {
            ConnectorLog.ResponseTooLarge(_logger, source.Id, limit);

            return new HttpCallResult(
                HttpOutcome.Error,
                status,
                null,
                $"Die Antwort von {source.DisplayName} ist grösser als {limit / 1024} kB "
                    + "und wurde verworfen.",
                Elapsed(started));
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var text = await ReadLimitedAsync(stream, limit, cancellationToken).ConfigureAwait(false);

            if (text is null)
            {
                ConnectorLog.ResponseTooLarge(_logger, source.Id, limit);

                return new HttpCallResult(
                    HttpOutcome.Error,
                    status,
                    null,
                    $"Die Antwort von {source.DisplayName} ist grösser als {limit / 1024} kB "
                        + "und wurde verworfen.",
                    Elapsed(started));
            }

            var body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);

            ConnectorLog.SourceAnswered(
                _logger,
                source.Id,
                (int)status,
                (int)Elapsed(started).TotalMilliseconds);

            return new HttpCallResult(HttpOutcome.Success, status, body, null, Elapsed(started));
        }
        catch (JsonException)
        {
            // Der häufigste Fall dahinter: eine Anmeldeseite oder eine
            // Fehlerseite in HTML, mit Status 200.
            ConnectorLog.ResponseNotJson(_logger, source.Id);

            return new HttpCallResult(
                HttpOutcome.Error,
                status,
                null,
                $"{source.DisplayName} hat kein JSON geliefert. Adresse und Pfad der Quelle prüfen.",
                Elapsed(started));
        }
        catch (IOException)
        {
            return new HttpCallResult(
                HttpOutcome.Error,
                status,
                null,
                $"Die Antwort von {source.DisplayName} liess sich nicht vollständig lesen. "
            + "Später erneut versuchen; bleibt es dabei, die Quelle mit dem Testabruf prüfen.",
                Elapsed(started));
        }
    }

    /// <summary>
    /// Liest höchstens <paramref name="limit"/> Bytes und bricht darüber ab.
    /// <c>null</c> heisst: die Grenze wurde überschritten.
    ///
    /// <b>Warum nicht <c>ReadAsStringAsync</c> und dann die Länge prüfen:</b>
    /// dann wäre die Antwort schon vollständig im Speicher. Bei einer
    /// versehentlich falschen Adresse — etwa auf einen Datei-Download — ist
    /// genau das der Schaden, der verhindert werden soll.
    /// </summary>
    private static async Task<string?> ReadLimitedAsync(
        Stream stream,
        int limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var target = new MemoryStream();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            if (target.Length + read > limit)
            {
                return null;
            }

            // WriteAsync auf einem MemoryStream schliesst sofort ab und kostet
            // nichts; der Analyzer verlangt es, und ein Sonderfall im Code
            // wäre den Streit nicht wert.
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(target.ToArray());
    }

    /// <summary>
    /// Setzt die Anmeldung ein. Die Werte kommen aus dem
    /// <see cref="IntegrationSecrets"/> und werden nirgends protokolliert.
    /// </summary>
    private void Authenticate(HttpRequestMessage request, HttpConnection connection)
    {
        var auth = connection.Auth;

        switch (auth.Type)
        {
            case AuthKind.ApiKey when auth.Name is { Length: > 0 } name:
                {
                    var key = _secrets.Get(auth.SecretRef);

                    if (key is null)
                    {
                        return;
                    }

                    if (auth.In == ApiKeyLocation.Header)
                    {
                        request.Headers.TryAddWithoutValidation(name, key);
                    }
                    else
                    {
                        var separator = request.RequestUri!.Query.Length > 0 ? '&' : '?';

                        request.RequestUri = new Uri(
                            $"{request.RequestUri}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(key)}");
                    }

                    break;
                }

            case AuthKind.Bearer:
                {
                    if (_secrets.Get(auth.SecretRef) is { Length: > 0 } token)
                    {
                        var scheme = string.IsNullOrWhiteSpace(auth.Scheme)
                            ? "Bearer"
                            : auth.Scheme.Trim();

                        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
                    }

                    break;
                }

            case AuthKind.Basic:
                {
                    var user = _secrets.Get(auth.UsernameSecretRef);
                    var password = _secrets.Get(auth.PasswordSecretRef);

                    if (user is { Length: > 0 })
                    {
                        var pair = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes($"{user}:{password ?? string.Empty}"));

                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", pair);
                    }

                    break;
                }

            case AuthKind.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Was ein Fehlerstatus für den Benutzer bedeutet — §15: Ursache und
    /// Abhilfe, nicht die Zahl allein.
    /// </summary>
    private static string DescribeStatus(string source, HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"{source} lehnt die Anmeldung ab ({(int)status}). Die Zugangsdaten in den "
                + "Einstellungen unter «Integrationen» prüfen.",

        HttpStatusCode.TooManyRequests =>
            $"{source} begrenzt die Anzahl Anfragen. Es wird später erneut versucht.",

        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
            $"{source} versteht die Anfrage nicht ({(int)status}). Pfad und Parameter der "
                + "Quelle prüfen.",

        >= HttpStatusCode.InternalServerError =>
            $"{source} meldet einen Serverfehler ({(int)status}).",

        _ => $"{source} antwortet mit {(int)status}.",
    };

    private HttpCallResult Skipped(DataSourceDefinition source, string message)
    {
        ConnectorLog.SourceSkipped(_logger, source.Id);

        return new HttpCallResult(HttpOutcome.Skipped, null, null, message, TimeSpan.Zero);
    }

    private static TimeSpan Elapsed(long started) =>
        System.Diagnostics.Stopwatch.GetElapsedTime(started);

    /// <summary>
    /// Vergisst, welche Quelle gerade pausiert ist — nach einer Änderung an der
    /// Konfiguration.
    ///
    /// <para>Ohne das galt eine Pause weiter, die zu einer Adresse gehörte, die
    /// es nicht mehr gibt: die Korrektur wirkte scheinbar nicht, und die
    /// Meldung nannte nur eine Wartezeit ohne Zusammenhang.</para>
    /// </summary>
    public void ResetHealth() => _health.Clear();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
