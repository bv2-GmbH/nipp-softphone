using System.Collections.Concurrent;
using System.Globalization;
using Linphone;
using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony.Model;
using CallStatus = Nipp.Core.Services.Telephony.Model.CallStatus;
// Namensfallen, siehe docs/sdk-api-notes.md. Das SDK und unsere Modelle
// überschneiden sich bei mehreren Namen; die Aliase machen die Absicht
// eindeutig, statt sich auf die Auflösungsreihenfolge zu verlassen:
//
//   Core        -> Nipp.Core (unser Namespace!) statt Linphone.Core
//   CallStatus  -> mehrdeutig zwischen Linphone und unserem Modell
using LinphoneCore = Linphone.Core;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Die eine Klasse, die den Linphone-Core besitzt (§6).
///
/// Verantwortlich für Factory- und Core-Initialisierung, Konten, Anrufe und
/// die Übersetzung der SDK-Callbacks in .NET-Ereignisse mit eigenen,
/// SDK-freien Argumenttypen. Die Verdrahtung der Callbacks selbst liegt in
/// <see cref="SipEventBridge"/>.
///
/// <b>Threading:</b> alle SDK-Aufrufe und alle Callbacks laufen auf dem Thread,
/// der <see cref="Pump"/> ruft — nach §6 der UI-Thread. Deshalb ist hier nichts
/// gesperrt und nichts blockiert; die <c>Task</c>-Rückgaben der Methoden sind
/// bereits erledigt, wenn sie zurückkommen. Sie stehen in der Signatur, weil
/// §6 sie so vorgibt und weil ab P5 Operationen dazukommen, die wirklich warten
/// (Echo-Kalibrierung, Provisioning-Abruf).
/// </summary>
public sealed class SipService : ISipService, ISipEventPump, IDisposable
{
    private readonly ILogger<SipService> _logger;
    private readonly SdkLoadProbe _loadProbe;
    private readonly SettingsApplier _applier;
    private readonly SdkLogBridge _sdkLog;

    /// <summary>Zuordnung eigener Kennung zu SDK-Anruf. Der einzige Ort, an dem beides zusammenkommt.</summary>
    private readonly ConcurrentDictionary<CallHandle, TrackedCall> _calls = new();

    private LinphoneCore? _core;
    private SipEventBridge? _bridge;
    private SipAccountSettings? _account;

    /// <summary>
    /// Die Freundesliste fuer das Besetztlampenfeld (§8.4). Eine eigene, damit
    /// die vom SDK gepflegte Standardliste unangetastet bleibt.
    ///
    /// <b>Sie wird einmal angelegt und danach nur noch abgeglichen</b> — nicht
    /// bei jeder Synchronisation ersetzt, wie urspruenglich gebaut. Das SDK
    /// speichert Listen in seiner Datenbank (<c>friends_list</c>, UNIQUE auf
    /// <c>name</c>) und stellt sie beim Start wieder her; ein zweites
    /// <c>AddFriendList</c> mit demselben Namen verletzte den Index und kam als
    /// SEH-Ausnahme zurueck. Siehe <see cref="EnsurePresenceList"/>.
    /// </summary>
    private FriendList? _presenceList;

    /// <summary>Name der Liste — auch der Schluessel in der SDK-Datenbank.</summary>
    private const string PresenceListName = "nipp-blf";

    /// <summary>
    /// Zuletzt gemeldeter Abonnementzustand je Nebenstelle. Damit wird im
    /// Pump nur protokolliert, was sich geaendert hat — sonst stuende
    /// alle fuenf Sekunden dieselbe Zeile da.
    ///
    /// <para>Seit W2.1 Etappe B4 eine eigene Klasse ohne SDK-Typ: der
    /// Zustandsname kommt als Zeichenkette herein, und was er bedeutet,
    /// steht dort.</para>
    /// </summary>
    private readonly PresenceWatch _presenceWatch = new();

    /// <summary>
    /// Die beobachteten Friends, mit der Adresse als Schluessel.
    ///
    /// Eigene Sammlung statt <c>FriendList.Friends</c>: das Iterieren ueber
    /// die Liste des Wrappers warf im ersten Anlauf eine SEH-Ausnahme aus
    /// nativem Code („External component has thrown an exception"). Was wir
    /// selbst angelegt haben, kennen wir auch selbst.
    /// </summary>
    private readonly Dictionary<string, Friend> _presenceFriends =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset _lastPresenceCheck = DateTimeOffset.MinValue;
    private static readonly TimeSpan PresenceCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Die zuletzt uebertragenen Audio-Einstellungen.
    ///
    /// Gebraucht fuer den Hotplug (§9.4): nur wer weiss, welches Geraet
    /// gewuenscht war, kann erkennen, dass es verschwunden ist — die Liste der
    /// vorhandenen Geraete allein sagt das nicht.
    /// </summary>
    private AudioSettings? _audioSettings;

    /// <summary>
    /// Normalisiert Rufnummern nach §8.1 — mit dem Länderpräfix aus den
    /// Einstellungen, sobald diese übertragen wurden.
    ///
    /// <para>Liegt hier und nicht nur im ViewModel, weil sonst jeder Aufrufer
    /// selbst daran denken müsste. Beim Weiterleiten hat das keiner getan: aus
    /// „079 123 45 67" wurde <c>sip:079 123 45 67@domain</c>, und die Übergabe
    /// scheiterte an einer Adresse mit Leerzeichen. Eine Regel, ein Ort.</para>
    /// </summary>
    private NumberNormalizer _normalizer = new(null);

    /// <summary>Die Geraete beim letzten Ereignis, um Zu- und Abgaenge zu erkennen.</summary>
    private IReadOnlyList<AudioDeviceInfo> _knownDevices = [];

    private DateTimeOffset _lastQualityUpdate = DateTimeOffset.MinValue;
    private bool _disposed;

    /// <summary>§16.7: lokal, in P5 aus den Einstellungen.</summary>
    public string RecordingDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "recordings");

    /// <summary>§8.2: höchstens zwei gleichzeitige Gespräche.</summary>
    private const int MaxConcurrentCalls = 2;

    /// <summary>
    /// Wie lange das Herunterfahren höchstens auf die Abmeldung wartet:
    /// 50 × 20 ms = eine Sekunde. Länger sieht niemand beim Beenden zu.
    /// </summary>
    private const int ShutdownMaxIterations = 50;

    private const int ShutdownIterationDelayMs = 20;

    /// <summary>§20.2: höchstens zehn Konten.</summary>
    private const int MaxAccountCount = 10;

    /// <summary>
    /// Die eingerichteten Konten samt Zustand und Meldung (W2.1 Etappe B2).
    ///
    /// <para>Hier lagen bis zum 24.09.2026 <b>drei</b> Woerterbuecher
    /// nebeneinander, und jede Stelle, die eines anfasste, musste an die
    /// beiden anderen denken. Jetzt ist es eine Klasse ohne SDK — und damit
    /// eine, die sich ohne Anlage pruefen laesst.</para>
    /// </summary>
    private readonly AccountRegistry _accounts = new();

    /// <summary>
    /// §8.2: das Qualitätspanel wird im Sekundenrhythmus erneuert, nicht bei
    /// jedem Iterate — bei 20 ms wären das 50 Aktualisierungen pro Sekunde.
    /// </summary>
    private static readonly TimeSpan QualityInterval = TimeSpan.FromSeconds(1);

    public SipService(
        ILogger<SipService> logger,
        SdkLoadProbe loadProbe,
        SettingsApplier applier,
        SdkLogBridge sdkLog)
    {
        _logger = logger;
        _loadProbe = loadProbe;
        _applier = applier;
        _sdkLog = sdkLog;
    }

    /// <summary>
    /// AP5.5: Einstellungen auf den laufenden Core uebertragen.
    ///
    /// Ohne diesen Weg blieben die Einstellungen wirkungslos — der Applier
    /// braucht den Core, und der verlaesst diese Klasse nicht (§6).
    /// </summary>
    public Task ApplySettingsAsync(NippSettings settings, CancellationToken cancellationToken = default)
    {
        var core = _core;

        if (core is null)
        {
            return Task.CompletedTask;
        }

        _applier.Apply(core, settings);
        _sdkLog.SetVerbosity(settings.Advanced.Logging);
        _audioSettings = settings.Audio;
        _normalizer = new NumberNormalizer(settings.Advanced.CountryPrefix);
        _knownDevices = GetAudioDevices();

        // §9.6: „Anrufe automatisch annehmen". Die Einstellung wurde bisher
        // gespeichert, angezeigt und nirgends gelesen — ein Schalter ohne
        // Wirkung.
        _autoAnswer = settings.Advanced.AutoAnswer;

        return Task.CompletedTask;
    }

    /// <summary>§9.6: Anrufe automatisch annehmen. Standard aus.</summary>
    private bool _autoAnswer;

    public RegistrationStatus RegistrationStatus { get; private set; } = RegistrationStatus.None;

    public bool IsInitialized => _core is not null;

    public IReadOnlyList<CallInfo> ActiveCalls =>
        _calls.Values.Select(c => c.Info).Where(i => i.IsActive).ToList();

    public event EventHandler<RegistrationChangedEventArgs>? RegistrationChanged;
    public event EventHandler<CallStateEventArgs>? CallStateChanged;
    public event EventHandler<CallQualityEventArgs>? QualityUpdated;
    public event EventHandler<PresenceEventArgs>? PresenceChanged;
    public event EventHandler<AudioDevicesChangedEventArgs>? AudioDevicesChanged;
    public event EventHandler<IReadOnlyList<AccountStatus>>? AccountsChanged;
    public event EventHandler<CallInfo>? AutoAnswerFailed;

    public event EventHandler<CallRejectedEventArgs>? CallRejectedBusy;

    /// <inheritdoc />
    public event EventHandler<TransferResultEventArgs>? TransferCompleted;

    /// <inheritdoc />
    public event EventHandler<DoNotDisturb>? DoNotDisturbChanged;

    private DoNotDisturb _doNotDisturb = DoNotDisturb.Aus;

    /// <inheritdoc />
    public DoNotDisturb DoNotDisturb => _doNotDisturb;

    /// <inheritdoc />
    public void SetDoNotDisturb(TimeSpan? dauer)
    {
        _doNotDisturb = dauer is { } d
            ? DoNotDisturb.Fuer(DateTimeOffset.UtcNow, d)
            : DoNotDisturb.Aus;

        ApplyRingerSilence();

        TelephonyLog.DoNotDisturbChanged(
            _logger,
            _doNotDisturb.Describe(DateTimeOffset.UtcNow) ?? "aus");

        DoNotDisturbChanged?.Invoke(this, _doNotDisturb);
    }

    /// <summary>
    /// Setzt den Klingelton des Core — oder nimmt ihn weg (ADR-055).
    ///
    /// <para><b>Über <c>Core.Ring</c> und nicht über die Lautstärke.</b> Ein
    /// leerer Pfad heisst für liblinphone «spiel nichts»; die Lautstärke auf
    /// null zu drehen wäre eine zweite Stelle, an der «laut» steht, und sie
    /// gälte auch für das Gespräch selbst.</para>
    ///
    /// <para>Der eingestellte Ton wird beim Aufheben neu gesetzt, nicht
    /// gemerkt: <c>SettingsApplier</c> ist die eine Stelle, die weiss, welche
    /// Datei gilt, und ein zwischengespeicherter Pfad liefe auseinander,
    /// sobald jemand den Klingelton wechselt.</para>
    /// </summary>
    private void ApplyRingerSilence()
    {
        if (_core is not { } core)
        {
            return;
        }

        try
        {
            if (_doNotDisturb.IsActive(DateTimeOffset.UtcNow))
            {
                core.Ring = string.Empty;
                return;
            }

            if (_audioSettings is { } audio)
            {
                _applier.ApplyRingtoneOnly(core, audio);
            }
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "SetztKlingelton", ex);
        }
    }

    /// <summary>§20.2, §9.1: Konto für ausgehende Anrufe.</summary>
    public string? DefaultAccountIdentity
    {
        get => _accounts.DefaultIdentity;
        set
        {
            if (value is not null && !_accounts.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Das Konto {value} ist nicht eingerichtet. Erst anmelden, dann als Standard setzen.");
            }

            _accounts.DefaultIdentity = value;

            if (value is not null && _core is not null)
            {
                var sdkAccount = FindSdkAccount(_core, value);
                if (sdkAccount is not null)
                {
                    _core.DefaultAccount = sdkAccount;
                }
            }

            RaiseAccountsChanged();
        }
    }

    public IReadOnlyList<AccountStatus> Accounts => _accounts.Snapshot();

    /// <summary>
    /// §9.4 und ADR-006: die Einstellung sagt „ein", der Canceller schaltet
    /// sich bei 8 kHz aber selbst ab. Hier steht, was tatsächlich gilt.
    /// </summary>
    public bool IsEchoCancellationEffective
    {
        get
        {
            var core = _core;
            if (core is null || !core.EchoCancellationEnabled)
            {
                return false;
            }

            // Ohne laufendes Gespräch gibt es nichts zu unterdrücken; dann
            // zählt die Einstellung.
            var running = _calls.Values.FirstOrDefault(c => c.Info.Status == CallStatus.Connected);
            if (running is null)
            {
                return true;
            }

            // Der Canceller unterstützt 8 kHz nicht. Der verhandelte Codec
            // verrät die Abtastrate.
            try
            {
                var clockRate = running.SdkCall.CurrentParams?.UsedAudioPayloadType?.ClockRate ?? 0;
                return clockRate > 8000;
            }
            catch (Exception ex)
            {
                QuietFailures.Report(_logger, "LiestAbtastrate", ex);
                return false;
            }
        }
    }

    public Task InitializeAsync(
        NippSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_core is not null)
        {
            return Task.CompletedTask;
        }

        // §10: Konfiguration unter %APPDATA%\nipp
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "nipp");

        // Setzt Factory-Verzeichnisse und Ressourcenpfade, prüft die Kette.
        // Muss vor CreateCore laufen (§6).
        var load = _loadProbe.Probe(configDirectory);
        if (!load.Loaded)
        {
            throw new InvalidOperationException(SipErrorCatalog.DescribeSdkUnavailable(load));
        }

        var factory = Factory.Instance;

        // §4, §9.6: die Meldungen des SDK gehoeren ins Protokoll von nipp.
        // Vor CreateCore, damit auch der Start des Core drinsteht.
        _sdkLog.Attach();

        _core = factory.CreateCore(
            configPath: Path.Combine(configDirectory, "linphonerc"),
            factoryConfigPath: null,
            systemContext: IntPtr.Zero);

        ApplyBaselineConfiguration(_core);

        // §6: nur EIN Listener pro Core. Die Bridge ist der einzige Ort, der
        // die Delegates setzt — wer sie anderswo überschreibt, hängt sie ab.
        _bridge = new SipEventBridge(_core, _logger);
        _bridge.RegistrationChanged += OnBridgeRegistrationChanged;
        _bridge.CallStateChanged += OnBridgeCallStateChanged;
        _bridge.PresenceChanged += (_, e) => PresenceChanged?.Invoke(this, e);
        _bridge.AudioDevicesChanged += OnBridgeAudioDevicesChanged;
        _bridge.TransferStateChanged += OnBridgeTransferStateChanged;

        if (settings is not null)
        {
            ApplyTransportPorts(_core, settings);
        }

        _core.Start();

        TelephonyLog.CoreStarted(_logger, _core.GlobalState.ToString());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Grundeinstellungen, die unabhängig vom Konto gelten. Ab P5 kommen sie
    /// aus <c>SettingsSchema</c>; bis dahin stehen hier die Vorgaben aus §9.
    /// </summary>
    /// <summary>
    /// Setzt den SIP-Port aus den Einstellungen (§9.2, ADR-019 Nachtrag).
    ///
    /// <para><b>Vor <c>Start()</c>, und nur dort.</b> <c>Transports</c> legt
    /// fest, worauf der Core lauscht; im Betrieb zu wechseln hiesse, alle
    /// Sockets neu aufzubauen und jede Registrierung kurz zu verlieren. Deshalb
    /// bleibt der Port in <c>SettingsApplier.RequiresRestart</c> — dieser
    /// Hinweis war schon immer richtig, nur hat bis zum 13.09.2026 auch ein
    /// Neustart nichts geändert: die Zeile hier fehlte.</para>
    ///
    /// <para>Ein Fehlschlag ist kein Grund, den Start abzubrechen: dann lauscht
    /// der Core auf seinem Standardport, und das ist immer noch ein
    /// funktionierendes Telefon. Er steht im Protokoll, weil «mein Port wirkt
    /// nicht» sonst wieder unbeantwortbar wäre.</para>
    /// </summary>
    private void ApplyTransportPorts(LinphoneCore core, NippSettings settings)
    {
        var (udp, tcp, tls) = TransportPorts.From(settings);

        if ((udp, tcp, tls) == (TransportPorts.SdkDefault, TransportPorts.SdkDefault, TransportPorts.SdkDefault))
        {
            return;
        }

        try
        {
            var transports = core.Transports;
            transports.UdpPort = udp;
            transports.TcpPort = tcp;
            transports.TlsPort = tls;
            core.Transports = transports;

            TelephonyLog.TransportPortsApplied(_logger, udp, tcp, tls);
        }
        catch (LinphoneException ex)
        {
            TelephonyLog.TransportPortsFailed(_logger, settings.Network.SipPort, ex.Message);
        }
    }

    private void ApplyBaselineConfiguration(LinphoneCore core)
    {
        // §2: Video ist ausgeschlossen. Capture und Display allein genügen
        // nicht — ohne die Policy bietet das SDK trotzdem Video an und meldet
        // selbst einen möglichen API-Fehlgebrauch (in AP2.3 so erlebt).
        core.VideoCaptureEnabled = false;
        core.VideoDisplayEnabled = false;
        var videoPolicy = core.VideoActivationPolicy;
        videoPolicy.AutomaticallyInitiate = false;
        videoPolicy.AutomaticallyAccept = false;
        core.VideoActivationPolicy = videoPolicy;

        // §9.3: SRTP anbieten, aber nicht erzwingen. Die aktuelle Anlage von
        // bv2 hat keine Verschlüsselung — mit Mandatory wäre kein Gespräch
        // möglich (ADR-007).
        if (core.MediaEncryptionSupported(MediaEncryption.SRTP))
        {
            core.MediaEncryption = MediaEncryption.SRTP;
        }

        core.MediaEncryptionMandatory = false;

        // §9.2: RTP-Portbereich. Im SDK unbelegt (-1/-1).
        core.SetAudioPortRange(7078, 7178);

        // §9.2: DSCP für Signalisierung (CS3) und Medien (EF)
        core.SipDscp = 24;
        core.AudioDscp = 46;

        // §9.4 und §9.5: Vorgaben, die im SDK anders stehen
        core.EchoCancellationEnabled = true;
        core.NoiseSuppressionEnabled = true;
        core.AdaptiveRateControlEnabled = true;
        core.UseRfc2833ForDtmf = true;
        core.UseInfoForDtmf = false;

        // §9.5: G.722 ist im SDK standardmässig AUS, soll aber Priorität 2
        // haben. PCMA und PCMU bleiben an — ohne sie scheitert die
        // Verhandlung mit vielen Trunks.
        EnablePayloadType(core, "G722", enable: true);
    }

    private void EnablePayloadType(LinphoneCore core, string mimeType, bool enable)
    {
        foreach (var payload in core.AudioPayloadTypes)
        {
            if (string.Equals(payload.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                payload.Enable(enable);
                TelephonyLog.PayloadTypeChanged(_logger, mimeType, enable);
                return;
            }
        }

        TelephonyLog.PayloadTypeMissing(_logger, mimeType);
    }

    public Task RegisterAccountAsync(SipAccountSettings account, CancellationToken cancellationToken = default)
    {
        var core = RequireCore();

        // §20.2: höchstens zehn Konten. Ein bereits eingerichtetes zu
        // ersetzen zählt nicht als neues.
        if (!_accounts.Contains(account.Identity)
            && _accounts.Count >= MaxAccountCount)
        {
            throw new InvalidOperationException(
                $"Es sind bereits {MaxAccountCount} Konten eingerichtet — mehr verwaltet nipp nicht. "
                    + "Ein nicht mehr benötigtes Konto entfernen, dann erneut versuchen.");
        }

        _account = account;
        _accounts.Set(account);

        // Das SDK speichert Konten in linphonerc und stellt sie beim Start
        // selbst wieder her — sichtbar daran, dass ein REGISTER schon läuft,
        // bevor diese Methode überhaupt gerufen wurde. Ohne das Aufräumen hier
        // käme bei jedem Start ein weiteres Konto derselben Identität dazu.
        RemoveExistingAccount(core, account.Identity);

        // Auth zuerst: ohne Zugangsdaten läuft der erste REGISTER ins 401 und
        // manche Anlagen sperren nach mehreren Versuchen.
        var authInfo = Factory.Instance.CreateAuthInfo(
            username: account.EffectiveAuthUserId,
            userid: account.EffectiveAuthUserId,
            passwd: account.Password,
            ha1: null,
            realm: null,
            domain: account.Domain);
        core.AddAuthInfo(authInfo);

        var accountParams = core.CreateAccountParams();
        var identity = Factory.Instance.CreateAddress(account.Identity);
        if (!string.IsNullOrWhiteSpace(account.DisplayName))
        {
            identity.DisplayName = account.DisplayName;
        }

        accountParams.IdentityAddress = identity;
        // ServerAddr (string) ist veraltet; ServerAddress erwartet eine Address.
        accountParams.ServerAddress = Factory.Instance.CreateAddress(
            $"sip:{account.Domain};transport={ToSdkTransport(account.Transport)}");
        accountParams.Transport = account.Transport switch
        {
            SipTransport.Udp => TransportType.Udp,
            SipTransport.Tcp => TransportType.Tcp,
            _ => TransportType.Tls,
        };
        accountParams.Expires = account.ExpiresSeconds;
        accountParams.RegisterEnabled = true;

        if (!string.IsNullOrWhiteSpace(account.OutboundProxy))
        {
            accountParams.RoutesAddresses = [Factory.Instance.CreateAddress(account.OutboundProxy)];
        }

        var sdkAccount = core.CreateAccount(accountParams);
        core.AddAccount(sdkAccount);

        // §9.1: das erste registrierte Konto ist Standard für ausgehende Anrufe.
        core.DefaultAccount ??= sdkAccount;

        // Set() hat das Standardkonto schon gesetzt, wenn es noch keines gab
        // — hier bleibt nur die Seite des SDK.

        RaiseAccountsChanged();

        TelephonyLog.AccountRegistering(_logger, account.ToString());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Entfernt ein bereits vorhandenes Konto derselben Identität, samt seinen
    /// Zugangsdaten.
    ///
    /// §9.1 verlangt beim Löschen eines Kontos ausdrücklich, dass die
    /// <c>AuthInfo</c> mitgeht — „sonst bleiben Zugangsdaten verwaist liegen".
    /// Dasselbe gilt hier: ein ersetztes Konto darf seine alten Zugangsdaten
    /// nicht zurücklassen.
    /// </summary>
    private void RemoveExistingAccount(LinphoneCore core, string identity)
    {
        var existing = core.AccountList
            .Where(a => string.Equals(
                a.Params?.IdentityAddress?.AsStringUriOnly(),
                identity,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var account in existing)
        {
            var username = account.Params?.IdentityAddress?.Username;
            var domain = account.Params?.IdentityAddress?.Domain;

            core.RemoveAccount(account);

            if (domain is { Length: > 0 })
            {
                // <b>Mit der Auth-ID suchen, nicht mit dem Benutzernamen.</b>
                // Angelegt wird die AuthInfo mit EffectiveAuthUserId (§9.1
                // erlaubt eine abweichende Authentifizierungs-ID), gesucht wurde
                // sie bisher mit dem Benutzernamen aus der Identität. Wo beides
                // auseinanderging, fand FindAuthInfo nichts — und die
                // Zugangsdaten blieben genau so liegen, wie §9.1 es verbietet.
                //
                // Der Benutzername bleibt als zweiter Versuch: eine AuthInfo aus
                // einer früheren Fassung kann noch unter ihm stehen.
                var candidates = new List<string>();

                if (_accounts.TryGet(identity, out var known)
                    && known.EffectiveAuthUserId is { Length: > 0 } authId)
                {
                    candidates.Add(authId);
                }

                if (username is { Length: > 0 } && !candidates.Contains(username, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(username);
                }

                foreach (var candidate in candidates)
                {
                    if (core.FindAuthInfo(null, candidate, domain) is { } authInfo)
                    {
                        core.RemoveAuthInfo(authInfo);
                    }
                }
            }

            TelephonyLog.AccountReplaced(_logger, identity);
        }
    }

    /// <summary>
    /// §9.1: „Ein Konto lässt sich löschen, wobei AuthInfo mitgelöscht wird —
    /// sonst bleiben Zugangsdaten verwaist liegen."
    /// </summary>
    public Task RemoveAccountAsync(string identity, CancellationToken cancellationToken = default)
    {
        var core = RequireCore();

        RemoveExistingAccount(core, identity);

        var warStandard = string.Equals(_accounts.DefaultIdentity, identity, StringComparison.OrdinalIgnoreCase);

        // Die Registry ruecken das Standardkonto selbst nach — ein Standard,
        // den es nicht mehr gibt, ist eine leere Kontoauswahl.
        _accounts.Remove(identity);

        if (warStandard)
        {
            if (_accounts.DefaultIdentity is { } nachfolger)
            {
                var replacement = FindSdkAccount(core, nachfolger);
                if (replacement is not null)
                {
                    core.DefaultAccount = replacement;
                }
            }
        }

        RaiseAccountsChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Echo-Kalibrierung (§9.4, AP5.7). Läuft etwa 15 Sekunden und belegt
    /// dabei Mikrofon und Lautsprecher.
    ///
    /// Das SDK meldet das Ergebnis nicht über ein Ereignis, sondern legt es in
    /// <c>Core.EchoCancellationCalibration</c> ab — also wird gepollt, bis der
    /// Wert steht oder die Zeit abläuft. Das Warten läuft über
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> und blockiert den
    /// UI-Thread nicht (§14.1).
    /// </summary>
    public async Task<int?> CalibrateEchoAsync(CancellationToken cancellationToken = default)
    {
        var core = RequireCore();

        if (_calls.Values.Any(c => c.Info.IsActive))
        {
            throw new InvalidOperationException(
                "Die Echo-Kalibrierung braucht Mikrofon und Lautsprecher für sich. "
                    + "Zuerst das laufende Gespräch beenden.");
        }

        TelephonyLog.EchoCalibrationStarted(_logger);
        core.StartEchoCancellerCalibration();

        // §9.4 nennt etwa 15 Sekunden; mit Reserve auf 25 begrenzt.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(true);

            var value = core.EchoCancellationCalibration;

            // Negativ heisst „läuft noch" oder „fehlgeschlagen"; positiv ist
            // das Ergebnis in Millisekunden.
            if (value > 0)
            {
                TelephonyLog.EchoCalibrationDone(_logger, value);
                return value;
            }
        }

        TelephonyLog.EchoCalibrationFailed(_logger);
        return null;
    }

    private static Account? FindSdkAccount(LinphoneCore core, string identity) =>
        core.AccountList.FirstOrDefault(a => string.Equals(
            a.Params?.IdentityAddress?.AsStringUriOnly(),
            identity,
            StringComparison.OrdinalIgnoreCase));

    private void RaiseAccountsChanged() => AccountsChanged?.Invoke(this, Accounts);

    private static string ToSdkTransport(SipTransport transport) => transport switch
    {
        SipTransport.Udp => "udp",
        SipTransport.Tcp => "tcp",
        _ => "tls",
    };

    public Task<CallHandle> PlaceCallAsync(
        string destination,
        string? accountIdentity = null,
        CancellationToken cancellationToken = default)
    {
        var core = RequireCore();

        if (ActiveCalls.Count >= MaxConcurrentCalls)
        {
            throw new TooManyCallsException();
        }

        // §20.2: über ein bestimmtes Konto telefonieren. Ohne Angabe bleibt es
        // beim Standardkonto.
        var identity = accountIdentity ?? _accounts.DefaultIdentity;
        var chosen = identity is not null ? FindSdkAccount(core, identity) : null;

        // §8.2: beim zweiten Anruf geht das laufende Gespräch auf Halten.
        // Das SDK macht das meist selbst, aber nicht zuverlässig in jedem
        // Zustand — und zwei gleichzeitig offene Mikrofone wären das
        // schlechteste denkbare Verhalten. Deshalb ausdrücklich.
        //
        // Gemerkt wird, wer deswegen pausiert wurde: scheitert der Aufbau
        // gleich darauf, muss das erste Gespräch zurückkommen. Vorher blieb es
        // stumm auf Halten, ohne dass jemand sagen konnte warum — und
        // umgekehrt erst nach dem Invite zu pausieren wäre genau das Fenster
        // mit zwei offenen Mikrofonen, das oben ausgeschlossen wird.
        var pausedForThisCall = PauseOthers(except: null);

        var address = Factory.Instance.CreateAddress(ToDialableAddress(destination, identity));

        // Den Aufnahmepfad hier festlegen, nicht erst beim Einschalten der
        // Aufnahme: Call.Params sind read-only und stammen aus genau diesem
        // Aufruf. Aufgezeichnet wird trotzdem erst, wenn StartRecording
        // gerufen wird — der Pfad allein schreibt nichts.
        var inviteParams = core.CreateCallParams(null);

        // Nur setzen, wenn es einen Pfad gibt: RecordFile nimmt kein null, und
        // ein unbrauchbarer Aufnahmeordner darf den Anruf nicht verhindern.
        if (BuildRecordingPath(destination) is { } recordFile)
        {
            inviteParams.RecordFile = recordFile;
        }

        // §20.2: das Konto gehört an diesen Anruf, nicht an den Core.
        //
        // Vorher wurde dafür core.DefaultAccount umgesetzt — mit zwei Folgen,
        // die beide erst später auffielen: ReadDefaultAccountStatus meldete
        // danach den Zustand des zuletzt benutzten Kontos als Gesamtzustand,
        // und ein Anruf über ein Nebenkonto verschob dauerhaft, was „Standard"
        // heisst. CallParams.Account ist im Wrapper setzbar (Zeile 25368) und
        // gilt genau für diesen einen Anruf.
        if (chosen is not null)
        {
            inviteParams.Account = chosen;
        }

        Call? sdkCall;

        try
        {
            sdkCall = core.InviteAddressWithParams(address, inviteParams);
        }
        catch
        {
            ResumeAfterFailedSecondCall(pausedForThisCall);
            throw;
        }

        if (sdkCall is null)
        {
            ResumeAfterFailedSecondCall(pausedForThisCall);

            throw new InvalidOperationException(
                SipErrorCatalog.DescribeCallFailure(null, destination, core.IsMediaEncryptionMandatory));
        }

        var handle = Track(sdkCall, destination, CallDirection.Outgoing, identity);
        TelephonyLog.CallPlaced(_logger, handle.ToString(), LogMasking.Number(destination));
        return Task.FromResult(handle);
    }

    /// <summary>
    /// Holt Gespräche zurück, die nur für einen zweiten Anruf pausiert wurden,
    /// der dann nicht zustande kam.
    ///
    /// <para>Fehler beim Fortsetzen werden geschluckt: der Aufrufer bekommt
    /// gleich die eigentliche Ursache des gescheiterten Anrufs zu sehen, und
    /// die ist die nützlichere Meldung.</para>
    /// </summary>
    /// <summary>
    /// Holt das letzte verbliebene Gespräch zurück, wenn es allein auf Halten
    /// liegt (§8.2, T322).
    ///
    /// <para><b>Nur bei genau einem.</b> Bleiben zwei stehen, hat der Benutzer
    /// eines davon selbst gehalten und makelt gleich — da gehört nichts
    /// entschieden. Und bleibt keines, gibt es nichts zu tun.</para>
    ///
    /// <para><b>Ein Fehlschlag ist kein Drama</b> und wird protokolliert statt
    /// geworfen: das Gespräch läuft weiter, es ist nur gehalten, und der
    /// Benutzer kann es selbst zurückholen. Eine Ausnahme aus dem Pump-Tick
    /// wäre schlimmer als ein gehaltenes Gespräch (ADR-053).</para>
    /// </summary>
    private void ResumeLastRemainingCall()
    {
        var uebrig = _calls.Values.Where(c => c.Info.IsActive).ToList();

        if (uebrig.Count != 1 || uebrig[0].Info.Status != CallStatus.OnHold)
        {
            return;
        }

        try
        {
            uebrig[0].SdkCall.Resume();
            TelephonyLog.LastCallResumed(_logger, uebrig[0].Handle.ToString());
        }
        catch (Exception ex)
        {
            TelephonyLog.CallResumeFailed(_logger, uebrig[0].Handle.ToString(), ex.Message);
        }
    }

    private void ResumeAfterFailedSecondCall(List<TrackedCall> paused)
    {
        foreach (var call in paused)
        {
            try
            {
                call.SdkCall.Resume();
            }
            catch (Exception ex)
            {
                TelephonyLog.CallResumeFailed(_logger, call.Handle.ToString(), ex.Message);
            }
        }
    }

    /// <summary>
    /// Macht aus einer Kurzwahl eine wählbare Adresse.
    ///
    /// <b>Die Domäne kommt vom gewählten Konto</b>, nicht vom zuletzt
    /// eingerichteten. Vorher stand hier <c>_account</c> — das ist das Konto
    /// aus dem letzten <c>RegisterAccountAsync</c>. Bei zwei Konten auf
    /// verschiedenen Anlagen (§20.2) wählte nipp die Nebenstelle damit auf der
    /// falschen Anlage, und die Meldung lautete „Nummer unbekannt".
    /// </summary>
    private string ToDialableAddress(string destination, string? accountIdentity) =>
        CallAddressing.ToDialable(destination, DomainOf(accountIdentity));

    /// <summary>
    /// Die Domäne eines Kontos. Ohne Angabe die des Standardkontos, und wenn
    /// auch das nichts hergibt, die des zuletzt eingerichteten — irgendeine
    /// Domäne ist besser als keine.
    /// </summary>
    private string? DomainOf(string? accountIdentity)
    {
        var identity = accountIdentity ?? _accounts.DefaultIdentity;

        if (identity is not null && _accounts.TryGet(identity, out var settings))
        {
            return settings.Domain;
        }

        return _account?.Domain;
    }

    /// <summary>
    /// Hält alle verbundenen Gespräche ausser einem — vor dem Aufbau eines
    /// zweiten Anrufs und <b>ebenso vor dem Annehmen</b> (§8.2, §8.6
    /// „Annehmen und halten").
    ///
    /// <para>Die Begründung steht seit jeher an <c>PlaceCallAsync</c>: das SDK
    /// macht das meist selbst, aber nicht zuverlässig in jedem Zustand, und
    /// zwei gleichzeitig offene Mikrofone wären das schlechteste denkbare
    /// Verhalten. Für das Annehmen galt dasselbe — nur getan wurde es dort
    /// nicht.</para>
    /// </summary>
    /// <returns>Die Gespräche, die deswegen pausiert wurden.</returns>
    private List<TrackedCall> PauseOthers(CallHandle? except)
    {
        var paused = new List<TrackedCall>();

        foreach (var running in _calls.Values)
        {
            if (running.Handle == except || running.Info.Status != CallStatus.Connected)
            {
                continue;
            }

            try
            {
                running.SdkCall.Pause();
                paused.Add(running);
                TelephonyLog.CallPausedForSecond(_logger, running.Handle.ToString());
            }
            catch (Exception ex)
            {
                // Ein Gespräch, das sich nicht halten lässt, darf das Annehmen
                // des neuen nicht verhindern.
                TelephonyLog.CallPauseFailed(_logger, running.Handle.ToString(), ex.Message);
            }
        }

        return paused;
    }

    public Task AcceptAsync(CallHandle callHandle, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);

        // Auch beim Annehmen den Aufnahmepfad mitgeben — aus demselben Grund
        // wie beim Aufbau: später gesetzt wirkt er nicht.
        var acceptParams = RequireCore().CreateCallParams(tracked.SdkCall);

        if (BuildRecordingPath(tracked.Info.RemoteNumber) is { } recordFile)
        {
            acceptParams.RecordFile = recordFile;
        }

        // §8.6 „Annehmen und halten": das laufende Gespräch geht auf Halten,
        // bevor das neue verbunden wird.
        PauseOthers(callHandle);

        // Anrufe werden am Call angenommen, nicht am Core — §6 nimmt
        // Core.AcceptCall an, das gibt es nicht (docs/sdk-api-notes.md).
        Sdk(callHandle, "AcceptWithParams",
            "Der Anruf liess sich nicht annehmen. Vermutlich hat die Gegenseite "
                + "inzwischen aufgelegt.",
            () => tracked.SdkCall.AcceptWithParams(acceptParams));

        // Diese Zeile fehlte bis zum 08.09.2026, und ihr Fehlen hat einen
        // ganzen Tag Fehlersuche gekostet: nach mehreren nicht angenommenen
        // Anrufen liess sich im Protokoll NICHT unterscheiden, ob der Benutzer
        // nicht gedrueckt hatte oder ob sein Druck verlorenging. Die
        // SIP-Antwort steht zwar im Trace, aber nur wenn das Annehmen bis zum
        // SDK kam — genau die Frage war ja offen.
        TelephonyLog.CallAccepted(_logger, callHandle.ToString());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Baut den Aufnahmepfad nach dem Schema aus §8.2:
    /// <c>JJJJ-MM-TT_HHMMSS_&lt;Nummer&gt;.wav</c>. Der Zeitstempel ist der des
    /// Anrufaufbaus, weil der Pfad zu diesem Zeitpunkt feststehen muss.
    /// </summary>
    private string? BuildRecordingPath(string remoteNumber)
    {
        try
        {
            Directory.CreateDirectory(RecordingDirectory);

            // Der Name selbst ist eine reine Rechnung und steht in
            // CallAddressing (W2.1 Etappe B5); hier bleibt, was das
            // Dateisystem anfasst.
            var name = CallAddressing.RecordingFileName(remoteNumber, DateTimeOffset.Now);

            return Path.Combine(RecordingDirectory, name);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // Ein unbrauchbarer Aufnahmeordner darf keinen Anruf verhindern.
            // Vorher warf diese Methode mitten aus PlaceCallAsync heraus, und
            // die Ausnahme fing niemand — der Klick auf „Anrufen" hätte die
            // App mitgenommen. Ohne Pfad ist nur die Aufnahme nicht möglich,
            // und StartRecordingAsync sagt das dann auch.
            TelephonyLog.RecordingDirectoryUnusable(_logger, RecordingDirectory, ex.Message);
            return null;
        }
    }

    public Task HangUpAsync(CallHandle callHandle, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);
        if (tracked.SdkCall.State is not (CallState.End or CallState.Released))
        {
            Sdk(callHandle, "Terminate",
                "Das Gespräch liess sich nicht beenden. Es wird gleich von selbst getrennt; "
                    + "sonst hilft ein Neustart von nipp.",
                tracked.SdkCall.Terminate);
        }

        // Gegenstueck zu CallAccepted: im Protokoll muss stehen, ob ein
        // Gespraech vom Benutzer beendet wurde oder von der Gegenseite. Sonst
        // sieht ein Abbruch der Anlage aus wie ein Klick und umgekehrt.
        TelephonyLog.CallHungUp(_logger, callHandle.ToString());
        return Task.CompletedTask;
    }


    public Task TransferAsync(CallHandle callHandle, string destination, TransferMode mode, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);

        if (mode == TransferMode.Blind)
        {
            // Normalisieren wie beim Wählen (§8.1). Ohne das ging die Rohform
            // aus dem Eingabefeld in die Adresse: aus „079 123 45 67" wurde
            // «sip:079 123 45 67@domain». Es war die einzige Stelle, an der
            // eine eingetippte Nummer ungeprüft weitergereicht wurde.
            var normalized = _normalizer.Normalize(destination);

            // Transfer(string) ist veraltet; TransferTo erwartet eine Address.
            // Die Domäne kommt vom Konto dieses Gesprächs, nicht vom zuletzt
            // eingerichteten (§20.2).
            Sdk(callHandle, "TransferTo",
                "Die Weiterleitung liess sich nicht anstossen. Das Gespräch läuft weiter — "
                    + "die Nummer prüfen und noch einmal versuchen.",
                () => tracked.SdkCall.TransferTo(Factory.Instance.CreateAddress(
                    ToDialableAddress(normalized, tracked.Info.AccountIdentity))));
            // W1.2: «angestossen», nicht «uebergeben». Die Anlage hat noch
            // nicht geantwortet — was daraus wird, meldet
            // OnBridgeTransferStateChanged.
            TelephonyLog.CallTransferStarted(
                _logger, callHandle.ToString(), LogMasking.Number(normalized), "blind");
            return Task.CompletedTask;
        }

        // Begleitet: das Ziel muss ein zweites, bestehendes Gespräch sein.
        // Ohne dieses kann nicht angekündigt werden — genau das unterscheidet
        // die beiden Varianten (§8.2).
        //
        // Connected oder OnHold, nicht IsActive: der Wrapper verlangt für das
        // Ziel „a running call". IsActive schliesst Dialing, Ringing und
        // Incoming ein — und weil der Wrapper die int-Rückgabe von
        // TransferToAnother verschluckt (-1 bei Fehler, hier void), wäre der
        // Fehlschlag unsichtbar geblieben: das Protokoll meldete „übergeben",
        // passiert wäre nichts.
        var other = _calls.Values
            .FirstOrDefault(c => c.Handle != callHandle
                && c.Info.Status is CallStatus.Connected or CallStatus.OnHold)
            ?? throw new InvalidOperationException(
                "Begleitetes Weiterleiten braucht ein zweites, bereits verbundenes Gespräch "
                    + "mit dem Ziel. Zuerst das Ziel anrufen, warten, bis es abnimmt, "
                    + "ankündigen, dann übergeben.");

        Sdk(callHandle, "TransferToAnother",
            "Die Übergabe liess sich nicht anstossen. Beide Gespräche laufen weiter.",
            () => tracked.SdkCall.TransferToAnother(other.SdkCall));
        TelephonyLog.CallTransferStarted(
            _logger, callHandle.ToString(), LogMasking.Number(other.Info.RemoteNumber), "begleitet");
        return Task.CompletedTask;
    }

    public Task SetHoldAsync(CallHandle callHandle, bool onHold, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);

        // Pause und Resume scheitern beide, wenn das SDK den Anruf gerade
        // selbst umstellt — etwa weil die Gegenseite halt oder auflegt. Das
        // ist ein Rennen und kein Fehler des Benutzers; die Meldung sagt
        // deshalb, was gilt, und nicht, was schiefging.
        if (onHold)
        {
            Sdk(callHandle, "Pause",
                "Das Gespräch liess sich nicht halten. Es läuft weiter.",
                tracked.SdkCall.Pause);
        }
        else
        {
            Sdk(callHandle, "Resume",
                "Das Gespräch liess sich nicht fortsetzen. Es bleibt gehalten — "
                    + "noch einmal versuchen.",
                tracked.SdkCall.Resume);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Schaltet das Mikrofon für <b>dieses</b> Gespräch stumm.
    ///
    /// <c>Call.MicrophoneMuted</c> und nicht <c>Core.MicEnabled</c>: letzteres
    /// gilt für den ganzen Core. Bei zwei Gesprächen (§8.2) stummte „Stumm"
    /// damit beide, und nach dem Makeln stimmte der angezeigte Zustand nicht
    /// mehr mit dem tatsächlichen überein.
    /// </summary>
    public Task SetMutedAsync(CallHandle callHandle, bool muted, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);

        tracked.SdkCall.MicrophoneMuted = muted;
        UpdateInfo(callHandle, info => info with { IsMuted = muted });
        return Task.CompletedTask;
    }

    public Task SendDtmfAsync(CallHandle callHandle, char digit, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);

        Sdk(callHandle, "SendDtmf",
            "Der Tastenton liess sich nicht senden.",
            () => tracked.SdkCall.SendDtmf((sbyte)digit));

        return Task.CompletedTask;
    }

    public Task<bool> StartRecordingAsync(CallHandle callHandle, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);

        // Der Pfad steht seit dem Aufbau des Anrufs fest — Call.Params sind
        // read-only. Fehlt er, wurde der Anruf ohne Params aufgebaut, und
        // nachträglich lässt sich daran nichts ändern.
        var effectivePath = tracked.SdkCall.Params?.RecordFile;
        if (string.IsNullOrEmpty(effectivePath))
        {
            TelephonyLog.RecordingPathRejected(_logger, callHandle.ToString(), "(beim Aufbau nicht gesetzt)");
            return Task.FromResult(false);
        }

        Sdk(callHandle, "StartRecording",
            "Die Aufnahme liess sich nicht starten. Das Gespräch läuft weiter.",
            tracked.SdkCall.StartRecording);

        // §8.2: der Indikator ist Pflicht. Er hängt an diesem Zustand, damit
        // die Oberfläche ihn nicht vergessen kann. Der Pfad wandert mit in den
        // Verlauf (§8.3) — sonst findet niemand die Aufnahme wieder.
        UpdateInfo(callHandle, info => info with { IsRecording = true, RecordingPath = effectivePath });
        TelephonyLog.RecordingStarted(_logger, callHandle.ToString(), LogMasking.Path(effectivePath));
        return Task.FromResult(true);
    }

    public Task StopRecordingAsync(CallHandle callHandle, CancellationToken cancellationToken = default)
    {
        var tracked = Resolve(callHandle);
        var path = tracked.SdkCall.Params?.RecordFile;

        Sdk(callHandle, "StopRecording",
            "Die Aufnahme liess sich nicht beenden. Sie endet spätestens mit dem Gespräch.",
            tracked.SdkCall.StopRecording);

        UpdateInfo(callHandle, info => info with { IsRecording = false });

        // Prüfen, ob tatsächlich etwas geschrieben wurde. Eine Aufnahme, die
        // der Benutzer für gemacht hält und die es nicht gibt, ist schlimmer
        // als eine, die von vornherein nicht startet.
        if (path is { Length: > 0 })
        {
            var file = new FileInfo(path);
            if (file.Exists && file.Length > 0)
            {
                TelephonyLog.RecordingStopped(_logger, callHandle.ToString(), LogMasking.Path(path), file.Length);
            }
            else
            {
                TelephonyLog.RecordingEmpty(_logger, callHandle.ToString(), LogMasking.Path(path));

                // Ohne Inhalt gehört der Pfad nicht in den Verlauf: ein
                // Eintrag, der eine Aufnahme verspricht und auf eine leere
                // Datei zeigt, ist schlechter als gar keiner.
                UpdateInfo(callHandle, info => info with { RecordingPath = null });
            }
        }

        return Task.CompletedTask;
    }

    public Task SetNetworkReachableAsync(bool reachable, CancellationToken cancellationToken = default)
    {
        RequireCore().NetworkReachable = reachable;
        TelephonyLog.NetworkReachabilityChanged(_logger, reachable);
        return Task.CompletedTask;
    }

    public IReadOnlyList<AudioDeviceInfo> GetAudioDevices()
    {
        var core = _core;
        if (core is null)
        {
            return [];
        }

        // ExtendedAudioDevices, nicht AudioDevices: letzteres liefert nur das
        // erste Gerät je Typ und ist leer, wenn alle Geräte Type=Unknown
        // melden — genau das ist auf der Testhardware der Fall
        // (docs/sdk-api-notes.md).
        return core.ExtendedAudioDevices
            .Select(d => new AudioDeviceInfo(
                Id: d.Id,
                Name: d.DeviceName,
                CanRecord: d.Capabilities.HasFlag(AudioDeviceCapabilities.CapabilityRecord),
                CanPlay: d.Capabilities.HasFlag(AudioDeviceCapabilities.CapabilityPlay)))
            .ToList();
    }

    /// <summary>
    /// Abonniert Präsenz für die übergebenen Adressen (BLF, §8.4, AP6.5).
    /// Jeder Aufruf beschreibt die vollständige Wunschliste; was fehlt, wird
    /// abbestellt, was neu ist, bestellt.
    ///
    /// <b>Warum nicht mehr „Liste weg, Liste neu".</b> So war es zuerst gebaut,
    /// mit der Begründung, ein Teilumbau hinterlasse verwaiste Abonnements.
    /// Tatsächlich hinterliess das Ersetzen einen Datenbankfehler: das SDK
    /// speichert Freundeslisten in <c>linphone.db</c> mit einem eindeutigen
    /// Namen, und das zweite <c>AddFriendList("nipp-blf")</c> im selben Lauf
    /// scheiterte am UNIQUE-Index — sichtbar als
    /// <c>Caught exception in MainDb::insertFriendList</c>, danach als
    /// SEH-Ausnahme hier. Die Anzeige überlebte das nur, weil die Zustände aus
    /// dem ersten Aufruf schon da waren; nach einer Team-Änderung wäre nichts
    /// Neues abonniert worden.
    ///
    /// Jetzt bleibt die Liste stehen. Abgeglichen wird über die eigene
    /// Sammlung <see cref="_presenceFriends"/>, Adressen werden mit
    /// <see cref="SipUri.Same"/> verglichen — das SDK meldet sie mit
    /// Anzeigename, die Einstellungen ohne.
    /// </summary>
    public Task WatchPresenceAsync(IReadOnlyList<string> sipAddresses, CancellationToken cancellationToken = default)
    {
        var core = _core;

        if (core is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var list = EnsurePresenceList(core);

            var wanted = sipAddresses
                .Where(static a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 1. Was nicht mehr gewünscht ist, geht raus — samt gemerktem Zustand,
            //    sonst zeigt die Liste einen Wert, den niemand mehr erneuert.
            foreach (var (address, friend) in _presenceFriends.ToList())
            {
                if (wanted.Any(w => SipUri.Same(w, address)))
                {
                    continue;
                }

                list.RemoveFriend(friend);
                _presenceFriends.Remove(address);

                // Sonst gilt ihr alter Zustand weiter, und wenn sie
                // zurueckkommt, bleibt der erste Wechsel stumm.
                _presenceWatch.Forget(address);
            }

            // 2. Was neu ist, kommt dazu.
            var added = 0;

            foreach (var address in wanted)
            {
                if (_presenceFriends.Keys.Any(known => SipUri.Same(known, address)))
                {
                    continue;
                }

                var friend = core.CreateFriendWithAddress(address);

                if (friend is null)
                {
                    // Eine unbrauchbare Adresse kostet nicht die ganze Liste.
                    TelephonyLog.PresenceAddressRejected(_logger);
                    continue;
                }

                // Edit/Done ist beim SDK Pflicht: ohne den Rahmen wird die
                // Änderung an einem Freund nicht übernommen.
                friend.Edit();
                friend.SubscribesEnabled = true;
                friend.IncSubscribePolicy = SubscribePolicy.SPAccept;
                friend.Done();

                list.AddFriend(friend);
                _presenceFriends[address] = friend;
                added++;
            }

            if (_presenceFriends.Count == 0)
            {
                list.SubscriptionsEnabled = false;
                TelephonyLog.PresenceWatchCleared(_logger);
                return Task.CompletedTask;
            }

            list.SubscriptionsEnabled = true;
            list.UpdateSubscriptions();

            TelephonyLog.PresenceWatchSynchronized(_logger, _presenceFriends.Count, added);
        }
        catch (Exception ex)
        {
            // Ohne Besetztlampenfeld telefoniert nipp weiter — das ist eine
            // Bequemlichkeit, keine Voraussetzung (§8.4).
            //
            // Mit vollständiger Ausnahme statt nur der Meldung: eine
            // SEHException aus nativem Code sagt in ihrer Meldung nichts
            // ("External component has thrown an exception"), und ohne
            // Stapelüberwachung war im Protokoll nicht zu erkennen, an welcher
            // der drei SDK-Operationen sie auftrat.
            TelephonyLog.PresenceWatchCrashed(_logger, ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Liefert die Liste für das Besetztlampenfeld — die aus diesem Lauf, die
    /// vom SDK aus der Datenbank wiederhergestellte, oder eine frisch angelegte;
    /// in dieser Reihenfolge.
    ///
    /// Eine wiederhergestellte Liste bringt ihre Friends mit. Die werden in
    /// <see cref="_presenceFriends"/> übernommen, damit der Abgleich sie kennt
    /// und nicht ein zweites Mal anlegt.
    /// </summary>
    private FriendList EnsurePresenceList(LinphoneCore core)
    {
        if (_presenceList is not null)
        {
            return _presenceList;
        }

        var restored = core.GetFriendListByName(PresenceListName);

        if (restored is not null)
        {
            try
            {
                foreach (var friend in restored.Friends)
                {
                    var address = friend.Address?.AsStringUriOnly();

                    if (address is { Length: > 0 })
                    {
                        _presenceFriends[address] = friend;
                    }
                }
            }
            catch (Exception ex)
            {
                // Dann kennt der Abgleich die alten Friends nicht und legt sie
                // erneut an — unschön, aber kein Ausfall.
                TelephonyLog.PresenceWatchFailed(
                    _logger, $"{ex.GetType().Name} beim Lesen der gespeicherten Liste: {ex.Message}");
            }

            TelephonyLog.PresenceListRestored(_logger, _presenceFriends.Count);
            _presenceList = restored;
            return restored;
        }

        var list = core.CreateFriendList();
        list.DisplayName = PresenceListName;
        core.AddFriendList(list);

        _presenceList = list;
        return list;
    }

    /// <summary>
    /// Ein Durchlauf der Ereignisschleife (§6). Wird vom Timer der App mit
    /// 20 ms gerufen. Nichts hier darf blockieren.
    /// </summary>
    public void Pump()
    {
        var core = _core;
        if (core is null)
        {
            return;
        }

        core.Iterate();

        // Zuerst und ohne Takt: ein Rufton, der nach dem Abnehmen noch
        // weiterspielt, ist im Gespräch zu hören.
        if (_ringbackStopPending)
        {
            _ringbackStopPending = false;
            StopRingback();
        }

        // §9.6: vorgemerkte automatische Annahmen — hier und nicht im
        // Callback, siehe AcceptPendingAutoAnswer.
        if (!_pendingAutoAnswer.IsEmpty)
        {
            AcceptPendingAutoAnswer();
        }

        // §8.2: der dritte Anruf, ebenfalls vorgemerkt statt im Callback
        // abgelehnt.
        while (_pendingDecline.TryDequeue(out var unwanted))
        {
            try
            {
                unwanted.Decline(Reason.Busy);
            }
            catch (Exception ex)
            {
                TelephonyLog.DeclineFailed(_logger, ex.Message);
            }
        }

        // §8.2: das letzte verbliebene Gespräch zurückholen, ebenfalls
        // vorgemerkt statt im Callback.
        if (_resumeLastPending)
        {
            _resumeLastPending = false;
            ResumeLastRemainingCall();
        }

        // §9.4: ein Gerätewechsel wurde gemeldet. Die Reaktion gehört hierher
        // und nicht in den Callback — siehe ReactToDeviceChange.
        if (_deviceChangePending)
        {
            _deviceChangePending = false;
            ApplyPendingDeviceChange();
        }

        // §10, ADR-055: «Nicht stören» läuft ab. Hier und nicht über einen
        // eigenen Taktgeber — der Pump tickt ohnehin alle 20 ms, und ein
        // zweiter Timer wäre eine zweite Stelle, an der der Zustand endet.
        if (_doNotDisturb.Until is not null && !_doNotDisturb.IsActive(DateTimeOffset.UtcNow))
        {
            SetDoNotDisturb(null);
        }

        // §9.4: der eigene Rufton beim Wählen, wenn die Anlage keinen schickt.
        var jetzt = DateTimeOffset.UtcNow;
        if (jetzt - _lastRingbackCheck >= RingbackInterval)
        {
            _lastRingbackCheck = jetzt;
            UpdateRingback(core, jetzt);
        }

        // §8.2: Qualitätswerte im Sekundenrhythmus, nicht bei jedem Iterate.
        var now = DateTimeOffset.UtcNow;
        if (now - _lastQualityUpdate < QualityInterval)
        {
            return;
        }

        _lastQualityUpdate = now;
        PublishQuality();

        if (now - _lastPresenceCheck >= PresenceCheckInterval)
        {
            _lastPresenceCheck = now;
            CheckPresenceSubscriptions();
        }
    }

    /// <summary>
    /// Meldet, was aus den Praesenz-Abonnements geworden ist (§8.4, Diagnose).
    ///
    /// <b>Warum als Abfrage und nicht als Ereignis.</b> Der erste Anlauf
    /// hing an <c>OnSubscriptionStateChanged</c> des Core — der gilt aber nur
    /// fuer Ereignisse aus <c>Core.Subscribe()</c>, nicht fuer die internen
    /// Abonnements einer <c>FriendList</c>. Zehn Nebenstellen waren abonniert,
    /// und es kam keine einzige Zeile. Der Zustand steht stattdessen an jedem
    /// <c>Friend</c>; ein Ereignis dafuer gibt es im Wrapper nicht.
    /// </summary>
    /// <summary>
    /// Die Zustaende der Praesenz-Abonnements nachsehen — alle fuenf Sekunden
    /// aus <see cref="Pump"/>.
    ///
    /// <para><b>Was gemeldet wird, entscheidet <see cref="PresenceWatch"/></b>
    /// (W2.1 Etappe B4): nur Wechsel, mit der Bedeutung daneben. Hier bleibt
    /// das Lesen am SDK — und das ist der Teil, der einen Friend erwischen
    /// kann, den das SDK nicht mehr kennt.</para>
    /// </summary>
    private void CheckPresenceSubscriptions()
    {
        if (_presenceList is null || _presenceFriends.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var (address, friend) in _presenceFriends)
            {
                string state;

                try
                {
                    state = friend.SubscriptionState.ToString();
                }
                catch (Exception ex)
                {
                    // Ein einzelner Friend, den das SDK nicht mehr kennt, kostet
                    // nicht die Pruefung der uebrigen.
                    TelephonyLog.PresenceWatchFailed(
                        _logger, $"{ex.GetType().Name} bei {address}: {ex.Message}");
                    continue;
                }

                if (_presenceWatch.Observe(address, state) is not { } meldung)
                {
                    continue;
                }

                if (meldung.IsError)
                {
                    TelephonyLog.PresenceSubscriptionFailed(
                        _logger, LogMasking.SipLine(meldung.Address), meldung.Hint);
                }
                else
                {
                    TelephonyLog.PresenceSubscriptionState(
                        _logger, LogMasking.SipLine(meldung.Address), meldung.State, meldung.Hint);
                }
            }
        }
        catch (Exception ex)
        {
            // Diagnose darf die Ereignisschleife nicht gefaehrden. Mit Typname:
            // eine SEHException sagt in ihrer Meldung nichts, ihr Typ dagegen
            // schon, naemlich „nativer Code".
            TelephonyLog.PresenceWatchFailed(_logger, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void PublishQuality()
    {
        foreach (var tracked in _calls.Values)
        {
            if (tracked.Info.Status is not CallStatus.Connected)
            {
                continue;
            }

            // Wie bei ReadCodec, ReadMos und ReadEncryption abgesichert: das
            // SDK wirft je nach Zustand, statt null zu liefern. Ohne das
            // verliesse die Ausnahme Pump(), und mit ihr fiele im selben Takt
            // die Praesenzpruefung aus — fuer eine Zahl, die nur angezeigt wird.
            CallQuality quality;

            try
            {
                var stats = tracked.SdkCall.AudioStats;

                if (stats is null)
                {
                    continue;
                }

                quality = new CallQuality(
                    RoundTripSeconds: stats.RoundTripDelay,
                    JitterBufferMilliseconds: stats.JitterBufferSizeMs,
                    ReceiverLossPercent: stats.ReceiverLossRate,
                    DownloadKbitPerSecond: stats.DownloadBandwidth,

                    // §8.2 nennt den geschaetzten MOS. Das SDK berechnet ihn
                    // laufend (1 bis 5); ausgelesen wurde er nie.
                    Mos: ReadMos(tracked.SdkCall));
            }
            catch (Exception ex)
            {
                QuietFailures.Report(_logger, "LiestGespraechsqualitaet", ex);
                continue;
            }

            QualityUpdated?.Invoke(this, new CallQualityEventArgs(tracked.Handle, quality));
        }
    }

    /// <summary>
    /// Die geschaetzte Sprachqualitaet des SDK (§8.2). <c>CurrentQuality</c>
    /// ist ein Mittel ueber die letzten Sekunden und damit das, was ein
    /// Benutzer gerade hoert; <c>AverageQuality</c> steht erst am Ende fest.
    /// </summary>
    private float ReadMos(Call call)
    {
        try
        {
            var value = call.CurrentQuality;

            // -1 heisst „noch kein Wert" — das SDK braucht einige Sekunden.
            return value > 0 ? value : 0;
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestMos", ex);
            return 0;
        }
    }

    /// <summary>
    /// Ein Konto meldet einen neuen Anmeldezustand (Paragraf 20.2).
    ///
    /// <para><b>Verbucht wird in <see cref="AccountRegistry"/></b> (W2.1
    /// Etappe B2); hier steht nur noch, was daraus folgt: melden,
    /// protokollieren, den wirksamen Gesamtzustand fuehren.</para>
    /// </summary>
    private void OnBridgeRegistrationChanged(object? sender, RegistrationChangedEventArgs e)
    {
        var ergebnis = _accounts.Record(
            e.AccountIdentity,
            e.Status,
            e.Message,

            // Paragraf 15: die Meldung nennt Ursache und Abhilfe, nicht den
            // rohen SDK-Text — und die Domaene des BETROFFENEN Kontos. Bei
            // zwei Konten auf verschiedenen Anlagen nannte die Fehlermeldung
            // vorher eine Anlage, die mit dem Fehler nichts zu tun hatte.
            static (text, settings) => SipErrorCatalog.DescribeRegistrationFailure(
                text,
                settings.Domain,
                !string.IsNullOrEmpty(settings.Password)));

        // <b>Nur eine echte Aenderung geht hinaus</b> (ADR-060). Eine
        // Erneuerung alle zehn Minuten aendert nichts, und an diesem Ereignis
        // haengen vier Empfaenger.
        if (ergebnis.AccountsChanged)
        {
            RaiseAccountsChanged();
        }

        // Nicht den Zustand aus dem Ereignis uebernehmen, sondern den des
        // Standardkontos abfragen.
        //
        // Grund: das SDK stellt beim Start gespeicherte Konten aus linphonerc
        // wieder her. Werden die ersetzt, senden sie ihre Abmeldung — und die
        // trifft je nach Laufzeit NACH der Anmeldung des neuen Kontos ein.
        // Wer dem letzten Ereignis glaubt, zeigt dann "Abgemeldet", obwohl
        // nipp registriert ist. Das Standardkonto ist die Wahrheit.
        RegistrationStatus = ReadDefaultAccountStatus() ?? e.Status;

        if (ergebnis.IsStale)
        {
            // <b>Ein Konto, das nipp nicht kennt, ist kein Alarm.</b> Es ist
            // das aus linphonerc wiederhergestellte, das gleich darauf
            // ersetzt wird. Am Geraet sah man eine Lampe, die kurz rot wurde,
            // und eine Fehlermeldung zu einem Konto, das in der Oberflaeche
            // gar nicht steht.
            //
            // <b>Kein return:</b> der wirksame Gesamtzustand geht unten
            // ohnehin hinaus, hier entfaellt nur die eigene Fehlermeldung.
            TelephonyLog.StaleAccountIgnored(_logger, e.AccountIdentity);
        }
        else if (e.Status == RegistrationStatus.Failed)
        {
            var erklaert = ergebnis.Message ?? e.Message ?? string.Empty;

            TelephonyLog.RegistrationFailed(_logger, erklaert);
            RegistrationChanged?.Invoke(this, e with { Message = erklaert });
            return;
        }

        // Den wirksamen Zustand protokollieren, nicht den rohen aus dem
        // Ereignis: sonst steht "Unregistered" im Log, wenn sich ein
        // ersetztes Altkonto abmeldet, waehrend nipp laengst registriert ist.
        // Ein Log, das dem widerspricht, was die App anzeigt, fuehrt den
        // Support in die Irre.
        TelephonyLog.RegistrationChanged(_logger, RegistrationStatus.ToString(), e.AccountIdentity);
        RegistrationChanged?.Invoke(this, e with { Status = RegistrationStatus });
    }

    /// <summary>
    /// Zustand des Standardkontos, oder <c>null</c>, wenn es keines gibt.
    ///
    /// <para>Die Abbildung steht <b>einmal</b>, in <see cref="SipEventBridge"/>.
    /// Hier stand vorher eine zweite Fassung ohne <c>Refreshing</c> und mit
    /// <c>Failed</c> als Auffangfall — und weil ihr Ergebnis das der Bridge
    /// überschrieb, wurde die Lampe bei jeder Erneuerung kurz rot und das
    /// Protokoll meldete einen Fehler, den es nicht gab.</para>
    /// </summary>
    private RegistrationStatus? ReadDefaultAccountStatus()
    {
        var account = _core?.DefaultAccount;

        if (account is null)
        {
            return null;
        }

        // Unbekannt heisst Zwischenzustand, nicht Fehler — dieselbe Regel wie
        // in der Bridge, die den Fall zusätzlich protokolliert.
        return SipEventBridge.MapKnownRegistrationStatus(account.State.ToString())
            ?? RegistrationStatus.InProgress;
    }

    /// <summary>
    /// Wie es der Weiterleitung ergeht (W1.2, Befund B5).
    ///
    /// <para><b>Was das SDK meldet, ist der Zustand des neuen Anrufs</b>, den
    /// die Übergabe erzeugt. <c>Connected</c> heisst: das Ziel hat abgenommen,
    /// die Übergabe steht. <c>Error</c> und <c>Released</c> vor dem Verbinden
    /// heissen: die Anlage oder das Ziel hat abgelehnt — ein 403, 404 oder 603
    /// auf den REFER.</para>
    ///
    /// <para>Bis zum 13.09.2026 war dieser Weg gar nicht abonniert.
    /// <c>TransferAsync</c> protokollierte «übergeben», bevor die Anlage
    /// geantwortet hatte, und ein Fehlschlag war unsichtbar.</para>
    /// </summary>
    private void OnBridgeTransferStateChanged(
        object? sender,
        SipEventBridge.SdkTransferStateEventArgs e)
    {
        // Das eigene Gespräch — nicht der neue Anruf. Nur seine Kennung kennt
        // der Benutzer, und nur sie steht in den Protokollzeilen davor.
        var tracked = _calls.Values.FirstOrDefault(c => c.Matches(e.Transferred));

        if (tracked is null)
        {
            return;
        }

        // Über den Namen, damit die Regel ohne SDK prüfbar bleibt und ein
        // unbekannter Zustand keine Fehlermeldung erfindet.
        var outcome = TransferOutcomes.From(e.State.ToString());

        TelephonyLog.TransferState(_logger, tracked.Handle.ToString(), e.State.ToString());

        if (outcome == TransferOutcome.InProgress)
        {
            return;
        }

        if (outcome == TransferOutcome.Failed)
        {
            TelephonyLog.TransferFailed(_logger, tracked.Handle.ToString());
        }
        else
        {
            TelephonyLog.CallTransferred(_logger, tracked.Handle.ToString());
        }

        TransferCompleted?.Invoke(this, new TransferResultEventArgs(tracked.Handle, outcome));
    }

    /// <summary>
    /// Der Zustand eines Anrufs hat sich geaendert.
    ///
    /// <para><b>Diese Methode entscheidet nichts</b> (W2.1 Etappe B1,
    /// ADR-074). Was das SDK sagt, steht als <c>CallSnapshot</c> im Ereignis;
    /// was daraus folgt, entscheidet <see cref="CallFlow"/>; hier wird nur
    /// ausgefuehrt. Vorher standen 165 Zeilen hier, und die sechs Regeln
    /// darin — jede einmal teuer bezahlt — waren nur als Kommentar
    /// festgehalten und nur an einer echten Anlage pruefbar.</para>
    ///
    /// <para><b>Was hier bleibt und nicht in CallFlow gehoert:</b> alles
    /// Zustandsaendernde. Ablehnen und Annehmen werden vorgemerkt und
    /// ausserhalb des Callbacks ausgefuehrt (ADR-053), der Anruf wird erst
    /// nach dem Ereignis aus der Verwaltung genommen, und die Zuordnung zum
    /// Konto braucht die Kontenliste dieses Dienstes.</para>
    /// </summary>
    private void OnBridgeCallStateChanged(object? sender, SipEventBridge.SdkCallStateEventArgs e)
    {
        var snapshot = e.Snapshot;
        var tracked = _calls.Values.FirstOrDefault(c => c.Matches(e.Call));

        var entscheidung = CallFlow.Decide(
            snapshot,
            tracked?.Info,
            ActiveCalls.Count,
            _autoAnswer,
            MaxConcurrentCalls,
            _ringback.IsPlaying);

        switch (entscheidung.Action)
        {
            case CallAction.Ignorieren:
                return;

            case CallAction.Ablehnen:
                AblehnenVormerken(e.Call, snapshot);
                return;

            case CallAction.Anlegen:
                tracked = Anlegen(e.Call, snapshot, entscheidung.AutoAnswer);
                break;
        }

        if (tracked is not null)
        {
            UebernehmenUndMelden(tracked, snapshot, entscheidung);
        }
    }

    /// <summary>
    /// Der dritte gleichzeitige Anruf (Paragraf 8.2).
    ///
    /// <para><b>Vormerken statt hier ablehnen:</b> ein <c>Decline</c> ist
    /// zustandsaendernd, und aus einem Callback heraus meldet das SDK die
    /// naechsten Zustaende mitten im laufenden Aufruf. Dieselbe Reentranz hat
    /// bei der automatischen Annahme schon einmal einen echten Fehler
    /// ergeben.</para>
    /// </summary>
    private void AblehnenVormerken(Call call, CallSnapshot snapshot)
    {
        TelephonyLog.IncomingCallRejected(_logger, LogMasking.Number(snapshot.Number));
        _pendingDecline.Enqueue(call);

        // Paragraf 8.2 verlangt „mit klarer Meldung" — bisher stand die nur
        // im Protokoll, wo sie niemand sucht.
        CallRejectedBusy?.Invoke(this, new CallRejectedEventArgs(snapshot.Number));
    }

    /// <summary>
    /// Ein eingehender Anruf, den nipp noch nicht kennt, kommt in die
    /// Verwaltung — und bekommt seine Kennung.
    /// </summary>
    private TrackedCall Anlegen(Call call, CallSnapshot snapshot, bool autoAnswer)
    {
        var handle = Track(call, snapshot.Number, CallDirection.Incoming, IdentityOf(snapshot));

        TelephonyLog.IncomingCall(_logger, handle.ToString(), LogMasking.Number(snapshot.Number));

        if (autoAnswer)
        {
            // Nur vormerken, nicht hier annehmen — siehe
            // AcceptPendingAutoAnswer.
            _pendingAutoAnswer.Enqueue(handle);
        }

        return _calls[handle];
    }

    /// <summary>
    /// Den neuen Stand uebernehmen, melden und aufraeumen — in dieser
    /// Reihenfolge, und die ist nicht beliebig.
    /// </summary>
    private void UebernehmenUndMelden(TrackedCall tracked, CallSnapshot snapshot, CallDecision entscheidung)
    {
        var info = CallFlow.Apply(tracked.Info, snapshot, DateTimeOffset.UtcNow);

        // Paragraf 15: im Fehlerfall die erklaerte Meldung, nicht den
        // SDK-Text. Sie braucht den Kern — ob Verschluesselung Pflicht ist,
        // steht dort und nicht am Anruf.
        if (info.Status == CallStatus.Failed)
        {
            var core = _core;
            info = info with
            {
                StatusMessage = SipErrorCatalog.DescribeCallFailure(
                    snapshot.Message,
                    info.RemoteNumber,
                    core?.IsMediaEncryptionMandatory ?? false),
            };
        }

        tracked.Info = info;
        tracked.EarlyMedia = snapshot.EarlyMedia;

        MerkeRuftonWeg(tracked, info);

        if (entscheidung.StopRingback)
        {
            // Hier nur vormerken: aus einem SDK-Callback wird das SDK nicht
            // angefasst.
            _ringbackStopPending = true;
        }

        if (entscheidung.Previous != info.Status)
        {
            // "neu" statt einer leeren Zeichenfolge: im Protokoll soll
            // erkennbar sein, dass es keinen Vorzustand gab, statt dass dort
            // nichts steht.
            TelephonyLog.CallStateChanged(
                _logger,
                tracked.Handle.ToString(),
                entscheidung.Previous?.ToString() ?? "neu",
                info.Status.ToString());
        }

        CallStateChanged?.Invoke(this, new CallStateEventArgs(info, entscheidung.Previous));

        // Beendete Anrufe aus der Verwaltung nehmen, aber erst nachdem das
        // Ereignis draussen ist — sonst findet die Oberflaeche den Anruf
        // nicht mehr, den sie gerade abschliessen will.
        if (entscheidung.Remove)
        {
            _calls.TryRemove(tracked.Handle, out _);
        }

        if (entscheidung.ResumeLast)
        {
            // Bleibt genau ein Gespraech uebrig und liegt es auf Halten,
            // gehoert es zurueckgeholt (ADR-073, T322) — sonst sitzt der
            // Benutzer vor einem stummen Gespraech, das er selbst nie
            // gehalten hat.
            _resumeLastPending = true;
        }
    }

    /// <summary>
    /// Der Zeitpunkt des SIP-Ereignisses, ab dem die Karenzzeit laeuft — und
    /// die eine Zeile, die sagt, welcher Weg beim Laeuten spielt.
    ///
    /// <para>Beide Wege spielen dieselbe Datei; am Gehoer sind sie nicht zu
    /// unterscheiden. Ohne diese Zeile ist im Protokoll nicht zu sehen, wer
    /// gerade klingelt — und genau das hat die Suche nach dem Fremdton
    /// zweimal aufgehalten.</para>
    /// </summary>
    private void MerkeRuftonWeg(TrackedCall tracked, CallInfo info)
    {
        if (info.Status != CallStatus.Ringing
            || info.Direction != CallDirection.Outgoing
            || tracked.RingingSince is not null)
        {
            return;
        }

        tracked.RingingSince = DateTimeOffset.UtcNow;

        TelephonyLog.RingbackWay(
            _logger,
            tracked.EarlyMedia
                ? "Early Media — die Anlage hat Vorrang, nipp beobachtet"
                : "das SDK spielt seine Datei (180 ohne SDP)");
    }

    /// <summary>
    /// §9.6 und §8.6: Anrufe automatisch annehmen.
    ///
    /// <b>Warum das nicht im Callback geschieht.</b> Ein <c>Accept</c> aus
    /// dem Zustands-Callback heraus lässt das SDK sofort die nächsten
    /// Zustände melden — mitten im laufenden Aufruf. Der äussere Rahmen
    /// schreibt danach seinen längst veralteten Zustand („eingehend")
    /// zurück an den Anruf und meldet ihn nach aussen: die Oberfläche bot
    /// „Annehmen" für ein Gespräch an, das bereits stand. Deshalb wird hier
    /// nur vorgemerkt und im nächsten Durchlauf der Ereignisschleife
    /// angenommen, höchstens zwanzig Millisekunden später.
    ///
    /// Der Hinweiston aus §8.6 gehört dazu: ein Gespräch, das sich ohne
    /// jedes Zeichen selbst öffnet, ist ein offenes Mikrofon im Raum.
    /// </summary>
    private void AcceptPendingAutoAnswer()
    {
        while (_pendingAutoAnswer.TryDequeue(out var handle))
        {
            if (!_calls.TryGetValue(handle, out var tracked)
                || tracked.Info.Status != CallStatus.Incoming)
            {
                // In der Zwischenzeit beendet oder von Hand angenommen.
                continue;
            }

            try
            {
                var core = RequireCore();

                if (DefaultNotificationSoundPath() is { } sound)
                {
                    core.PlayLocal(sound);
                }

                var acceptParams = core.CreateCallParams(tracked.SdkCall);

                if (BuildRecordingPath(tracked.Info.RemoteNumber) is { } recordFile)
                {
                    acceptParams.RecordFile = recordFile;
                }

                // Auch hier halten statt zwei offene Mikrofone (§8.6).
                PauseOthers(handle);

                tracked.SdkCall.AcceptWithParams(acceptParams);

                TelephonyLog.CallAutoAnswered(_logger, handle.ToString());
            }
            catch (Exception ex)
            {
                // Ein gescheitertes automatisches Annehmen darf den Anruf nicht
                // verlieren — er klingelt dann eben weiter. Damit er in diesem
                // Fall nicht unbemerkt bleibt, wird die Oberfläche eigens
                // benachrichtigt: sie hat den Toast wegen der Einstellung
                // unterdrückt und muss ihn nachholen.
                TelephonyLog.AutoAnswerFailed(_logger, $"{ex.GetType().Name}: {ex.Message}");
                AutoAnswerFailed?.Invoke(this, tracked.Info);
            }
        }
    }

    /// <summary>
    /// Vorgemerkte Anrufe für die automatische Annahme. Abgearbeitet im
    /// nächsten <see cref="Pump"/>, nicht im Callback.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<CallHandle> _pendingAutoAnswer = new();

    /// <summary>
    /// Eingehende Anrufe, die abgelehnt werden sollen, weil schon zwei
    /// Gespräche offen sind (§8.2). Ebenfalls im nächsten <see cref="Pump"/>,
    /// nicht im Callback: <c>Decline</c> ist zustandsändernd, und das SDK meldet
    /// daraufhin sofort die nächsten Zustände — mitten im laufenden Aufruf.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<Call> _pendingDecline = new();

    /// <summary>
    /// Ein Gespräch ist beendet worden — falls genau eines übrig bleibt und
    /// auf Halten liegt, gehört es zurückgeholt (§8.2).
    ///
    /// <para><b>Warum eine Vormerkung und kein <c>Resume()</c> im Callback.</b>
    /// Dieselbe Reentranz wie bei <c>_pendingDecline</c>: <c>Resume</c> ist
    /// zustandsändernd, und aus dem Zustands-Callback heraus meldet das SDK
    /// die nächsten Zustände mitten im laufenden Aufruf.</para>
    /// </summary>
    private bool _resumeLastPending;

    /// <summary>
    /// Ob ein Gerätewechsel gemeldet wurde und noch zu beantworten ist.
    ///
    /// <para>Ein blosses Flag genügt: mehrere Wechsel kurz hintereinander
    /// führen ohnehin zu derselben Reaktion, nämlich die gespeicherte Wahl neu
    /// anzuwenden. Der Zustand wird beim Abarbeiten aus dem SDK gelesen, nicht
    /// aus dem Ereignis.</para>
    /// </summary>
    private volatile bool _deviceChangePending;

    /// <summary>
    /// Ob ein laufender eigener Rufton sofort enden soll.
    ///
    /// <para><b>Warum ein Flag und nicht der Aufruf selbst.</b> Der Zustand
    /// wechselt in einem SDK-Callback, und dort wird nichts am SDK geändert
    /// (§14.1) — auch kein Tonspieler. Abgearbeitet wird es unmittelbar nach
    /// <c>Iterate()</c> im selben Durchlauf, also innerhalb von Millisekunden;
    /// über den 200-ms-Takt der Auswertung würde der Rufton hörbar in das
    /// gerade angenommene Gespräch hineinragen.</para>
    /// </summary>
    private bool _ringbackStopPending;

    /// <summary>Der Wächter, der über den eigenen Rufton entscheidet (§9.4).</summary>
    private readonly RingbackWatch _ringback = new();

    /// <summary>Der eigene Tonspieler für den Rufton. Nur zwischen Start und Ende belegt.</summary>
    private Player? _ringbackPlayer;

    /// <summary>Wann der eigene Rufton anfing — für die Spielzeit im Protokoll.</summary>
    private DateTimeOffset? _ringbackStartedAt;

    /// <summary>
    /// Die letzten Messwerte des empfangenen Stroms, für die Protokollzeilen.
    ///
    /// Damit die Zeile beim Aufhören dieselben Zahlen nennt, auf denen die
    /// Entscheidung beruhte, statt sie ein zweites Mal aus dem SDK zu holen.
    /// </summary>
    private RingbackMeasure? _lastRingbackMeasure;

    /// <summary>
    /// Für welchen Anruf der Beginn des fremden Audiostroms schon protokolliert
    /// ist. Die Zeile gehört genau einmal je Anruf ins Protokoll.
    /// </summary>
    private CallHandle? _remoteAudioLoggedFor;

    /// <summary>Was über den empfangenen Strom gemessen wurde.</summary>
    private readonly record struct RingbackMeasure(
        float Kbit,
        uint? Packets,
        float? JitterMs,
        float? VolumeDb);

    /// <summary>
    /// Der Tonspieler für die Klingelton-Probe aus den Einstellungen.
    ///
    /// Ein eigener und nicht der des Ruftons: die Probe läuft auf dem
    /// Klingelgerät, der Rufton auf der Wiedergabekarte, und beide könnten sich
    /// theoretisch überschneiden — jemand probiert einen Klingelton, während
    /// nebenher ein Anruf wählt.
    /// </summary>
    private Player? _previewPlayer;

    private DateTimeOffset _lastRingbackCheck;

    /// <summary>
    /// Wie oft der Rufton-Wächter befragt wird.
    ///
    /// Nicht bei jedem <c>Iterate()</c>: die Auswertung liest die
    /// Audio-Statistik des Anrufs aus dem nativen Code, und fünfzigmal in der
    /// Sekunde ist das für eine Entscheidung, die eine Karenz von 800 ms hat,
    /// verschwendet.
    /// </summary>
    private static readonly TimeSpan RingbackInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Ein kurzer Ton aus dem SDK-Paket für die automatische Annahme. Fehlt er,
    /// bleibt es still — dann ist der Hinweis weg, nicht die Funktion.
    /// </summary>
    private static string? DefaultNotificationSoundPath()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "share", "sounds", "linphone", "incoming_chat.wav");

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// §9.4, AP5.6: ein Gerät ist gekommen oder gegangen.
    ///
    /// „Ein während des Gesprächs verschwindendes Headset darf das Gespräch
    /// nicht abreissen lassen. Auf das Standardgerät zurückfallen und den
    /// Benutzer informieren."
    ///
    /// <b>Was hier früher stand und falsch war:</b> „das SDK fällt selbst auf
    /// das Standardgerät zurück, die Oberfläche muss es nur melden". Das erste
    /// war eine ungeprüfte Annahme, das zweite geschah nicht — das Ereignis
    /// wurde von niemandem abonniert. Jetzt wird beides ausdrücklich getan:
    /// die Wahl neu angewendet, das laufende Gespräch umgeschaltet, und ein
    /// fertiger Satz für die Oberfläche mitgegeben.
    /// </summary>
    private void OnBridgeAudioDevicesChanged(object? sender, EventArgs e)
    {
        // <b>Hier wird nur vorgemerkt.</b> Die Reaktion setzte Dutzende
        // Core-Eigenschaften, schaltete laufende Gespräche um und griff über
        // ApplyCertificates auf Dateien zu — alles mitten im SDK-Callback, den
        // §6 und §14.1 dafür ausschliessen. Die automatische Annahme macht es
        // gleich nebenan schon richtig; hier fehlte es.
        _deviceChangePending = true;
    }

    /// <summary>
    /// Beantwortet einen gemeldeten Gerätewechsel — gerufen aus
    /// <see cref="Pump"/>, also ausserhalb des SDK-Callbacks.
    /// </summary>
    private void ApplyPendingDeviceChange()
    {
        var devices = GetAudioDevices();
        TelephonyLog.AudioDevicesChanged(_logger, devices.Count);

        var notice = ReactToDeviceChange(devices);

        _knownDevices = devices;
        AudioDevicesChanged?.Invoke(this, new AudioDevicesChangedEventArgs(devices, notice));
    }

    /// <summary>
    /// Stellt die Gerätewahl wieder her, soweit möglich, und beschreibt in
    /// einem Satz, was der Benutzer merken wird.
    /// </summary>
    private string? ReactToDeviceChange(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var core = _core;

        if (core is null || _audioSettings is not { } audio)
        {
            return null;
        }

        // <b>Was der Wechsel bedeutet, entscheidet AudioDeviceChoice</b>
        // (W2.1 Etappe B3) — ohne SDK und damit ohne Geraet pruefbar. Hier
        // wird nur noch ausgefuehrt.
        var urteil = AudioDeviceChoice.Evaluate(
            _knownDevices,
            devices,
            [audio.InputDeviceId, audio.OutputDeviceId, audio.RingerDeviceId],
            !_calls.IsEmpty);

        // Die Wahl in jedem Fall neu anwenden: ein wieder eingestecktes Gerät
        // soll zurückkommen, ein verschwundenes wird von ApplyDevice
        // übergangen — dann gilt der Windows-Standard, und genau das will §9.4.
        //
        // ApplyAudioOnly und nicht Apply: das vollständige Apply überträgt auch
        // Netzwerk, NAT und Codecs, und ein hier gebautes NippSettings trüge
        // dort die Werkseinstellungen. Ein Kopfhörerwechsel hat schon einmal
        // still die Codec-Reihenfolge und die Zertifikatsprüfung
        // zurückgesetzt.
        try
        {
            _applier.ApplyAudioOnly(core, audio);
            RetargetRunningCalls(core);
        }
        catch (Exception ex)
        {
            TelephonyLog.AudioDeviceSwitchFailed(_logger, ex.Message);

            return AudioDeviceChoice.SwitchFailedNotice;
        }

        if (urteil.LostNames.Count > 0)
        {
            TelephonyLog.AudioDeviceLost(_logger, string.Join(", ", urteil.LostNames));
        }

        return urteil.Notice;
    }

    /// <summary>
    /// Schaltet laufende Gespräche auf die aktuellen Standardgeräte um.
    ///
    /// <c>DefaultInputAudioDevice</c> und <c>DefaultOutputAudioDevice</c>
    /// wirken nur auf <b>neue</b> Gespräche. Ein laufendes behält sein Gerät,
    /// bis man es ihm einzeln sagt — und das ist genau der Fall, um den es in
    /// §9.4 geht.
    /// </summary>
    private void RetargetRunningCalls(LinphoneCore core)
    {
        if (_calls.IsEmpty)
        {
            return;
        }

        var input = core.DefaultInputAudioDevice;
        var output = core.DefaultOutputAudioDevice;

        foreach (var tracked in _calls.Values)
        {
            // RemoteOnHold gehoert dazu: dort laeuft die eigene Aufnahmerichtung
            // weiter, und ein Geraetewechsel muss sie mitnehmen.
            if (tracked.Info.Status is not (CallStatus.Connected or CallStatus.OnHold or CallStatus.RemoteOnHold))
            {
                continue;
            }

            if (input is not null)
            {
                tracked.SdkCall.InputAudioDevice = input;
            }

            if (output is not null)
            {
                tracked.SdkCall.OutputAudioDevice = output;
            }
        }

        TelephonyLog.CallsRetargeted(_logger, _calls.Count);
    }

    /// <summary>
    /// Bildet den SDK-Zustand auf den eigenen ab.
    ///
    /// <para><c>null</c> heisst „dieser Zustand sagt nichts Neues" — der
    /// bisherige bleibt dann stehen. Das betrifft die Zwischenzustände beim
    /// Neuverhandeln (<c>Referred</c>, <c>EarlyUpdating</c>,
    /// <c>EarlyUpdatedByRemote</c>) und alles Unbekannte.</para>
    ///
    /// <para><b>Warum das nicht „Dialing" sein darf.</b> Vorher fiel jeder
    /// unbekannte Zustand auf <c>Dialing</c> — auch <c>Resuming</c>. Nach dem
    /// Makeln zeigte die Gesprächsansicht dadurch kurz „wird aufgebaut" für ein
    /// Gespräch, das längst stand; und <c>RetargetRunningCalls</c> übersprang
    /// solche Gespräche beim Gerätewechsel, weil es nur verbundene und
    /// gehaltene kennt.</para>
    /// </summary>
    /// <summary>
    /// Fragt den Wächter und führt aus, was er sagt (§9.4).
    ///
    /// <para>Läuft im <c>Pump()</c>, alle 200 ms, nie in einem Callback. Die
    /// Entscheidung selbst steht in <see cref="RingbackWatch"/> und ist dort
    /// ohne Gerät geprüft; hier steht nur der Weg zum SDK.</para>
    /// </summary>
    private void UpdateRingback(LinphoneCore core, DateTimeOffset now)
    {
        try
        {
            var sample = ReadRingbackSample();
            var action = _ringback.Update(sample, now);

            if (sample is not null)
            {
                var mass = _lastRingbackMeasure;

                TelephonyLog.RingbackTick(
                    _logger,
                    Zahl(mass?.Kbit),
                    Zahl(mass?.VolumeDb, "nicht messbar"),
                    mass?.Packets?.ToString(CultureInfo.InvariantCulture) ?? "unbekannt",
                    Zahl(mass?.JitterMs),
                    sample.IsEarlyMedia);

                // Genau einmal je Anruf: wann der Strom der Gegenstelle
                // eingesetzt hat. Der Messwert, der am 10.09.2026 fehlte.
                if (_ringback.StromBeginn is { } beginn && _remoteAudioLoggedFor != sample.Call)
                {
                    _remoteAudioLoggedFor = sample.Call;

                    TelephonyLog.RemoteAudioStarted(
                        _logger,
                        (int)beginn.TotalMilliseconds,
                        mass?.Packets?.ToString(CultureInfo.InvariantCulture) ?? "unbekannt");
                }
            }

            switch (action)
            {
                case RingbackAction.Start:
                    StartRingback(core, now);
                    break;

                case RingbackAction.Restart:
                    RestartRingback();
                    break;

                case RingbackAction.Stop:
                    StopRingback();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Ein Ton darf die Ereignisschleife nicht gefaehrden — dieselbe
            // Linie wie bei PublishQuality. Mit Typname: eine SEHException
            // sagt in ihrer Meldung nichts, ihr Typ dagegen schon.
            TelephonyLog.RingbackFailed(_logger, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Der eine wählende Anruf, um den es gehen kann — oder <c>null</c>.
    ///
    /// Es gibt höchstens einen: §8.2 erlaubt zwei Gespräche, aber nur eines
    /// davon wählt. Läuten mehrere, entscheidet der erste; ein zweiter Rufton
    /// gleichzeitig wäre ohnehin nicht zu unterscheiden.
    /// </summary>
    private RingbackSample? ReadRingbackSample()
    {
        foreach (var tracked in _calls.Values)
        {
            if (tracked.Info.Direction != CallDirection.Outgoing
                || tracked.Info.Status != CallStatus.Ringing)
            {
                continue;
            }

            var mass = ReadRingbackMeasure(tracked.SdkCall);
            _lastRingbackMeasure = mass;

            return new RingbackSample(
                tracked.Handle,
                tracked.EarlyMedia,
                mass.Kbit,
                PlayerIdle: _ringbackPlayer is null
                    || _ringbackPlayer.State != PlayerState.Playing,
                mass.VolumeDb,
                mass.Packets,
                tracked.RingingSince);
        }

        _lastRingbackMeasure = null;

        return null;
    }

    /// <summary>
    /// Was über den empfangenen Strom zu messen ist — <b>in einem Zugriff.</b>
    ///
    /// <para><b>Der Paketzähler ist der entscheidende Wert.</b>
    /// <c>DownloadBandwidth</c> ist ein Sekundenmittel und stand am 10.09.2026
    /// im Entscheidungsmoment beide Male auf 0, während der Strom der Anlage
    /// lief; <c>RtpPacketRecv</c> wird pro Paket geführt. Beides kommt aus
    /// derselben Statistik, also wird sie einmal geholt und nicht zweimal —
    /// jeder Zugriff kann werfen.</para>
    /// </summary>
    private RingbackMeasure ReadRingbackMeasure(Call call)
    {
        var volume = ReadPlayVolume(call);

        try
        {
            // Wie bei ReadCodec und ReadMos: das SDK wirft je nach Zustand,
            // statt null zu liefern. Keine Statistik heisst „es kommt nichts" —
            // und genau das ist der Fall, um den es geht.
            if (call.AudioStats is not { } stats)
            {
                return new RingbackMeasure(0f, null, null, volume);
            }

            return new RingbackMeasure(
                stats.DownloadBandwidth,
                stats.RtpPacketRecv,
                stats.JitterBufferSizeMs,
                volume);
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestRuftonmesswerte", ex);
            return new RingbackMeasure(0f, null, null, volume);
        }
    }

    /// <summary>
    /// Der gemessene Pegel des empfangenen Signals (dBm0), oder <c>null</c>,
    /// wenn er sich nicht ermitteln liess.
    ///
    /// <para>Wie bei <see cref="ReadDownloadBandwidth"/>: das SDK wirft je
    /// nach Zustand. <b>Hier heisst „keine Angabe" aber nicht „nichts zu
    /// hören"</b> — deshalb <c>null</c> und nicht 0. Eine 0 wäre in dBm0 sehr
    /// laut und würde den eigenen Rufton fälschlich abschalten, also genau den
    /// Fehler wiederholen, um den es geht.</para>
    /// </summary>
    private float? ReadPlayVolume(Call call)
    {
        try
        {
            var volume = call.PlayVolume;

            // linphone meldet mit einem sehr tiefen Wert, dass keine Messung
            // vorliegt. Der gilt hier als „nicht messbar", nicht als Stille —
            // stumm ist es auch dann, aber die Unterscheidung gehört dem
            // Aufrufer.
            return float.IsNaN(volume) ? null : volume;
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestPegel", ex);
            return null;
        }
    }

    /// <summary>
    /// Ein Messwert für das Protokoll, oder ein Strich. <b>Immer
    /// <c>InvariantCulture</c></b> — ein Protokoll mit Dezimalkommas ist von
    /// Werkzeugen nicht auszuwerten.
    /// </summary>
    private static string Zahl(float? wert, string fehlt = "-") =>
        wert is { } w ? w.ToString("0.0", CultureInfo.InvariantCulture) : fehlt;

    /// <summary>Der Grund in der Sprache des Protokolls.</summary>
    private static string GrundText(RingbackGrund grund) => grund switch
    {
        RingbackGrund.KeinStrom => "kein Strom der Gegenstelle",
        RingbackGrund.StillerStrom => "Strom ohne messbaren Pegel",
        RingbackGrund.HoerbaresAudio => "hoerbares Audio der Anlage",
        RingbackGrund.KeinEarlyMedia => "kein Early Media, das SDK spielt selbst",
        RingbackGrund.KeinWaehlenderAnruf => "kein waehlender Anruf mehr",
        RingbackGrund.AndererAnruf => "ein anderer Anruf waehlt",
        _ => "ohne Angabe",
    };

    private void StartRingback(LinphoneCore core, DateTimeOffset now)
    {
        var datei = NippSounds.Ringback();

        if (datei is null)
        {
            // Ohne Datei kein Ton. Gemeldet hat das schon der SettingsApplier
            // beim Start (2616); hier wäre es dieselbe Zeile bei jedem Anruf.
            return;
        }

        StopRingback();

        // Die Karte der ALTEN API — dieselbe, die SettingsApplier.ApplyToneCards
        // nachzieht. Der Tonspieler des SDK liest keine andere.
#pragma warning disable CS0612 // Veralteter Member: siehe ApplyToneCards
        var karte = core.PlaybackDevice;
#pragma warning restore CS0612

        var player = core.CreateLocalPlayer(karte, null, IntPtr.Zero);

        if (player is null)
        {
            TelephonyLog.RingbackFailed(_logger, "CreateLocalPlayer lieferte nichts");
            return;
        }

        player.Open(datei);
        player.Start();
        _ringbackPlayer = player;
        _ringbackStartedAt = now;

        var mass = _lastRingbackMeasure;

        TelephonyLog.RingbackStarted(
            _logger,
            karte ?? "(Standardkarte)",
            GrundText(_ringback.Grund),
            Zahl(mass?.Kbit),
            Zahl(mass?.VolumeDb, "nicht messbar"),
            mass?.Packets?.ToString(CultureInfo.InvariantCulture) ?? "unbekannt",
            (int)_ringback.Gewartet.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void PlaySoundPreview(string path)
    {
        var core = _core;

        if (core is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            StopPreview();

            // Das Klingelgeraet, wenn eines gewaehlt ist — der Klingelton wird
            // dort gespielt (ring_sndcard), nicht auf der Wiedergabekarte.
            // Beides steht nur in der alten API, dieselbe Begruendung wie in
            // SettingsApplier.ApplyToneCards.
#pragma warning disable CS0612 // Veralteter Member: siehe ApplyToneCards
            var karte = string.IsNullOrEmpty(core.RingerDevice)
                ? core.PlaybackDevice
                : core.RingerDevice;
#pragma warning restore CS0612

            var player = core.CreateLocalPlayer(karte, null, IntPtr.Zero);

            if (player is null)
            {
                return;
            }

            player.Open(path);
            player.Start();
            _previewPlayer = player;
        }
        catch (Exception ex)
        {
            // Eine Probe ist eine Bequemlichkeit. Scheitert sie, steht das im
            // Protokoll und sonst nichts — telefoniert wird unveraendert.
            TelephonyLog.SoundPreviewFailed(_logger, ex.GetType().Name);
        }
    }

    private void StopPreview()
    {
        var player = _previewPlayer;
        _previewPlayer = null;

        if (player is null)
        {
            return;
        }

        try
        {
            player.Pause();
            player.Close();
        }
        catch (Exception ex)
        {
            TelephonyLog.SoundPreviewFailed(_logger, ex.GetType().Name);
        }
    }

    private void RestartRingback()
    {
        var player = _ringbackPlayer;

        if (player is null)
        {
            return;
        }

        player.Seek(0);
        player.Start();
    }

    private void StopRingback()
    {
        var player = _ringbackPlayer;
        _ringbackPlayer = null;
        var begonnen = _ringbackStartedAt;
        _ringbackStartedAt = null;

        if (player is null)
        {
            return;
        }

        try
        {
            player.Pause();
            player.Close();
            var letztes = ReadRingbackSample();
            var mass = _lastRingbackMeasure;

            TelephonyLog.RingbackStopped(
                _logger,
                begonnen is { } start ? (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds : -1,
                GrundText(_ringback.Grund),
                Zahl(mass?.Kbit),
                Zahl(mass?.VolumeDb, "nicht messbar"),
                letztes is null ? "kein waehlender Anruf mehr" : letztes.IsEarlyMedia.ToString());
        }
        catch (Exception ex)
        {
            TelephonyLog.RingbackFailed(_logger, ex.GetType().Name);
        }
    }

    private static CallStatus? MapCallStatus(CallState state) => state switch
    {
        CallState.OutgoingInit or CallState.OutgoingProgress => CallStatus.Dialing,
        CallState.OutgoingRinging or CallState.OutgoingEarlyMedia => CallStatus.Ringing,
        CallState.IncomingReceived or CallState.IncomingEarlyMedia => CallStatus.Incoming,
        CallState.Connected or CallState.StreamsRunning or CallState.UpdatedByRemote
            or CallState.Updating or CallState.Resuming => CallStatus.Connected,
        CallState.Pausing or CallState.Paused => CallStatus.OnHold,
        CallState.PausedByRemote => CallStatus.RemoteOnHold,
        CallState.Error => CallStatus.Failed,
        CallState.End or CallState.Released => CallStatus.Ended,
        _ => null,
    };

    private string? ReadCodec(Call call)
    {
        try
        {
            return call.CurrentParams?.UsedAudioPayloadType?.MimeType;
        }
        catch (Exception ex)
        {
            // Vor der Verhandlung gibt es keinen Codec. Das SDK wirft dabei
            // je nach Zustand, statt null zu liefern — erwartet, aber einmal
            // je Sitzung sichtbar (W1.7).
            QuietFailures.Report(_logger, "LiestCodec", ex);
            return null;
        }
    }

    private MediaEncryptionMode ReadEncryption(Call call)
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

    private CallHandle Track(Call sdkCall, string number, CallDirection direction, string? accountIdentity)
    {
        var handle = CallHandle.New();
        var info = new CallInfo(
            Handle: handle,
            RemoteNumber: number,
            RemoteDisplayName: sdkCall.RemoteAddress?.DisplayName,
            Direction: direction,
            Status: direction == CallDirection.Incoming ? CallStatus.Incoming : CallStatus.Dialing,
            StatusMessage: null,
            StartedAt: DateTimeOffset.UtcNow,
            ConnectedAt: null,
            IsMuted: false,
            IsRecording: false,
            Codec: null,
            Encryption: MediaEncryptionMode.Unknown,
            AccountIdentity: accountIdentity);

        _calls[handle] = new TrackedCall(handle, sdkCall) { Info = info };
        return handle;
    }

    /// <summary>
    /// Zu welchem Konto ein eingehender Anruf gehört (§20.2).
    ///
    /// Zuerst über die Kontozuordnung des SDK, dann über die To-Adresse des
    /// INVITE. Bleibt beides ohne Treffer, gilt das Standardkonto — ein Anruf
    /// ohne zuordenbares Konto ist immer noch ein Anruf.
    /// </summary>
    /// <summary>
    /// Welches Konto zu diesem Anruf gehoert.
    ///
    /// <para><b>Ohne SDK-Zugriff</b> (W2.1 Etappe B1): die beiden Adressen
    /// stehen in der Momentaufnahme, die Zuordnung braucht die Kontenliste
    /// dieses Dienstes und bleibt deshalb hier.</para>
    /// </summary>
    private string? IdentityOf(CallSnapshot snapshot)
    {
        if (snapshot.SdkAccountIdentity is { Length: > 0 } fromSdk
            && _accounts.Contains(fromSdk))
        {
            return fromSdk;
        }

        if (snapshot.ToAddress is { Length: > 0 } to)
        {
            var matched = _accounts.Identities.FirstOrDefault(k => SipUri.Same(k, to));

            if (matched is not null)
            {
                return matched;
            }
        }

        return _accounts.DefaultIdentity;
    }

    /// <summary>
    /// Warum das Gespräch endete (§20.3). Das SDK nennt den Grund am Anruf;
    /// ohne brauchbare Angabe entscheidet der Zustand.
    ///
    /// Ohne diese Auswertung stand in der Anrufliste „fehlgeschlagen", wenn
    /// das Ziel schlicht besetzt war.
    /// </summary>
    private CallEndReason ReadEndReason(Call call, CallStatus status)
    {
        Reason reason;

        try
        {
            reason = call.Reason;
        }
        catch (Exception ex)
        {
            QuietFailures.Report(_logger, "LiestAnrufende", ex);
            return status == CallStatus.Failed ? CallEndReason.Failed : CallEndReason.Normal;
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
            // Teil dieser Aenderung.
            //
            // Vorher stand hier "_ => CallEndReason.Failed". Am 08.09.2026 kamen
            // drei eingehende Anrufe herein, klingelten 19 bis 23 Sekunden und
            // wurden dann vom ANRUFER abgebrochen (CANCEL, Q.850 cause 16 —
            // normal aufgelegt). In der Anrufliste standen sie als
            // "fehlgeschlagen". Das ist nicht nur ungenau, es hat den Verdacht
            // erzeugt, nipp habe etwas falsch gemacht: der Benutzer sah drei
            // Fehler, wo drei verpasste Anrufe waren.
            //
            // Ein Anruf, der nie verbunden war und dessen Grund nichts Klares
            // sagt, ist verpasst (eingehend) beziehungsweise ohne Antwort
            // geblieben (ausgehend) — nicht kaputt.
            _ => status == CallStatus.Failed ? CallEndReason.Failed : CallEndReason.Normal,
        };
    }

    /// <summary>
    /// Das Gespräch zu einer Kennung, oder eine Ausnahme mit einem Satz, den
    /// der Benutzer lesen kann.
    ///
    /// <para><b>Warum das nicht selten ist.</b> <c>_calls.TryRemove</c> läuft
    /// bei <c>End</c> und <c>Released</c>, also <b>bevor</b> die Oberfläche
    /// nachgezogen hat; die Knöpfe der Gesprächsansicht bleiben einen Wimpernschlag
    /// klickbar. Wer «Stumm» drückt, während die Gegenseite auflegt, landet
    /// hier — das ist ein Rennen, kein Programmfehler, und es darf nicht wie
    /// einer aussehen.</para>
    ///
    /// <para>Die Kennung stand bis zum 13.09.2026 im Text (ADR-053, Befund
    /// B14). Sie ist eine GUID und sagt dem Benutzer nichts; ins Protokoll
    /// gehört sie, in die Meldung nicht.</para>
    /// </summary>
    private TrackedCall Resolve(CallHandle handle) =>
        _calls.TryGetValue(handle, out var tracked)
            ? tracked
            : throw new InvalidOperationException(
                "Dieses Gespräch ist bereits beendet.");

    /// <summary>
    /// Führt einen Aufruf ins SDK aus und übersetzt sein Scheitern (ADR-053).
    ///
    /// <para><b>Warum übersetzt und nicht durchgereicht.</b> Der Wrapper wirft
    /// <c>LinphoneException</c>, sobald eine native Funktion etwas anderes als
    /// 0 zurückgibt — bei <c>Pause</c>, <c>Resume</c>, <c>SendDtmf</c> und
    /// <c>Terminate</c>. Diese Ausnahme darf die Telefonieschicht nicht
    /// verlassen: ViewModels kennen keine SDK-Typen, das ist eine
    /// Architekturgrenze und ein Test erzwingt sie. Bis zum 13.09.2026 wurde
    /// sie nirgends gefangen — ein «Halten» auf ein Gespräch, das das SDK
    /// gerade selbst umgestellt hatte, nahm den Prozess mit.</para>
    ///
    /// <para><b>Ohne innere Ausnahme.</b> Die Kette bliebe ein Weg, auf dem ein
    /// SDK-Typ doch hinauswandert. Was für die Fehlersuche zählt — Typ und
    /// maskierte Meldung — steht in der Protokollzeile, zusammen mit der
    /// Kennung des Gesprächs.</para>
    /// </summary>
    /// <param name="handle">Für die Protokollzeile.</param>
    /// <param name="operation">Was versucht wurde, in der Sprache des Codes.</param>
    /// <param name="userMessage">
    /// Was der Benutzer liest. Sagt, was nicht ging, und was er tun kann.
    /// </param>
    /// <param name="action">Der Aufruf ins SDK.</param>
    private void Sdk(CallHandle handle, string operation, string userMessage, Action action)
    {
        try
        {
            action();
        }
        catch (LinphoneException ex)
        {
            TelephonyLog.SdkCallFailed(
                _logger, operation, handle.ToString(), ex.GetType().Name, CallbackGuard.Describe(ex));

            throw new InvalidOperationException(userMessage);
        }
    }

    private void UpdateInfo(CallHandle handle, Func<CallInfo, CallInfo> change)
    {
        if (_calls.TryGetValue(handle, out var tracked))
        {
            var previous = tracked.Info.Status;
            tracked.Info = change(tracked.Info);
            CallStateChanged?.Invoke(this, new CallStateEventArgs(tracked.Info, previous));
        }
    }

    private LinphoneCore RequireCore() =>
        _core ?? throw new InvalidOperationException(
            "Die Telefonie ist noch nicht bereit. Einen Moment warten und erneut versuchen — bleibt es dabei, hilft ein Neustart von nipp.");

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var core = _core;
        if (core is null)
        {
            return Task.CompletedTask;
        }

        // Vor allem anderen: der eigene Tonspieler haelt eine Datei offen und
        // haengt am Core. Erst danach darf der Core stoppen.
        _ringback.Reset();
        StopRingback();
        StopPreview();

        foreach (var tracked in _calls.Values)
        {
            if (tracked.SdkCall.State is (CallState.End or CallState.Released))
            {
                continue;
            }

            // Ein Gespraech, das sich nicht mehr beenden laesst, darf das
            // Herunterfahren nicht aufhalten: der Core stoppt gleich ohnehin,
            // und ein Wurf hier laesse die Abmeldung der Konten aus. Genau
            // dieser Pfad hat vom 07. bis zum 12.09.2026 dafuer gesorgt, dass
            // nipp als Prozess liegenblieb (T134).
            try
            {
                tracked.SdkCall.Terminate();
            }
            catch (LinphoneException ex)
            {
                TelephonyLog.SdkCallFailed(
                    _logger, "Terminate", tracked.Handle.ToString(),
                    ex.GetType().Name, CallbackGuard.Describe(ex));
            }
        }

        // Die Präsenz-Abonnements abbestellen, bevor der Core stoppt.
        //
        // Ohne das behält die Anlage sie, und beim nächsten Start beantwortet
        // sie die verwaisten Dialoge mit NOTIFY, die niemand mehr kennt — im
        // Protokoll vom 05.09.2026 einundzwanzigmal „Receiving NOTIFY with
        // to-tag but no known dialog here" samt SIP 481.
        try
        {
            if (_presenceList is not null)
            {
                _presenceList.SubscriptionsEnabled = false;
                _presenceList.UpdateSubscriptions();
            }
        }
        catch (Exception ex)
        {
            TelephonyLog.PresenceWatchFailed(_logger, $"{ex.GetType().Name}: {ex.Message}");
        }

        // Reihenfolge: erst stoppen, dann Zugangsdaten löschen. Umgekehrt
        // scheitert der abschliessende REGISTER mit Expires=0 an fehlenden
        // Zugangsdaten und die Anlage sieht das Konto weiter als angemeldet
        // (in AP2.3 so passiert).
        core.Stop();

        // Dem SDK Gelegenheit geben, Abmeldung und Abbestellung zu senden.
        //
        // 25 Durchläufe ohne Pause waren dafür zu wenig: sie sind in gut einer
        // Millisekunde durch, in der kein Paket die Gegenstelle erreicht.
        // Jetzt wird gewartet, bis der Core wirklich abgemeldet ist, längstens
        // eine Sekunde — beim Beenden darf niemand länger zusehen.
        var iterations = 0;

        while (iterations < ShutdownMaxIterations)
        {
            core.Iterate();
            iterations++;

            if (core.GlobalState == GlobalState.Off)
            {
                break;
            }

            // CA1849 verlangt hier ein await. Das wäre falsch: diese Methode
            // wird beim Beenden synchron gerufen (App.ExitApplication, aus dem
            // Menü des Infobereichs), und ein await auf dem UI-Thread mit
            // blockierendem Aufrufer wäre genau der Deadlock, den §14.1
            // beschreibt. Die App beendet sich gerade — blockieren ist hier
            // die Absicht, nicht ein Versehen.
#pragma warning disable CA1849
            Thread.Sleep(ShutdownIterationDelayMs);
#pragma warning restore CA1849
        }

        TelephonyLog.ShutdownUnregistered(_logger, iterations, core.GlobalState.ToString());

        core.ClearAllAuthInfo();
        _calls.Clear();
        TelephonyLog.CoreStopped(_logger);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bridge?.Dispose();
        _core?.Stop();
        _core = null;
    }

    /// <summary>
    /// Verbindet eigene Kennung und SDK-Anruf. Bewusst eine Klasse und kein
    /// Record: <see cref="Info"/> wird laufend ersetzt.
    /// </summary>
    private sealed class TrackedCall(CallHandle handle, Call sdkCall)
    {
        public CallHandle Handle { get; } = handle;

        public Call SdkCall { get; } = sdkCall;

        public required CallInfo Info { get; set; }

        /// <summary>
        /// Ob das SDK für diesen Anruf im Zustand <c>OutgoingEarlyMedia</c>
        /// ist.
        ///
        /// <para>Steht hier und nicht in <c>CallInfo</c>, weil es niemanden
        /// ausserhalb angeht: die Oberfläche zeigt „läutet", ob mit Early Media
        /// oder ohne. Nur der Rufton-Wächter muss beides unterscheiden — bei
        /// <c>OutgoingRinging</c> spielt das SDK selbst.</para>
        /// </summary>
        public bool EarlyMedia { get; set; }

        /// <summary>
        /// Wann das SDK diesen Anruf als läutend gemeldet hat.
        ///
        /// <para>Die Karenzzeit des Ruftons läuft ab hier und nicht ab dem
        /// ersten Durchlauf, der den Anruf sieht: am 10.09.2026 lagen zwischen
        /// beidem 260 ms, weil der Aufbau des Audiostroms die Ereignisschleife
        /// aufhielt. Sonst bedeutet die Konstante nicht, was sie sagt.</para>
        /// </summary>
        public DateTimeOffset? RingingSince { get; set; }

        /// <summary>
        /// Ob dieses Objekt denselben Anruf meint. Verglichen wird über die
        /// Call-ID, weil das SDK bei Zustandswechseln nicht immer dieselbe
        /// .NET-Instanz liefert.
        /// </summary>
        public bool Matches(Call other)
        {
            if (ReferenceEquals(SdkCall, other))
            {
                return true;
            }

            try
            {
                return SdkCall.CallLog?.CallId is { Length: > 0 } id
                    && id == other.CallLog?.CallId;
            }
            catch (Exception)
            {
                // Ohne Logger an dieser Stelle: Matches laeuft in einem
                // record-Typ ohne Abhaengigkeiten, und der Fall ist
                // harmlos — zwei Anrufe, von denen einer keine CallId mehr
                // hat, sind nicht derselbe.
                return false;
            }
        }
    }
}
