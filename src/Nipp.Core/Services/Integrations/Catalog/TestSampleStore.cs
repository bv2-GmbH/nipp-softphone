using System.Text.Json.Nodes;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Catalog;

/// <summary>
/// Womit der Karten-Designer seine Vorschau füllt (K2, K4).
///
/// <para><b>Nur im Arbeitsspeicher, Prozesslaufzeit.</b> §21.2 verbietet einen
/// Cache auf der Platte, und die Zusage an das das API-Team des Journals lautet
/// „höchstens fünf Minuten im Arbeitsspeicher, nie auf die Platte". Eine
/// gespeicherte Testantwort wäre genau dieser Cache, nur dauerhaft und
/// ausserhalb jeder Aufbewahrungsfrist — und sie läge in einer Datei, die
/// niemand als personenbezogen erwartet.</para>
///
/// <para><b>Damit die Vorschau trotzdem nie leer ist</b>, bringt jede Vorlage
/// eine <b>erfundene</b> Beispielantwort mit. Die darf auf der Platte liegen,
/// weil sie niemanden betrifft. Ein echter Testabruf schlägt sie zur Laufzeit
/// — wer seine Feldpfade prüft, sieht dann seine eigenen Daten in der
/// Vorschau, und beim nächsten Start ist das wieder weg.</para>
///
/// <para><b>Nicht threadsicher, und das ist Absicht.</b> Geschrieben wird aus
/// dem Testabruf der Einstellungen, gelesen aus dem Designer — beides auf dem
/// UI-Thread. Ein <c>ConcurrentDictionary</c> hier würde vortäuschen, dass ein
/// Zugriff von aussen vorgesehen ist; er ist es nicht. Genau in diese Falle
/// ist der Anruferkontext schon einmal gelaufen, in der anderen Richtung: er
/// schrieb aus dem Threadpool in Dictionaries, die der UI-Thread liest.</para>
/// </summary>
public sealed class TestSampleStore
{
    /// <summary>Je Quellenkennung die letzte Antwort im Rohzustand.</summary>
    private readonly Dictionary<string, JsonNode> _samples = new(StringComparer.Ordinal);

    /// <summary>Welche Kennungen aus einem echten Abruf stammen.</summary>
    private readonly HashSet<string> _fromLiveCall = new(StringComparer.Ordinal);

    private readonly ConnectorLibrary? _library;

    /// <param name="library">
    /// Woher die erfundenen Beispielantworten kommen (ADR-040). <c>null</c>
    /// heisst: nur echte Testabrufe — dann ist die Vorschau im Designer leer,
    /// solange keiner gelaufen ist.
    ///
    /// <b>Diesen Aufrufer übersieht man leicht:</b> an ihm hängt die Vorschau
    /// im Karten-Designer, nicht nur die Einstellungsseite.
    /// </param>
    public TestSampleStore(ConnectorLibrary? library = null) => _library = library;

    /// <summary>
    /// Legt die Antwort eines echten Testabrufs ab. Sie schlägt eine
    /// Beispielantwort und überlebt den Prozess nicht.
    ///
    /// <para><b>Der Rückgabewert ist der Punkt</b> (13.09.2026). Diese Methode
    /// kehrte bei einer unlesbaren Antwort <b>still</b> zurück — und der
    /// Aufrufer zählte den Abruf trotzdem als gelungen, meldete «1 von 1
    /// Quellen haben geantwortet» und zeichnete die alten Beispieldaten neu.
    /// Die Vorschau zeigte danach dasselbe wie vorher, ohne dass irgendwo ein
    /// Wort darüber stand.</para>
    ///
    /// <para><b>Ein stiller Fänger ist die Lücke, die dieses Projekt mehrfach
    /// bezahlt hat.</b> Wer das Ergebnis nicht ansieht, verhält sich wie
    /// vorher; wer es ansieht, kann sagen, was nicht geklappt hat.</para>
    /// </summary>
    /// <param name="rawResponse">
    /// Die <b>ungekürzte</b> Antwort. Eine für die Anzeige gekürzte Fassung ist
    /// kein gültiges JSON mehr und landet hier im <c>false</c>-Zweig — genau
    /// das war der Befund, siehe <c>IntegrationTester.Vollstaendig</c>.
    /// </param>
    /// <returns>
    /// <c>true</c>, wenn die Antwort übernommen wurde. <c>false</c>, wenn sie
    /// leer war oder sich nicht lesen liess — die vorhandenen Vorschaudaten
    /// bleiben dann stehen.
    /// </returns>
    public bool SetFromLiveCall(string sourceId, string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        JsonNode? geparst;

        try
        {
            geparst = JsonNode.Parse(rawResponse);
        }
        catch (System.Text.Json.JsonException)
        {
            // Eine Antwort, die kein JSON ist, ist als Vorschau nutzlos — aber
            // kein Grund, die vorhandene wegzuwerfen. Der Aufrufer sagt es.
            return false;
        }

        if (geparst is null)
        {
            return false;
        }

        _samples[sourceId] = geparst;
        _fromLiveCall.Add(sourceId);

        return true;
    }

    /// <summary>
    /// Was für diese Quelle als Vorschaudaten gilt: die Antwort des letzten
    /// Testabrufs, sonst die erfundene Beispielantwort ihrer Vorlage.
    /// </summary>
    public JsonNode? For(string sourceId)
    {
        if (_samples.TryGetValue(sourceId, out var vorhanden))
        {
            return vorhanden;
        }

        return _library?.Templates
            .FirstOrDefault(t => t.Source.Id == sourceId)
            ?.SampleResponse;
    }

    /// <summary>
    /// Ob die Vorschaudaten dieser Quelle aus einem echten Abruf stammen.
    ///
    /// Die Oberfläche schreibt das dazu — „Vorschau mit Beispieldaten" gegen
    /// „Vorschau mit der Antwort von 14:32". Ohne diesen Unterschied hält
    /// jemand eine erfundene Zeile für seine eigenen Daten und richtet die
    /// Karte auf Felder aus, die seine Quelle nie liefert.
    /// </summary>
    public bool IsFromLiveCall(string sourceId) => _fromLiveCall.Contains(sourceId);

    /// <summary>Vergisst alles — beim Wechsel der Konfiguration.</summary>
    public void Clear()
    {
        _samples.Clear();
        _fromLiveCall.Clear();
    }

    /// <summary>
    /// Baut aus den Vorschaudaten einen Schnappschuss, wie ihn ein echter
    /// Anruf erzeugen würde — die Eingabe der Kartenvorschau.
    ///
    /// <para><b>Über dasselbe Mapping wie im Betrieb.</b> Eine Vorschau, die
    /// ihre Felder anders gewinnt als der Anruf, ist keine Vorschau. Und der
    /// Fall, für den das zählt, ist der häufigste beim Einrichten: der
    /// JSONPath ist falsch, und die Karte bleibt leer. Das soll <b>in</b> der
    /// Vorschau zu sehen sein, nicht erst am Telefon.</para>
    /// </summary>
    /// <param name="config">Die Konfiguration, deren Quellen gefragt werden.</param>
    /// <param name="number">
    /// Die Nummer, die der Vorschau zugrunde liegt — für <c>number.e164</c>
    /// und den Rückfall „sonst die Nummer".
    /// </param>
    public ContextSnapshot BuildSnapshot(IntegrationConfig config, PhoneNumberKey number)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(number);

        var sources = new Dictionary<string, ContextFragment>(StringComparer.Ordinal);

        foreach (var source in config.DataSources)
        {
            // <b>Ausgeschaltete Quellen bleiben ausdrücklich drin</b>, obwohl
            // der Testabruf nur eingeschaltete fragt. Am 13.09.2026 war das
            // einmal andersherum gebaut, und drei Tests haben es zurückgeholt:
            // <b>eine Vorlage wird nie eingeschaltet ausgeliefert</b>
            // (ADR-040), eingeschaltet wird nach dem Testabruf. Wer hier auf
            // Enabled prüft, macht die Vorschau beim Einrichten leer — also
            // genau dann, wenn man sie braucht (§21.2: sie ist nie leer, weil
            // jede Vorlage eine erfundene Beispielantwort mitbringt).
            if (source.LookupByPhone is not { } capability)
            {
                continue;
            }

            var rohdaten = For(source.Id);

            if (rohdaten is null)
            {
                // Eine Quelle ohne Vorschaudaten wird als „nichts gefunden"
                // gezeigt und nicht weggelassen: so sieht der Designer, dass
                // sie da ist und ein Testabruf fehlt.
                sources[source.Id] = new ContextFragment(
                    source.Id,
                    source.DisplayName,
                    SourceState.Empty,
                    new Dictionary<string, ContextValue>(StringComparer.Ordinal),
                    "Kein Testabruf auf diesem Gerät — Verbindung testen.",
                    Priority: source.Priority);

                continue;
            }

            if (!MappingEngine.TryCompile(capability.ToMapping(), out var mapping, out _))
            {
                continue;
            }

            var ambient = new Dictionary<string, ContextValue>(StringComparer.Ordinal)
            {
                ["number.e164"] = ContextValue.FromText(number.E164),
                ["number.national"] = ContextValue.FromText(number.National),
                ["number.digits"] = ContextValue.FromText(number.Digits),
                ["status"] = ContextValue.FromNumber(200),
            };

            var gemappt = MappingEngine.Map(mapping, rohdaten, ambient);

            sources[source.Id] = new ContextFragment(
                source.Id,
                source.DisplayName,
                gemappt.IsEmpty ? SourceState.Empty : SourceState.Success,
                gemappt.Fields,
                Priority: source.Priority);
        }

        return new ContextSnapshot(CallHandle.New(), number, sources);
    }

    /// <summary>
    /// Die Nummer, mit der eine Vorschau ohne eigene Angabe rechnet.
    ///
    /// Eine Schweizer Mobilnummer in der üblichen Form: sie zeigt, wie
    /// <c>formatPhone</c> aussieht, und ist erkennbar erfunden.
    /// </summary>
    public static PhoneNumberKey DefaultPreviewNumber { get; } =
        PhoneNumberKey.From("0791234567", new NumberNormalizer("+41"));
}
