using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Fragt ein fremdes System nach einer Rufnummer (§21.1).
///
/// Setzt die Teile zusammen, die es schon gibt: die Anfragevorlage baut die
/// Adresse, der HTTP-Zugang holt die Antwort mit Zeit- und Grössengrenze, und
/// das Mapping macht daraus Felder im eigenen Namensraum. Diese Klasse selbst
/// entscheidet nur, <b>ob</b> gefragt wird und <b>wie</b> das Ergebnis zu
/// deuten ist.
/// </summary>
public sealed class HttpCallerContextProvider : ICallerContextProvider
{
    private readonly DataSourceDefinition _source;
    private readonly HttpRequestTemplate _request;
    private readonly CompiledMapping _mapping;
    private readonly IntegrationHttpClient _http;
    private readonly bool _allowInternal;

    private HttpCallerContextProvider(
        DataSourceDefinition source,
        HttpRequestTemplate request,
        CompiledMapping mapping,
        IntegrationHttpClient http,
        bool allowInternal,
        TimeSpan timeout)
    {
        _source = source;
        _request = request;
        _mapping = mapping;
        _http = http;
        _allowInternal = allowInternal;
        Timeout = timeout;
    }

    public string SourceId => _source.Id;

    public string DisplayName => _source.DisplayName;

    /// <summary>Aus der Konfiguration der Quelle.</summary>
    public int Priority => _source.Priority;

    public TimeSpan Timeout { get; }

    /// <summary>
    /// Baut einen Anbieter aus einer geprüften Quellenbeschreibung, oder
    /// <c>null</c>. Wie beim Suchanbieter ist <c>null</c> kein Fehler an
    /// dieser Stelle — der Validator hat den Grund längst gemeldet.
    /// </summary>
    public static HttpCallerContextProvider? TryCreate(
        DataSourceDefinition source,
        IntegrationHttpClient http,
        bool allowInternalNumbers)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.LookupByPhone is not { } capability || source.Http is null)
        {
            return null;
        }

        try
        {
            if (!MappingEngine.TryCompile(capability.ToMapping(), out var mapping, out _))
            {
                return null;
            }

            var timeout = TimeSpan.FromMilliseconds(
                capability.TimeoutMs ?? source.Http.TimeoutMs);

            return new HttpCallerContextProvider(
                source,
                HttpRequestTemplate.Compile(capability.Request),
                mapping,
                http,
                allowInternalNumbers,
                timeout);
        }
        catch (Exception ex) when (ex is ExpressionParseException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ob diese Nummer nach aussen geht.
    ///
    /// <b>Interne Nummern nicht</b>, ausser es ist ausdrücklich erlaubt
    /// (§21.4): für einen Kollegen auf Nebenstelle 151 hat kein CRM eine
    /// Antwort, und die Nebenstelle eines Mitarbeiters hat auf einem fremden
    /// Server nichts zu suchen. Notrufnummern gelten ebenfalls als intern.
    /// </summary>
    public bool AppliesTo(PhoneNumberKey number, CallDirection direction)
    {
        ArgumentNullException.ThrowIfNull(number);

        return number.IsLookupCandidate(_allowInternal);
    }

    public async Task<ContextFragment> LookupAsync(
        PhoneNumberKey number,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(number);

        var scope = new NumberScope(number);

        var result = await _http.SendAsync(
            _source,
            _request,
            scope,
            Timeout,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsUsable)
        {
            return new ContextFragment(
                SourceId,
                DisplayName,
                result.Outcome switch
                {
                    HttpOutcome.Timeout => SourceState.Timeout,
                    HttpOutcome.Skipped => SourceState.Skipped,
                    _ => SourceState.Error,
                },
                EmptyFields,
                result.Message,
                result.Elapsed,
                Priority: Priority);
        }

        // Der Status gehört in die Umgebung: eine Regel wie
        // "emptyWhen: status == 404" braucht ihn, und ein 404 kommt hier als
        // brauchbare Antwort an, nicht als Fehler.
        var ambient = scope.AsDictionary();
        ambient["status"] = ContextValue.FromNumber((int)(result.StatusCode ?? 0));

        var mapped = MappingEngine.Map(_mapping, result.Body, ambient);

        var state = result.Outcome == HttpOutcome.NotFound || mapped.IsEmpty || !HasAnyValue(mapped)
            ? SourceState.Empty
            : SourceState.Success;

        return new ContextFragment(
            SourceId,
            DisplayName,
            state,
            mapped.Fields,
            Message: null,
            result.Elapsed,
            Priority: Priority);
    }

    /// <summary>
    /// Ob überhaupt ein Feld etwas enthält.
    ///
    /// Eine Antwort, die technisch in Ordnung ist und lauter leere Felder
    /// ergibt, ist für den Benutzer nichts Gefundenes — und eine Karte mit
    /// leeren Zeilen sieht aus wie ein Fehler. Ohne diese Prüfung bräuchte
    /// jede Quelle eine <c>emptyWhen</c>-Regel, nur um das zu sagen.
    /// </summary>
    private static bool HasAnyValue(MappingResult mapped) =>
        mapped.Fields.Values.Any(static value => !value.IsEmpty);

    private static readonly Dictionary<string, ContextValue> EmptyFields =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Was die Anfragevorlage sieht: die Rufnummer in ihren Formen.
    /// </summary>
    private sealed class NumberScope(PhoneNumberKey number) : IExpressionScope
    {
        public ContextValue Resolve(string path) => path switch
        {
            "number.e164" => ContextValue.FromText(number.E164),
            "number.national" => ContextValue.FromText(number.National),
            "number.digits" => ContextValue.FromText(number.Digits),
            _ => ContextValue.Null,
        };

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;

        public Dictionary<string, ContextValue> AsDictionary() =>
            new(StringComparer.Ordinal)
            {
                ["number.e164"] = ContextValue.FromText(number.E164),
                ["number.national"] = ContextValue.FromText(number.National),
                ["number.digits"] = ContextValue.FromText(number.Digits),
            };
    }
}
