namespace Nipp.Core.Services.Telephony.Model;

/// <summary>
/// Zustand der Registrierung eines SIP-Kontos.
///
/// Eigener Typ statt <c>Linphone.RegistrationState</c>: §6 verlangt, dass kein
/// SDK-Typ den Telefonie-Dienst verlässt. Das SDK kennt vier Werte
/// (None, Progress, Ok, Cleared) plus einen Fehlerzustand; hier steht die
/// Bedeutung, nicht die SDK-Schreibweise.
/// </summary>
public enum RegistrationStatus
{
    /// <summary>Kein Konto eingerichtet oder noch nichts unternommen.</summary>
    None,

    /// <summary>REGISTER gesendet, Antwort steht aus.</summary>
    InProgress,

    /// <summary>Registriert. Ausgehende und eingehende Anrufe sind möglich.</summary>
    Registered,

    /// <summary>Abgemeldet — das Konto wurde bewusst deaktiviert.</summary>
    Unregistered,

    /// <summary>Fehlgeschlagen. Der Grund steht in der begleitenden Meldung.</summary>
    Failed,
}

/// <summary>
/// Zustand eines einzelnen Anrufs. Bewusst gröber als die Zustandsmaschine des
/// SDK (die rund zwanzig Zustände kennt): die Oberfläche braucht nur die
/// Unterscheidungen, die ein Benutzer auch sieht.
/// </summary>
public enum CallStatus
{
    /// <summary>Wird aufgebaut, es klingelt noch nicht.</summary>
    Dialing,

    /// <summary>Ausgehend, die Gegenstelle klingelt.</summary>
    Ringing,

    /// <summary>Eingehend, wartet auf Annahme oder Ablehnung.</summary>
    Incoming,

    /// <summary>Verbunden, Sprachkanäle laufen.</summary>
    Connected,

    /// <summary>Von uns gehalten.</summary>
    OnHold,

    /// <summary>Von der Gegenstelle gehalten.</summary>
    RemoteOnHold,

    /// <summary>Beendet — gleich welcher Grund.</summary>
    Ended,

    /// <summary>Mit Fehler beendet. Der Grund steht in <see cref="CallInfo.StatusMessage"/>.</summary>
    Failed,
}

/// <summary>Richtung eines Anrufs, für Anzeige und Verlauf (§8.3).</summary>
public enum CallDirection
{
    Incoming,
    Outgoing,
}

/// <summary>
/// Art der Weiterleitung. §8.2 verlangt, dass beide im UI unterscheidbar sind —
/// „kein verstecktes Verhalten".
/// </summary>
public enum TransferMode
{
    /// <summary>
    /// Blind: sofort abgeben, ohne Rückfrage. Wir erfahren nicht, ob das Ziel
    /// abnimmt.
    /// </summary>
    Blind,

    /// <summary>
    /// Begleitet: erst selbst beim Ziel anrufen, ankündigen, dann übergeben.
    /// Braucht ein zweites, bereits bestehendes Gespräch.
    /// </summary>
    Attended,
}

/// <summary>
/// Verweis auf einen laufenden Anruf, den Aufrufer ausserhalb des Dienstes
/// halten dürfen.
///
/// Absichtlich ein undurchsichtiger Bezeichner statt eines Verweises auf
/// <c>Linphone.Call</c>: so kann kein ViewModel versehentlich am Dienst vorbei
/// auf das SDK zugreifen, und der Dienst behält die Kontrolle über die
/// Lebensdauer der nativen Objekte.
/// </summary>
/// <param name="Value">Vom Dienst vergeben, innerhalb einer Sitzung eindeutig.</param>
public readonly record struct CallHandle(Guid Value)
{
    public static CallHandle New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Momentaufnahme eines Anrufs für die Anzeige. Unveränderlich — bei jeder
/// Zustandsänderung entsteht eine neue Instanz, damit die Oberfläche nicht
/// auf einem halb aktualisierten Objekt bindet.
/// </summary>
/// <param name="Handle">Verweis für weitere Aktionen an diesem Anruf.</param>
/// <param name="RemoteNumber">Nummer der Gegenstelle, so wie sie das SIP-Signal liefert.</param>
/// <param name="RemoteDisplayName">
/// Anzeigename aus dem SIP-Signal, falls vorhanden. Die Auflösung über
/// Kontakte macht der <c>ClipResolver</c> (§8.4), nicht dieser Dienst.
/// </param>
/// <param name="Direction">Eingehend oder ausgehend.</param>
/// <param name="Status">Aktueller Zustand.</param>
/// <param name="StatusMessage">Begleittext des SDK, vor allem im Fehlerfall.</param>
/// <param name="StartedAt">Zeitpunkt des Aufbaus — Grundlage für den Dauer-Timer (§8.2).</param>
/// <param name="ConnectedAt">Zeitpunkt der Verbindung; erst ab hier zählt die Gesprächsdauer.</param>
/// <param name="IsMuted">Ob das Mikrofon stumm ist.</param>
/// <param name="IsRecording">
/// Ob aufgezeichnet wird. §8.2: ein sichtbarer Indikator ist Pflicht — in der
/// Schweiz ist das Mitschneiden ohne Kenntnis der Gegenseite strafbar.
/// </param>
/// <param name="Codec">Verhandelter Audio-Codec, sobald bekannt.</param>
/// <param name="Encryption">Verhandelte Medienverschlüsselung, sobald bekannt.</param>
/// <param name="AccountIdentity">
/// Über welches Konto dieses Gespräch läuft (§20.2). Bestimmt die Domäne für
/// Kurzwahlen und Weiterleitungsziele und landet im Verlauf — bei mehreren
/// Konten auf verschiedenen Anlagen ist das keine Nebensache.
/// </param>
/// <param name="EndReason">
/// Warum das Gespräch endete, soweit die Anlage es sagt. Nur bei beendeten
/// Gesprächen gesetzt; liefert dem Verlauf „besetzt" und „abgelehnt" (§20.3).
/// </param>
/// <param name="RecordingPath">
/// Wohin eine laufende Aufnahme schreibt, sonst <c>null</c>. Der Verlauf
/// übernimmt den Pfad, damit sich eine Aufnahme später wiederfinden lässt
/// (§8.3).
/// </param>
public sealed record CallInfo(
    CallHandle Handle,
    string RemoteNumber,
    string? RemoteDisplayName,
    CallDirection Direction,
    CallStatus Status,
    string? StatusMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? ConnectedAt,
    bool IsMuted,
    bool IsRecording,
    string? Codec,
    MediaEncryptionMode Encryption,
    string? AccountIdentity = null,
    CallEndReason? EndReason = null,
    string? RecordingPath = null)
{
    /// <summary>Wie lange das Gespräch steht; null, solange es nicht verbunden ist.</summary>
    public TimeSpan? Duration =>
        ConnectedAt is null ? null : DateTimeOffset.UtcNow - ConnectedAt.Value;

    // Hier stand `DisplayLabel` — „der Anzeigename aus dem Signal, sonst die
    // Nummer". Es war der einzige richtungsneutrale Name im System und kannte
    // keine Kontakte; bei eingehenden Anrufen kaschierte das der CLIP der
    // Anlage, bei ausgehenden stand deshalb die blosse Nummer in Kopfzeile,
    // Makel-Liste, Toast und Infobereich. Wer den Namen des Gesprächspartners
    // braucht, fragt `CallPartyResolver` (ADR-043). `RemoteDisplayName` bleibt,
    // was es ist: was die Signalisierung hergab.

    /// <summary>Ob an diesem Anruf noch Aktionen möglich sind.</summary>
    public bool IsActive => Status is not (CallStatus.Ended or CallStatus.Failed);
}

/// <summary>
/// Warum ein Gespräch geendet hat — soweit die Gegenstelle oder die Anlage
/// einen Grund genannt hat.
///
/// Bewusst nicht identisch mit <c>CallOutcome</c> der Anrufliste: der Verlauf
/// verbindet den Grund mit der Richtung und damit, ob überhaupt verbunden war.
/// Diese Aufzählung sagt nur, was das Signal hergab.
/// </summary>
public enum CallEndReason
{
    /// <summary>Regulär beendet — eine Seite hat aufgelegt.</summary>
    Normal,

    /// <summary>Die Gegenstelle war besetzt (486).</summary>
    Busy,

    /// <summary>Das Gespräch wurde abgelehnt (603).</summary>
    Declined,

    /// <summary>Es wurde nicht abgenommen.</summary>
    NoAnswer,

    /// <summary>Technisch gescheitert; der Grund steht in der Meldung.</summary>
    Failed,
}

/// <summary>
/// Medienverschlüsselung eines Gesprächs (§9.3).
///
/// <c>None</c> ist gegen die aktuelle Anlage von bv2 der Regelfall (ADR-007) —
/// deshalb muss der Zustand im Gespräch sichtbar sein, und zwar nach §8.4 nie
/// nur über Farbe, sondern auch als Text.
/// </summary>
public enum MediaEncryptionMode
{
    Unknown,
    None,
    Srtp,
    Zrtp,
    Dtls,
}

/// <summary>
/// Qualitätswerte eines laufenden Gesprächs für das Panel aus §8.2.
/// Wird im Sekundenrhythmus erneuert, nicht bei jedem Iterate.
/// </summary>
/// <param name="RoundTripSeconds">Umlaufzeit in Sekunden, wie das SDK sie liefert.</param>
/// <param name="JitterBufferMilliseconds">
/// Grösse des Jitter-<b>Puffers</b> in Millisekunden — nicht der Jitter selbst.
/// Die Anzeige hiess bis zum 05.09.2026 „Jitter" und zeigte diesen Wert; das
/// ist etwas anderes und liest sich bei einem grossen Puffer wie eine
/// schlechte Leitung.
/// </param>
/// <param name="ReceiverLossPercent">Paketverlust in Empfangsrichtung, in Prozent.</param>
/// <param name="DownloadKbitPerSecond">Empfangene Bandbreite in kbit/s.</param>
/// <param name="Mos">
/// Geschätzte Sprachqualität von 1 bis 5, wie das SDK sie laufend berechnet
/// (§8.2 nennt „geschätzter MOS"). 0, solange kein Wert vorliegt.
/// </param>
public sealed record CallQuality(
    float RoundTripSeconds,
    float JitterBufferMilliseconds,
    float ReceiverLossPercent,
    float DownloadKbitPerSecond,
    float Mos = 0)
{
    /// <summary>
    /// Grobe Einstufung für die Oberfläche. Die Schwellen sind bewusst
    /// grosszügig: ein Softphone, das bei jedem Zucken „schlecht" anzeigt,
    /// wird nicht mehr beachtet.
    /// </summary>
    public CallQualityRating Rating => this switch
    {
        { ReceiverLossPercent: > 5 } or { RoundTripSeconds: > 0.4f } => CallQualityRating.Poor,
        { ReceiverLossPercent: > 1 } or { RoundTripSeconds: > 0.2f } => CallQualityRating.Fair,
        _ => CallQualityRating.Good,
    };
}

/// <summary>Einstufung der Gesprächsqualität für die Anzeige.</summary>
public enum CallQualityRating
{
    Good,
    Fair,
    Poor,
}
