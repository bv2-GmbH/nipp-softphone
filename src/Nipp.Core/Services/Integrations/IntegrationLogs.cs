using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Integrations;

/// <summary>
/// Protokollmeldungen rund um die Anbindung externer Systeme (§21.2).
///
/// <b>Keine Rufnummern, keine Suchtexte, keine Namen, keine Adressen mit
/// Parametern, keine Antwortinhalte, keine Zugangsdaten.</b> Protokolliert
/// wird, was zur Diagnose taugt und niemanden betrifft: welche Quelle, welcher
/// Zustand, welcher Status, wie lange, wie viele Felder.
///
/// Die Regel ist dieselbe wie bei <c>ContactLog</c> — gezählt wird, nicht
/// aufgezählt —, hier aber schärfer: ein Log mit Rufnummern und den Namen
/// dahinter ist ein Bewegungsprofil. <c>IntegrationBoundaryTests</c> prüft die
/// Platzhalternamen in dieser Datei.
///
/// EventId-Bereiche: 3600 Connector, 3700 Anruferkontext, 3800 Kontaktsuche,
/// 3900 Karten. Die Bereiche bis 3519 sind belegt.
/// </summary>
internal static partial class ConnectorLog
{
    [LoggerMessage(EventId = 3600, Level = LogLevel.Information,
        Message = "Integrationen geladen: {Sources} Quellen, davon {Active} aktiv")]
    public static partial void ConfigLoaded(ILogger logger, int sources, int active);

    [LoggerMessage(EventId = 3601, Level = LogLevel.Information,
        Message = "Keine Integrationskonfiguration unter {Path} — es sind keine externen Quellen eingerichtet")]
    public static partial void NoConfig(ILogger logger, string path);

    [LoggerMessage(EventId = 3602, Level = LogLevel.Error,
        Message = "Die Integrationskonfiguration in {Path} ist nicht lesbar: {Reason}. "
            + "Es sind keine externen Quellen aktiv; das Telefonieren ist davon nicht betroffen.")]
    public static partial void ConfigUnreadable(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 3603, Level = LogLevel.Information,
        Message = "Beschaedigte Integrationskonfiguration aufgehoben als {Path}")]
    public static partial void ConfigPreserved(ILogger logger, string path);

    [LoggerMessage(EventId = 3604, Level = LogLevel.Warning,
        Message = "Quelle {Source} ist nicht brauchbar eingerichtet und bleibt aus: {Reason}")]
    public static partial void SourceInvalid(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 3605, Level = LogLevel.Information,
        Message = "Integrationskonfiguration gespeichert nach {Path}")]
    public static partial void ConfigSaved(ILogger logger, string path);

    [LoggerMessage(EventId = 3610, Level = LogLevel.Debug,
        Message = "Quelle {Source} hat geantwortet: HTTP {Status} in {Milliseconds} ms")]
    public static partial void SourceAnswered(ILogger logger, string source, int status, int milliseconds);

    [LoggerMessage(EventId = 3611, Level = LogLevel.Warning,
        Message = "Quelle {Source} antwortet nicht innerhalb von {Milliseconds} ms")]
    public static partial void SourceTimedOut(ILogger logger, string source, int milliseconds);

    [LoggerMessage(EventId = 3612, Level = LogLevel.Warning,
        Message = "Quelle {Source} nicht erreichbar: {Reason}")]
    public static partial void SourceFailed(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 3613, Level = LogLevel.Warning,
        Message = "Quelle {Source} lehnt die Anfrage ab: HTTP {Status}")]
    public static partial void SourceRejected(ILogger logger, string source, int status);

    [LoggerMessage(EventId = 3614, Level = LogLevel.Warning,
        Message = "Antwort von {Source} ueberschreitet {Limit} Bytes und wurde verworfen")]
    public static partial void ResponseTooLarge(ILogger logger, string source, int limit);

    [LoggerMessage(EventId = 3615, Level = LogLevel.Warning,
        Message = "Antwort von {Source} ist kein JSON")]
    public static partial void ResponseNotJson(ILogger logger, string source);

    [LoggerMessage(EventId = 3616, Level = LogLevel.Debug,
        Message = "Quelle {Source} uebersprungen")]
    public static partial void SourceSkipped(ILogger logger, string source);

    [LoggerMessage(EventId = 3617, Level = LogLevel.Debug,
        Message = "Quelle {Source}: {Fields} Felder gemappt, {Problems} Hinweise")]
    public static partial void Mapped(ILogger logger, string source, int fields, int problems);

    [LoggerMessage(EventId = 3620, Level = LogLevel.Information,
        Message = "Integrationskonfiguration von {Host} uebernommen: {Sources} Quellen")]
    public static partial void ProvisioningApplied(ILogger logger, string host, int sources);

    [LoggerMessage(EventId = 3621, Level = LogLevel.Warning,
        Message = "Integrationskonfiguration von {Host} nicht abrufbar: {Reason}. "
            + "Es gilt weiter, was zuletzt gespeichert wurde.")]
    public static partial void ProvisioningFailed(ILogger logger, string host, string reason);

    [LoggerMessage(EventId = 3622, Level = LogLevel.Warning,
        Message = "Integrationskonfiguration von {Host} ist unbrauchbar und wurde verworfen")]
    public static partial void ProvisioningBroken(ILogger logger, string host);

    [LoggerMessage(EventId = 3623, Level = LogLevel.Warning,
        Message = "Die Adresse der Integrationskonfiguration wurde abgelehnt: nur https ist erlaubt")]
    public static partial void ProvisioningRejected(ILogger logger);

    [LoggerMessage(EventId = 3606, Level = LogLevel.Warning,
        Message = "Ein Empfaenger der Integrationskonfiguration hat eine Ausnahme geworfen "
            + "({Reason}). Sie wurde abgefangen — nipp telefoniert unveraendert weiter.")]
    public static partial void ConfigNotifyFailed(ILogger logger, string reason);
}

/// <summary>
/// Der Anruferkontext (§21.1).
///
/// <b>Nie die Rufnummer und nie ein Feldinhalt.</b> Ein Softphone, das
/// protokolliert, wer angerufen hat und was das CRM dazu wusste, legt ein
/// Bewegungsprofil mit Kundendaten an. Protokolliert wird die Quelle, ihr
/// Zustand und die Dauer.
/// </summary>
internal static partial class CallerContextLog
{
    [LoggerMessage(EventId = 3700, Level = LogLevel.Debug,
        Message = "Anruferkontext: {Sources} Quellen werden gefragt")]
    public static partial void LookupStarted(ILogger logger, int sources);

    [LoggerMessage(EventId = 3701, Level = LogLevel.Debug,
        Message = "Anruferkontext {Source}: {State} nach {Milliseconds} ms")]
    public static partial void SourceAnswered(ILogger logger, string source, string state, int milliseconds);

    [LoggerMessage(EventId = 3702, Level = LogLevel.Warning,
        Message = "Anruferkontext {Source} antwortet nicht rechtzeitig")]
    public static partial void SourceTimedOut(ILogger logger, string source);

    [LoggerMessage(EventId = 3703, Level = LogLevel.Warning,
        Message = "Anruferkontext {Source} fehlgeschlagen: {Reason}")]
    public static partial void SourceFailed(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 3704, Level = LogLevel.Warning,
        Message = "Der Anruferkontext liess sich nicht starten: {Reason}. "
            + "Das Gespraech ist davon nicht betroffen.")]
    public static partial void StartFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3705, Level = LogLevel.Warning,
        Message = "Ein Empfaenger des Anruferkontexts hat eine Ausnahme geworfen ({Reason}). "
            + "Sie wurde abgefangen — das Gespraech laeuft unveraendert weiter.")]
    public static partial void PublishFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3706, Level = LogLevel.Information,
        Message = "Die Integrationen wurden geaendert — der Zwischenspeicher des "
            + "Anruferkontexts wurde geleert")]
    public static partial void CacheCleared(ILogger logger);

    // ADR-053: ein Empfaenger des Anruferkontexts hat geworfen. Nur der Typ —
    // die Meldung einer Ausnahme aus einer fremden Vorlage traegt oft genau
    // das, was nicht ins Protokoll gehoert (Paragraph 21.2).
    [LoggerMessage(EventId = 3708, Level = LogLevel.Warning,
        Message = "Ein Empfaenger des Anruferkontexts hat eine Ausnahme ausgeloest "
            + "({ExceptionType}). Der Anruf laeuft weiter; eine Karte kann leer bleiben.")]
    public static partial void ContextDeliveryFailed(ILogger logger, string exceptionType);
}

/// <summary>
/// Die Kontaktsuche über mehrere Quellen (§21.1).
///
/// <b>Nie der Suchtext.</b> Wonach jemand sucht, ist so aussagekräftig wie
/// die Nummer, die er anruft — dieselbe Regel wie oben. Protokolliert wird
/// die Quelle, ihr Zustand und die Anzahl Treffer.
/// </summary>
internal static partial class ContactSearchLog
{
    [LoggerMessage(EventId = 3800, Level = LogLevel.Debug,
        Message = "Suche in {Source}: {Count} Treffer")]
    public static partial void SourceAnswered(ILogger logger, string source, int count);

    [LoggerMessage(EventId = 3801, Level = LogLevel.Warning,
        Message = "Suche in {Source} antwortet nicht rechtzeitig")]
    public static partial void SourceTimedOut(ILogger logger, string source);

    [LoggerMessage(EventId = 3802, Level = LogLevel.Warning,
        Message = "Suche in {Source} fehlgeschlagen: {Reason}")]
    public static partial void SourceFailed(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 3803, Level = LogLevel.Warning,
        Message = "Die Suche ist fehlgeschlagen: {Reason}. Die Kontaktliste bleibt benutzbar.")]
    public static partial void SearchFailed(ILogger logger, string reason);
}

/// <summary>
/// Die Karten (§21, ADR-032).
///
/// <b>Nie ein Feldinhalt.</b> Eine Karte trägt genau die Daten, die nicht ins
/// Protokoll gehören — Name, Firma, Gesprächsinhalt. Protokolliert wird die
/// Kennung der Karte und die Anzahl Befunde, nie ein Wert und nie ein
/// aufgelöster Ausdruck.
/// </summary>
internal static partial class CardLog
{
    [LoggerMessage(EventId = 3900, Level = LogLevel.Warning,
        Message = "Karte {Card} hat {Problems} Befunde und wird nicht benutzt; "
            + "es gilt die mitgelieferte Karte dieser Art")]
    public static partial void CardBroken(ILogger logger, string card, int problems);
}

/// <summary>
/// Der Karten-Designer (K4). Eigene Klasse, weil sie aus ViewModels gerufen
/// wird und nicht aus den Diensten -- und weil hier dieselbe Regel gilt: kein
/// Feldinhalt, keine Beschriftung eines Werts, nur Struktur und Anzahl.
/// </summary>
internal static partial class CardDesignerLog
{
    [LoggerMessage(EventId = 3910, Level = LogLevel.Information,
        Message = "Karte {Card} der Art {Kind} gespeichert: {Sections} Abschnitte, {Elements} Bausteine")]
    public static partial void Saved(ILogger logger, string card, string kind, int sections, int elements);

    [LoggerMessage(EventId = 3911, Level = LogLevel.Information,
        Message = "Aufbau der Karte {From} in die Karte {Kind} uebernommen")]
    public static partial void CopiedFrom(ILogger logger, string from, string kind);
}
