using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.History;

/// <summary>
/// Ein Eintrag der Anrufliste (§8.3, §20.3).
///
/// <b>Gespeichert wird alles, angezeigt wird wenig.</b> §20.3 verlangt in der
/// Liste nur Nummer, Dauer, Uhrzeit und Ergebnis. Die übrigen Felder kosten
/// nichts, werden aber für die Diagnose (§9.6) und für den Rückruf gebraucht —
/// deshalb bleiben sie in der Datenbank.
/// </summary>
/// <param name="Id">Fortlaufend, von der Datenbank vergeben.</param>
/// <param name="Number">Nummer der Gegenstelle, normalisiert (§8.1).</param>
/// <param name="DisplayName">Aufgelöster Name, falls bekannt (§8.4 CLIP).</param>
/// <param name="Direction">Eingehend oder ausgehend.</param>
/// <param name="Outcome">Was mit dem Anruf passiert ist — die vierte Spalte aus §20.3.</param>
/// <param name="StartedAt">Zeitpunkt des Aufbaus. Liefert die Uhrzeit für die Anzeige.</param>
/// <param name="Duration">Gesprächsdauer; <c>null</c>, wenn nie verbunden.</param>
/// <param name="Codec">Verhandelter Codec — nur Diagnose.</param>
/// <param name="AccountIdentity">Über welches Konto (§20.2) — nur Diagnose.</param>
/// <param name="RecordingPath">Pfad der Aufnahme, falls eine entstand (§8.2).</param>
/// <param name="SeenAt">
/// Wann der Eintrag angesehen wurde, oder <c>null</c> (ADR-035).
///
/// <para><b>In der Datenbank und nicht im Arbeitsspeicher.</b> Das Abzeichen
/// in der Umschaltleiste zählt die ungesehenen verpassten Anrufe; ein Zustand,
/// der einen Neustart nicht überlebt, brächte die weggeklickte Zahl beim
/// nächsten Start zurück. nipp läuft im Infobereich und wird selten beendet —
/// aber wenn, wäre es genau der Moment, in dem die Anzeige lügt.</para>
///
/// <para>Ein Zeitstempel und kein Wahrheitswert: er kostet dasselbe und sagt
/// bei einer Frage aus dem Betrieb zusätzlich, <b>wann</b> jemand hingeschaut
/// hat. Ein Inhalt ist er nicht — ADR-022 und ADR-027 sind nicht berührt.</para>
/// </param>
public sealed record CallHistoryEntry(
    long Id,
    string Number,
    string? DisplayName,
    CallDirection Direction,
    CallOutcome Outcome,
    DateTimeOffset StartedAt,
    TimeSpan? Duration,
    string? Codec,
    string? AccountIdentity,
    string? RecordingPath,
    DateTimeOffset? SeenAt = null)
{
    /// <summary>Was in der Liste steht: Name, sonst Nummer (§20.3).</summary>
    /// <summary>
    /// Was in der Liste steht: der Name, wenn einer bekannt ist, sonst die
    /// Nummer — und die lesbar gruppiert, nicht als Ziffernkette.
    /// </summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? Telephony.PhoneNumberFormat.ForDisplay(Number)
            : DisplayName;

    /// <summary>Ob eine Aufnahme vorliegt und noch existiert.</summary>
    public bool HasRecording =>
        RecordingPath is { Length: > 0 } path && File.Exists(path);

    /// <summary>
    /// Ob der Eintrag noch niemandem aufgefallen ist — die Zeile steht dann
    /// fett, und er zählt im Abzeichen mit (ADR-035).
    ///
    /// <para><b>Nur verpasste Anrufe sind je „neu".</b> Wäre jeder selbst
    /// gewählte Anruf fett, sagte die Schrift etwas anderes als die Zahl
    /// daneben — zwei Anzeigen für denselben Sachverhalt, die sich
    /// unterscheiden. Genau diese Bauart hat schon einmal einen eingehenden
    /// Anruf unsichtbar gemacht.</para>
    /// </summary>
    public bool IsNew => Outcome == CallOutcome.Missed && SeenAt is null;
}

/// <summary>
/// Was mit einem Anruf passiert ist (§8.3, §20.3).
/// </summary>
public enum CallOutcome
{
    /// <summary>Gespräch kam zustande.</summary>
    Answered,

    /// <summary>Eingehend, nicht angenommen.</summary>
    Missed,

    /// <summary>Eingehend, bewusst abgelehnt.</summary>
    Declined,

    /// <summary>Ausgehend, Gegenstelle nahm nicht ab.</summary>
    NoAnswer,

    /// <summary>Ausgehend, besetzt.</summary>
    Busy,

    /// <summary>Technisch gescheitert — der Grund steht im Log.</summary>
    Failed,
}

/// <summary>
/// Leitet aus einem beendeten Gespräch ab, was mit ihm passiert ist (§20.3,
/// vierte Spalte).
///
/// <b>Reine Funktion, absichtlich ohne Abhängigkeiten</b> — wie
/// <c>NumberNormalizer</c> und <c>ClipResolver</c>: das ist die Stelle, an der
/// „besetzt" zu „fehlgeschlagen" wird, wenn niemand hinsieht, und nur so
/// lässt sie sich vollständig prüfen.
/// </summary>
public static class CallOutcomeRules
{
    /// <summary>
    /// Das Ergebnis eines beendeten Gesprächs.
    ///
    /// <b>Verbunden schlägt alles:</b> wer gesprochen hat, hat den Anruf
    /// angenommen — auch wenn die Gegenstelle danach mit „besetzt" auflegt.
    /// Danach entscheidet der Grund, den die Anlage genannt hat; ohne Grund
    /// die Richtung.
    /// </summary>
    public static CallOutcome Determine(
        DateTimeOffset? connectedAt,
        CallEndReason? endReason,
        CallDirection direction) =>
        (connectedAt, endReason, direction) switch
        {
            (not null, _, _) => CallOutcome.Answered,
            (_, CallEndReason.Busy, _) => CallOutcome.Busy,
            (_, CallEndReason.Declined, _) => CallOutcome.Declined,
            (_, CallEndReason.Failed, _) => CallOutcome.Failed,
            (_, _, CallDirection.Incoming) => CallOutcome.Missed,
            _ => CallOutcome.NoAnswer,
        };
}

/// <summary>
/// Ergebnis und Richtung in Worten (§20.3).
///
/// <para><b>Einmal und nicht dreimal.</b> Dieselbe Zuordnung stand im
/// <c>HistoryDetailConverter</c> der Oberfläche; mit der Karte in der
/// Anrufliste käme eine zweite Stelle dazu und mit dem Toast eine dritte. Zwei
/// Kopien einer Regel sind zwei Gelegenheiten, sie falsch zu haben — dieselbe
/// Überlegung wie bei „beginnt hier ein Anruf zu klingeln?".</para>
/// </summary>
public static class CallOutcomeText
{
    /// <summary>
    /// Was mit dem Anruf passiert ist. Die Richtung entscheidet mit: ein
    /// angenommener eingehender Anruf ist „angenommen", ein ausgehender
    /// „verbunden".
    /// </summary>
    public static string For(CallOutcome outcome, CallDirection direction) => outcome switch
    {
        CallOutcome.Answered => direction == CallDirection.Incoming ? "angenommen" : "verbunden",
        CallOutcome.Missed => "verpasst",
        CallOutcome.Declined => "abgelehnt",
        CallOutcome.NoAnswer => "keine Antwort",
        CallOutcome.Busy => "besetzt",
        _ => "fehlgeschlagen",
    };

    /// <summary>Die Richtung in einem Wort.</summary>
    public static string For(CallDirection direction) =>
        direction == CallDirection.Incoming ? "eingehend" : "ausgehend";
}

/// <summary>Filter der Anrufliste (§8.3).</summary>
public enum CallHistoryFilter
{
    All,
    Missed,
    Incoming,
    Outgoing,
    Recorded,
}
