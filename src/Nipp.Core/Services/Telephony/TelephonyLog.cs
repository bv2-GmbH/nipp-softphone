using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Protokollmeldungen der Telefonie als quellgenerierte Delegaten (CA1848).
///
/// Hier ist das kein Formalismus: der Iterate-Timer läuft mit 20 ms (§6), und
/// bei Protokollstufe „Debug" (§9.6) entstehen im Gespräch sehr viele
/// Meldungen. Die Erweiterungsmethoden würden dabei jedes Argument boxen und
/// die Vorlage jedes Mal neu formatieren.
/// </summary>
internal static partial class TelephonyLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Linphone SDK geladen: Version {Version}, {Grammars} Grammatiken, {Plugins} Plugins")]
    public static partial void SdkLoaded(ILogger logger, string? version, int grammars, int plugins);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Keine belr-Grammatiken unter {Directory}. Ein Core-Start würde mit "
            + "'Unable to load VCARD grammar' abbrechen — der Post-Build-Schritt hat nicht "
            + "gegriffen (siehe build/Linphone.Sdk.targets).")]
    public static partial void GrammarsMissing(ILogger logger, string directory);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Keine Mediastreamer-Plugins unter {Directory}. Ohne libmswasapi.dll gibt es "
            + "kein Audio, und die Fehlermeldung sagt das nicht.")]
    public static partial void PluginsMissing(ILogger logger, string directory);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Linphone SDK nicht ladbar ({ExceptionType}). Zu prüfen: sind alle nativen DLLs "
            + "im Ausgabeverzeichnis, und ist der Prozess x64? Mit 'dumpbin /dependents' eingrenzen.")]
    public static partial void SdkLoadFailed(ILogger logger, Exception exception, string exceptionType);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information,
        Message = "Core gestartet, GlobalState = {State}")]
    public static partial void CoreStarted(ILogger logger, string state);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Information,
        Message = "Core gestoppt")]
    public static partial void CoreStopped(ILogger logger);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug,
        Message = "GlobalState -> {State} ({Message})")]
    public static partial void GlobalStateChanged(ILogger logger, string state, string message);

    [LoggerMessage(EventId = 2020, Level = LogLevel.Information,
        Message = "Konto wird angemeldet: {Account}")]
    public static partial void AccountRegistering(ILogger logger, string account);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Information,
        Message = "Registrierung {Status} fuer {Identity}")]
    public static partial void RegistrationChanged(ILogger logger, string status, string identity);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Error,
        Message = "{Explanation}")]
    public static partial void RegistrationFailed(ILogger logger, string explanation);

    [LoggerMessage(EventId = 2024, Level = LogLevel.Debug,
        Message = "Registrierung von {Identity} fehlgeschlagen — das Konto ist in nipp nicht "
            + "eingerichtet. Das SDK stellt beim Start gespeicherte Konten wieder her; dieses "
            + "wird gleich ersetzt. Kein Alarm, der wirksame Zustand ist in Ordnung.")]
    public static partial void StaleAccountIgnored(ILogger logger, string identity);

    [LoggerMessage(EventId = 2030, Level = LogLevel.Information,
        Message = "Anruf {Handle} an {Destination} aufgebaut")]
    public static partial void CallPlaced(ILogger logger, string handle, string destination);

    [LoggerMessage(EventId = 2031, Level = LogLevel.Information,
        Message = "Eingehender Anruf {Handle} von {Number}")]
    public static partial void IncomingCall(ILogger logger, string handle, string number);

    [LoggerMessage(EventId = 2032, Level = LogLevel.Warning,
        Message = "Eingehender Anruf von {Number} abgelehnt: schon zwei Gespraeche offen")]
    public static partial void IncomingCallRejected(ILogger logger, string number);

    [LoggerMessage(EventId = 2033, Level = LogLevel.Debug,
        Message = "Anruf {Handle}: {Previous} -> {Current}")]
    public static partial void CallStateChanged(ILogger logger, string handle, string previous, string current);

    // W1.2: die Vorlage hiess bis zum 13.09.2026 «weitergeleitet» und wurde
    // geschrieben, BEVOR die Anlage geantwortet hatte. Sie heisst jetzt
    // «angestossen», und ob es geklappt hat, sagt die Zeile darunter.
    [LoggerMessage(EventId = 2034, Level = LogLevel.Information,
        Message = "Weiterleitung von {Handle} an {Destination} angestossen ({Mode})")]
    public static partial void CallTransferStarted(
        ILogger logger, string handle, string destination, string mode);

    [LoggerMessage(EventId = 2112, Level = LogLevel.Information,
        Message = "Weiterleitung von {Handle} angenommen — die Uebergabe steht")]
    public static partial void CallTransferred(ILogger logger, string handle);

    [LoggerMessage(EventId = 2035, Level = LogLevel.Information,
        Message = "Aufnahme fuer {Handle} gestartet: {Path}")]
    public static partial void RecordingStarted(ILogger logger, string handle, string path);

    [LoggerMessage(EventId = 2040, Level = LogLevel.Information,
        Message = "Netzwerk erreichbar: {Reachable}")]
    public static partial void NetworkReachabilityChanged(ILogger logger, bool reachable);

    [LoggerMessage(EventId = 2041, Level = LogLevel.Information,
        Message = "Audiogeraete geaendert, jetzt {Count}")]
    public static partial void AudioDevicesChanged(ILogger logger, int count);

    [LoggerMessage(EventId = 2050, Level = LogLevel.Information,
        Message = "Codec {MimeType} {Enabled}")]
    public static partial void PayloadTypeChanged(ILogger logger, string mimeType, bool enabled);

    [LoggerMessage(EventId = 2051, Level = LogLevel.Warning,
        Message = "Codec {MimeType} ist im SDK nicht vorhanden")]
    public static partial void PayloadTypeMissing(ILogger logger, string mimeType);

    [LoggerMessage(EventId = 2023, Level = LogLevel.Information,
        Message = "Vorhandenes Konto {Identity} samt Zugangsdaten entfernt, bevor es neu angelegt wird")]
    public static partial void AccountReplaced(ILogger logger, string identity);

    [LoggerMessage(EventId = 2036, Level = LogLevel.Information,
        Message = "Aufnahme fuer {Handle} beendet: {Path} ({Bytes:N0} Bytes)")]
    public static partial void RecordingStopped(ILogger logger, string handle, string path, long bytes);

    [LoggerMessage(EventId = 2037, Level = LogLevel.Error,
        Message = "Aufnahme fuer {Handle} hat keine Daten geschrieben: {Path} fehlt oder ist leer. "
            + "Die Anzeige behauptete eine Aufnahme, die es nicht gibt.")]
    public static partial void RecordingEmpty(ILogger logger, string handle, string path);

    [LoggerMessage(EventId = 2038, Level = LogLevel.Error,
        Message = "Aufnahmepfad fuer {Handle} wurde vom SDK nicht uebernommen: {Path}. "
            + "Es wird nicht aufgezeichnet.")]
    public static partial void RecordingPathRejected(ILogger logger, string handle, string path);

    [LoggerMessage(EventId = 2039, Level = LogLevel.Information,
        Message = "Gespraech {Handle} gehalten, weil ein zweiter Anruf aufgebaut wird")]
    public static partial void CallPausedForSecond(ILogger logger, string handle);

    [LoggerMessage(EventId = 2045, Level = LogLevel.Warning,
        Message = "Gespraech {Handle} liess sich nicht halten: {Reason}")]
    public static partial void CallPauseFailed(ILogger logger, string handle, string reason);

    [LoggerMessage(EventId = 2044, Level = LogLevel.Warning,
        Message = "Ein dritter Anruf liess sich nicht ablehnen: {Reason}")]
    public static partial void DeclineFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2043, Level = LogLevel.Warning,
        Message = "Gespraech {Handle} liess sich nach einem gescheiterten zweiten Anruf nicht "
            + "fortsetzen: {Reason}. Es bleibt gehalten — im Zweifel neu aufbauen.")]
    public static partial void CallResumeFailed(ILogger logger, string handle, string reason);

    /// <summary>
    /// Das letzte verbliebene Gespraech wurde von selbst zurueckgeholt (T322).
    ///
    /// <para>Es steht im Protokoll, weil der Benutzer es <b>nicht</b> selbst
    /// getan hat: wer hinterher fragt, warum ein gehaltenes Gespraech ploetzlich
    /// wieder lief, findet hier die Antwort.</para>
    /// </summary>
    [LoggerMessage(EventId = 2045, Level = LogLevel.Information,
        Message = "Gespraech {Handle} war allein auf Halten und wurde zurueckgeholt (Paragraph 8.2)")]
    public static partial void LastCallResumed(ILogger logger, string handle);

    [LoggerMessage(EventId = 2060, Level = LogLevel.Information,
        Message = "Echo-Kalibrierung gestartet, dauert etwa 15 Sekunden")]
    public static partial void EchoCalibrationStarted(ILogger logger);

    [LoggerMessage(EventId = 2061, Level = LogLevel.Information,
        Message = "Echo-Kalibrierung abgeschlossen: {Milliseconds} ms")]
    public static partial void EchoCalibrationDone(ILogger logger, int milliseconds);

    [LoggerMessage(EventId = 2062, Level = LogLevel.Warning,
        Message = "Echo-Kalibrierung lieferte kein Ergebnis. Mikrofon und Lautsprecher pruefen.")]
    public static partial void EchoCalibrationFailed(ILogger logger);

    [LoggerMessage(EventId = 2070, Level = LogLevel.Information,
        Message = "Besetztlampenfeld: {Count} Nebenstellen abonniert")]
    public static partial void PresenceWatchStarted(ILogger logger, int count);

    [LoggerMessage(EventId = 2071, Level = LogLevel.Information,
        Message = "Besetztlampenfeld abgeschaltet, alle Abonnements beendet")]
    public static partial void PresenceWatchCleared(ILogger logger);

    [LoggerMessage(EventId = 2072, Level = LogLevel.Warning,
        Message = "Besetztlampenfeld nicht moeglich: {Reason}")]
    public static partial void PresenceWatchFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2073, Level = LogLevel.Warning,
        Message = "Eine Nebenstellenadresse liess sich nicht deuten und wird nicht beobachtet")]
    public static partial void PresenceAddressRejected(ILogger logger);

    [LoggerMessage(EventId = 2074, Level = LogLevel.Information,
        Message = "Unbekannter Registrierungszustand '{State}' vom SDK. Wird als Zwischenzustand "
            + "behandelt — falls das oefter auftritt, gehoert er in MapRegistrationStatus.")]
    public static partial void UnknownRegistrationState(ILogger logger, string state);

    [LoggerMessage(EventId = 2080, Level = LogLevel.Warning,
        Message = "Audiogeraet verschwunden: {Devices}. nipp faellt auf das Standardgeraet zurueck.")]
    public static partial void AudioDeviceLost(ILogger logger, string devices);

    [LoggerMessage(EventId = 2081, Level = LogLevel.Warning,
        Message = "Umstellen auf ein anderes Audiogeraet fehlgeschlagen: {Reason}")]
    public static partial void AudioDeviceSwitchFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2082, Level = LogLevel.Information,
        Message = "{Count} laufende Gespraeche auf das aktuelle Audiogeraet umgestellt")]
    public static partial void CallsRetargeted(ILogger logger, int count);

    [LoggerMessage(EventId = 2090, Level = LogLevel.Information,
        Message = "Abonnement '{Event}' fuer {Resource}: {State}")]
    public static partial void SubscriptionState(
        ILogger logger, string @event, string resource, string state);

    [LoggerMessage(EventId = 2091, Level = LogLevel.Warning,
        Message = "Abonnement '{Event}' fuer {Resource} abgelehnt: {Reason} (SIP {Code} {Phrase}). {Hint}")]
    public static partial void SubscriptionFailed(
        ILogger logger, string @event, string resource, string reason, int code, string phrase, string hint);

    [LoggerMessage(EventId = 2092, Level = LogLevel.Information,
        Message = "NOTIFY '{Event}' von {Resource}, {Bytes} Bytes")]
    public static partial void NotifyReceived(
        ILogger logger, string @event, string resource, int bytes);

    [LoggerMessage(EventId = 2093, Level = LogLevel.Information,
        Message = "Abonnement 'presence' fuer {Resource}: {State}. {Hint}")]
    public static partial void PresenceSubscriptionState(
        ILogger logger, string resource, string state, string hint);

    [LoggerMessage(EventId = 2094, Level = LogLevel.Warning,
        Message = "Abonnement 'presence' fuer {Resource} abgelehnt. {Hint}")]
    public static partial void PresenceSubscriptionFailed(
        ILogger logger, string resource, string hint);

    [LoggerMessage(EventId = 2075, Level = LogLevel.Information,
        Message = "Besetztlampenfeld: {Total} Nebenstellen abonniert ({Added} neu)")]
    public static partial void PresenceWatchSynchronized(ILogger logger, int total, int added);

    [LoggerMessage(EventId = 2076, Level = LogLevel.Information,
        Message = "Besetztlampenfeld: gespeicherte Liste aus der SDK-Datenbank uebernommen, {Count} Nebenstellen")]
    public static partial void PresenceListRestored(ILogger logger, int count);

    [LoggerMessage(EventId = 2077, Level = LogLevel.Debug,
        Message = "Konto zu einem eingehenden Anruf nicht ermittelbar ({Reason}) — es gilt das Standardkonto")]
    public static partial void AccountLookupFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2078, Level = LogLevel.Warning,
        Message = "Besetztlampenfeld: Fehler beim Abgleich der Abonnements")]
    public static partial void PresenceWatchCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2079, Level = LogLevel.Information,
        Message = "Abmeldung beim Herunterfahren nach {Iterations} Durchlaeufen: {State}")]
    public static partial void ShutdownUnregistered(ILogger logger, int iterations, string state);

    [LoggerMessage(EventId = 2085, Level = LogLevel.Information,
        Message = "TLS-Wurzelzertifikate aus dem Windows-Speicher bereitgestellt: {Count} Zertifikate nach {Path}")]
    public static partial void RootCaExported(ILogger logger, int count, string path);

    [LoggerMessage(EventId = 2086, Level = LogLevel.Warning,
        Message = "TLS-Wurzelzertifikate liessen sich nicht bereitstellen ({Reason}). "
            + "Eine TLS-Verbindung zur Anlage kann daran scheitern; das SDK braucht die Zertifikate als Datei.")]
    public static partial void RootCaFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2087, Level = LogLevel.Information,
        Message = "Zertifikatspruefung: {Enabled}")]
    public static partial void CertificateVerification(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 2088, Level = LogLevel.Information,
        Message = "Anruf {Handle} umgeleitet auf {Destination}")]
    public static partial void CallRedirected(ILogger logger, string handle, string destination);

    [LoggerMessage(EventId = 2089, Level = LogLevel.Information,
        Message = "Anruf {Handle} automatisch angenommen (Paragraph 9.6)")]
    public static partial void CallAutoAnswered(ILogger logger, string handle);

    [LoggerMessage(EventId = 2095, Level = LogLevel.Warning,
        Message = "Automatisches Annehmen fehlgeschlagen ({Reason}) — der Anruf klingelt weiter")]
    public static partial void AutoAnswerFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2096, Level = LogLevel.Warning,
        Message = "Aufnahmeordner {Path} ist nicht verwendbar ({Reason}). Das Gespraech laeuft, "
            + "aufnehmen liesse sich dabei nicht.")]
    public static partial void RecordingDirectoryUnusable(ILogger logger, string path, string reason);

    // Der eigene Rufton (§9.4, RingbackWatch). Absichtlich Information und
    // nicht Debug: wenn beim Waehlen wieder nichts zu hoeren ist, ist die
    // erste Frage, ob nipp ueberhaupt angefangen hat und auf welcher Karte.
    // <b>Mit dem gemessenen Grund, nicht mit einer Annahme.</b> Bis zum
    // 10.09.2026 behauptete diese Zeile "die Gegenstelle schickt Early Media
    // ohne Audio" — und lag zweimal falsch: der Strom lief, nipp konnte ihn
    // nur nicht sehen (Sekundenmittel). Eine Protokollzeile, die eine Ursache
    // behauptet, die sie nicht gemessen hat, ist schlechter als keine.
    [LoggerMessage(EventId = 2097, Level = LogLevel.Information,
        Message = "Eigener Rufton gestartet auf {Card} — {Reason} (Empfang {Kbit} kbit/s, "
            + "Pegel {Volume} dBm0, Pakete {Packets}, gewartet {WaitedMs} ms, Paragraph 9.4)")]
    public static partial void RingbackStarted(
        ILogger logger,
        string card,
        string reason,
        string kbit,
        string volume,
        string packets,
        int waitedMs);

    // Mit den Messwerten, die zum Aufhoeren gefuehrt haben. Ohne sie stand am
    // 08.09.2026 nur "gestartet" und 402 ms spaeter "beendet" im Protokoll —
    // und keine Moeglichkeit zu sagen, warum. Die Spielzeit kam am 10.09.2026
    // dazu: 168 ms gegen 2282 ms ist der ganze Unterschied zwischen einem
    // Fehler und der Absicht.
    [LoggerMessage(EventId = 2098, Level = LogLevel.Information,
        Message = "Eigener Rufton beendet nach {PlayedMs} ms ({Reason}; Empfang {Kbit} kbit/s, "
            + "Pegel {Volume} dBm0, Early Media: {EarlyMedia})")]
    public static partial void RingbackStopped(
        ILogger logger,
        int playedMs,
        string reason,
        string kbit,
        string volume,
        string earlyMedia);

    [LoggerMessage(EventId = 2099, Level = LogLevel.Warning,
        Message = "Eigener Rufton nicht moeglich ({Reason}) — beim Waehlen bleibt es still, "
            + "das Gespraech selbst ist davon unberuehrt")]
    public static partial void RingbackFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
        Message = "Klingelton-Probe nicht moeglich ({Reason})")]
    public static partial void SoundPreviewFailed(ILogger logger, string reason);

    /// <summary>
    /// Der Anruf wurde angenommen — <b>vom SDK entgegengenommen</b>, nicht nur
    /// angeklickt.
    ///
    /// <para>Ohne diese Zeile war am 08.09.2026 nicht zu entscheiden, ob ein
    /// nicht zustande gekommenes Gespräch daran lag, dass niemand drückte,
    /// oder daran, dass der Druck verlorenging. Nur die Anrufkennung, keine
    /// Nummer (§21.2).</para>
    /// </summary>
    [LoggerMessage(EventId = 2101, Level = LogLevel.Information,
        Message = "Anruf {Call} angenommen")]
    public static partial void CallAccepted(ILogger logger, string call);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information,
        Message = "Anruf {Call} beendet oder abgelehnt (durch den Benutzer)")]
    public static partial void CallHungUp(ILogger logger, string call);

    // <b>Der Messwert, der die Frage direkt beantwortet.</b> Am 10.09.2026
    // musste er aus einem Sekundenmittel zurueckgerechnet werden, und genau
    // deshalb dauerte die Erklaerung zwei Stunden statt zehn Sekunden.
    [LoggerMessage(EventId = 2103, Level = LogLevel.Information,
        Message = "Audiostrom der Gegenstelle beginnt {Ms} ms nach dem Laeuten "
            + "({Packets} Pakete bisher)")]
    public static partial void RemoteAudioStarted(ILogger logger, int ms, string packets);

    // Debug, weil es je Anruf rund fuenfundzwanzig Zeilen sind. Ohne sie ist
    // eine Ruftonkadenz im Protokoll nicht sichtbar: der Pegel stand dort
    // bisher genau einmal, im Moment des Aufhoerens.
    [LoggerMessage(EventId = 2104, Level = LogLevel.Debug,
        Message = "Rufton-Takt: Empfang {Kbit} kbit/s, Pegel {Volume} dBm0, Pakete {Packets}, "
            + "Puffer {JitterMs} ms, Early Media {EarlyMedia}")]
    public static partial void RingbackTick(
        ILogger logger,
        string kbit,
        string volume,
        string packets,
        string jitterMs,
        bool earlyMedia);

    // <b>Welcher Weg spielt.</b> Beide Wege spielen dieselbe Datei — am Gehoer
    // sind sie nicht zu unterscheiden, und ohne diese Zeile war "das ist nicht
    // der Ton der Anlage" keinem der beiden zuzuordnen.
    [LoggerMessage(EventId = 2105, Level = LogLevel.Information,
        Message = "Rufton beim Laeuten: {Way}")]
    public static partial void RingbackWay(ILogger logger, string way);

    // ADR-053: ein Abonnent hat im SDK-Callback geworfen. Die Zeile steht auf
    // Warning und nicht auf Debug, weil sie sonst genau dann fehlt, wenn
    // jemand fragt, warum die Oberflaeche nicht nachgezogen hat. Der Name des
    // Callbacks ist das Einzige, was hinterher sagt, wo es geknallt hat — die
    // Aufrufliste ist an dieser Stelle wertlos, sie endet im nativen Rahmen.
    [LoggerMessage(EventId = 2106, Level = LogLevel.Warning,
        Message = "Ein Abonnent von {Callback} hat eine Ausnahme ausgeloest ({ExceptionType}): "
            + "{Reason}. Der Anruf laeuft weiter; die Oberflaeche kann an dieser Stelle "
            + "veraltet sein.")]
    public static partial void CallbackFailed(
        ILogger logger, string callback, string exceptionType, string reason);

    // ADR-053: ein Aufruf ins SDK ist fehlgeschlagen. Getrennt von
    // CallbackFailed, weil hier WIR den Fehler haben und nicht ein Abonnent —
    // und weil der Benutzer dazu eine Meldung bekommt, die Zeile also die
    // technische Haelfte derselben Geschichte ist.
    [LoggerMessage(EventId = 2107, Level = LogLevel.Warning,
        Message = "SDK-Aufruf {Operation} fuer Gespraech {Handle} fehlgeschlagen "
            + "({ExceptionType}): {Reason}")]
    public static partial void SdkCallFailed(
        ILogger logger, string operation, string handle, string exceptionType, string reason);

    // ADR-019, Nachtrag: der SIP-Port kommt jetzt an. Die Zeile ist die
    // Gegenprobe — bis zum 13.09.2026 war "mein Port wirkt nicht" nicht zu
    // beantworten, weil nirgends stand, worauf der Core lauscht. 0 heisst
    // "Standard des SDK".
    [LoggerMessage(EventId = 2108, Level = LogLevel.Information,
        Message = "Lauscht auf UDP {Udp}, TCP {Tcp}, TLS {Tls} (0 = Standard des SDK)")]
    public static partial void TransportPortsApplied(ILogger logger, int udp, int tcp, int tls);

    [LoggerMessage(EventId = 2109, Level = LogLevel.Warning,
        Message = "Der SIP-Port {Port} liess sich nicht setzen ({Reason}). nipp lauscht auf "
            + "dem Standardport — vermutlich belegt ihn ein anderes Programm.")]
    public static partial void TransportPortsFailed(ILogger logger, int port, string reason);

    // W1.2: der Verlauf einer Weiterleitung. Auf Debug, weil es mehrere
    // Zwischenzustaende gibt; das Ergebnis selbst steht in der Meldung an den
    // Benutzer.
    [LoggerMessage(EventId = 2110, Level = LogLevel.Debug,
        Message = "Weiterleitung von {Handle}: {State}")]
    public static partial void TransferState(ILogger logger, string handle, string state);

    [LoggerMessage(EventId = 2111, Level = LogLevel.Warning,
        Message = "Die Weiterleitung von {Handle} ist gescheitert. Das eigene Gespraech laeuft "
            + "je nach Anlage weiter.")]
    public static partial void TransferFailed(ILogger logger, string handle);

    // ADR-055: «Nicht stoeren». Auf Information, weil «es hat nicht
    // geklingelt» sonst nicht zu beantworten waere — und das ist genau die
    // Frage, die ein stummer Klingelton erzeugt.
    [LoggerMessage(EventId = 2113, Level = LogLevel.Information,
        Message = "Nicht stoeren: {Zustand}. Anrufe kommen weiterhin an, nur der Klingelton "
            + "schweigt.")]
    public static partial void DoNotDisturbChanged(ILogger logger, string zustand);
}
