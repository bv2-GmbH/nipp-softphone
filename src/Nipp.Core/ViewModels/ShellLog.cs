using Microsoft.Extensions.Logging;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Protokollmeldungen der Hauptansicht.
///
/// Eigene Datei ohne SDK-Bezug — die Namensfalle
/// <c>Linphone.LogLevel</c>/<c>Microsoft.Extensions.Logging.LogLevel</c> aus
/// <c>docs/sdk-api-notes.md</c> bricht den Generator sonst mit Meldungen, die
/// nicht auf die Ursache zeigen.
/// </summary>
internal static partial class ShellLog
{
    [LoggerMessage(EventId = 3400, Level = LogLevel.Warning,
        Message = "Kontakte liessen sich nicht laden: {Reason}. Telefonieren geht weiter.")]
    public static partial void ContactsFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Team-Reihenfolge geaendert ({Count} Nebenstellen)")]
    public static partial void TeamReordered(ILogger logger, int count);

    /// <summary>
    /// <b>Nur die Zahl.</b> Weder der Gruppenname noch der Anzeigename noch
    /// eine Nummer gehoeren ins Protokoll (Paragraph 21.2, ADR-022) — wer in
    /// welcher Gruppe steht, ist eine Aussage ueber Personen.
    /// </summary>
    [LoggerMessage(EventId = 3402, Level = LogLevel.Information,
        Message = "Gruppe gewechselt ({Count} Nebenstellen)")]
    public static partial void TeamGroupChanged(ILogger logger, int count);

    /// <summary>
    /// Eine Wahl, die Paragraph 8.2 abgelehnt hat.
    ///
    /// <para><b>Die Zahl ist der ganze Punkt.</b> Am 23.09.2026 sah der
    /// Benutzer diese Ablehnung, waehrend <em>ein</em> Gespraech lief — und im
    /// Protokoll war der Vorgang nicht zu finden: elf Minuten ohne einen
    /// einzigen Eintrag, weil der <c>catch</c> nur <c>LastError</c> setzte.
    /// Damit liess sich hinterher nicht sagen, ob nipp falsch zaehlte oder ob
    /// etwas anderes passiert war. Steht die Zahl da, ist beides in einer
    /// Zeile zu unterscheiden.</para>
    ///
    /// <para><b>Warum nicht ueber <c>QuietFailures</c>:</b> das meldet einmal
    /// je Stelle und Sitzung, und genau der zweite Fall waere dann wieder
    /// unsichtbar. Gewaehlt wird von Hand und nicht im Pump-Takt — hier
    /// entsteht kein Rauschen.</para>
    ///
    /// <para><b>Keine Nummer</b> (Paragraph 21.2, ADR-022). Wohin gewaehlt
    /// wurde, gehoert nicht ins Protokoll; wie viele Gespraeche offen waren,
    /// ist eine Aussage ueber den Zustand des Programms.</para>
    /// </summary>
    [LoggerMessage(EventId = 3404, Level = LogLevel.Information,
        Message = "Wahl abgelehnt: schon {Count} Gespraech(e) offen (Paragraph 8.2)")]
    public static partial void DialRejectedTooManyCalls(ILogger logger, int count);

    /// <summary>
    /// Eine Wahl, die aus einem anderen Grund nicht zustande kam.
    ///
    /// <b>Nur der Typ, kein Text</b> — dieselbe Ueberlegung wie bei
    /// <see cref="UiUpdateFailed"/>: die Meldung einer Ausnahme traegt oft
    /// genau das, was nicht ins Protokoll gehoert. Der Benutzer bekommt den
    /// erklaerten Satz auf den Bildschirm, das Protokoll bekommt den Typ.
    /// </summary>
    [LoggerMessage(EventId = 3405, Level = LogLevel.Warning,
        Message = "Wahl nicht zustande gekommen ({ExceptionType})")]
    public static partial void DialFailed(ILogger logger, string exceptionType);

    // ADR-053: eine Ausnahme beim Nachziehen der Oberflaeche. Nur der Typ,
    // kein Text — die Meldung einer Ausnahme traegt oft genau das, was nicht
    // ins Protokoll gehoert (Paragraph 21.2).
    [LoggerMessage(EventId = 3403, Level = LogLevel.Warning,
        Message = "Die Oberflaeche liess sich nicht aktualisieren ({ExceptionType}). "
            + "Eine Ansicht kann veraltet sein.")]
    public static partial void UiUpdateFailed(ILogger logger, string exceptionType);
}
