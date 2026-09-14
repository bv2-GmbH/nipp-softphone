using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Was eine Quelle gerade tut oder getan hat (§21.1: die Oberfläche soll auf
/// diese Zustände reagieren können).
/// </summary>
public enum SourceState
{
    /// <summary>Wird gerade gefragt.</summary>
    Loading,

    /// <summary>Hat geantwortet und etwas geliefert.</summary>
    Success,

    /// <summary>Hat geantwortet, kennt diese Nummer aber nicht.</summary>
    Empty,

    /// <summary>Netz, Anmeldung oder Antwort waren unbrauchbar.</summary>
    Error,

    /// <summary>Hat nicht rechtzeitig geantwortet.</summary>
    Timeout,

    /// <summary>Wurde nicht gefragt — abgeschaltet, Geheimnis fehlt, interne Nummer.</summary>
    Skipped,
}

/// <summary>
/// Der Beitrag genau einer Quelle zu einem Anruf.
/// </summary>
/// <param name="SourceId">Die Kennung der Quelle — zugleich der Namensraum auf der Karte.</param>
/// <param name="DisplayName">Wie die Quelle in der Oberfläche heisst.</param>
/// <param name="State">Wie es ausging.</param>
/// <param name="Fields">Die gemappten Felder. Nie <c>null</c>, notfalls leer.</param>
/// <param name="Message">
/// Ein fertiger Satz für die Oberfläche nach §15, wenn etwas zu sagen ist.
/// Ohne Rufnummer, ohne Adresse, ohne Antwortinhalt.
/// </param>
/// <param name="Elapsed">Wie lange die Quelle gebraucht hat.</param>
/// <param name="FromCache">Ob die Angaben aus dem Zwischenspeicher kommen.</param>
/// <param name="Priority">
/// Die Priorität der Quelle — kleinere Zahl gewinnt, wie in
/// <c>DataSourceDefinition.Priority</c>.
///
/// <para><b>Wofür sie hier steht.</b> <c>role('name')</c> und
/// <c>anyOf(...)</c> fragen nicht eine bestimmte Quelle, sondern „hat
/// irgendeine ein Feld dieser Bedeutung?" — und dann muss entschieden sein,
/// <b>welche</b> gewinnt. <see cref="ContextSnapshot.Sources"/> ist ein
/// Dictionary und hat keine verlässliche Reihenfolge; sich auf die
/// Einfügereihenfolge zu verlassen wäre genau die Art Annahme, die beim
/// ersten Entfernen eines Eintrags still bricht.</para>
/// </param>
public sealed record ContextFragment(
    string SourceId,
    string DisplayName,
    SourceState State,
    IReadOnlyDictionary<string, ContextValue> Fields,
    string? Message = null,
    TimeSpan Elapsed = default,
    bool FromCache = false,
    int Priority = int.MaxValue)
{
    private static readonly Dictionary<string, ContextValue> NoFields =
        new(StringComparer.Ordinal);

    /// <summary>Eine Quelle, die gerade gefragt wird.</summary>
    public static ContextFragment Loading(
        string sourceId,
        string displayName,
        int priority = int.MaxValue) =>
        new(sourceId, displayName, SourceState.Loading, NoFields, Priority: priority);

    /// <summary>Eine Quelle, die nicht gefragt wurde.</summary>
    public static ContextFragment Skipped(
        string sourceId,
        string displayName,
        string? message = null,
        int priority = int.MaxValue) =>
        new(sourceId, displayName, SourceState.Skipped, NoFields, message, Priority: priority);

    /// <summary>Ob diese Quelle etwas beigetragen hat, das sich anzeigen lässt.</summary>
    public bool HasData => State == SourceState.Success && Fields.Count > 0;
}

/// <summary>
/// Alles, was zu einem Anruf bekannt ist (§21.1).
///
/// <b>Unveränderlich, wie <c>CallInfo</c></b> — und aus demselben Grund: die
/// Oberfläche bindet darauf, und ein Objekt, das sich unter ihr ändert, führt
/// zu halb aktualisierten Anzeigen. Jede Antwort einer Quelle erzeugt einen
/// neuen Schnappschuss.
///
/// Der Aufbau entspricht dem, was der Auftrag verlangt: die <b>Herkunft ist
/// der Schlüssel der ersten Ebene</b>, und die Oberfläche adressiert nur
/// <c>quelle.feld</c> — nie einen JSON-Pfad des Fremdsystems.
///
/// <code>
/// { "crm": { "customerName": "…" }, "erp": { "customerNumber": "…" } }
/// </code>
/// </summary>
/// <param name="Call">Zu welchem Anruf.</param>
/// <param name="Number">Die Rufnummer in ihren Formen.</param>
/// <param name="Sources">Quellenkennung zu Beitrag.</param>
/// <param name="Facts">
/// Was über den Anruf selbst bekannt ist — Zeit, Dauer, Ergebnis —, oder
/// <c>null</c>.
///
/// <para><b>Neben <see cref="Number"/> und nicht als Quelle.</b> Ein
/// synthetisches Fragment mit der Kennung <c>call</c> wäre einfacher gewesen
/// und an drei Stellen falsch: es stünde in <see cref="SourcesByPriority"/>,
/// würde bei <c>role()</c> mitgewichtet und erschiene in der Oberfläche als
/// Quelle, die niemand eingerichtet hat. Die Rufnummer gehört aus demselben
/// Grund schon nicht zu den Quellen.</para>
/// </param>
public sealed record ContextSnapshot(
    CallHandle Call,
    PhoneNumberKey Number,
    IReadOnlyDictionary<string, ContextFragment> Sources,
    CallFacts? Facts = null)
{
    /// <summary>Ein Schnappschuss, in dem noch nichts steht.</summary>
    public static ContextSnapshot Empty(CallHandle call, PhoneNumberKey number) =>
        new(call, number, new Dictionary<string, ContextFragment>(StringComparer.Ordinal));

    /// <summary>Ob keine Quelle mehr aussteht.</summary>
    public bool IsComplete =>
        Sources.Values.All(static f => f.State != SourceState.Loading);

    /// <summary>
    /// Die Beiträge in der Reihenfolge, in der sie gewinnen — kleinere
    /// Priorität zuerst, bei Gleichstand nach Kennung.
    ///
    /// <b>Nach Kennung als zweites Merkmal, und nicht zufällig:</b> zwei
    /// Quellen mit derselben Priorität sollen nicht bei jedem Anruf eine
    /// andere Antwort gewinnen lassen. Eine Karte, die manchmal den einen und
    /// manchmal den anderen Namen zeigt, ist schlimmer als eine, die immer
    /// denselben zeigt.
    /// </summary>
    public IEnumerable<ContextFragment> SourcesByPriority =>
        Sources.Values
            .OrderBy(static f => f.Priority)
            .ThenBy(static f => f.SourceId, StringComparer.Ordinal);

    /// <summary>
    /// Ein Feld <b>nach Bedeutung</b>, ohne Quelle: der erste nicht-leere
    /// Wert eines Feldes dieses Namens über alle Quellen, nach Priorität.
    ///
    /// <para><b>Warum das eine eigene Frage ist.</b> Die Plattform ist
    /// generisch (§21): welche Quellen es gibt, entscheidet die Konfiguration,
    /// und sie heissen beim einen Kunden <c>crm</c> und beim nächsten
    /// anders. Eine Karte, die <c>crm.contactName</c> nennt, ist bei
    /// jedem anderen Kunden leer — genau das war der Zustand der
    /// mitgelieferten Karten bis zum 07.09.2026, und in dieser Form stand die
    /// Regel doppelt: einmal als <c>coalesce</c> in der Karte und einmal als
    /// fest verdrahtete Feldliste im <c>ToastComposer</c>.</para>
    ///
    /// <para>Leer heisst hier <see cref="ContextValue.Null"/> und nicht
    /// „Fehler": eine Quelle, die dieses Feld nicht mappt, ist der
    /// Normalfall.</para>
    /// </summary>
    public ContextValue FieldAcrossSources(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return ContextValue.Null;
        }

        foreach (var fragment in SourcesByPriority)
        {
            if (fragment.Fields.TryGetValue(field, out var value) && !value.IsEmpty)
            {
                return value;
            }
        }

        return ContextValue.Null;
    }

    /// <summary>
    /// Ein Feld über seinen Pfad, etwa <c>crm.customerName</c>.
    ///
    /// Kennt zusätzlich den Namensraum <c>number</c> — die Rufnummer gehört
    /// zum Anruf und nicht zu einer Quelle, muss auf einer Karte aber
    /// genauso ansprechbar sein.
    /// </summary>
    public ContextValue Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ContextValue.Null;
        }

        var separator = path.IndexOf('.', StringComparison.Ordinal);

        if (separator <= 0 || separator == path.Length - 1)
        {
            return ContextValue.Null;
        }

        var space = path[..separator];
        var field = path[(separator + 1)..];

        if (string.Equals(space, "number", StringComparison.Ordinal))
        {
            return field switch
            {
                "e164" => ContextValue.FromText(Number.E164),
                "national" => ContextValue.FromText(Number.National),
                "digits" => ContextValue.FromText(Number.Digits),
                _ => ContextValue.Null,
            };
        }

        if (string.Equals(space, "call", StringComparison.Ordinal))
        {
            // Ohne Angaben ist jedes Feld leer — und leer heisst auf einer
            // Karte „die Zeile verschwindet", nicht „Fehler". Beim laufenden
            // Anruf gibt es noch keine Dauer und kein Ergebnis; die Karte in
            // der Anrufliste hat beides.
            return Facts is not { } fakten
                ? ContextValue.Null
                : field switch
                {
                    "startedAt" => ContextValue.FromDate(fakten.StartedAt),
                    "date" => ContextValue.FromText(fakten.DateText),
                    "time" => ContextValue.FromText(fakten.TimeText),
                    "duration" => ContextValue.FromText(fakten.DurationText),
                    "outcome" => ContextValue.FromText(fakten.Outcome),
                    "direction" => ContextValue.FromText(fakten.Direction),
                    _ => ContextValue.Null,
                };
        }

        return Sources.TryGetValue(space, out var fragment)
            && fragment.Fields.TryGetValue(field, out var value)
                ? value
                : ContextValue.Null;
    }

    /// <summary>Setzt den Beitrag einer Quelle und liefert den neuen Schnappschuss.</summary>
    public ContextSnapshot With(ContextFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var sources = new Dictionary<string, ContextFragment>(Sources, StringComparer.Ordinal)
        {
            [fragment.SourceId] = fragment,
        };

        return this with { Sources = sources };
    }
}
