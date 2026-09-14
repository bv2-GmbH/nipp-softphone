using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Telephony;

// Bewusst in einer eigenen Datei OHNE "using Linphone".
//
// Namensfalle, die dritte (siehe docs/sdk-api-notes.md): Linphone.LogLevel und
// Microsoft.Extensions.Logging.LogLevel heissen gleich. In einer Datei mit
// "using Linphone" kann der Quellgenerator fuer [LoggerMessage] die Attribute
// nicht mehr aufloesen — die Fehlermeldung spricht dann von fehlenden
// Implementierungen partieller Methoden und verschweigt die Ursache.
//
// Die anderen Log-Klassen (TelephonyLog, ConnectivityLog) liegen aus demselben
// Grund getrennt.

internal static partial class SettingsApplierLog
{
    [LoggerMessage(EventId = 2600, Level = LogLevel.Information,
        Message = "Einstellungen auf den laufenden Core uebertragen")]
    public static partial void Applied(ILogger logger);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Warning,
        Message = "Medienverschluesselung {Mode} wird vom SDK nicht unterstuetzt — Einstellung ignoriert")]
    public static partial void EncryptionUnsupported(ILogger logger, string mode);

    [LoggerMessage(EventId = 2602, Level = LogLevel.Warning,
        Message = "Verschluesselung wird ERZWUNGEN. Gespraeche zu Gegenstellen ohne SRTP scheitern "
            + "damit hart (Paragraph 14.7). Die aktuelle Anlage von bv2 bietet keine "
            + "Verschluesselung an (ADR-007).")]
    public static partial void EncryptionMandatoryWarning(ILogger logger);

    [LoggerMessage(EventId = 2603, Level = LogLevel.Information,
        Message = "Audiogeraet {DeviceId} ist nicht mehr vorhanden — es bleibt beim Standardgeraet")]
    public static partial void DeviceMissing(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 2604, Level = LogLevel.Information,
        Message = "Codecs aktiv, in dieser Reihenfolge: {Codecs}")]
    public static partial void CodecsApplied(ILogger logger, string codecs);

    [LoggerMessage(EventId = 2605, Level = LogLevel.Error,
        Message = "Codec-Einstellung abgelehnt: PCMA und PCMU sind beide aus. Die Verhandlung "
            + "wuerde mit vielen Trunks scheitern (Paragraph 9.5).")]
    public static partial void CodecsInvalid(ILogger logger);

    [LoggerMessage(EventId = 2606, Level = LogLevel.Debug,
        Message = "Payload-Type-Nummern werden vom SDK dynamisch vergeben — die festen Werte "
            + "aus Paragraph 9.5 sind nicht setzbar")]
    public static partial void PayloadNumbersDynamic(ILogger logger);

    [LoggerMessage(EventId = 2607, Level = LogLevel.Information,
        Message = "Klingelton: {Path}")]
    public static partial void RingtoneApplied(ILogger logger, string path);

    // Keine Backslashes in einer LoggerMessage-Vorlage: der Quellgenerator
    // uebernimmt den Text unveraendert in ein Zeichenfolgenliteral, und aus
    // "\s" wird dort eine nicht erkannte Escapesequenz (CS1009). Deshalb
    // Schraegstriche — Windows versteht sie in Pfaden ohnehin.
    [LoggerMessage(EventId = 2608, Level = LogLevel.Warning,
        Message = "Kein Klingelton gefunden — eingehende Anrufe klingeln am Geraet nicht. "
            + "Erwartet wird Assets/Sounds/nipp-ring.wav im Ausgabeverzeichnis "
            + "(erzeugt von tools/Build-Sounds.py), als Rueckfall "
            + "share/sounds/linphone/rings/oldphone-mono.wav aus dem SDK-Paket.")]
    public static partial void RingtoneMissing(ILogger logger);

    [LoggerMessage(EventId = 2609, Level = LogLevel.Information,
        Message = "Zertifikatspruefung fuer TLS: {Enabled}")]
    public static partial void CertificateVerification(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 2610, Level = LogLevel.Information,
        Message = "Audiowahl neu uebertragen (Geraetewechsel) — Netzwerk, NAT und Codecs "
            + "bleiben unberuehrt")]
    public static partial void AudioApplied(ILogger logger);

    [LoggerMessage(EventId = 2611, Level = LogLevel.Information,
        Message = "Soundkarte fuer Toene (Freizeichen, Tastentoene): {Card}")]
    public static partial void ToneCard(ILogger logger, string card);

    [LoggerMessage(EventId = 2612, Level = LogLevel.Information,
        Message = "Soundkarte fuer Toene nachgezogen auf: {Card}")]
    public static partial void ToneCardChanged(ILogger logger, string card);

    [LoggerMessage(EventId = 2613, Level = LogLevel.Warning,
        Message = "Soundkarte fuer Toene konnte nicht gesetzt werden ({Reason}) - "
                  + "das Freizeichen bleibt womoeglich stumm")]
    public static partial void ToneCardFailed(ILogger logger, string reason);

    // Eine abgelehnte Karte ist noch kein Fehler: der Setter der alten API
    // wirft bei jedem Namen, den er nicht kennt, und genau darum gibt es
    // mehrere Kandidaten. Erst wenn alle scheitern, kommt 2613.
    [LoggerMessage(EventId = 2614, Level = LogLevel.Debug,
        Message = "Soundkarte {Card} fuer Toene abgelehnt ({Reason}) - naechster Kandidat")]
    public static partial void ToneCardRejected(ILogger logger, string card, string reason);

    [LoggerMessage(EventId = 2615, Level = LogLevel.Information,
        Message = "Rufton beim Waehlen: {Path}")]
    public static partial void RingbackApplied(ILogger logger, string path);

    [LoggerMessage(EventId = 2616, Level = LogLevel.Warning,
        Message = "Kein Rufton beim Waehlen gefunden. Erwartet wird "
            + "Assets/Sounds/nipp-ringback.wav im Ausgabeverzeichnis (erzeugt von "
            + "tools/Build-Sounds.py), als Rueckfall share/sounds/linphone/ringback.wav "
            + "aus dem SDK-Paket.")]
    public static partial void RingbackMissing(ILogger logger);
}
