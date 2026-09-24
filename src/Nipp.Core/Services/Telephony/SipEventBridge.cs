using Linphone;
using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Telephony.Model;

using LinphoneCore = Linphone.Core;

// Linphone bringt einen eigenen CallStatus mit (den der Anrufliste des SDK),
// und der hat mit unserem nichts zu tun. Der Alias macht an jeder Stelle
// sichtbar, welcher gemeint ist — ohne ihn ist es ein CS0104, mit einem
// blossen using waere es eine stille Verwechslung.
using NippCallStatus = Nipp.Core.Services.Telephony.Model.CallStatus;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Übersetzt die Callbacks des SDK in .NET-Ereignisse.
///
/// <b>Warum es diese Klasse zwingend gibt:</b> §6 nimmt an, dass sich mehrere
/// Listener über <c>Core.AddListener</c> anmelden lassen. Das stimmt nicht —
/// der C#-Wrapper hat genau ein <c>Core.Listener</c>-Property mit 97
/// Delegate-Eigenschaften (docs/sdk-api-notes.md). Es kann also nur einen
/// Interessenten geben. Diese Klasse ist dieser eine Interessent und verteilt
/// weiter an beliebig viele .NET-Abonnenten.
///
/// <b>Wer die Delegates anderswo setzt, hängt die Bridge stillschweigend ab</b>
/// — ohne Fehler, ohne Meldung. Deshalb sind sie ausschliesslich hier zu setzen.
///
/// <b>Threading:</b> die Callbacks kommen auf dem Thread an, der
/// <c>Core.Iterate()</c> ruft — nach §6 der UI-Thread. Nichts hier darf
/// blockieren (§14.1).
/// </summary>
internal sealed class SipEventBridge : IDisposable
{
    private readonly LinphoneCore _core;
    private readonly ILogger _logger;
    private bool _disposed;

    public SipEventBridge(LinphoneCore core, ILogger logger)
    {
        _core = core;
        _logger = logger;

        var listener = core.Listener;

        // ADR-053: JEDER Callback laeuft durch den Waechter. Was hier
        // hineingereicht wird, ist ein Delegat, den natives Fremdgebiet ruft —
        // eine Ausnahme, die daraus austritt, sieht kein try dieses Prozesses
        // mehr. Wer hier einen Callback ohne Guard.Run setzt, nimmt genau
        // diesen Schutz wieder weg, und es faellt erst am Geraet auf.
        listener.OnAccountRegistrationStateChanged = (core, account, state, message) =>
            CallbackGuard.Run(_logger, nameof(OnAccountRegistrationStateChanged),
                () => OnAccountRegistrationStateChanged(core, account, state, message));

        listener.OnCallStateChanged = (core, call, state, message) =>
            CallbackGuard.Run(_logger, nameof(OnCallStateChanged),
                () => OnCallStateChanged(core, call, state, message));

        listener.OnNotifyPresenceReceived = (core, friend) =>
            CallbackGuard.Run(_logger, nameof(OnNotifyPresenceReceived),
                () => OnNotifyPresenceReceived(core, friend));

        // Diagnose fuer das Besetztlampenfeld (§8.4). Ohne diese beiden
        // weiss niemand, ob die Anlage den SUBSCRIBE angenommen hat.
        listener.OnSubscriptionStateChanged = (core, lev, state) =>
            CallbackGuard.Run(_logger, nameof(OnSubscriptionStateChanged),
                () => OnSubscriptionStateChanged(core, lev, state));

        listener.OnNotifyReceived = (core, lev, notifiedEvent, body) =>
            CallbackGuard.Run(_logger, nameof(OnNotifyReceived),
                () => OnNotifyReceived(core, lev, notifiedEvent, body));

        listener.OnAudioDevicesListUpdated = core =>
            CallbackGuard.Run(_logger, nameof(OnAudioDevicesListUpdated),
                () => OnAudioDevicesListUpdated(core));

        listener.OnGlobalStateChanged = (core, state, message) =>
            CallbackGuard.Run(_logger, nameof(OnGlobalStateChanged),
                () => OnGlobalStateChanged(core, state, message));

        // W1.2: das Ergebnis einer Weiterleitung. Bis zum 13.09.2026 war
        // dieser Callback nicht abonniert, und ein abgelehnter REFER damit
        // unsichtbar — das Protokoll meldete Erfolg, bevor die Anlage
        // geantwortet hatte.
        listener.OnTransferStateChanged = (core, transferred, state) =>
            CallbackGuard.Run(_logger, nameof(OnTransferStateChanged),
                () => OnTransferStateChanged(core, transferred, state));
    }

    /// <summary>Registrierungszustand eines Kontos hat sich geändert.</summary>
    public event EventHandler<RegistrationChangedEventArgs>? RegistrationChanged;

    /// <summary>
    /// Anrufzustand hat sich geändert. Trägt den rohen SDK-Anruf, weil nur
    /// <see cref="SipService"/> ihn einer eigenen Kennung zuordnen kann.
    /// Bleibt deshalb <c>internal</c> — dieser Typ verlässt die Schicht nicht.
    /// </summary>
    public event EventHandler<SdkCallStateEventArgs>? CallStateChanged;


    public event EventHandler<PresenceEventArgs>? PresenceChanged;

    public event EventHandler<EventArgs>? AudioDevicesChanged;

    /// <summary>
    /// Der Zustand einer laufenden Weiterleitung (W1.2). Traegt den rohen
    /// SDK-Anruf, weil nur <see cref="SipService"/> ihn einer eigenen Kennung
    /// zuordnen kann — wie bei <see cref="CallStateChanged"/>.
    /// </summary>
    public event EventHandler<SdkTransferStateEventArgs>? TransferStateChanged;

    private void OnAccountRegistrationStateChanged(
        LinphoneCore core,
        Account account,
        Linphone.RegistrationState state,
        string message)
    {
        var identity = account.Params?.IdentityAddress?.AsString() ?? "unbekannt";

        RegistrationChanged?.Invoke(this, new RegistrationChangedEventArgs(
            Status: MapRegistrationStatus(state),
            AccountIdentity: identity,
            Message: message));
    }

    /// <summary>
    /// Bildet den SDK-Zustand auf unseren ab.
    ///
    /// <b>Hier lag ein Fehler, und er ist lehrreich.</b> Ursprünglich stand
    /// hier „ein unbekannter Wert gilt als Fehler, nicht als Erfolg" — gut
    /// gemeint, aber falsch: das SDK kennt <c>Refreshing</c> (Wert 5), und das
    /// ist der ganz normale Vorgang, wenn eine Registrierung vor ihrem Ablauf
    /// erneuert wird. Der fiel in den Fehlerzweig, die LED wurde für eine halbe
    /// Sekunde rot, und im Protokoll stand „Zugangsdaten, Domain und Transport
    /// prüfen" für einen Vorgang, der unmittelbar danach erfolgreich war.
    ///
    /// Deshalb jetzt umgekehrt: <b>nur ein ausdrücklicher Fehlerzustand ist ein
    /// Fehler.</b> Ein unbekannter Wert ist ein Zwischenzustand — er wird
    /// protokolliert, damit er auffällt, aber er alarmiert niemanden.
    /// </summary>
    private RegistrationStatus MapRegistrationStatus(Linphone.RegistrationState state)
    {
        var name = state.ToString();

        if (MapKnownRegistrationStatus(name) is { } known)
        {
            return known;
        }

        // Ein Zustand, den diese Fassung nicht kennt. Kein Grund für eine
        // Fehlermeldung an den Benutzer, aber einer für einen Protokolleintrag
        // — beim nächsten SDK-Wechsel steht hier die Antwort.
        TelephonyLog.UnknownRegistrationState(_logger, name);
        return RegistrationStatus.InProgress;
    }

    /// <summary>
    /// Die Zustandsregel selbst — <b>einmal</b>, ohne Protokollierung, damit
    /// auch <c>SipService</c> sie benutzen kann statt einer eigenen Kopie.
    /// <c>null</c> heisst „diese Fassung kennt den Zustand nicht".
    ///
    /// <para>Der Anlass ist eine echte Regression: dieselbe Abbildung stand ein
    /// zweites Mal in <c>SipService.ReadDefaultAccountStatus</c>, dort ohne
    /// <c>Refreshing</c> und mit <c>Failed</c> als Auffangfall. Beim Erneuern
    /// der Registrierung überschrieb sie das richtige Ergebnis dieser Klasse:
    /// die Lampe wurde rot und das Protokoll meldete einen Fehler, den es nicht
    /// gab. Genau die Lehre aus CLAUDE.md — zwei Kopien einer Regel sind zwei
    /// Gelegenheiten, sie falsch zu haben, und eine, nur die eine zu
    /// korrigieren.</para>
    /// </summary>
    internal static RegistrationStatus? MapKnownRegistrationStatus(string? stateName) => stateName switch
    {
        "Ok" => RegistrationStatus.Registered,
        "Progress" or "Refreshing" => RegistrationStatus.InProgress,
        "Cleared" => RegistrationStatus.Unregistered,
        "None" => RegistrationStatus.None,
        "Failed" => RegistrationStatus.Failed,
        _ => null,
    };

    private void OnCallStateChanged(LinphoneCore core, Call call, CallState state, string message) =>
        CallStateChanged?.Invoke(this, new SdkCallStateEventArgs(call, state, message, Lies(call, state, message)));

    /// <summary>
    /// Der Anruf als eigener Wert — <b>einmal gelesen, an einer Stelle</b>
    /// (W2.1 Etappe B1, ADR-074).
    ///
    /// <para><b>Warum hier und nicht im Dienst.</b> Diese Klasse ist die
    /// Stelle, die das SDK uebersetzt; das ist ihr Zweck. Bis zum 24.09.2026
    /// las <c>SipService</c> mitten in seiner Zustandsmaschine fuenf
    /// Eigenschaften direkt am <c>Call</c> — und weil jede davon einen
    /// laufenden Anruf braucht, war die ganze Methode nur an einer echten
    /// Anlage pruefbar.</para>
    ///
    /// <para><b>Jeder Lesezugriff ist einzeln abgesichert</b>, und das ist
    /// kein Vorratsbau: das SDK wirft je nach Zustand, statt <c>null</c> zu
    /// liefern — vor der Verhandlung gibt es keinen Codec, und an einem
    /// beendeten Anruf nicht mehr jede Angabe. Gemeldet wird das einmal je
    /// Sitzung (W1.7), nicht bei jedem Ereignis.</para>
    /// </summary>
    private CallSnapshot Lies(Call call, CallState state, string message)
    {
        var status = MapCallStatus(state);

        return new CallSnapshot(
            Number: LiesAdresse(call, static c => c.RemoteAddress?.Username) ?? "unbekannt",
            DisplayName: LiesAdresse(call, static c => c.RemoteAddress?.DisplayName),
            Status: status,
            Message: message,
            IsIncomingNew: state is CallState.IncomingReceived or CallState.IncomingEarlyMedia,

            // Paragraf 9.4: «Ringing» steht fuer zwei verschiedene Dinge — mit
            // Early Media spielt die Anlage, ohne spielt das SDK selbst.
            EarlyMedia: state == CallState.OutgoingEarlyMedia,

            Codec: LiesCodec(call),
            Encryption: LiesVerschluesselung(call),

            // Nur beim letzten Ereignis: danach ist der Anruf aus der
            // Verwaltung, und der Grund stuende nirgends (Paragraf 20.3).
            EndReason: status is NippCallStatus.Ended or NippCallStatus.Failed
                ? LiesEndgrund(call, status.Value)
                : null,

            SdkAccountIdentity: LiesAdresse(call, static c => c.Params?.Account?.Params?.IdentityAddress?.AsStringUriOnly()),
            ToAddress: LiesAdresse(call, static c => c.ToAddress?.AsStringUriOnly()),
            RawState: state.ToString());
    }

    private string? LiesAdresse(Call call, Func<Call, string?> was)
    {
        try
        {
            var wert = was(call);
            return string.IsNullOrWhiteSpace(wert) ? null : wert;
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestAdresse", ex);
            return null;
        }
    }

    private string? LiesCodec(Call call)
    {
        try
        {
            return call.CurrentParams?.UsedAudioPayloadType?.MimeType;
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestCodec", ex);
            return null;
        }
    }

    private MediaEncryptionMode LiesVerschluesselung(Call call)
    {
        try
        {
            return call.CurrentParams?.MediaEncryption switch
            {
                MediaEncryption.None => MediaEncryptionMode.None,
                MediaEncryption.SRTP => MediaEncryptionMode.Srtp,
                MediaEncryption.ZRTP => MediaEncryptionMode.Zrtp,
                MediaEncryption.DTLS => MediaEncryptionMode.Dtls,
                _ => MediaEncryptionMode.Unknown,
            };
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestVerschluesselung", ex);
            return MediaEncryptionMode.Unknown;
        }
    }

    /// <summary>
    /// Warum das Gespraech endete (Paragraf 20.3). Das SDK nennt den Grund am
    /// Anruf; ohne brauchbare Angabe entscheidet der Zustand.
    ///
    /// <para>Ohne diese Auswertung stand in der Anrufliste „fehlgeschlagen",
    /// wenn das Ziel schlicht besetzt war.</para>
    /// </summary>
    private CallEndReason LiesEndgrund(Call call, NippCallStatus status)
    {
        Reason reason;

        try
        {
            reason = call.Reason;
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestAnrufende", ex);
            return status == NippCallStatus.Failed ? CallEndReason.Failed : CallEndReason.Normal;
        }

        return reason switch
        {
            Reason.Busy => CallEndReason.Busy,
            Reason.Declined or Reason.DoNotDisturb => CallEndReason.Declined,
            Reason.NotAnswered or Reason.TemporarilyUnavailable => CallEndReason.NoAnswer,

            // Ein echter Fehler ist nur, was einer ist: Netz, Anlage,
            // Berechtigung, Protokoll. Diese Liste ist ausdruecklich und
            // vollstaendig — der Rest faellt unten durch.
            Reason.IOError or Reason.NotFound or Reason.NotAcceptable
                or Reason.Forbidden or Reason.Unauthorized or Reason.NoMatch
                or Reason.Gone or Reason.MovedPermanently or Reason.AddressIncomplete
                or Reason.NotImplemented or Reason.BadGateway or Reason.ServerTimeout
                or Reason.SessionIntervalTooSmall or Reason.Unknown
                => CallEndReason.Failed,

            // Alles Uebrige entscheidet der Zustand, und das ist der wichtige
            // Teil dieser Regel.
            //
            // Vorher stand hier "_ => CallEndReason.Failed". Am 08.09.2026 kamen
            // drei eingehende Anrufe herein, klingelten 19 bis 23 Sekunden und
            // wurden dann vom ANRUFER abgebrochen (CANCEL, Q.850 cause 16 —
            // normal aufgelegt). In der Anrufliste standen sie als
            // "fehlgeschlagen". Das ist nicht nur ungenau, es hat den Verdacht
            // erzeugt, nipp habe etwas falsch gemacht: der Benutzer sah drei
            // Fehler, wo drei verpasste Anrufe waren.
            _ => status == NippCallStatus.Failed ? CallEndReason.Failed : CallEndReason.Normal,
        };
    }

    /// <summary>
    /// Der SDK-Zustand als eigener. <c>null</c> heisst: ein Zwischenschritt
    /// ohne eigene Aussage — der bisherige Zustand bleibt stehen.
    /// </summary>
    private static NippCallStatus? MapCallStatus(CallState state) => state switch
    {
        CallState.OutgoingInit or CallState.OutgoingProgress => NippCallStatus.Dialing,
        CallState.OutgoingRinging or CallState.OutgoingEarlyMedia => NippCallStatus.Ringing,
        CallState.IncomingReceived or CallState.IncomingEarlyMedia => NippCallStatus.Incoming,
        CallState.Connected or CallState.StreamsRunning or CallState.UpdatedByRemote
            or CallState.Updating or CallState.Resuming => NippCallStatus.Connected,
        CallState.Pausing or CallState.Paused => NippCallStatus.OnHold,
        CallState.PausedByRemote => NippCallStatus.RemoteOnHold,
        CallState.Error => NippCallStatus.Failed,
        CallState.End or CallState.Released => NippCallStatus.Ended,
        _ => null,
    };

    private void OnSubscriptionStateChanged(
        LinphoneCore core,
        Linphone.Event linphoneEvent,
        SubscriptionState state)
    {
        var resource = linphoneEvent.Resource?.AsStringUriOnly() ?? "unbekannt";
        var eventName = linphoneEvent.Name ?? "unbekannt";

        if (state != SubscriptionState.Error)
        {
            TelephonyLog.SubscriptionState(_logger, eventName, LogMasking.SipLine(resource), state.ToString());
            return;
        }

        var reason = linphoneEvent.Reason.ToString();
        var code = linphoneEvent.ErrorInfo?.ProtocolCode ?? 0;
        var phrase = linphoneEvent.ErrorInfo?.Phrase ?? string.Empty;

        // §15: die Meldung soll sagen, was zu tun ist. Bei genau diesem Fehler
        // ist die Antwort bekannt.
        var hint = reason switch
        {
            "BadEvent" => "Die Anlage kennt das Ereignis 'presence' nicht. Fuer ein "
                + "Besetztlampenfeld will sie vermutlich 'dialog' (RFC 4235) — das waere ein "
                + "Umbau, siehe docs/decisions.md.",
            "Forbidden" or "Declined" => "Die Anlage lehnt das Abonnement ab. Meist fehlt dem "
                + "Konto die Berechtigung, fremde Nebenstellen zu beobachten.",
            "NotFound" => "Die Anlage kennt diese Nebenstelle nicht. Die SIP-Adresse in den "
                + "Einstellungen pruefen.",
            _ => "Grund unklar. Der SIP-Code oben hilft der Anlagenadministration weiter.",
        };

        TelephonyLog.SubscriptionFailed(
            _logger, eventName, LogMasking.SipLine(resource), reason, code, phrase, hint);
    }

    /// <summary>
    /// Ein NOTIFY ist eingetroffen — die Gegenprobe zum SUBSCRIBE.
    ///
    /// Ein angenommenes Abonnement ohne NOTIFY bedeutet, dass die Anlage zwar
    /// zustimmt, aber nichts zu melden hat: typisch, wenn die beobachtete
    /// Nebenstelle ihre Präsenz nicht veröffentlicht. Auch das ist eine
    /// Antwort, und ohne diese Zeile wäre sie nicht von „gar nichts passiert"
    /// zu unterscheiden.
    /// </summary>
    private void OnNotifyReceived(
        LinphoneCore core,
        Linphone.Event linphoneEvent,
        string notifiedEvent,
        Content body)
    {
        var resource = linphoneEvent.Resource?.AsStringUriOnly() ?? "unbekannt";
        var size = body?.Size ?? 0;

        TelephonyLog.NotifyReceived(_logger, notifiedEvent, LogMasking.SipLine(resource), (int)size);
    }

    private void OnNotifyPresenceReceived(LinphoneCore core, Friend friend)
    {
        // §8.4: BLF-Zustände. Die Anzeige erfolgt farblich UND als Text —
        // das entscheidet die Oberfläche, hier wird nur übersetzt.
        // AsStringUriOnly, nicht AsString: letzteres liefert die Adresse MIT
        // Anzeigename — "152" <sip:152@pbx.example.ch>. Die Einstellungen
        // kennen sip:152@pbx.example.ch, und so fand der Vergleich nie zusammen:
        // zehn Zustaende kamen an, zehn Lampen blieben auf "unbekannt".
        var address = friend.Address?.AsStringUriOnly() ?? string.Empty;
        if (address.Length == 0)
        {
            return;
        }

        PresenceChanged?.Invoke(this, new PresenceEventArgs(address, MapPresence(friend)));
    }

    private static PresenceStatus MapPresence(Friend friend)
    {
        // ConsolidatedPresence fasst die Einzelmeldungen zusammen. Auch hier
        // über den Namen, damit ein neuer Wert nicht als "verfügbar" durchgeht.
        return friend.ConsolidatedPresence.ToString() switch
        {
            "Online" => PresenceStatus.Available,
            "Busy" => PresenceStatus.OnCall,
            "DoNotDisturb" => PresenceStatus.OnCall,
            "Away" => PresenceStatus.Away,
            "Offline" => PresenceStatus.Offline,
            _ => PresenceStatus.Unknown,
        };
    }

    /// <summary>
    /// Wie es der Weiterleitung ergeht (W1.2, Befund B5).
    ///
    /// <para><b>Was das SDK hier meldet</b>, ist der Zustand des <b>neuen</b>
    /// Anrufs, den die Uebergabe erzeugt — <c>OutgoingProgress</c>,
    /// <c>OutgoingRinging</c>, dann <c>Connected</c> oder <c>Error</c>.
    /// <paramref name="transferred"/> ist dagegen das <b>eigene</b> Gespraech,
    /// also das, dessen Kennung der Benutzer sieht.</para>
    /// </summary>
    private void OnTransferStateChanged(LinphoneCore core, Call transferred, CallState state) =>
        TransferStateChanged?.Invoke(this, new SdkTransferStateEventArgs(transferred, state));

    private void OnAudioDevicesListUpdated(LinphoneCore core) =>
        AudioDevicesChanged?.Invoke(this, EventArgs.Empty);

    private void OnGlobalStateChanged(LinphoneCore core, GlobalState state, string message) =>
        TelephonyLog.GlobalStateChanged(_logger, state.ToString(), message);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Die Delegates lösen, damit nach dem Herunterfahren keine Callbacks
        // mehr in abgeräumte Objekte laufen.
        try
        {
            var listener = _core.Listener;
            listener.OnAccountRegistrationStateChanged = null;
            listener.OnCallStateChanged = null;
            listener.OnNotifyPresenceReceived = null;
            listener.OnSubscriptionStateChanged = null;
            listener.OnNotifyReceived = null;
            listener.OnAudioDevicesListUpdated = null;
            listener.OnGlobalStateChanged = null;
        }
        catch (Exception)
        {
            // Wenn der Core schon abgeräumt ist, gibt es nichts mehr zu lösen.
        }
    }

    /// <summary>
    /// Anrufzustand mitsamt dem rohen SDK-Anruf. <c>internal</c>, weil dieser
    /// Typ die Schichtgrenze aus §6 nicht überschreiten darf.
    /// </summary>
    internal sealed class SdkCallStateEventArgs(Call call, CallState state, string message, CallSnapshot snapshot) : EventArgs
    {
        /// <summary>
        /// Der rohe Anruf. <b>Bleibt daneben stehen</b>, solange
        /// <c>SipService</c> ihn zum Ausfuehren braucht — fuer <c>Matches</c>,
        /// <c>Accept</c>, <c>Decline</c> und <c>Terminate</c>. Gelesen wird
        /// aus ihm nichts mehr: das steht in <see cref="Snapshot"/>.
        /// </summary>
        public Call Call { get; } = call;

        public CallState State { get; } = state;

        public string Message { get; } = message;

        /// <summary>
        /// Was das SDK ueber diesen Anruf sagt, einmal gelesen und ohne
        /// SDK-Typ (W2.1 Etappe B1). Daraus entscheidet <c>CallFlow</c>.
        /// </summary>
        public CallSnapshot Snapshot { get; } = snapshot;
    }

    /// <summary>
    /// Der Zustand einer Weiterleitung mitsamt dem rohen SDK-Anruf (W1.2).
    /// <c>internal</c> aus demselben Grund wie oben.
    /// </summary>
    internal sealed class SdkTransferStateEventArgs(Call transferred, CallState state) : EventArgs
    {
        /// <summary>Das eigene Gespraech, das uebergeben werden sollte.</summary>
        public Call Transferred { get; } = transferred;

        /// <summary>Der Zustand des NEUEN Anrufs, den die Uebergabe erzeugt.</summary>
        public CallState State { get; } = state;
    }
}
