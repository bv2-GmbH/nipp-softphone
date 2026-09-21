using Microsoft.Extensions.Logging;

namespace Nipp.App.Diagnostics;

/// <summary>
/// Protokollmeldungen der App als quellgenerierte Delegaten.
///
/// Warum nicht direkt <c>logger.LogInformation(...)</c>: CA1848. Die
/// Erweiterungsmethoden boxen jedes Argument und formatieren die Vorlage bei
/// jedem Aufruf neu. Bei einem Softphone mit einem 20-ms-Iterate-Timer (§6) und
/// Debug-Protokollierung (§9.6) ist das kein akademischer Punkt — deshalb
/// stehen alle Meldungen hier und werden vom Generator in typisierte,
/// allokationsfreie Aufrufe übersetzt.
///
/// Neue Meldungen kommen hier dazu, nicht als Aufruf im Code.
/// </summary>
internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "nipp startet (Version {Version})")]
    public static partial void Starting(ILogger logger, string version);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Critical,
        Message = "Unbehandelte Ausnahme: {Message}")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string message);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Die Telefonie liess sich nicht starten. nipp läuft weiter, kann aber nicht telefonieren.")]
    public static partial void TelephonyStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Telefonie nicht verfügbar: {Reason}")]
    public static partial void TelephonyUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Eine Instanz laeuft bereits — Aktivierung weitergereicht")]
    public static partial void RedirectedToRunningInstance(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Einzelinstanz nicht verfuegbar ({Reason}) — nipp kann mehrfach laufen")]
    public static partial void SingleInstanceUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information,
        Message = "Anruf-URI empfangen: {Number}")]
    public static partial void CallUriReceived(ILogger logger, string number);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information,
        Message = "Fenster geschlossen — nipp laeuft im Infobereich weiter")]
    public static partial void HiddenToTray(ILogger logger);

    [LoggerMessage(EventId = 1050, Level = LogLevel.Warning,
        Message = "Kontakte liessen sich beim Start nicht laden: {Reason}")]
    public static partial void ContactsFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1051, Level = LogLevel.Warning,
        Message = "Provisionierung: {Reason}")]
    public static partial void ProvisioningWarning(ILogger logger, string reason);

    [LoggerMessage(EventId = 1052, Level = LogLevel.Warning,
        Message = "Integrationen sind nicht verfuegbar: {Reason}. "
            + "nipp telefoniert unveraendert weiter (Paragraph 21.2).")]
    public static partial void IntegrationsUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information,
        Message = "Tastenkuerzel: Anruf {Action}")]
    public static partial void HotkeyAction(ILogger logger, string action);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning,
        Message = "Tastenkuerzel konnte nicht ausgefuehrt werden: {Reason}")]
    public static partial void HotkeyFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning,
        Message = "Symbol der Titelleiste liess sich nicht laden: {Reason}")]
    public static partial void TitleBarIconFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning,
        Message = "Symbol im Info-Bereich der Einstellungen liess sich nicht laden: {Reason}")]
    public static partial void AboutLogoFailed(ILogger logger, string reason);

    // Der Weg zurueck ins Fenster (Paragraph 10). Er ist der einzige: nipp
    // lebt im Infobereich, und wenn er ausfaellt, ist die Anwendung nur noch
    // ueber den Task-Manager erreichbar.
    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning,
        Message = "Fenster liess sich nicht ueber WinUI zeigen ({Reason}) — es folgt der Win32-Weg")]
    public static partial void ShowFromTrayFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Warning,
        Message = "Der Karten-Designer liess sich nicht auf seine Groesse setzen ({Reason}) "
            + "-- er ist trotzdem benutzbar")]
    public static partial void CardDesignerSizeFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information,
        Message = "Fenster ueber den Win32-Rueckfall gezeigt, sichtbar: {Visible}")]
    public static partial void ShownByFallback(ILogger logger, bool visible);

    // 1053, nicht 1052: die Kennung war doppelt vergeben (IntegrationsUnavailable),
    // und wer sein Protokoll danach filtert, bekam zwei verschiedene Dinge.
    [LoggerMessage(EventId = 1053, Level = LogLevel.Warning,
        Message = "Einstellungen konnten nicht uebernommen werden: {Reason}. "
            + "Sie gelten nach einem Neustart von nipp.")]
    public static partial void SettingsNotApplied(ILogger logger, string reason);

    [LoggerMessage(EventId = 1054, Level = LogLevel.Warning,
        Message = "Die Telefonie liess sich nicht geordnet beenden")]
    public static partial void TelephonyShutdownFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Ein Ziehvorgang in der Kontaktliste hat begonnen.
    ///
    /// <para><b>Warum diese Zeile.</b> Bis zum 13.09.2026 war «die Zeile
    /// springt zurueck» von «es wurde gar nicht gezogen» nicht zu
    /// unterscheiden: ueber den Ziehvorgang stand keine einzige Zeile im
    /// Protokoll. Dieselbe Luecke wie beim Symbol im Infobereich und bei den
    /// HID-Reports — zum dritten Mal.</para>
    ///
    /// <para><b>Nur die Anzahl</b>, kein Name und keine Kennung: eine
    /// Team-Kennung lautet <c>team:&lt;kurzwahl&gt;</c> und ist damit selbst
    /// eine Rufnummer (Paragraf 21.2, ADR-022).</para>
    /// </summary>
    [LoggerMessage(EventId = 1063, Level = LogLevel.Debug,
        Message = "Ziehvorgang angefragt (Sortiermodus={Mode}, Team-Zeile={Known})")]
    public static partial void DragStarted(ILogger logger, bool mode, bool known);

    /// <summary>
    /// Ein Ziehvorgang endete, ohne dass etwas verschoben wurde — der Zug
    /// wurde zurueckgezogen oder das Ziel hat ihn abgelehnt.
    /// </summary>
    [LoggerMessage(EventId = 1064, Level = LogLevel.Debug,
        Message = "Ziehvorgang endete ohne Verschiebung (Ergebnis {Result})")]
    public static partial void DragWithoutMove(ILogger logger, string result);

    /// <summary>
    /// Ein Zug ist durchgelaufen — und ob die Zeile dabei die Gruppe
    /// gewechselt hat.
    ///
    /// <para><b>Das ist die Zeile, die den Befund vom 13.09.2026 sichtbar
    /// macht.</b> ADR-042 nahm an, WinUI haenge die Zeile beim gruppierten
    /// Umsortieren selbst zwischen den Gruppen um. Trifft das nicht zu,
    /// passiert genau hier nichts — und bis heute stand darueber kein Wort
    /// im Protokoll.</para>
    /// </summary>
    [LoggerMessage(EventId = 1065, Level = LogLevel.Debug,
        Message = "Ziehvorgang beendet (Gruppe gewechselt: {Changed})")]
    public static partial void DragEnded(ILogger logger, bool changed);

    [LoggerMessage(EventId = 1055, Level = LogLevel.Debug,
        Message = "Update-Pruefung beim Start abgebrochen ({Kind}) — ohne Folgen fuer den Betrieb")]
    public static partial void UpdateCheckFailed(ILogger logger, string kind);

    /// <summary>
    /// Ein Schritt beim Beenden. Wirkt kleinlich und ist es nicht: am
    /// 07.09.2026 lief das Herunterfahren durch, und der Prozess blieb
    /// trotzdem liegen. Wer das sucht, braucht die <b>erste fehlende</b>
    /// Zeile — dieselbe Diagnose wie beim Start.
    /// </summary>
    [LoggerMessage(EventId = 1056, Level = LogLevel.Debug,
        Message = "Beenden: {Step}")]
    public static partial void ExitStep(ILogger logger, string step);

    [LoggerMessage(EventId = 1058, Level = LogLevel.Warning,
        Message = "Beenden: {Service} brauchte {Milliseconds} ms")]
    public static partial void ExitStepSlow(ILogger logger, string service, long milliseconds);

    [LoggerMessage(EventId = 1059, Level = LogLevel.Warning,
        Message = "Beenden: {Service} liess sich nicht freigeben ({Reason})")]
    public static partial void ExitStepFailed(ILogger logger, string service, string reason);

    [LoggerMessage(EventId = 1057, Level = LogLevel.Warning,
        Message = "Beenden hat drei Sekunden nicht genuegt — der Prozess wird hart beendet. "
            + "Die letzte 'Beenden:'-Zeile davor sagt, wie weit es kam")]
    public static partial void ExitForced(ILogger logger);

    // ADR-053: ein Ereignisbehandler hat geworfen. Ein async-void-Handler hat
    // keinen Aufrufer mehr, der faengt — ohne diese Zeile waere der Absturz
    // die einzige Spur gewesen. Nur der Typ, nie der Text: eine Meldung traegt
    // oft genau das, was nicht ins Protokoll gehoert (Paragraph 21.2).
    [LoggerMessage(EventId = 1060, Level = LogLevel.Warning,
        Message = "Der Ereignisbehandler {Handler} hat eine Ausnahme ausgeloest "
            + "({ExceptionType}). nipp laeuft weiter.")]
    public static partial void HandlerFailed(ILogger logger, string handler, string exceptionType);

    // W1.1: der Rueckfall beim Klingeln, wenn keine Benachrichtigung
    // durchkommt. Eigene Zeile und nicht ShownByFallback: die beiden Wege
    // unterscheiden sich genau darin, ob der Fokus wechselt, und das ist die
    // Frage, wegen der es die Zeile gibt.
    [LoggerMessage(EventId = 1061, Level = LogLevel.Information,
        Message = "Fenster beim Klingeln gezeigt, ohne den Fokus zu nehmen (sichtbar: {Visible}) "
            + "— es kommt keine Benachrichtigung durch")]
    public static partial void ShownWithoutFocus(ILogger logger, bool visible);

    // W1.7: die Nachrichtenschlange des Fensters hat die Aktion abgelehnt.
    // Das heisst fast immer: es wird gerade heruntergefahren. Ohne diese
    // Zeile verfaellt sie lautlos — und «Beenden tut nichts» saehe genau aus
    // wie T134, den dieses Projekt schon einmal fuenf Tage gesucht hat.
    [LoggerMessage(EventId = 1062, Level = LogLevel.Warning,
        Message = "«{Was}» konnte nicht an den UI-Thread gereicht werden — die "
            + "Nachrichtenschlange ist zu. Laeuft nipp gerade herunter?")]
    public static partial void DispatcherRejected(ILogger logger, string was);
}
