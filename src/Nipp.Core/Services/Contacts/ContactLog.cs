using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Protokollmeldungen rund um Kontakte und Präsenz.
///
/// Eigene Datei ohne <c>using Linphone</c> — die Namensfalle aus
/// <c>docs/sdk-api-notes.md</c> (<c>Linphone.LogLevel</c> gegen
/// <c>Microsoft.Extensions.Logging.LogLevel</c>) bricht sonst den Generator.
///
/// <b>Keine Namen und keine Nummern in den Meldungen.</b> Ein Adressbuch im
/// Protokoll wäre eine Datenschutzverletzung mit Ansage (§11) — gezählt wird,
/// nicht aufgezählt.
/// </summary>
internal static partial class ContactLog
{
    [LoggerMessage(EventId = 3300, Level = LogLevel.Information,
        Message = "Kontakte geladen: {Count}")]
    public static partial void StoreRefreshed(ILogger logger, int count);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Debug,
        Message = "Kein Outlook auf dieser Maschine — persoenliche Kontakte bleiben leer")]
    public static partial void OutlookMissing(ILogger logger);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information,
        Message = "Outlook gelesen: {Usable} von {Total} Eintraegen mit waehlbarer Nummer")]
    public static partial void OutlookLoaded(ILogger logger, int usable, int total);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Warning,
        Message = "Outlook nicht lesbar: {Reason}. nipp laeuft ohne persoenliche Kontakte weiter.")]
    public static partial void OutlookFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Warning,
        Message = "Outlook antwortet seit {Seconds} s nicht — vermutlich ein offener Dialog. Abgebrochen.")]
    public static partial void OutlookTimedOut(ILogger logger, int seconds);

    [LoggerMessage(EventId = 3305, Level = LogLevel.Debug,
        Message = "Outlook-Eintrag {Index} uebersprungen: {Reason}")]
    public static partial void OutlookItemSkipped(ILogger logger, int index, string reason);

    [LoggerMessage(EventId = 3310, Level = LogLevel.Information,
        Message = "Besetztlampenfeld: {Count} Nebenstellen abonniert")]
    public static partial void BlfSubscribed(ILogger logger, int count);

    [LoggerMessage(EventId = 3311, Level = LogLevel.Warning,
        Message = "Besetztlampenfeld nicht moeglich: {Reason}")]
    public static partial void BlfFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3312, Level = LogLevel.Debug,
        Message = "Praesenz geaendert: {Presence}")]
    public static partial void BlfPresence(ILogger logger, string presence);

    [LoggerMessage(EventId = 3306, Level = LogLevel.Information,
        Message = "Outlook war nach der Zeitgrenze doch noch fertig: {Count} Kontakte. "
            + "Sie werden beim naechsten Laden ohne Wartezeit verwendet.")]
    public static partial void OutlookLateResult(ILogger logger, int count);

    [LoggerMessage(EventId = 3307, Level = LogLevel.Information,
        Message = "{Count} Kontakte aus dem verspaeteten Durchlauf uebernommen")]
    public static partial void OutlookLateResultUsed(ILogger logger, int count);

    [LoggerMessage(EventId = 2350, Level = LogLevel.Information,
        Message = "Outlook laeuft nicht — persoenliche Kontakte bleiben aus. "
            + "nipp startet Outlook absichtlich nicht selbst (Paragraph 8.4).")]
    public static partial void OutlookNotRunning(ILogger logger);

    // W2.2 (B22): ein Empfaenger der Kontaktliste hat geworfen. Nur der Typ —
    // eine Ausnahmemeldung traegt oft genau das, was nicht ins Protokoll
    // gehoert (Paragraph 21.2).
    [LoggerMessage(EventId = 2630, Level = LogLevel.Warning,
        Message = "Ein Empfaenger der Kontaktliste hat eine Ausnahme ausgeloest "
            + "({ExceptionType}). Die Liste selbst ist geladen.")]
    public static partial void NotifyFailed(ILogger logger, string exceptionType);
}
