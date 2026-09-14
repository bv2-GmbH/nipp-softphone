using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Http;

/// <summary>
/// Der gemeinsame HTTP-Zugang der Integrationen (§21.2).
///
/// <b>Geprüft wird gegen einen eigenen Nachrichtenkanal, nicht gegen einen
/// Server.</b> Nur so lassen sich Zeitgrenze, Grössengrenze und
/// Statusbehandlung überhaupt gezielt auslösen — mit einem echten Server
/// prüfte man den Server.
///
/// Der wichtigste Test in dieser Datei ist der letzte: er liest mit, was ins
/// Protokoll geschrieben wird, und schlägt fehl, sobald dort eine Rufnummer,
/// eine Adresse oder ein Schlüssel auftaucht.
/// </summary>
public sealed class IntegrationHttpClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>Ein Kanal, der antwortet, was der Test vorgibt.</summary>
    private sealed class Kanal(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> antwort)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Letzte { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Letzte = request;
            return await antwort(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Kanal Antwortet(HttpStatusCode status, string body = "{}") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    /// <summary>Ein Kanal, der nie antwortet — bis abgebrochen wird.</summary>
    private static Kanal AntwortetNie() =>
        new(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

    private IntegrationSecrets Geheimnisse() =>
        new(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

    private IntegrationHttpClient Client(
        Kanal kanal,
        IntegrationSecrets? geheimnisse = null,
        ILogger<IntegrationHttpClient>? protokoll = null) =>
        new(
            geheimnisse ?? Geheimnisse(),
            protokoll ?? NullLogger<IntegrationHttpClient>.Instance,
            kanal);

    private static DataSourceDefinition Quelle(
        AuthDefinition? auth = null,
        int timeoutMs = 1500,
        int maxBytes = 1024 * 1024,
        string baseUrl = "https://crm.example.ch/api/v2") =>
        new()
        {
            Id = "crm",
            DisplayName = "Muster-CRM",
            Http = new HttpConnection
            {
                BaseUrl = baseUrl,
                TimeoutMs = timeoutMs,
                MaxResponseBytes = maxBytes,
                Auth = auth ?? new AuthDefinition(),
            },
        };

    private static HttpRequestTemplate Anfrage(string path = "/contacts", string? queryValue = null) =>
        HttpRequestTemplate.Compile(new RequestDefinition
        {
            Method = "GET",
            Path = path,
            Query = queryValue is null
                ? []
                : new Dictionary<string, string> { ["phone"] = queryValue },
        });

    /// <summary>Ein Bereich mit der Rufnummer, wie ihn der Anruferkontext mitgibt.</summary>
    private sealed class Bereich : IExpressionScope
    {
        public ContextValue Resolve(string path) => path switch
        {
            "number.e164" => ContextValue.FromText("+41791234567"),
            "number.national" => ContextValue.FromText("0791234567"),
            _ => ContextValue.Null,
        };

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;
    }

    // --- Der Normalfall ---

    [Fact]
    public async Task Eine_erfolgreiche_Antwort_wird_gelesen()
    {
        using var client = Client(Antwortet(HttpStatusCode.OK, """{ "name": "Muster AG" }"""));

        var ergebnis = await client.SendAsync(Quelle(), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Success, ergebnis.Outcome);
        Assert.Equal("Muster AG", ergebnis.Body!["name"]!.GetValue<string>());
    }

    /// <summary>
    /// Die Basisadresse trägt einen Pfad (<c>/api/v2</c>). Ein Anfragepfad mit
    /// führendem Schrägstrich würde ihn bei naiver Zusammensetzung verwerfen —
    /// aus <c>/api/v2</c> plus <c>/contacts</c> würde <c>/contacts</c>.
    /// </summary>
    [Fact]
    public async Task Der_Pfad_der_Basisadresse_bleibt_erhalten()
    {
        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal);

        await client.SendAsync(Quelle(), Anfrage("/contacts"), new Bereich());

        Assert.Equal(
            "https://crm.example.ch/api/v2/contacts",
            kanal.Letzte!.RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// Ohne Kodierung würde aus <c>+41791234567</c> im Parameter die Zahl
    /// <c>41791234567</c> — das <c>+</c> steht dort für ein Leerzeichen. Die
    /// Gegenstelle suchte dann nach einer anderen Nummer und fände nichts.
    /// </summary>
    [Fact]
    public async Task Die_Rufnummer_wird_im_Parameter_kodiert()
    {
        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal);

        await client.SendAsync(Quelle(), Anfrage(queryValue: "{{number.e164}}"), new Bereich());

        Assert.Contains("phone=%2B41791234567", kanal.Letzte!.RequestUri!.Query, StringComparison.Ordinal);
    }

    // --- Anmeldung ---

    [Fact]
    public async Task Ein_Api_Schluessel_geht_als_Kopfzeile_hinaus()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm.apiKey", "geheim-123");

        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal, geheimnisse);

        var quelle = Quelle(new AuthDefinition
        {
            Type = AuthKind.ApiKey,
            In = ApiKeyLocation.Header,
            Name = "X-Api-Key",
            SecretRef = "crm.apiKey",
        });

        await client.SendAsync(quelle, Anfrage(), new Bereich());

        Assert.Equal("geheim-123", kanal.Letzte!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task Ein_Bearer_Token_wird_gesetzt()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm.token", "abc");

        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal, geheimnisse);

        var quelle = Quelle(new AuthDefinition { Type = AuthKind.Bearer, SecretRef = "crm.token" });

        await client.SendAsync(quelle, Anfrage(), new Bereich());

        Assert.Equal("Bearer", kanal.Letzte!.Headers.Authorization!.Scheme);
        Assert.Equal("abc", kanal.Letzte.Headers.Authorization.Parameter);
    }

    /// <summary>
    /// Das Django REST Framework verlangt <c>Authorization: Token …</c>.
    /// das CRM ist so eine API — und antwortet auf eine Anfrage ohne
    /// Anmeldung irreführend mit <c>WWW-Authenticate: Bearer</c>.
    /// </summary>
    [Fact]
    public async Task Ein_eigenes_Schema_ersetzt_Bearer()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm", "abc");

        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal, geheimnisse);

        var quelle = Quelle(new AuthDefinition
        {
            Type = AuthKind.Bearer,
            SecretRef = "crm",
            Scheme = "Token",
        });

        await client.SendAsync(quelle, Anfrage(), new Bereich());

        Assert.Equal("Token", kanal.Letzte!.Headers.Authorization!.Scheme);
        Assert.Equal("abc", kanal.Letzte.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Ohne_Schema_bleibt_es_bei_Bearer()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm.token", "abc");

        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal, geheimnisse);

        var quelle = Quelle(new AuthDefinition
        {
            Type = AuthKind.Bearer,
            SecretRef = "crm.token",
            Scheme = "   ",
        });

        await client.SendAsync(quelle, Anfrage(), new Bereich());

        Assert.Equal("Bearer", kanal.Letzte!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Basic_Auth_wird_richtig_kodiert()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("erp.user", "nipp");
        geheimnisse.Set("erp.password", "pw");

        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal, geheimnisse);

        var quelle = Quelle(new AuthDefinition
        {
            Type = AuthKind.Basic,
            UsernameSecretRef = "erp.user",
            PasswordSecretRef = "erp.password",
        });

        await client.SendAsync(quelle, Anfrage(), new Bereich());

        var erwartet = Convert.ToBase64String(Encoding.UTF8.GetBytes("nipp:pw"));

        Assert.Equal(erwartet, kanal.Letzte!.Headers.Authorization!.Parameter);
    }

    /// <summary>
    /// Mit leerem Schlüssel zu fragen ergäbe 401 und sähe für den Benutzer aus
    /// wie ein kaputter Server. §15: sagen, was fehlt und was zu tun ist.
    /// </summary>
    [Fact]
    public async Task Ohne_hinterlegtes_Geheimnis_wird_gar_nicht_erst_gefragt()
    {
        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal);

        var quelle = Quelle(new AuthDefinition
        {
            Type = AuthKind.Bearer,
            SecretRef = "crm.fehlt",
        });

        var ergebnis = await client.SendAsync(quelle, Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Skipped, ergebnis.Outcome);
        Assert.Null(kanal.Letzte);
        Assert.Contains("Einstellungen", ergebnis.Message!, StringComparison.Ordinal);
    }

    // --- Grenzen ---

    [Fact]
    public async Task Eine_zu_langsame_Quelle_laeuft_in_ihre_Zeitgrenze()
    {
        using var client = Client(AntwortetNie());

        var ergebnis = await client.SendAsync(Quelle(timeoutMs: 200), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Timeout, ergebnis.Outcome);
        Assert.Contains("antwortet nicht", ergebnis.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein Abbruch von aussen — das Gespräch ist vorbei — ist kein Fehler und
    /// bekommt keine Meldung. Es gibt nichts zu berichten.
    /// </summary>
    [Fact]
    public async Task Ein_Abbruch_von_aussen_meldet_nichts()
    {
        using var client = Client(AntwortetNie());
        using var abbruch = new CancellationTokenSource();

        var aufruf = client.SendAsync(
            Quelle(timeoutMs: 5000),
            Anfrage(),
            new Bereich(),
            timeout: null,
            cancellationToken: abbruch.Token);

        await abbruch.CancelAsync();

        var ergebnis = await aufruf;

        Assert.Equal(HttpOutcome.Skipped, ergebnis.Outcome);
        Assert.Null(ergebnis.Message);
    }

    /// <summary>
    /// Die Grenze greift <b>beim Lesen</b>. Würde erst die fertige Antwort
    /// geprüft, läge sie schon vollständig im Speicher — bei einer
    /// versehentlich auf einen Download gerichteten Adresse genau der Schaden,
    /// der verhindert werden soll.
    /// </summary>
    [Fact]
    public async Task Eine_zu_grosse_Antwort_wird_verworfen()
    {
        var gross = "{\"x\":\"" + new string('a', 20_000) + "\"}";

        using var client = Client(Antwortet(HttpStatusCode.OK, gross));

        var ergebnis = await client.SendAsync(Quelle(maxBytes: 8192), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Error, ergebnis.Outcome);
        Assert.Contains("grösser als", ergebnis.Message!, StringComparison.Ordinal);
    }

    // --- Statusbehandlung ---

    [Fact]
    public async Task Ein_404_heisst_nichts_gefunden_und_ist_kein_Fehler()
    {
        using var client = Client(Antwortet(HttpStatusCode.NotFound));

        var ergebnis = await client.SendAsync(Quelle(), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.NotFound, ergebnis.Outcome);
        Assert.Null(ergebnis.Message);
        Assert.True(ergebnis.IsUsable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Anmeldung")]
    [InlineData(HttpStatusCode.Forbidden, "Anmeldung")]
    [InlineData(HttpStatusCode.TooManyRequests, "begrenzt")]
    [InlineData(HttpStatusCode.InternalServerError, "Serverfehler")]
    [InlineData(HttpStatusCode.BadRequest, "versteht die Anfrage nicht")]
    public async Task Ein_Fehlerstatus_wird_in_einen_verstaendlichen_Satz_uebersetzt(
        HttpStatusCode status,
        string erwartet)
    {
        using var client = Client(Antwortet(status));

        var ergebnis = await client.SendAsync(Quelle(), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Error, ergebnis.Outcome);
        Assert.Contains(erwartet, ergebnis.Message!, StringComparison.Ordinal);
        Assert.Contains("Muster-CRM", ergebnis.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Der häufigste Fall dahinter: eine Anmeldeseite in HTML, mit Status 200.
    /// </summary>
    [Fact]
    public async Task Eine_Antwort_die_kein_Json_ist_wird_erklaert()
    {
        var kanal = new Kanal((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Bitte anmelden</html>", Encoding.UTF8, "text/html"),
        }));

        using var client = Client(kanal);

        var ergebnis = await client.SendAsync(Quelle(), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Error, ergebnis.Outcome);
        Assert.Contains("kein JSON", ergebnis.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Netzfehler_nimmt_den_Anruf_nicht_mit()
    {
        var kanal = new Kanal((_, _) => throw new HttpRequestException("DNS"));

        using var client = Client(kanal);

        var ergebnis = await client.SendAsync(Quelle(), Anfrage(), new Bereich());

        Assert.Equal(HttpOutcome.Error, ergebnis.Outcome);
        Assert.Contains("nicht erreichbar", ergebnis.Message!, StringComparison.Ordinal);
    }

    // --- Datenschutz im Protokoll ---

    /// <summary>
    /// <b>Der wichtigste Test dieser Datei</b> (§21.2). Ein Softphone, das
    /// Rufnummern und die Namen dahinter protokolliert, legt ein
    /// Bewegungsprofil an. Geprüft wird der ganze Ablauf einer Anfrage:
    /// Erfolg, Zeitüberschreitung, Fehlerstatus.
    /// </summary>
    [Fact]
    public async Task Im_Protokoll_stehen_weder_Rufnummer_noch_Schluessel_noch_Adresse()
    {
        var mitschrift = new Mitschrift();
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm.apiKey", "geheim-123");

        var quelle = Quelle(new AuthDefinition
        {
            Type = AuthKind.ApiKey,
            In = ApiKeyLocation.Header,
            Name = "X-Api-Key",
            SecretRef = "crm.apiKey",
        });

        var anfrage = Anfrage("/contacts/{{number.e164}}", "{{number.national}}");

        using (var client = Client(Antwortet(HttpStatusCode.OK), geheimnisse, mitschrift))
        {
            await client.SendAsync(quelle, anfrage, new Bereich());
        }

        using (var client = Client(AntwortetNie(), geheimnisse, mitschrift))
        {
            await client.SendAsync(quelle with { Http = quelle.Http! with { TimeoutMs = 200 } }, anfrage, new Bereich());
        }

        using (var client = Client(Antwortet(HttpStatusCode.Unauthorized), geheimnisse, mitschrift))
        {
            await client.SendAsync(quelle, anfrage, new Bereich());
        }

        var alles = string.Join(Environment.NewLine, mitschrift.Zeilen);

        Assert.NotEmpty(mitschrift.Zeilen);
        Assert.DoesNotContain("geheim-123", alles, StringComparison.Ordinal);
        Assert.DoesNotContain("41791234567", alles, StringComparison.Ordinal);
        Assert.DoesNotContain("0791234567", alles, StringComparison.Ordinal);
        Assert.DoesNotContain("crm.example.ch", alles, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Api-Key", alles, StringComparison.Ordinal);

        // Und keine Ziffernfolge, die als Rufnummer durchgehen könnte.
        Assert.DoesNotMatch(new Regex(@"\d{7,}"), alles);
    }

    /// <summary>Ein Protokoll, das alles mitschreibt, was hineingegeben wird.</summary>
    private sealed class Mitschrift : ILogger<IntegrationHttpClient>
    {
        public List<string> Zeilen { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Zeilen.Add(formatter(state, exception));

            if (exception is not null)
            {
                Zeilen.Add(exception.ToString());
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
