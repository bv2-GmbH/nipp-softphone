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

    // ADR-053: eine Ausnahme beim Nachziehen der Oberflaeche. Nur der Typ,
    // kein Text — die Meldung einer Ausnahme traegt oft genau das, was nicht
    // ins Protokoll gehoert (Paragraph 21.2).
    [LoggerMessage(EventId = 3403, Level = LogLevel.Warning,
        Message = "Die Oberflaeche liess sich nicht aktualisieren ({ExceptionType}). "
            + "Eine Ansicht kann veraltet sein.")]
    public static partial void UiUpdateFailed(ILogger logger, string exceptionType);
}
