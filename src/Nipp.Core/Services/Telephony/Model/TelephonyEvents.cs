namespace Nipp.Core.Services.Telephony.Model;

/// <summary>
/// Registrierungszustand hat sich geändert.
/// </summary>
/// <param name="Status">Neuer Zustand.</param>
/// <param name="AccountIdentity">Betroffenes Konto, als SIP-Adresse.</param>
/// <param name="Message">
/// Begleittext. Im Fehlerfall der Grund — er gehört nach §15 in die
/// Benutzermeldung, nicht nur ins Log.
/// </param>
public sealed record RegistrationChangedEventArgs(
    RegistrationStatus Status,
    string AccountIdentity,
    string? Message);

/// <summary>Zustand eines Anrufs hat sich geändert.</summary>
/// <param name="Call">Momentaufnahme nach der Änderung.</param>
/// <param name="Previous">
/// Zustand davor — erlaubt Übergänge zu erkennen, etwa „hat abgenommen".
///
/// <c>null</c> heißt: der Anruf ist <b>neu</b>, es gab keinen Zustand davor. Das ist
/// kein Sonderfall für Feinheiten, sondern die Grundlage dafür, dass ein eingehender
/// Anruf überhaupt bemerkt wird: der Dienst legt ihn bereits mit
/// <see cref="CallStatus.Incoming"/> an, bevor er das erste Ereignis meldet. Stand hier
/// derselbe Wert wie in <paramref name="Call"/>, sah jeder Empfänger, der auf den
/// Übergang prüfte, einen Anruf, der schon immer geklingelt hatte — und tat nichts.
/// </param>
public sealed record CallStateEventArgs(CallInfo Call, CallStatus? Previous)
{
    /// <summary>
    /// Ob hier ein Anruf <b>zu klingeln beginnt</b> — der Moment für den Toast
    /// (§8.6).
    ///
    /// <b>Der Vergleich muss <c>is not</c> sein, nicht <c>!=</c>.</b> Ein
    /// neuer Anruf meldet <c>null</c> als Vorzustand; mit <c>!=</c> war die
    /// Bedingung nie wahr, und der Toast erschien nie. Weil nipp im
    /// Infobereich lebt, war das der einzige sichtbare Hinweis auf einen
    /// eingehenden Anruf.
    ///
    /// <b>Wofür das <i>nicht</i> taugt: die Navigation in die
    /// Gesprächsansicht.</b> Die hängt nicht an einem Zustandsübergang,
    /// sondern daran, ob ein Anruf schon einmal angezeigt wurde — siehe
    /// <c>MainWindow._announcedCalls</c>. Ein ausgehender Anruf durchläuft
    /// andere Zustände als ein eingehender, und jede Liste erlaubter Übergänge
    /// hat bisher eine der beiden Richtungen vergessen.
    /// </summary>
    public bool IsNewIncoming =>
        Call.Status == CallStatus.Incoming && Previous is not CallStatus.Incoming;

    /// <summary>
    /// Ob dieses Gespräch <b>jetzt</b> zu Ende gegangen ist — und nicht schon
    /// vorher. Nur dann gehört es in die Anrufliste (§20.3).
    /// </summary>
    public bool HasJustEnded =>
        Call.Status is CallStatus.Ended or CallStatus.Failed
        && Previous is not (CallStatus.Ended or CallStatus.Failed);
}

/// <summary>Neue Qualitätswerte für ein laufendes Gespräch (§8.2).</summary>
public sealed record CallQualityEventArgs(CallHandle Handle, CallQuality Quality);

/// <summary>
/// Präsenz einer beobachteten Nebenstelle hat sich geändert (BLF, §8.4).
/// </summary>
/// <param name="SipAddress">Beobachtete Adresse.</param>
/// <param name="Presence">Neuer Zustand.</param>
public sealed record PresenceEventArgs(string SipAddress, PresenceStatus Presence);

/// <summary>
/// Besetztlampenfeld-Zustände (§8.4).
///
/// Die Anzeige erfolgt farblich **und** als Text — nie nur über Farbe. Das ist
/// eine Barrierefreiheitsanforderung, keine Stilfrage.
/// </summary>
public enum PresenceStatus
{
    Unknown,
    Available,
    Ringing,
    OnCall,
    Away,
    Offline,
}

/// <summary>
/// Die Liste der Audiogeräte hat sich geändert — ein Headset wurde ein- oder
/// ausgesteckt (§9.4).
///
/// §9.4 verlangt: ein während des Gesprächs verschwindendes Headset darf das
/// Gespräch nicht abreissen lassen. Auf das Standardgerät zurückfallen und den
/// Benutzer informieren.
/// </summary>
/// <param name="Devices">Die Geräte, die es jetzt gibt.</param>
/// <param name="Notice">
/// Ein fertiger Satz für die Oberfläche, wenn der Wechsel spürbar war — sonst
/// <c>null</c>.
///
/// Fertig formuliert und nicht als Code: §15 verlangt, dass eine Meldung
/// Ursache und Abhilfe nennt, und beides weiss nur die Stelle, die den Wechsel
/// bemerkt hat. Ein Aufzählungswert, den die Oberfläche in Text übersetzen
/// müsste, verteilte dasselbe Wissen auf zwei Orte.
/// </param>
public sealed record AudioDevicesChangedEventArgs(
    IReadOnlyList<AudioDeviceInfo> Devices,
    string? Notice = null);

/// <summary>
/// Ein eingehender Anruf wurde abgelehnt, weil bereits zwei Gespräche offen
/// sind (§8.2: „Mehr als zwei wird abgelehnt mit klarer Meldung").
///
/// <para>Die Meldung ist der Punkt. Bisher stand die Ablehnung nur im
/// Protokoll — der Anrufer hörte besetzt, und am Arbeitsplatz geschah
/// sichtbar nichts. Wer gerade zwei Gespräche führt, soll hinterher wissen,
/// dass jemand vergeblich versucht hat durchzukommen.</para>
/// </summary>
/// <param name="Number">
/// Die Nummer des Anrufers, damit die Oberfläche sie auflösen und anzeigen
/// kann. <b>Nicht für das Protokoll</b> — dort wird maskiert.
/// </param>
public sealed record CallRejectedEventArgs(string Number);

/// <summary>
/// Ein Audiogerät, SDK-frei.
/// </summary>
/// <param name="Id">Bezeichner des SDK, zur Wiederauswahl.</param>
/// <param name="Name">Anzeigename, wie Windows ihn meldet.</param>
/// <param name="CanRecord">Ob das Gerät aufnehmen kann (Mikrofon).</param>
/// <param name="CanPlay">Ob das Gerät wiedergeben kann (Lautsprecher, Klingel).</param>
public sealed record AudioDeviceInfo(string Id, string Name, bool CanRecord, bool CanPlay);

/// <summary>
/// Wie eine Weiterleitung ausgegangen ist (W1.2, Befund B5).
/// </summary>
public enum TransferOutcome
{
    /// <summary>Das Ziel wird gerufen. Noch keine Aussage.</summary>
    InProgress,

    /// <summary>Das Ziel hat abgenommen — die Uebergabe ist vollzogen.</summary>
    Succeeded,

    /// <summary>
    /// Die Anlage oder das Ziel hat abgelehnt. Das eigene Gespraech laeuft
    /// je nach Anlage weiter.
    /// </summary>
    Failed,
}

/// <summary>
/// Das Ergebnis einer Weiterleitung (W1.2, Befund B5).
///
/// <para><b>Warum es dieses Ereignis gibt.</b> <c>TransferAsync</c>
/// protokollierte «uebergeben» <b>vor</b> jeder Antwort der Anlage, und der
/// Kommentar daneben gab zu, dass der Wrapper den Rueckgabewert verschluckt.
/// Ein 403, 404 oder 603 auf den REFER war damit unsichtbar: kein Fehlertext,
/// das Gespraech blieb je nach Anlage gehalten stehen, und im Protokoll stand
/// Erfolg. Genau die Zeile, die eine Ursache behauptet, die sie nicht gemessen
/// hat.</para>
/// </summary>
/// <param name="Handle">Das Gespraech, das uebergeben werden sollte.</param>
/// <param name="Outcome">Wie es ausgegangen ist.</param>
public sealed record TransferResultEventArgs(CallHandle Handle, TransferOutcome Outcome);
