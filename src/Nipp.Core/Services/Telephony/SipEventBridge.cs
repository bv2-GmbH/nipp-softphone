using Linphone;
using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Telephony.Model;

using LinphoneCore = Linphone.Core;

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
        CallStateChanged?.Invoke(this, new SdkCallStateEventArgs(call, state, message));

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
    internal sealed class SdkCallStateEventArgs(Call call, CallState state, string message) : EventArgs
    {
        public Call Call { get; } = call;

        public CallState State { get; } = state;

        public string Message { get; } = message;
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
