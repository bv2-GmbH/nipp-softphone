using System.Text.Json.Serialization;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Alle Einstellungen von nipp — die Tabellen aus §9 als Code (AP5.1).
///
/// Typisiert statt generisch als Schlüssel-Wert-Ablage: so steht jeder
/// Standardwert genau einmal, die Serialisierung ist testbar, und ein
/// Tippfehler in einem Namen ist ein Compilerfehler statt eines stillen
/// Fehlverhaltens im Feld.
///
/// <b>Wo die Werte von §9 abweichen, steht die Begründung am Feld.</b> Die
/// Spezifikation wurde an mehreren Stellen von der Wirklichkeit des SDK
/// eingeholt (docs/sdk-api-notes.md, ADR-006, ADR-007).
/// </summary>
public sealed record NippSettings
{
    /// <summary>
    /// Schemaversion für die Migration. Wird erhöht, wenn ein Feld seine
    /// Bedeutung ändert — nicht, wenn eines dazukommt.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    public List<SipAccountSettings> Accounts { get; init; } = [];
    public NetworkSettings Network { get; init; } = new();
    public NatMediaSettings NatMedia { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public CodecSettings Codecs { get; init; } = new();
    public ContactSettings Contacts { get; init; } = new();
    public AdvancedSettings Advanced { get; init; } = new();
    public UpdateSettings Update { get; init; } = new();

    /// <summary>
    /// Welche Profilpfade der Benutzer selbst eingestellt hat (ADR-054).
    ///
    /// <para><b>Wozu.</b> Ein Provisionierungsprofil überschrieb bis zum
    /// 13.09.2026 bei <b>jedem Start</b> jeden Wert, den es nannte — auch
    /// solche, die nicht gesperrt waren und die der Benutzer von Hand geändert
    /// hatte. `docs/provisioning.md` sagte das Gegenteil: der Benutzer sei die
    /// letzte Ebene. Damit galt faktisch das Profil, und die Sperre entschied
    /// nur übers Ausgrauen.</para>
    ///
    /// <para><b>Wer hier einträgt.</b> Genau eine Stelle:
    /// <c>SettingsService.Write</c> vergleicht den neuen Stand über
    /// <see cref="ProvisioningCatalog"/> mit dem alten und nimmt jeden Pfad
    /// auf, dessen Wert sich geändert hat. Kein Aufrufer muss daran denken —
    /// und das ist Absicht: eine Erlaubnisliste, an die sich jemand erinnern
    /// muss, ist genau die Bauart, die ADR-045 für das Speichern schon einmal
    /// verworfen hat.</para>
    ///
    /// <para><b>Eine Sperre gewinnt trotzdem.</b> Wird ein Pfad im Profil
    /// gesperrt, wird er angewendet und hier wieder entfernt — das ist der Weg
    /// des Administrators, einen Benutzerwert zurückzuholen.</para>
    ///
    /// <para>Eine Datei von vor dem 13.09.2026 hat das Feld nicht und bekommt
    /// eine leere Menge; das Profil gewinnt dort beim ersten Start noch einmal,
    /// wie bisher.</para>
    /// </summary>
    public List<string> UserOverrides { get; init; } = [];
}

/// <summary>
/// §9.6 („Version / Update prüfen") und ADR-039 — die Update-Prüfung.
///
/// <para>Steht bewusst nicht unter <see cref="AdvancedSettings"/>: der Kanal
/// ist keine erweiterte Einstellung, sondern die Frage, welche Fassungen
/// dieser Arbeitsplatz überhaupt bekommt. Wer sie sucht, sucht sie oben.</para>
/// </summary>
public sealed record UpdateSettings
{
    /// <summary>
    /// Ob beim Start nach Updates gesucht wird. Standard ein.
    ///
    /// <para>Aus heisst wirklich aus — kein Abruf, keine Verbindung zu GitHub.
    /// Der Schalter ist für Arbeitsplätze gedacht, deren Fassung jemand anders
    /// verwaltet.</para>
    /// </summary>
    public bool CheckOnStart { get; init; } = true;

    /// <summary>
    /// Welcher Kanal. Standard <see cref="UpdateChannel.Stable"/>.
    ///
    /// <para>ADR-039: die Kanäle sind getrennte Feeds. Ein Wechsel wirkt beim
    /// nächsten Abruf, und der Rückweg von <see cref="UpdateChannel.Beta"/>
    /// nach <see cref="UpdateChannel.Stable"/> ist technisch ein Downgrade —
    /// er ist ausdrücklich erlaubt, sonst wäre beta eine Einbahnstrasse.</para>
    /// </summary>
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;
}

/// <summary>
/// Der Release-Kanal (ADR-039).
///
/// <para>Die Werte heissen wie die Velopack-Kanäle, aus denen sie werden:
/// <c>win-stable</c> und <c>win-beta</c>. Die Zuordnung steht an genau einer
/// Stelle im <c>UpdateService</c>.</para>
/// </summary>
public enum UpdateChannel
{
    /// <summary>Die abgenommene Fassung. Standard für alle Arbeitsplätze.</summary>
    Stable,

    /// <summary>Vorab, zum Ausprobieren. Kann Fehler haben, die stable nicht hat.</summary>
    Beta,
}

/// <summary>§8.4 — Kontaktquellen und Besetztlampenfeld.</summary>
public sealed record ContactSettings
{
    /// <summary>
    /// Persönliche Kontakte aus Outlook einlesen (ADR-009). Aus, wenn kein
    /// Outlook da ist oder niemand die Liste braucht — das spart einen
    /// COM-Start je Aktualisierung.
    /// </summary>
    public bool UseOutlook { get; init; } = true;

    /// <summary>
    /// Wie lange die Outlook-Kontakte gelten, bevor neu gelesen wird (AP6.3:
    /// zwölf Stunden). Ein Adressbuch ändert sich selten; ein COM-Durchlauf je
    /// Fensteröffnung wäre reine Last.
    /// </summary>
    public int OutlookCacheHours { get; init; } = 12;

    /// <summary>
    /// Die Team-Nebenstellen. Sie kommen aus der Provisionierung (§17) oder
    /// werden von Hand gepflegt — und <b>nur</b> für sie wird Präsenz
    /// abonniert (§14.8).
    /// </summary>
    public List<TeamExtension> Team { get; init; } = [];

    /// <summary>
    /// Die Gruppen, in denen die Nebenstellen stehen — in ihrer Reihenfolge.
    ///
    /// <para><b>Eine eigene Liste und nicht implizit aus den Einträgen
    /// abgeleitet</b> (ADR-041). Sonst wäre eine <b>leere</b> Gruppe nicht
    /// anlegbar — man müsste „Support" erst befüllen, um sie zu haben —, das
    /// Umbenennen liesse eine gerade leere Gruppe verschwinden, und die
    /// Gruppenreihenfolge hinge an der Reihenfolge ihres ersten Mitglieds: wer
    /// im Sortiermodus einen Eintrag nach oben zöge, verschöbe die ganze
    /// Gruppe.</para>
    ///
    /// <para><b>Der Standard ist nicht der Name „Team", sondern die erste
    /// Gruppe.</b> Sie heisst ab Werk so und darf umbenannt werden, ohne dass
    /// Einträge ohne eigene Gruppe heimatlos werden.</para>
    /// </summary>
    public List<string> Groups { get; init; } = [TeamGroups.DefaultName];

    /// <summary>
    /// Besetztlampenfeld einschalten (§8.4). Jede beobachtete Nebenstelle
    /// kostet die Anlage ein Abonnement — deshalb steht das unter Kontrolle
    /// und gilt nie für Outlook-Kontakte (§14.8).
    /// </summary>
    public bool EnableBlf { get; init; } = true;
}

/// <summary>
/// Eine beobachtbare Nebenstelle im eigenen Team (§8.4).
/// </summary>
/// <param name="DisplayName">Name in der Liste.</param>
/// <param name="Extension">Kurzwahl, die gewählt wird.</param>
/// <param name="SipAddress">
/// Vollständige SIP-Adresse für das Präsenz-Abonnement. Fehlt sie, erscheint
/// die Nebenstelle in der Liste, wird aber nicht beobachtet — ohne Adresse
/// gibt es niemanden, den man abonnieren könnte.
/// </param>
/// <param name="Mobile">
/// Die Handynummer, falls bekannt. <b>Sie erzeugt kein zweites
/// Präsenz-Abonnement</b> — beobachtet wird über <paramref name="SipAddress"/>,
/// und die Last auf der Anlage ändert sich um null (§14.8).
/// </param>
/// <param name="Group">
/// In welcher Gruppe der Kontaktliste die Nebenstelle steht. <c>null</c> heisst
/// „in der ersten Gruppe" — also dort, wo ab Werk „Team" steht.
///
/// <b>Vorgabe <c>null</c> und nicht <c>"Team"</c>:</b> <c>SettingsService</c>
/// schreibt mit <c>WhenWritingNull</c>, ein nicht gesetztes Feld erscheint also
/// gar nicht in der Datei. Ein deklarierter Vorgabewert wäre die Falle — ob
/// System.Text.Json ihn bei einem fehlenden Member einsetzt oder
/// <c>default(T)</c>, hängt an der Laufzeitfassung; im zweiten Fall stünde
/// <c>null</c> in einem nicht-nullable <c>string</c>, ohne Compilerwarnung.
/// <c>TeamExtensionJsonTests</c> hält das empirisch fest, statt es zu zitieren.
/// </param>
public sealed record TeamExtension(
    string DisplayName,
    string Extension,
    string? SipAddress = null,
    string? Mobile = null,
    string? Group = null);

/// <summary>§9.2 — Netzwerk und Transport.</summary>
public sealed record NetworkSettings
{
    /// <summary>
    /// SIP-Port. §9.2 nennt 5061 für TLS als Standard. Bei UDP/TCP ist 5060
    /// üblich; der Wert gehört zum Transport, nicht zum Konto.
    /// </summary>
    public int SipPort { get; init; } = 5061;

    /// <summary>§9.2: Serverzertifikat prüfen, Standard ein.</summary>
    public bool VerifyServerCertificate { get; init; } = true;

    /// <summary>§9.2: Keep-Alive in Sekunden.</summary>
    public int KeepAliveSeconds { get; init; } = 30;

    /// <summary>§9.2: IPv6, Standard aus.</summary>
    public bool EnableIpv6 { get; init; }

    /// <summary>§9.2: Netzwerkwechsel erkennen, Standard ein (AP3.6).</summary>
    public bool DetectNetworkChanges { get; init; } = true;

    /// <summary>§9.2: RTP-Portbereich 7078–7178. Im SDK unbelegt (-1/-1).</summary>
    public int RtpPortMin { get; init; } = 7078;

    public int RtpPortMax { get; init; } = 7178;

    /// <summary>§9.2: DSCP für Signalisierung, CS3 = 24.</summary>
    public int SipDscp { get; init; } = 24;

    /// <summary>§9.2: DSCP für Medien, EF = 46.</summary>
    public int AudioDscp { get; init; } = 46;
}

/// <summary>§9.3 — NAT und Medien.</summary>
public sealed record NatMediaSettings
{
    public string? StunServer { get; init; }

    /// <summary>§9.3: ICE, Standard ein.</summary>
    public bool EnableIce { get; init; } = true;

    public bool EnableTurn { get; init; }
    public string? TurnServer { get; init; }
    public string? TurnUsername { get; init; }

    /// <summary>§9.3: SRTP als Standard.</summary>
    public MediaEncryptionSetting Encryption { get; init; } = MediaEncryptionSetting.Srtp;

    /// <summary>
    /// §9.3 gab ursprünglich <b>ein</b> vor. Steht seit Rev. 3 auf <b>aus</b>:
    /// die aktuelle Anlage von bv2 hat keine Verschlüsselung, mit „ein" wäre
    /// kein Gespräch möglich (ADR-007). SRTP wird weiter angeboten.
    ///
    /// Wer das einschaltet, muss wissen, dass Gespräche zu Gegenstellen ohne
    /// SRTP hart scheitern — die Meldung dazu steht in
    /// <see cref="SipErrorCatalog.DescribeCallFailure"/> (§14.7).
    /// </summary>
    public bool EncryptionMandatory { get; init; }

    /// <summary>§9.3: adaptive Bitrate, Standard ein.</summary>
    public bool AdaptiveBitrate { get; init; } = true;

    /// <summary>
    /// §9.3: Video aus und im UI ausgegraut. §2 schliesst Video aus — das Feld
    /// existiert nur, damit die Oberfläche zeigen kann, dass es bewusst aus
    /// ist, statt die Frage offenzulassen.
    /// </summary>
    [JsonIgnore]
    public static bool VideoEnabled => false;
}

/// <summary>§9.3: Medienverschlüsselung als Einstellung.</summary>
public enum MediaEncryptionSetting
{
    None,
    Srtp,
    Zrtp,
    Dtls,
}

/// <summary>§9.4 — Audio.</summary>
public sealed record AudioSettings
{
    /// <summary>
    /// Gerätekennung des SDK. <c>null</c> bedeutet „dem Windows-Standard
    /// folgen" — so steht es in §9.4 und so soll es bleiben, wenn der
    /// Benutzer nichts wählt.
    /// </summary>
    public string? InputDeviceId { get; init; }

    public string? OutputDeviceId { get; init; }

    /// <summary>§9.4: Klingelgerät, Standard = Lautsprecher.</summary>
    public string? RingerDeviceId { get; init; }

    /// <summary>§9.4: Wiedergabelautstärke 0–100, Standard 72.</summary>
    public int PlaybackVolume { get; init; } = 72;

    /// <summary>§9.4: Mikrofonpegel 0–100, Standard 50.</summary>
    public int MicrophoneLevel { get; init; } = 50;

    /// <summary>
    /// §9.4: Echounterdrückung, Standard ein.
    ///
    /// <b>Vorbehalt (ADR-006):</b> bei 8-kHz-Codecs — also bei praktisch jedem
    /// Externgespräch — schaltet sich der Canceller selbst ab und meldet
    /// „does not support sampling rate 8000Hz". Die Einstellung sagt dann
    /// etwas zu, das nicht eintritt. Die Oberfläche muss den tatsächlichen
    /// Zustand zeigen, nicht den gewünschten.
    /// </summary>
    public bool EchoCancellation { get; init; } = true;

    /// <summary>
    /// §9.4: automatische Aussteuerung.
    ///
    /// <b>Standard aus, abweichend von §9.4 (ADR-011).</b> Die
    /// SDK-Dokumentation zu <c>Core.AgcEnabled</c> sagt über den Algorithmus:
    /// „This algorithm is very experimental, not usable in its current state."
    /// Ab Werk etwas einzuschalten, von dem der Hersteller abrät, wäre keine
    /// Vorgabe, sondern eine Falle. Der Schalter bleibt, weil §9.4 ihn nennt
    /// und weil er jetzt tatsächlich wirkt — vorher wurde er nirgends
    /// übertragen.
    /// </summary>
    public bool AutomaticGainControl { get; init; }

    /// <summary>§9.4: Rauschunterdrückung (RNNoise ab SDK 5.5), Standard ein.</summary>
    public bool NoiseSuppression { get; init; } = true;

    /// <summary>
    /// §9.4: Klingelton. <c>null</c> heisst mitgelieferte WAV.
    ///
    /// Das SDK sucht beim Start eine Datei, die im win64-Paket nicht enthalten
    /// ist (`notes_of_the_optimistic.mkv`) — nipp muss einen eigenen Ton
    /// beisteuern (§16.2 Branding).
    /// </summary>
    public string? RingtonePath { get; init; }

    /// <summary>Ergebnis der letzten Echo-Kalibrierung in ms (§9.4, AP5.7).</summary>
    public int? EchoCalibrationMs { get; init; }
}

/// <summary>
/// §9.5 — Codecs.
///
/// Die Reihenfolge in <see cref="Order"/> bestimmt die Priorität im SDP.
/// §14.10 warnt ausdrücklich: das Ergebnis <b>im SIP-Log</b> gegenprüfen,
/// nicht dem UI-Zustand glauben.
/// </summary>
public sealed record CodecSettings
{
    /// <summary>
    /// Codecs in absteigender Priorität. Die Vorgabe folgt §9.5, mit einer
    /// Korrektur: <b>G.729 fehlt</b> — bcg729 ist nicht im Build (§3), die
    /// Zeile in §9.5 geht ins Leere.
    ///
    /// Die festen Payload-Type-Nummern aus §9.5 (Opus 96, speex 102) sind
    /// <b>nicht setzbar</b>: das SDK vergibt dynamische Typen erst beim SDP.
    /// Deshalb steht hier nur der Name.
    /// </summary>
    public List<string> Order { get; init; } = ["opus", "G722", "PCMA", "PCMU"];

    /// <summary>
    /// Aktive Codecs. §9.5 will G.722 aktiv auf Priorität 2 — im SDK ist es
    /// standardmässig <b>aus</b> und muss eingeschaltet werden.
    /// </summary>
    public List<string> Enabled { get; init; } = ["opus", "G722", "PCMA", "PCMU"];

    /// <summary>§9.5: DTMF-Modus.</summary>
    public DtmfMode Dtmf { get; init; } = DtmfMode.Rfc2833;

    /// <summary>
    /// §9.5: „PCMA und PCMU dürfen nicht beide deaktiviert werden — das UI
    /// verhindert es, weil sonst die Verhandlung mit vielen Trunks scheitert."
    /// Die Regel steht hier und nicht nur im UI, damit auch ein
    /// Provisioning-Profil sie nicht umgehen kann (§11).
    /// </summary>
    public bool IsValid => Enabled.Contains("PCMA", StringComparer.OrdinalIgnoreCase)
        || Enabled.Contains("PCMU", StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// §9.5: DTMF-Modus. Im SDK sind das <b>zwei</b> Schalter
/// (<c>UseRfc2833ForDtmf</c> und <c>UseInfoForDtmf</c>), hier eine Auswahl —
/// die Kombination „beide an" ist kein sinnvoller Zustand.
/// </summary>
public enum DtmfMode
{
    Rfc2833,
    SipInfo,
    Inband,
}

/// <summary>§9.6 — Erweitert.</summary>
public sealed record AdvancedSettings
{
    /// <summary>§9.6: mit Windows starten, Standard ein.</summary>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>§9.6: minimiert im Infobereich starten, Standard ein.</summary>
    public bool StartMinimized { get; init; } = true;

    /// <summary>§9.6: Standard für tel:, sip:, callto:, Standard ein.</summary>
    public bool RegisterProtocolHandlers { get; init; } = true;

    /// <summary>§9.6: globaler Hotkey, Standard Strg+Umschalt+A.</summary>
    public string GlobalHotkey { get; init; } = "Ctrl+Shift+A";

    /// <summary>
    /// Das systemweite Kürzel fürs Stummschalten (C7, ADR-050).
    ///
    /// <para><b>Leer heisst: keines.</b> Anders als <see cref="GlobalHotkey"/>
    /// ist es nicht vorbelegt — ein zweites Kürzel, das nipp beim ersten Start
    /// ungefragt für sich beansprucht, nimmt es womöglich der Anwendung weg,
    /// in der man gerade sitzt. Wer es will, trägt es ein; der Vorschlag
    /// daneben ist <c>Ctrl+Shift+M</c>, dasselbe wie in Teams.</para>
    ///
    /// <para>Stummschalten ist im Alltag der häufigste Griff im Gespräch, und
    /// nipp lebt im Infobereich: ohne systemweites Kürzel heisst er «Fenster
    /// suchen, nach vorn holen, hinsehen, klicken».</para>
    /// </summary>
    public string MuteHotkey { get; init; } = string.Empty;

    /// <summary>§9.6: Anrufe automatisch annehmen, Standard aus.</summary>
    public bool AutoAnswer { get; init; }

    /// <summary>
    /// Ob nipp Berichte an das Headset schickt — der Notausgang aus H5
    /// (ADR-068). Standard <b>ein</b>: ohne Berichte gibt es keine Lampen,
    /// kein Klingeln im Ohr und keine Stummtaste, die mitleuchtet.
    ///
    /// <para><b>Wofür es ihn trotzdem gibt.</b> Jeder Ausgangsbericht ist ein
    /// Eingriff in das Gespräch eines anderen Programms: nipp teilt das
    /// HID-Call-Control-Interface, und Teams liest die Antwort des Geräts als
    /// Tastendruck. Was nipp dagegen tut, ist an zwei Geräten gemessen — am
    /// Jabra PRO 9470 und am Link 400. <b>Ein drittes Gerät kann sich anders
    /// verhalten</b>, und dann braucht es einen Weg, der ohne neue Fassung
    /// auskommt und den ein Administrator über ein Profil setzen kann.</para>
    ///
    /// <para><b>Aus heisst wirklich aus</b>, auch für den Abschlussbericht.
    /// Das ist die unbequemere Wahl: wer mitten im Klingeln abschaltet, muss
    /// die Lampe am Gerät selbst löschen. Die bequemere — «einmal noch
    /// aufräumen» — hiesse, dass ein Notausgang ein letztes Mal genau das
    /// tut, wovor er schützen soll.</para>
    /// </summary>
    public bool SendHeadsetSignals { get; init; } = true;

    /// <summary>§9.6: Protokollierung.</summary>
    public LogVerbosity Logging { get; init; } = LogVerbosity.Info;

    /// <summary>§9.6: Provisioning-URI (§11).</summary>
    public string? ProvisioningUri { get; init; }

    /// <summary>
    /// Ob ein Provisionierungsprofil auch unverschlüsselt über http geholt
    /// werden darf. <b>Standard aus</b> (ADR-012).
    ///
    /// Ein Profil legt Konten, Zugangsdaten und Team-Nebenstellen fest. Über
    /// http liest das jeder im Netz mit und kann es ersetzen. Der Schalter
    /// existiert, weil es Anlagen ohne TLS-Webserver gibt — er ist eine
    /// bewusste Ausnahme, keine Bequemlichkeit, und ein Profil aus dem Netz
    /// kann ihn nicht selbst setzen.
    /// </summary>
    public bool AllowInsecureProvisioning { get; init; }

    /// <summary>
    /// §8.1: Länderpräfix für die Normalisierung, Standard +41.
    /// Steht bei den erweiterten Einstellungen und nicht beim Konto, weil es
    /// den Standort betrifft, nicht die Anmeldung.
    /// </summary>
    public string CountryPrefix { get; init; } = "+41";

    /// <summary>
    /// §16.7, entschieden am 04.09.2026: lokal, konfigurierbar.
    /// <c>null</c> heisst <c>%LOCALAPPDATA%\nipp\recordings</c>.
    /// </summary>
    public string? RecordingDirectory { get; init; }

    /// <summary>§8.3: Aufbewahrung der Anrufliste in Tagen, Standard 365.</summary>
    public int HistoryRetentionDays { get; init; } = 365;

    /// <summary>
    /// §20.4: Erscheinungsbild. Standard ist <see cref="AppTheme.System"/> —
    /// nipp übernimmt die Einstellung von Windows und zieht Änderungen im
    /// laufenden Betrieb nach.
    /// </summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>§20.5: Fenster immer im Vordergrund. Standard aus.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>
    /// §20.2: welches Konto beim Start für ausgehende Anrufe gewählt ist.
    /// <c>null</c> heisst: das erste registrierte (§9.1).
    /// </summary>
    public string? DefaultAccountIdentity { get; init; }

    /// <summary>
    /// §20.1: ob die Wähltastatur eingeblendet ist. Merkt sich die letzte Wahl.
    ///
    /// <para><b>Vorgabe seit dem 13.09.2026: aus</b> (C4 aus dem zweiten
    /// Review). Gerechnet auf die Standardgrösse 400 × 660: Titelleiste 32,
    /// Seitenrand 16, Kontozeile rund 56, Nummernfeld 40, <b>Wähltastatur rund
    /// 200</b>, Umschaltleiste 52. Der Kontaktliste blieben damit rund 250
    /// Pixel — fünf Zeilen, verteilt auf zwei Abschnitte mit Kopfzeilen. Ohne
    /// die Tastatur sind es zehn.</para>
    ///
    /// <para>Und sie ist auf einem Arbeitsplatz-PC redundant: Ziffern tippt man
    /// auf der Tastatur, und für Sprachmenüs steht die eigene Zehnertastatur in
    /// der Gesprächsansicht. <b>Wer sie will, schaltet sie einmal ein</b> — der
    /// Umschalter sitzt unverändert rechts im Nummernfeld, und die Wahl
    /// überlebt den Neustart.</para>
    ///
    /// <para>Mit Touch-Geräten im Feld wäre das eine andere Rechnung. Solange
    /// nipp auf Arbeitsplätzen mit Maus und Tastatur läuft, trägt die Vorgabe
    /// «aus».</para>
    /// </summary>
    public bool ShowDialpad { get; init; }

    /// <summary>
    /// §8.4: ob der Abschnitt „Team" in der Kontaktliste aufgeklappt ist.
    /// Wie <see cref="ShowDialpad"/> reiner Anzeigezustand — er wird über
    /// <c>SaveViewState</c> abgelegt und löst nichts aus.
    /// </summary>
    public bool ShowTeamContacts { get; init; } = true;

    /// <summary>§8.4: ob der Abschnitt „Outlook" aufgeklappt ist.</summary>
    public bool ShowOutlookContacts { get; init; } = true;

    /// <summary>
    /// Ob dem Benutzer schon gesagt wurde, dass Schliessen nicht beendet
    /// (W1.3, Befund C10).
    ///
    /// <para><b>Der Befund.</b> §10 legt fest, dass das Fensterkreuz nipp in
    /// den Infobereich legt statt es zu beenden — richtig für ein Telefon, das
    /// erreichbar bleiben soll. Gesagt wurde es nie: das Fenster verschwand,
    /// und im Protokoll stand eine Zeile, die niemand liest. Wer nipp beendet
    /// glaubte, wunderte sich über ein Klingeln aus dem Nichts, und «wie
    /// beende ich das?» ist die Support-Frage, die daraus folgt.</para>
    ///
    /// <para>Reiner Anzeigezustand, also über <c>SaveViewState</c>: dass ein
    /// Hinweis gezeigt wurde, ist kein Grund, ins SDK zu greifen.</para>
    /// </summary>
    public bool TrayHintSeen { get; init; }

    /// <summary>
    /// Welche Team-Gruppen zugeklappt sind (ADR-041).
    ///
    /// <para><b>Die zugeklappten und nicht die aufgeklappten:</b> eine neu
    /// angelegte Gruppe ist damit offen, ohne dass jemand sie eintragen muss —
    /// und eine entfernte hinterlässt höchstens einen Namen, der niemanden
    /// stört.</para>
    ///
    /// <para>Reiner Anzeigezustand, also über <c>SaveViewState</c>: an
    /// <c>Changed</c> hängen fünf Empfänger bis hinunter zum Präsenz-Neuabo,
    /// und ein zugeklappter Abschnitt ist kein Grund, ins SDK zu greifen.</para>
    /// </summary>
    public List<string> CollapsedTeamGroups { get; init; } = [];

    /// <summary>
    /// Wo das Fenster zuletzt stand, als „X,Y,Breite,Höhe" in logischen
    /// Pixeln. <c>null</c> heisst: noch nie verschoben, nipp positioniert
    /// selbst.
    ///
    /// Als Zeichenkette und nicht als vier Felder, weil es genau einen
    /// Verwender gibt und vier Zahlen in der Einstellungsdatei mehr Platz
    /// einnehmen als sie wert sind. <c>WindowPlacement</c> zerlegt sie.
    /// </summary>
    public string? WindowPlacement { get; init; }
}

/// <summary>
/// §20.4: Erscheinungsbild.
///
/// <see cref="System"/> ist der Standard und bedeutet nicht „einmal beim Start
/// nachsehen", sondern „mitziehen" — wer Windows umstellt, sieht nipp
/// sofort mitwechseln.
/// </summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>§9.6: Protokollierung.</summary>
public enum LogVerbosity
{
    Off,
    Info,
    Debug,
}
