using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Die Telefonie, wie der Rest der Anwendung sie sieht.
///
/// Hinter dieser Grenze liegt das Linphone SDK, davor nur eigene Typen (§6).
/// Kein <c>Linphone.Call</c> verlässt den Dienst — das ist die Stelle, an der
/// sich später ein SDK-Wechsel oder ein Mock aufhängen lässt.
///
/// Abweichung von §6: die Eigenschaft heisst <c>RegistrationStatus</c>, nicht
/// <c>RegistrationState</c>. Grund ist die Namensfalle aus
/// <c>docs/sdk-api-notes.md</c> — <c>Linphone.RegistrationState</c> existiert
/// ebenfalls, und zwei gleich benannte Typen in einer Datei mit
/// <c>using Linphone</c> sind eine Fehlerquelle ohne Gegenwert.
/// </summary>
public interface ISipService
{
    /// <summary>Zustand des Standardkontos.</summary>
    RegistrationStatus RegistrationStatus { get; }

    /// <summary>
    /// Alle eingerichteten Konten mit ihrem Zustand (§20.2). Höchstens
    /// <see cref="MaxAccounts"/>. Speist die Kontoauswahl samt Status-LED
    /// aus §20.1.
    /// </summary>
    IReadOnlyList<AccountStatus> Accounts { get; }

    /// <summary>
    /// Konto für ausgehende Anrufe. §9.1: „das erste registrierte ist Standard
    /// für ausgehende Anrufe" — hier lässt es sich wechseln.
    /// </summary>
    string? DefaultAccountIdentity { get; set; }

    /// <summary>Wird ausgelöst, wenn sich die Kontoliste ändert (§20.2).</summary>
    event EventHandler<IReadOnlyList<AccountStatus>>? AccountsChanged;

    /// <summary>
    /// Laufende Gespräche, höchstens zwei (§8.2). Momentaufnahme — die Liste
    /// ändert sich nicht unter der Hand.
    /// </summary>
    IReadOnlyList<CallInfo> ActiveCalls { get; }

    /// <summary>Ob die Telefonie einsatzbereit ist.</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Baut den Core auf und startet ihn. Registriert noch kein Konto —
    /// das macht <see cref="RegisterAccountAsync"/>.
    /// </summary>
    /// <param name="settings">
    /// Die Einstellungen, soweit sie <b>vor</b> dem Start des Core gelten
    /// müssen — heute nur der SIP-Port (§9.2). Alles andere kommt danach über
    /// <see cref="ApplySettingsAsync"/>. <c>null</c> heisst: Standardports.
    /// </param>
    Task InitializeAsync(
        Settings.NippSettings? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Richtet ein Konto ein und meldet es an. Mehrere Konten sind möglich;
    /// das erste registrierte ist Standard für ausgehende Anrufe (§9.1).
    /// </summary>
    Task RegisterAccountAsync(SipAccountSettings account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baut einen Anruf auf. <paramref name="destination"/> ist bereits
    /// normalisiert — die Normalisierung ist eine reine Funktion aussen
    /// (§8.1, <c>NumberNormalizer</c>), damit sie testbar bleibt.
    ///
    /// Ohne <paramref name="accountIdentity"/> wird das Standardkonto
    /// verwendet (§9.1, §20.2).
    /// </summary>
    /// <exception cref="TooManyCallsException">Wenn schon zwei Gespräche offen sind (§8.2).</exception>
    Task<CallHandle> PlaceCallAsync(
        string destination,
        string? accountIdentity = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Startet die Echo-Kalibrierung (§9.4, AP5.7). Dauert etwa 15 Sekunden
    /// und liefert das Ergebnis in Millisekunden.
    ///
    /// Während der Kalibrierung darf kein Gespräch laufen — sie belegt Mikrofon
    /// und Lautsprecher.
    /// </summary>
    /// <returns>Verzögerung in ms, oder <c>null</c>, wenn die Messung scheiterte.</returns>
    Task<int?> CalibrateEchoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Meldet, ob die Echounterdrückung im laufenden Gespräch <b>tatsächlich</b>
    /// arbeitet.
    ///
    /// §9.4 führt sie als „ein", aber bei 8-kHz-Codecs schaltet sie sich selbst
    /// ab (ADR-006). Die Oberfläche muss den tatsächlichen Zustand zeigen, nicht
    /// den gewünschten — sonst verspricht die Einstellung etwas, das bei jedem
    /// Externgespräch nicht eintritt.
    /// </summary>
    bool IsEchoCancellationEffective { get; }

    /// <summary>Nimmt einen eingehenden Anruf an.</summary>
    Task AcceptAsync(CallHandle callHandle, CancellationToken cancellationToken = default);

    /// <summary>Legt auf oder lehnt ab — je nach Zustand des Anrufs.</summary>
    Task HangUpAsync(CallHandle callHandle, CancellationToken cancellationToken = default);


    /// <summary>
    /// Leitet weiter. Bei <see cref="TransferMode.Blind"/> sofort, bei
    /// <see cref="TransferMode.Attended"/> an ein zweites, bestehendes
    /// Gespräch (§8.2).
    /// </summary>
    Task TransferAsync(CallHandle callHandle, string destination, TransferMode mode, CancellationToken cancellationToken = default);

    /// <summary>Hält das Gespräch oder holt es zurück.</summary>
    Task SetHoldAsync(CallHandle callHandle, bool onHold, CancellationToken cancellationToken = default);

    /// <summary>Schaltet das Mikrofon stumm oder wieder ein.</summary>
    Task SetMutedAsync(CallHandle callHandle, bool muted, CancellationToken cancellationToken = default);

    /// <summary>Sendet eine DTMF-Ziffer.</summary>
    Task SendDtmfAsync(CallHandle callHandle, char digit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ordner für Aufnahmen (§16.7: lokal, konfigurierbar). Muss gesetzt sein,
    /// <b>bevor</b> ein Anruf aufgebaut wird — siehe
    /// <see cref="StartRecordingAsync"/>.
    /// </summary>
    string RecordingDirectory { get; set; }

    /// <summary>
    /// Startet die Aufnahme. §8.2: ein sichtbarer Indikator ist Pflicht — in
    /// der Schweiz ist das Mitschneiden ohne Kenntnis der Gegenseite strafbar.
    /// Der Dienst setzt <see cref="CallInfo.IsRecording"/>, damit die
    /// Oberfläche das nicht vergessen kann.
    ///
    /// <b>Abweichung von §6:</b> dort steht <c>StartRecordingAsync(call, path)</c>
    /// mit dem Pfad als Parameter. Das ist mit diesem SDK nicht umsetzbar:
    /// <c>Call.Params</c> sind laut Wrapper-Doku <i>read-only</i> und stammen
    /// aus den Parametern, die beim Aufbau des Anrufs übergeben wurden. Ein
    /// später gesetzter Pfad wird stillschweigend verworfen — die Aufnahme
    /// meldet Erfolg und schreibt nichts (in der Abnahme so aufgetreten).
    /// Deshalb legt der Dienst den Pfad beim Aufbau fest, aus
    /// <see cref="RecordingDirectory"/> und der Gegenstelle.
    /// </summary>
    /// <returns>Wahr, wenn die Aufnahme tatsächlich läuft.</returns>
    Task<bool> StartRecordingAsync(CallHandle callHandle, CancellationToken cancellationToken = default);

    /// <summary>Beendet die Aufnahme.</summary>
    Task StopRecordingAsync(CallHandle callHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Meldet dem SDK, dass sich die Netzwerklage geändert hat (§9.2, §14).
    /// Löst eine Neuregistrierung aus.
    /// </summary>
    Task SetNetworkReachableAsync(bool reachable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Überträgt die Einstellungen auf den laufenden Core (AP5.5).
    ///
    /// Der Core liegt hinter dieser Grenze (§6), der
    /// <c>SettingsApplier</c> braucht ihn aber — deshalb geht der Weg über den
    /// Dienst statt umgekehrt. Was einen Neustart braucht, sagt
    /// <c>SettingsApplier.RequiresRestart</c>; hier wird angewendet, was
    /// sofort wirkt.
    /// </summary>
    Task ApplySettingsAsync(Settings.NippSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Verfügbare Audiogeräte (§9.4, AP5.6).</summary>
    IReadOnlyList<AudioDeviceInfo> GetAudioDevices();

    /// <summary>
    /// Spielt eine Klangdatei zur Probe — auf dem <b>Klingelgerät</b>, wenn
    /// eines gewählt ist (§9.4).
    ///
    /// <para>Damit lässt sich ein Klingelton in den Einstellungen anhören,
    /// ohne sich anrufen zu lassen. Das Gerät ist dabei der Punkt: wer ein
    /// separates Klingelgerät eingestellt hat, will genau dort hören, ob der
    /// Ton kommt — bei einem stummen Klingeln ist die Datei der zweite
    /// Verdacht und das Gerät der erste.</para>
    ///
    /// <para>Wirft nicht: eine Probe, die nicht klappt, darf nichts kosten.
    /// Ein laufender Anruf bleibt unberührt.</para>
    /// </summary>
    void PlaySoundPreview(string path);

    /// <summary>
    /// Abonniert die Präsenz der übergebenen SIP-Adressen (BLF, §8.4, AP6.5).
    /// Jeder Aufruf ersetzt die bisherige Liste vollständig — so lässt sich
    /// eine Nebenstelle auch wieder abbestellen.
    ///
    /// §14.8: <b>nur Team-Nebenstellen.</b> Jedes Abonnement kostet die Anlage
    /// eine dauerhafte SUBSCRIBE-Beziehung; ein Adressbuch mit tausend
    /// Outlook-Kontakten würde sie in die Knie zwingen. Die Begrenzung
    /// durchzusetzen ist Sache des Aufrufers, nicht dieses Dienstes — er kennt
    /// die Herkunft einer Adresse nicht.
    /// </summary>
    Task WatchPresenceAsync(IReadOnlyList<string> sipAddresses, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entfernt ein Konto samt seinen Zugangsdaten. §9.1: „Ein Konto lässt sich
    /// löschen, wobei <c>AuthInfo</c> mitgelöscht wird — sonst bleiben
    /// Zugangsdaten verwaist liegen."
    /// </summary>
    Task RemoveAccountAsync(string identity, CancellationToken cancellationToken = default);

    /// <summary>Höchstzahl gleichzeitiger Konten (§20.2).</summary>
    static int MaxAccounts => 10;

    /// <summary>Fährt die Telefonie herunter und meldet die Konten ab.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);

    event EventHandler<RegistrationChangedEventArgs>? RegistrationChanged;
    event EventHandler<CallStateEventArgs>? CallStateChanged;
    event EventHandler<CallQualityEventArgs>? QualityUpdated;
    event EventHandler<PresenceEventArgs>? PresenceChanged;
    event EventHandler<AudioDevicesChangedEventArgs>? AudioDevicesChanged;

    /// <summary>
    /// Das automatische Annehmen (§9.6) ist gescheitert; der Anruf klingelt
    /// weiter.
    ///
    /// Die Oberfläche unterdrückt den Toast, solange die Einstellung gesetzt
    /// ist (§8.6). Ohne dieses Ereignis bliebe ein Anruf, der sich nicht
    /// annehmen liess, vollkommen unsichtbar — kein Fenster, kein Ton, keine
    /// Benachrichtigung.
    /// </summary>
    event EventHandler<CallInfo>? AutoAnswerFailed;

    /// <summary>
    /// Ein eingehender Anruf wurde abgewiesen, weil schon zwei Gespräche offen
    /// sind (§8.2).
    ///
    /// <para>§8.2 verlangt dafür ausdrücklich „eine klare Meldung". Ohne dieses
    /// Ereignis gab es sie nur im Protokoll: der Anrufer hörte besetzt, und am
    /// Arbeitsplatz war nichts davon zu sehen.</para>
    /// </summary>
    event EventHandler<CallRejectedEventArgs>? CallRejectedBusy;

    /// <summary>
    /// Wie eine Weiterleitung ausgegangen ist (W1.2, Befund B5).
    ///
    /// <para>Feuert erst, wenn es eine Antwort gibt — nicht beim Anstossen.
    /// Bis zum 13.09.2026 gab es dieses Ereignis nicht, und ein abgelehnter
    /// REFER war unsichtbar: kein Fehlertext, das Gespraech blieb je nach
    /// Anlage gehalten stehen, und im Protokoll stand Erfolg.</para>
    /// </summary>
    event EventHandler<TransferResultEventArgs>? TransferCompleted;

    /// <summary>
    /// «Nicht stören» hat sich geändert — eingeschaltet, aufgehoben oder
    /// abgelaufen (§10, ADR-055).
    /// </summary>
    event EventHandler<DoNotDisturb>? DoNotDisturbChanged;

    /// <summary>Ob und wie lange der Klingelton schweigt (§10, ADR-055).</summary>
    DoNotDisturb DoNotDisturb { get; }

    /// <summary>
    /// Schaltet den Klingelton für <paramref name="dauer"/> stumm;
    /// <c>null</c> hebt es auf.
    ///
    /// <para><b>Nur der Klingelton.</b> Anrufe kommen weiterhin an, der Toast
    /// erscheint, und die Anlage erfährt nichts davon.</para>
    /// </summary>
    void SetDoNotDisturb(TimeSpan? dauer);
}

/// <summary>
/// Die Ereignisschleife des SDK, getrennt vom Rest.
///
/// §6: <c>Core.Iterate()</c> muss regelmässig gerufen werden, sonst verarbeitet
/// das SDK keine Netzwerkereignisse und feuert keine Callbacks — auf Desktop
/// macht es das <b>nicht</b> selbst.
///
/// Warum ein eigenes Interface: der Takt kommt aus einem
/// <c>DispatcherQueueTimer</c> auf dem UI-Thread, und der gehört zu WinUI.
/// Nipp.Core soll keine UI-Abhängigkeit bekommen — also sagt Core hier nur,
/// <b>was</b> es braucht, und die App liefert den Takt.
/// </summary>
public interface ISipEventPump
{
    /// <summary>
    /// Einen Durchlauf der Ereignisschleife. Alle 20 ms zu rufen, solange die
    /// Anwendung läuft — auch wenn das Fenster geschlossen und die App nur im
    /// Infobereich ist (§10).
    ///
    /// <b>Nichts Blockierendes in den ausgelösten Callbacks</b> (§14.1): sie
    /// laufen auf demselben Thread. Ein <c>await</c> auf eine Netzwerkoperation
    /// friert die Oberfläche ein.
    /// </summary>
    void Pump();
}

/// <summary>
/// Ein dritter gleichzeitiger Anruf wurde versucht. §8.2 lässt höchstens zwei
/// zu und verlangt eine klare Meldung — der Text steht in
/// <see cref="SipErrorCatalog.DescribeTooManyCalls"/>.
/// </summary>
public sealed class TooManyCallsException : InvalidOperationException
{
    public TooManyCallsException()
        : base(SipErrorCatalog.DescribeTooManyCalls())
    {
    }

    public TooManyCallsException(string message)
        : base(message)
    {
    }

    public TooManyCallsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Zugangsdaten und Verbindungsparameter eines SIP-Kontos (§9.1, §9.2).
///
/// Das Passwort steht hier im Klartext, weil das SDK es so braucht — die
/// Ablage erfolgt über DPAPI (§11), und <see cref="ToString"/> gibt es
/// niemals aus.
/// </summary>
public sealed record SipAccountSettings
{
    public required string Username { get; init; }
    public required string Domain { get; init; }
    public required string Password { get; init; }

    /// <summary>Fällt auf <see cref="Username"/> zurück, wenn leer (§9.1).</summary>
    public string? AuthUserId { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>UDP, TCP oder TLS. §9.2 gibt TLS als Standard vor.</summary>
    public SipTransport Transport { get; init; } = SipTransport.Tls;

    public string? OutboundProxy { get; init; }

    /// <summary>Registrierungsdauer in Sekunden, Standard 600 (§9.1).</summary>
    public int ExpiresSeconds { get; init; } = 600;

    public string EffectiveAuthUserId =>
        string.IsNullOrWhiteSpace(AuthUserId) ? Username : AuthUserId;

    public string Identity => $"sip:{Username}@{Domain}";

    /// <summary>Ohne Passwort — diese Ausgabe darf in Logs und Diagnose (§9.6).</summary>
    public override string ToString() =>
        $"{Username}@{Domain} über {Transport}, Auth-ID '{EffectiveAuthUserId}', Expires {ExpiresSeconds} s";
}

/// <summary>Transportprotokoll für die SIP-Signalisierung (§9.2).</summary>
public enum SipTransport
{
    Udp,
    Tcp,
    Tls,
}
