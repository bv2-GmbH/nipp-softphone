using System.Text.Json;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Integrations.Config;

/// <summary>
/// Das Ergebnis eines Testabrufs (§21.4, Schritt „Test Request ausführen").
/// </summary>
/// <param name="Outcome">Wie die Anfrage ausging.</param>
/// <param name="StatusCode">HTTP-Status, soweit einer ankam.</param>
/// <param name="Elapsed">Wie lange es gedauert hat.</param>
/// <param name="Message">Was zu sagen ist, oder <c>null</c>.</param>
/// <param name="ResponsePreview">
/// Die Antwort <b>zum Ansehen</b>: eingerückt und bei
/// <see cref="IntegrationTester.MaxPreviewChars"/> gekürzt. Wer eine
/// 500-kB-Antwort in ein Textfeld eines 400 Pixel breiten Fensters legt, hilft
/// niemandem.
/// </param>
/// <param name="Fields">
/// Was das Mapping daraus gemacht hat — die eigentliche Antwort auf die Frage,
/// die ein Administrator hier stellt: „kommt an, was ich erwartet habe?"
/// </param>
/// <param name="Diagnostics">Was beim Mappen nicht ging.</param>
/// <param name="ResponseBody">
/// Die Antwort <b>zum Weiterverarbeiten</b>: ungekürzt.
///
/// <para><b>Warum es zwei Felder sind</b> (13.09.2026). Es gab nur eines, es
/// hiess <c>RawResponse</c>, und sein Kommentar sagte ausdrücklich „zum Ansehen
/// da, nicht zum Weiterverarbeiten". <b>Zwei Stellen verarbeiteten es
/// trotzdem weiter</b> — der Karten-Designer und die Einstellungsseite —, und
/// bei Antworten über der Kürzungsgrenze kam dort nichts an: abgeschnittenes
/// JSON, der Leser warf, der Fänger schwieg. Die Vorschau zeigte weiter
/// erfundene Beispieldaten, und die Statuszeile meldete Erfolg.
///
/// <para><b>Ein Satz im Kommentar schützt keine Schnittstelle.</b> Der Name
/// muss es tun: was <c>Preview</c> heisst, legt niemand in einen Parser.</para>
/// </param>
public sealed record ConnectionTestResult(
    HttpOutcome Outcome,
    int? StatusCode,
    TimeSpan Elapsed,
    string? Message,
    string? ResponsePreview,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> Diagnostics,
    string? ResponseBody = null)
{
    public bool IsSuccess => Outcome is HttpOutcome.Success or HttpOutcome.NotFound;
}

/// <summary>
/// Führt einen Testabruf gegen eine Quelle aus (§21.4).
///
/// <b>Warum das ein eigener Dienst ist und nicht Teil des ViewModels.</b> Der
/// Ablauf beim Einrichten — Verbindung, Anmeldung, Testabruf, Antwort ansehen,
/// Felder zuordnen — ist der Kern der Verwaltung, und er soll ohne Fenster
/// prüfbar sein. Ein ViewModel, das nebenbei HTTP spricht, ist es nicht.
///
/// <b>Was diesen Test vom Betrieb unterscheidet:</b> hier wird die Rohantwort
/// zurückgegeben. Im Betrieb wäre das falsch — dort verlässt nur das Mapping
/// die Schicht (§21.1). Beim Einrichten ist die Rohantwort die Information,
/// die man braucht, um einen Pfad zu schreiben.
/// </summary>
public sealed class IntegrationTester(IntegrationHttpClient http)
{
    /// <summary>
    /// Wie viel der Antwort gezeigt wird. Genug, um die Struktur zu erkennen;
    /// wenig genug, dass es sich in einem schmalen Fenster lesen lässt.
    /// </summary>
    public const int MaxPreviewChars = 8192;

    private static readonly JsonSerializerOptions PreviewOptions = new() { WriteIndented = true };

    /// <summary>
    /// Prüft die Fähigkeit „Kontext zu einer Rufnummer".
    /// </summary>
    /// <param name="source">Die Quelle, so wie sie eingerichtet ist.</param>
    /// <param name="testNumber">Eine Nummer, zu der es eine Antwort geben sollte.</param>
    /// <param name="countryPrefix">Für die Normalisierung, aus den Einstellungen.</param>
    public Task<ConnectionTestResult> TestLookupAsync(
        DataSourceDefinition source,
        string testNumber,
        string? countryPrefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.LookupByPhone is not { } capability)
        {
            return Task.FromResult(Skipped("Für diese Quelle ist kein Anruferkontext eingerichtet."));
        }

        var number = PhoneNumberKey.From(testNumber, new NumberNormalizer(countryPrefix));

        var ambient = new Dictionary<string, ContextValue>(StringComparer.Ordinal)
        {
            ["number.e164"] = ContextValue.FromText(number.E164),
            ["number.national"] = ContextValue.FromText(number.National),
            ["number.digits"] = ContextValue.FromText(number.Digits),
        };

        return RunAsync(source, capability.Request, capability.ToMapping(), ambient, cancellationToken);
    }

    /// <summary>Prüft die Fähigkeit „Kontakte suchen".</summary>
    public Task<ConnectionTestResult> TestSearchAsync(
        DataSourceDefinition source,
        string testQuery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.SearchContacts is not { } capability)
        {
            return Task.FromResult(Skipped("Für diese Quelle ist keine Kontaktsuche eingerichtet."));
        }

        var ambient = new Dictionary<string, ContextValue>(StringComparer.Ordinal)
        {
            ["query.text"] = ContextValue.FromText(testQuery),
            ["query.limit"] = ContextValue.FromNumber(5),
        };

        return RunAsync(source, capability.Request, capability.ToMapping(), ambient, cancellationToken);
    }

    private async Task<ConnectionTestResult> RunAsync(
        DataSourceDefinition source,
        RequestDefinition request,
        MappingDefinition mapping,
        Dictionary<string, ContextValue> ambient,
        CancellationToken cancellationToken)
    {
        HttpRequestTemplate template;

        try
        {
            template = HttpRequestTemplate.Compile(request);
        }
        catch (Exception ex) when (ex is ExpressionParseException or ArgumentException)
        {
            return Skipped($"Die Anfrage lässt sich nicht bilden: {ex.Message}");
        }

        if (!MappingEngine.TryCompile(mapping, out var compiled, out var mappingErrors))
        {
            return Skipped($"Das Mapping ist unbrauchbar: {string.Join(" | ", mappingErrors)}");
        }

        var scope = new DictionaryScope(ambient);

        var result = await http.SendAsync(
            source,
            template,
            scope,
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        var status = result.StatusCode is { } code ? (int)code : (int?)null;

        if (!result.IsUsable)
        {
            return new ConnectionTestResult(
                result.Outcome,
                status,
                result.Elapsed,
                result.Message,
                ResponsePreview: null,
                NoFields,
                []);
        }

        ambient["status"] = ContextValue.FromNumber(status ?? 0);

        // Bei einer Trefferliste wird der erste Treffer gemappt: er zeigt, ob
        // die Feldpfade stimmen. Alle zu zeigen wäre in einem schmalen Fenster
        // unlesbar und beantwortet dieselbe Frage nicht besser.
        MappingResult mapped;

        if (compiled.ItemsPath is null)
        {
            mapped = MappingEngine.Map(compiled, result.Body, ambient);
        }
        else
        {
            var items = MappingEngine.MapItems(compiled, result.Body, limit: 1, ambient);
            mapped = items.Count > 0 ? items[0] : MappingResult.Empty;
        }

        return new ConnectionTestResult(
            result.Outcome,
            status,
            result.Elapsed,
            Message: null,
            ResponsePreview: Preview(result.Body),
            mapped.Fields.ToDictionary(
                static f => f.Key,
                static f => f.Value.AsText(),
                StringComparer.Ordinal),
            mapped.Diagnostics,
            ResponseBody: Vollstaendig(result.Body));
    }

    /// <summary>
    /// Die Antwort <b>zum Weiterverarbeiten</b>: ungekürzt und unverändert.
    ///
    /// <para><b>Warum es das zusätzlich zu <see cref="Preview"/> gibt</b>
    /// (13.09.2026). Die Vorschau im Karten-Designer zeigte nach einem echten
    /// Abruf weiter die erfundenen Beispieldaten. Die Ursache lag nicht im
    /// Zeichnen: der Designer bekam die <b>Anzeigefassung</b>, also den bei
    /// 8192 Zeichen abgeschnittenen Text mit «… (gekürzt)» am Ende. Das ist
    /// kein gültiges JSON mehr, <c>JsonNode.Parse</c> warf, und der Fänger im
    /// Probenspeicher kehrte still zurück.</para>
    ///
    /// <para><b>Der Kommentar an <see cref="Preview"/> sagte es schon:</b> «Sie
    /// ist zum Ansehen da, nicht zum Weiterverarbeiten». Er stand über einer
    /// Eigenschaft, die trotzdem weiterverarbeitet wurde — ein Satz allein
    /// schützt keine Schnittstelle. Jetzt sind es zwei Felder, und die Frage
    /// stellt sich nicht mehr.</para>
    ///
    /// <para><b>Ohne eigene Grenze:</b> die Grösse ist bereits im
    /// HTTP-Client gedeckelt (§21.2). Eine zweite Schranke hier wäre eine
    /// zweite Wahrheit darüber, wie gross eine Antwort sein darf.</para>
    /// </summary>
    private static string? Vollstaendig(System.Text.Json.Nodes.JsonNode? body) =>
        body?.ToJsonString(PreviewOptions);

    /// <summary>
    /// Die Antwort <b>zum Ansehen</b>: eingerückt und gekürzt. Nicht
    /// weiterverarbeiten — dafür gibt es <see cref="Vollstaendig"/>.
    /// </summary>
    private static string? Preview(System.Text.Json.Nodes.JsonNode? body)
    {
        if (body is null)
        {
            return null;
        }

        var text = body.ToJsonString(PreviewOptions);

        return text.Length <= MaxPreviewChars
            ? text
            : string.Concat(text.AsSpan(0, MaxPreviewChars), "\n… (gekürzt)");
    }

    private static ConnectionTestResult Skipped(string message) =>
        new(HttpOutcome.Skipped, null, TimeSpan.Zero, message, null, NoFields, []);

    private static readonly Dictionary<string, string> NoFields = new(StringComparer.Ordinal);

    /// <summary>Ein Bereich aus festen Werten — für den Testabruf.</summary>
    private sealed class DictionaryScope(IReadOnlyDictionary<string, ContextValue> values)
        : IExpressionScope
    {
        public ContextValue Resolve(string path) =>
            values.TryGetValue(path, out var value) ? value : ContextValue.Null;

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;
    }
}
