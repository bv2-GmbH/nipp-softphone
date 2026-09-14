using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Nipp.App.Diagnostics;
using Nipp.App.Telephony;
using Nipp.App.Theming;
using Nipp.App.Windows;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Search;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.Services.Updates;
using Nipp.Core.Services.Windows;
using Nipp.Core.ViewModels;
using Serilog;

// Namensfalle, die gleiche wie bei Nipp.Core und Linphone.Core: innerhalb von
// Nipp.App loest ein blankes "Windows" auf den eigenen Namespace
// Nipp.App.Windows auf, nicht auf die Windows-Runtime. Ein Alias macht die
// Absicht eindeutig; ein blosses using auf den Namespace waere ausserdem
// mehrdeutig, weil LaunchActivatedEventArgs dort UND in Microsoft.UI.Xaml
// existiert.
using LaunchActivation = global::Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
using ProtocolActivation = global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;

namespace Nipp.App;

/// <summary>
/// Anwendungslebenszyklus und DI-Container.
///
/// Hier hängt der Iterate-Timer des SDK (§6): am App-Lebenszyklus, nicht am
/// Fenster — nipp bleibt im Infobereich aktiv, wenn das Fenster geschlossen
/// wird (§10).
/// </summary>
public partial class App : Application, IDisposable
{
    private readonly ILogger<App> _logger;
    private Window? _window;

    /// <summary>
    /// Das Hauptfenster — für alles, was in WinUI 3 ein Fensterhandle braucht,
    /// etwa die Datei-Dialoge (<see cref="Windows.FilePickers"/>). Aus einem
    /// <c>XamlRoot</c> ist das Fenster nicht zu bekommen.
    /// </summary>
    internal Window? MainWindowRef => _window;
    private TrayIconHost? _tray;
    private ToastService? _toasts;
    private Nipp.Core.Services.Telephony.Model.SdkLoadResult? _sdkStatus;

    /// <summary>
    /// Ob nipp wirklich beendet werden soll. §10: „Schliessen des Fensters
    /// beendet die App nicht" — nur das Infobereich-Menü tut das.
    /// </summary>
    private bool _exiting;

    /// <summary>Ergebnis der SDK-Ladeprüfung beim Start (AP2.4).</summary>
    public Nipp.Core.Services.Telephony.Model.SdkLoadResult? SdkStatus => _sdkStatus;

    /// <summary>
    /// Der Dienstanbieter der App. §15 verbietet statische Singletons; diese
    /// Eigenschaft ist die eine Ausnahme, die der WinUI-Lebenszyklus erzwingt —
    /// XAML erzeugt <see cref="App"/> selbst, es gibt keinen Konstruktor, in
    /// den sich etwas hineingeben liesse. Alles andere kommt über DI.
    /// </summary>
    public IServiceProvider Services { get; }

    public App()
    {
        // Notfallprotokoll, bevor irgendetwas anderes läuft. Eine Ausnahme im
        // Konstruktor oder beim Laden der XAML-Ressourcen kommt sonst nirgends
        // an: Serilog steht dann noch nicht, und das Windows-Ereignisprotokoll
        // zeigt nur den generischen .NET-Fehlercode 0xe0434352.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashFile(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => WriteCrashFile(e.Exception);

        Services = ConfigureServices();
        _logger = Services.GetRequiredService<ILogger<App>>();

        InitializeComponent();

        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var version = GetType().Assembly.GetName().Version?.ToString() ?? "unbekannt";
        AppLog.Starting(_logger, version);

        // Die Aktivierung einmal holen und weiterreichen: sie trägt bei einem
        // tel:-Klick die URI, und genau die ging bisher verloren.
        var activation = TryGetActivation();

        // §10: Einzelinstanz. Ein zweiter Start reicht seine Argumente an die
        // laufende Instanz weiter — sonst öffnete ein tel:-Link eine zweite
        // App, die sich nicht registrieren kann.
        if (!EnsureSingleInstance(activation))
        {
            Exit();
            return;
        }

        var settings = Services.GetRequiredService<SettingsService>().Load();
        ApplyLogVerbosity(settings.Advanced.Logging);

        // §17: Provisionierung, bevor irgendetwas mit den Einstellungen
        // geschieht — ein Profil kann Konten mitbringen. Ein Fehler dabei darf
        // den Start nicht verhindern, dafuer sorgt der Dienst selbst.
        settings = ApplyProvisioning(settings);

        // §8.3: Anrufliste beim Start aufräumen.
        Services.GetRequiredService<CallHistoryStore>().Purge(settings.Advanced.HistoryRetentionDays);
        ApplyWindowsIntegration(settings);

        StartTelephony(settings);

        _window = new MainWindow();
        StartShellServices();

        // §9.6: minimiert starten, wenn so eingerichtet und per Autostart
        // aufgerufen.
        if (!(settings.Advanced.StartMinimized && WasStartedMinimized()))
        {
            _window.Activate();
        }

        // §10: eine Aktivierung mit tel:-URI wählt direkt, ohne Rückfrage.
        // Beim Start darf auch die Kommandozeile gelten: unpackaged reicht der
        // HKCU-Handler die URI so herein.
        HandleActivation(activation, allowCommandLine: true);

        // §9.6 und ADR-039: zuletzt, und nur nachsehen.
        StartUpdateCheck(settings.Update);
    }

    /// <summary>
    /// Sieht nach, ob es eine neuere Fassung gibt (ADR-039).
    ///
    /// <para><b>Drei Dinge daran sind Absicht.</b> Erstens steht der Aufruf
    /// ganz am Ende von <see cref="OnLaunched"/>: Anmeldung, Fenster und
    /// Telefonie sind dann fertig, und nichts davon wartet auf GitHub. Ein
    /// Softphone, dessen Start von einem Webdienst abhängt, ist kaputt gebaut
    /// — dieselbe Regel wie beim Provisioning (docs/provisioning.md).</para>
    ///
    /// <para>Zweitens läuft er nicht auf dem UI-Thread mit: der bedient alle
    /// 20 ms <c>Core.Iterate()</c>, und ein hängender HTTP-Aufruf wäre dort
    /// ein stockendes Gespräch.</para>
    ///
    /// <para>Drittens wird <b>nur gefragt, nicht geladen</b>. Was gefunden
    /// wird, steht als Zeile in den Einstellungen und wartet dort.</para>
    /// </summary>
    private void StartUpdateCheck(UpdateSettings settings)
    {
        var updates = Services.GetRequiredService<UpdateService>();

        _ = Task.Run(async () =>
        {
            try
            {
                await updates.CheckAsync(settings, onStart: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Der Dienst faengt selbst; das hier ist die Zusage, dass ein
                // Update-Problem den Prozess unter keinen Umstaenden mitnimmt.
                AppLog.UpdateCheckFailed(_logger, ex.GetType().Name);
            }
        });

        StarteTaeglichePruefung(settings);
    }

    /// <summary>
    /// Sieht auch im Betrieb nach, nicht nur beim Start (W1.5, Befund E8).
    ///
    /// <para><b>Der Befund.</b> Die Prüfung lief genau einmal, beim
    /// Prozessstart. nipp lebt aber im Infobereich, und Schliessen beendet es
    /// nicht (§10) — ein Arbeitsplatz, der Wochen durchläuft, erfuhr nie von
    /// einer neuen Fassung. Für ein Programm, das seine Updates selbst
    /// verteilt, ist das die Hälfte der Zusage.</para>
    ///
    /// <para><b>Täglich, nicht stündlich.</b> Ein Release kommt nicht öfter,
    /// und jede Prüfung ist eine Anfrage an GitHub mit dem Token aus der
    /// Provisionierung. Geladen wird weiterhin nichts von selbst
    /// (ADR-039).</para>
    /// </summary>
    private void StarteTaeglichePruefung(UpdateSettings settings)
    {
        if (!settings.CheckOnStart)
        {
            // Wer die Prüfung beim Start abgewählt hat, will sie auch nicht im
            // Betrieb. Ein zweiter Schalter dafür wäre eine Einstellung mehr
            // für dieselbe Frage.
            return;
        }

        _updateTimer = _window?.DispatcherQueue.CreateTimer();

        if (_updateTimer is null)
        {
            return;
        }

        _updateTimer.Interval = TimeSpan.FromHours(24);
        _updateTimer.IsRepeating = true;

        _updateTimer.Tick += (_, _) => _ = Task.Run(async () =>
        {
            try
            {
                var jetzt = Services.GetRequiredService<SettingsService>().Current.Update;
                var dienst = Services.GetRequiredService<UpdateService>();

                // onStart: false — die Prüfung «beim Start» hat eine eigene
                // Bedeutung im Dienst, und dies hier ist keiner.
                await dienst.CheckAsync(jetzt, onStart: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.UpdateCheckFailed(_logger, ex.GetType().Name);
            }
        });

        _updateTimer.Start();
    }

    /// <summary>Die tägliche Update-Prüfung (W1.5). Läuft bis zum Beenden.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _updateTimer;

    /// <summary>
    /// Die Aktivierungsargumente dieses Starts, oder <c>null</c> ohne
    /// Paketidentität — dann steht die URI in der Kommandozeile.
    /// </summary>
    private static AppActivationArguments? TryGetActivation()
    {
        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs();
        }
        catch (Exception ex)
        {
            // Erwartet ohne Paketidentitaet — dann steht die URI in der
            // Kommandozeile. Einmal je Sitzung sichtbar (W1.7): ohne die Zeile
            // waere «der tel:-Link tut nichts» nicht von «hier kommt nichts
            // an» zu unterscheiden.
            QuietFailures.Report(
                ((App)Current).Services.GetRequiredService<ILogger<App>>(),
                "LiestAktivierung",
                ex);

            return null;
        }
    }

    /// <summary>§10: <c>AppInstance.FindOrRegisterForKey</c> mit Weiterleitung.</summary>
    private bool EnsureSingleInstance(AppActivationArguments? activation)
    {
        try
        {
            var instance = AppInstance.FindOrRegisterForKey("nipp-single-instance");

            if (!instance.IsCurrent)
            {
                if (activation is null)
                {
                    // Ohne Aktivierungsargumente gibt es nichts
                    // weiterzureichen. Diese Instanz zu beenden hiesse, eine
                    // tel:-Nummer aus der Kommandozeile stillschweigend
                    // fallenzulassen — also lieber selbst weitermachen.
                    AppLog.SingleInstanceUnavailable(_logger, "keine Aktivierungsargumente");
                    return true;
                }

                instance.RedirectActivationToAsync(activation).AsTask().GetAwaiter().GetResult();

                AppLog.RedirectedToRunningInstance(_logger);
                return false;
            }

            instance.Activated += OnRedirectedActivation;
            return true;
        }
        catch (Exception ex)
        {
            // Ohne Paketidentität gibt es keine AppInstance — und das ist der
            // Normalfall, nicht die Ausnahme: ausgeliefert wird unpackaged
            // (ADR-008, ADR-038).
            //
            // <b>Hier stand bis zum 13.09.2026 «dann läuft nipp eben
            // mehrfach».</b> Zwei Instanzen registrieren dasselbe Konto — die
            // zweite verdrängt die erste an der Anlage —, schreiben
            // settings.json gegeneinander und öffnen dasselbe HID-Handle; und
            // das Tastenkürzel der zweiten scheitert mit «von einer anderen
            // Anwendung belegt», was dann in die Irre führt (W2.4, B23).
            AppLog.SingleInstanceUnavailable(_logger, ex.Message);

            return EnsureSingleInstanceWithoutPackage(activation);
        }
    }

    /// <summary>
    /// Die Einzelinstanz ohne Paketidentität (W2.4, Befund B23).
    ///
    /// <para>Mutex plus Named Pipe — dasselbe, was <c>AppInstance</c> mit
    /// Paketidentität tut: «bin ich der Erste?» und, falls nicht, die
    /// Kommandozeile an den Ersten reichen, damit ein <c>tel:</c>-Klick nicht
    /// verlorengeht.</para>
    /// </summary>
    private bool EnsureSingleInstanceWithoutPackage(AppActivationArguments? activation)
    {
        _singleInstance = new SingleInstanceGuard(_logger);

        _singleInstance.Activated += nummer =>
            Eingereiht("Aktivierung einer zweiten Instanz", () =>
            {
                BringToFront();

                // Was die ZWEITE Instanz mitgebracht hat, nicht die eigene
                // Kommandozeile — genau die Unterscheidung, an der
                // HandleActivation schon einmal gescheitert ist: die laufende
                // Instanz durchsuchte ihre alte Kommandozeile und wählte die
                // Nummer von vorhin.
                if (nummer.Length > 0)
                {
                    WaehleAusAktivierung(nummer);
                }
            });

        // Die Nummer wird HIER herausgelöst, nicht im Wächter: der liegt im
        // Kern und weiss nichts über tel:-URIs. Über die Pipe geht damit
        // genau das, was ankommen soll.
        if (_singleInstance.TryBecomeFirstInstance(
            ExtractCallTarget(activation, allowCommandLine: true) ?? string.Empty))
        {
            return true;
        }

        _singleInstance.Dispose();
        _singleInstance = null;
        return false;
    }

    /// <summary>Die Sperre ohne Paketidentität. <c>null</c> mit Paket.</summary>
    private SingleInstanceGuard? _singleInstance;

    private void OnRedirectedActivation(object? sender, AppActivationArguments args) =>
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            BringToFront();

            // allowCommandLine: false — hier gilt ausschliesslich, was die
            // zweite Instanz mitgebracht hat. Die eigene Kommandozeile ist
            // die von vorhin, und ein zweiter Start ohne URI (Startmenue,
            // Verknuepfung, Taskleiste) haette damit die Nummer von damals
            // erneut gewaehlt.
            HandleActivation(args, allowCommandLine: false);
        });

    /// <summary>
    /// Nimmt eine <c>tel:</c>-Aktivierung entgegen und wählt (§10).
    ///
    /// <b>Der Fehler, der hier lag.</b> Diese Methode las früher immer
    /// <c>Environment.GetCommandLineArgs()</c> — auch dann, wenn Windows die
    /// Aktivierung an die <b>laufende</b> Instanz weitergereicht hatte. Weil
    /// nipp im Infobereich weiterläuft (§10), ist genau das der Normalfall:
    /// ein tel:-Klick aus Outlook oder dem CRM erreichte die laufende
    /// Instanz, die dann ihre eigene, alte Kommandozeile durchsuchte. Wählte
    /// sie überhaupt, dann die Nummer von vorhin.
    /// </summary>
    private void HandleActivation(AppActivationArguments? activation, bool allowCommandLine)
    {
        if (ExtractCallTarget(activation, allowCommandLine) is not { } number)
        {
            return;
        }

        WaehleAusAktivierung(number);
    }

    /// <summary>
    /// Wählt eine Nummer, die über eine Aktivierung hereingekommen ist (§10).
    ///
    /// <para>Herausgelöst, weil es seit W2.4 zwei Wege dorthin gibt: die
    /// <c>AppInstance</c>-Weiterleitung mit Paketidentität und die Pipe ohne.
    /// Beide sollen dasselbe tun — eine zweite Kopie wäre die Stelle, an der
    /// sie auseinanderlaufen.</para>
    /// </summary>
    private void WaehleAusAktivierung(string number)
    {
        AppLog.CallUriReceived(_logger, LogMasking.Number(number));

        var shell = Services.GetRequiredService<ShellViewModel>();
        shell.DialedNumber = number;

        BringToFront();

        if (shell.DialCommand.CanExecute(null))
        {
            shell.DialCommand.Execute(null);
        }
    }

    /// <summary>
    /// Zieht die Rufnummer aus einer Aktivierung — packaged aus der
    /// Protokoll-URI, unpackaged aus der Kommandozeile, die der HKCU-Handler
    /// mitgibt (<see cref="WindowsIntegration"/>).
    /// </summary>
    /// <param name="allowCommandLine">
    /// Ob die eigene Kommandozeile als Quelle gelten darf. Beim Start ja —
    /// unpackaged kommt die URI genau so herein. Bei einer weitergereichten
    /// Aktivierung <b>nein</b>: dort steht die Kommandozeile dieses,
    /// laengst laufenden Prozesses, und die trug moeglicherweise vor Stunden
    /// eine ganz andere Nummer.
    /// </param>
    private static string? ExtractCallTarget(AppActivationArguments? activation, bool allowCommandLine)
    {
        if (activation?.Data is ProtocolActivation protocol
            && WindowsIntegration.ParseCallUri(protocol.Uri?.OriginalString) is { } fromUri)
        {
            return fromUri;
        }

        // Unpackaged gibt es keine ProtocolActivation: der HKCU-Handler ruft
        // «nipp.exe "%1"», und die URI kommt als gewoehnliche Kommandozeile
        // herein — die Aktivierung meldet dann Kind == Launch.
        //
        // Das gilt ausdruecklich auch fuer eine weitergereichte Aktivierung:
        // dort stehen die Argumente der ZWEITEN Instanz, also die frisch
        // angeklickte Nummer. Genau die ging bisher verloren — die Pruefung
        // kannte nur den Protokollfall, und danach verwarf allowCommandLine
        // alles Weitere. Das Fenster kam nach vorn, gewaehlt wurde nichts.
        //
        // Die Unterscheidung zu Environment.GetCommandLineArgs() weiter unten
        // bleibt dabei erhalten: das ist die Kommandozeile DIESES Prozesses.
        if (activation?.Data is LaunchActivation launch && !string.IsNullOrWhiteSpace(launch.Arguments))
        {
            foreach (var argument in launch.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (WindowsIntegration.ParseCallUri(argument.Trim('"')) is { } fromLaunch)
                {
                    return fromLaunch;
                }
            }
        }

        if (!allowCommandLine)
        {
            return null;
        }

        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
        {
            if (WindowsIntegration.ParseCallUri(argument) is { } fromCommandLine)
            {
                return fromCommandLine;
            }
        }

        return null;
    }

    private static bool WasStartedMinimized() =>
        Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase);

    private void StartTelephony(NippSettings settings)
    {
        var probe = Services.GetRequiredService<SdkLoadProbe>();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "nipp");

        _sdkStatus = probe.Probe(dataDirectory);

        // Vor der SDK-Pruefung abonnieren, nicht danach.
        //
        // Protokollstufe, Tastenkuerzel, Autostart und die Protokoll-Handler
        // haengen alle an diesem Ereignis und haben mit dem SDK nichts zu tun.
        // Stand die Zeile hinter der Pruefung, wirkte auf einem Rechner ohne
        // vollstaendiges SDK keine dieser Einstellungen — bis zum naechsten
        // Neustart, und ohne dass irgendetwas den Zusammenhang nannte.
        Services.GetRequiredService<SettingsService>().Changed += OnSettingsChanged;

        if (_sdkStatus?.IsComplete != true)
        {
            AppLog.TelephonyUnavailable(_logger, SipErrorCatalog.DescribeSdkUnavailable(_sdkStatus!));
            return;
        }

        try
        {
            var sip = Services.GetRequiredService<ISipService>();
            sip.InitializeAsync(settings).GetAwaiter().GetResult();

            if (settings.Advanced.RecordingDirectory is { Length: > 0 } recordings)
            {
                sip.RecordingDirectory = recordings;
            }

            // Erst den Takt, dann anmelden: ohne laufende Ereignisschleife
            // käme die Antwort auf den REGISTER nie an (§6).
            Services.GetRequiredService<SipPumpHost>().Start();
            Services.GetRequiredService<ConnectivityMonitor>().Start();

            // Die Tasten am Headset (§22.5). Nach InitializeAsync, weil der
            // Dienst sich an CallStateChanged haengt, und vor dem ersten
            // Anruf — dazwischen darf nichts liegen.
            Services.GetRequiredService<HeadsetCallControl>().Start();

            // AP5.5: die Einstellungen auf den Core übertragen, bevor sich ein
            // Konto anmeldet. Codecs und Verschlüsselung müssen stehen, wenn
            // das erste INVITE hinausgeht — danach gesetzt gälten sie erst
            // beim übernächsten Gespräch.
            sip.ApplySettingsAsync(settings).GetAwaiter().GetResult();

            // §20.2: alle eingerichteten Konten anmelden, höchstens zehn.
            foreach (var account in settings.Accounts.Take(10))
            {
                sip.RegisterAccountAsync(account).GetAwaiter().GetResult();
            }

            if (settings.Advanced.DefaultAccountIdentity is { Length: > 0 } preferred
                && settings.Accounts.Exists(a => a.Identity == preferred))
            {
                sip.DefaultAccountIdentity = preferred;
            }

            // Die ViewModels jetzt erzeugen, nicht erst beim ersten Öffnen
            // ihrer Seite: sie abonnieren im Konstruktor, und wer später
            // entsteht, hat die Ereignisse eines laufenden Anrufs nie gesehen.
            _ = Services.GetRequiredService<ShellViewModel>();
            _ = Services.GetRequiredService<ActiveCallViewModel>();
        }
        catch (Exception ex)
        {
            AppLog.TelephonyStartFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Holt die Integrationskonfiguration, wenn das Profil eine Adresse nennt
    /// (§21.3).
    ///
    /// <b>Ohne await und nach dem ersten Zeichnen</b> — die Begründung steht
    /// an der Aufrufstelle. Ein Fehler dabei kostet die Integrationen, sonst
    /// nichts.
    /// </summary>
    private void FetchIntegrationProfile() => _ = Task.Run(async () =>
    {
        try
        {
            var uri = Services.GetRequiredService<ProvisioningService>().LastIntegrationsUri;

            if (uri is null)
            {
                return;
            }

            await Services.GetRequiredService<IntegrationProvisioning>()
                .ApplyAsync(uri)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.IntegrationsUnavailable(_logger, Describe(ex));
        }
    });

    /// <summary>
    /// §17: Factory-Config und Remote-Profil anwenden.
    ///
    /// Synchron im Startpfad, mit fuenf Sekunden Grenze im Dienst: die Konten
    /// kommen moeglicherweise aus dem Profil, und sie erst nach der
    /// Anmeldung nachzureichen hiesse, zweimal zu registrieren.
    /// </summary>
    private NippSettings ApplyProvisioning(NippSettings settings)
    {
        try
        {
            var provisioning = Services.GetRequiredService<ProvisioningService>();

            if (provisioning.ApplyAsync().GetAwaiter().GetResult())
            {
                // Der Dienst hat gespeichert; die eben geladene Fassung ist
                // damit veraltet.
                settings = Services.GetRequiredService<SettingsService>().Current;
            }

            if (provisioning.LastError is { Length: > 0 } error)
            {
                AppLog.ProvisioningWarning(_logger, error);
            }
        }
        catch (Exception ex)
        {
            // §17 ausdruecklich: der Start geht weiter.
            AppLog.ProvisioningWarning(_logger, ex.Message);
        }

        return settings;
    }

    /// <summary>
    /// AP5.5: geänderte Einstellungen an den laufenden Core weiterreichen.
    ///
    /// Was einen Neustart braucht, sagt die Einstellungsseite dem Benutzer
    /// selbst (<c>SettingsApplier.RequiresRestart</c>); hier wird nur
    /// übertragen, was sofort wirkt.
    /// </summary>
    private void OnSettingsChanged(object? sender, NippSettings settings)
    {
        ApplyLogVerbosity(settings.Advanced.Logging);

        try
        {
            Services.GetRequiredService<ISipService>()
                .ApplySettingsAsync(settings)
                .GetAwaiter()
                .GetResult();

            if (settings.Advanced.RecordingDirectory is { Length: > 0 } recordings)
            {
                Services.GetRequiredService<ISipService>().RecordingDirectory = recordings;
            }

            var hotkeys = Services.GetRequiredService<GlobalHotkeyService>();

            hotkeys.Apply(HotkeyRole.AnnehmenAuflegen, settings.Advanced.GlobalHotkey);
            hotkeys.Apply(HotkeyRole.Stumm, settings.Advanced.MuteHotkey);

            ApplyWindowsIntegration(settings);
        }
        catch (Exception ex)
        {
            // Eine Einstellung, die der Core ablehnt, darf nicht die App
            // mitnehmen — sie steht dann eben erst nach einem Neustart.
            AppLog.SettingsNotApplied(_logger, ex.Message);
        }
    }

    /// <summary>Infobereich und Benachrichtigungen (§10, §8.6).</summary>
    private void StartShellServices()
    {
        if (_window is null)
        {
            return;
        }

        _tray = Services.GetRequiredService<TrayIconHost>();

        // <b>BEIDE ueber die DispatcherQueue, nicht direkt.</b> H.NotifyIcon
        // fuehrt fuer sein Symbol ein eigenes Nachrichtenfenster mit eigener
        // Pumpe; seine Ereignisse kommen deshalb NICHT auf dem UI-Thread an.
        //
        // Beim Oeffnen endete ein direkter Aufruf in „COMException:
        // Unzulaessiges Fenster. Es gehoert zu einem anderen Thread." — belegt
        // im Protokoll vom 07.09.2026 —, und das Fenster blieb weg.
        //
        // <b>Beim Beenden ist es tueckischer, weil nichts wirft.</b> Die
        // Korrektur vom 07.09.2026 hat diese Zeile uebersehen, und damit lief
        // das ganze Herunterfahren auf dem Tray-Thread. Dessen letzte
        // Anweisung ist Application.Exit() — und die ist dort schlicht
        // WIRKUNGSLOS: die XAML-Nachrichtenschleife laeuft auf dem
        // [STAThread]-Hauptthread weiter, und der ist der einzige
        // Vordergrundthread des Prozesses. Ergebnis: "Beenden" baute alles
        // ordentlich ab, und der Prozess blieb weitere sieben Sekunden liegen,
        // bis der Waechter ihn hart beendete — achtmal so in `beenden.txt`
        // nachzulesen, jedes Mal mit demselben Abstand (T134).
        //
        // Der Beleg, dass es der Thread war und nicht ein haengender Dienst:
        // im Protokoll steht beim Beenden genau einmal
        // „[belle-sip] There is no object pool created in thread [...]". Der
        // Thread, der abbaut, war nie der, der alle 20 ms iteriert.
        // W1.7: der Rueckgabewert von TryEnqueue wird geprueft. Ist die
        // Warteschlange beim Herunterfahren schon zu, verfaellt die Aktion —
        // lautlos. Beim Beenden waere das T134 in neuer Form: alles sieht
        // richtig aus, der Prozess bleibt liegen, und im Protokoll steht
        // nichts. Die Zeile kostet nichts und beantwortet die Frage.
        _tray.OpenRequested += (_, _) => Eingereiht("Fenster öffnen", BringToFront);
        _tray.ExitRequested += (_, _) => Eingereiht("Beenden", ExitApplication);
        _tray.Start();

        ZeigeInstallationshinweis();

        _toasts = new ToastService(
            Services.GetRequiredService<ISipService>(),
            Services.GetRequiredService<SettingsService>(),

            // Der Anruferkontext (§21, ADR-030): der Toast zeigt ihn, sobald
            // er da ist. Der Dienst wird hier nur abonniert, nicht gestartet —
            // das macht StartIntegrations weiter unten, nach dem Laden der
            // Konfiguration.
            Services.GetRequiredService<CallerContextService>(),

            // Welche Karte für die Benachrichtigung gilt (K5, ADR-034). Ohne
            // eigene Karte bleibt es bei der mitgelieferten Zusammensetzung.
            Services.GetRequiredService<CardResolver>(),

            // Der Name des Gespraechspartners (ADR-043) — dieselbe Stelle wie
            // Kopfzeile, Leiste, Makel-Liste und Infobereich.
            Services.GetRequiredService<CallPartyResolver>(),
            Services.GetRequiredService<ILogger<ToastService>>(),
            _window.DispatcherQueue);
        _toasts.CallAccepted += (_, _) => BringToFront();

        // W1.3: ein Klick auf den Toast selbst holt das Fenster — mehr nicht.
        // Der Anruf klingelt weiter, bis jemand «Annehmen» trifft.
        _toasts.OpenRequested += (_, _) => BringToFront();
        _toasts.Start();

        // §10: Schliessen beendet nipp nicht — es verschwindet in den
        // Infobereich. Nur das Menü dort beendet wirklich.
        _window.Closed += OnWindowClosed;

        StartContacts();
        StartIntegrations();
        StartHotkey(Services.GetRequiredService<SettingsService>().Current);
    }

    /// <summary>
    /// Lädt die Integrationskonfiguration (§21.3).
    ///
    /// <b>Hier und nicht im Startpfad der Telefonie.</b> Es ist ein
    /// Dateizugriff von wenigen Millisekunden, aber die Reihenfolge ist die
    /// Zusage aus §21.2: nichts an den Integrationen darf zwischen dem Start
    /// und dem ersten möglichen Anruf stehen. Scheitert das Laden, gibt es
    /// eben keine externen Quellen — der Speicher meldet es und gibt eine
    /// leere Konfiguration zurück.
    /// </summary>
    private void StartIntegrations()
    {
        try
        {
            Services.GetRequiredService<IntegrationConfigStore>().Load();

            // Die Anbietervorlagen (ADR-040): die mitgelieferte und alles, was
            // unter dem Benutzerprofil liegt. Ein Dateizugriff von
            // wenigen Millisekunden -- und trotzdem hier und nicht im
            // Startpfad der Telefonie, aus demselben Grund wie alles andere in
            // dieser Methode.
            Services.GetRequiredService<ConnectorLibrary>().Reload();

            // Erst nach dem Laden: der Dienst fragt die Konfiguration, ob er
            // ueberhaupt nachschlagen soll.
            Services.GetRequiredService<CallerContextService>().Start();

            // Und die Integrationsdatei aus dem Provisionierungsprofil — im
            // Hintergrund, ohne await. Sie darf zwischen dem Start und dem
            // ersten moeglichen Anruf nicht stehen (Paragraf 21.2); im
            // schlimmsten Fall stehen die Integrationen eine Sekunde spaeter
            // bereit, und das merkt niemand.
            FetchIntegrationProfile();
        }
        catch (Exception ex)
        {
            // Der Speicher fängt selbst ab, was er kennt. Bleibt trotzdem
            // etwas übrig, ist es kein Grund, den Start zu gefährden: ohne
            // Integrationen ist nipp immer noch ein Telefon.
            AppLog.IntegrationsUnavailable(_logger, Describe(ex));
        }
    }

    /// <summary>
    /// Kontakte und Besetztlampenfeld (§8.4).
    ///
    /// Bewusst nach dem ersten Zeichnen und ohne <c>await</c>: das Einlesen aus
    /// Outlook dauert Sekunden, und AP7.8 verlangt einen Kaltstart unter drei
    /// Sekunden. Bis die Liste da ist, zeigt die Kontaktansicht ihren Hinweis —
    /// telefonieren geht die ganze Zeit.
    /// </summary>
    private void StartContacts() => _ = Task.Run(async () =>
    {
        try
        {
            await Services.GetRequiredService<ContactStore>().RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.ContactsFailed(_logger, Describe(ex));
            return;
        }

        // Die Praesenz-Abonnements gehen ins SDK, und das will den UI-Thread
        // (§6). Aus dem Hintergrund gerufen quittiert das SDK das nicht mit
        // einem Fehler, sondern mit sporadisch ausbleibenden Ereignissen —
        // die unangenehmste Art von Fehler.
        _window?.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await Services.GetRequiredService<BlfService>().SynchronizeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLog.ContactsFailed(_logger, Describe(ex));
            }
        });
    });

    /// <summary>
    /// Ausnahmetext, der auch dann etwas sagt, wenn die Meldung leer ist.
    ///
    /// Eine COMException aus dem falschen Thread hat genau das: keine Meldung.
    /// Im Protokoll stand dann „Kontakte liessen sich nicht laden:" und sonst
    /// nichts — daran ist eine Stunde verlorengegangen.
    /// </summary>
    private static string Describe(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message)
            ? $"{ex.GetType().Name} (HRESULT 0x{ex.HResult:X8}) ohne Meldung"
            : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Sagt beim <b>ersten</b> Schliessen, dass nipp weiterläuft (W1.3, C10).
    ///
    /// <para>Über den Infobereich, nicht über einen Dialog: das Fenster ist in
    /// diesem Moment gerade weg, und ein Dialog, der es zurückholt, machte den
    /// Vorgang rückgängig, den der Benutzer eben ausgelöst hat.</para>
    ///
    /// <para><b>Genau einmal je Arbeitsplatz.</b> Ein Hinweis, der bei jedem
    /// Schliessen kommt, ist nach dem zweiten Mal eine Belästigung — und wer
    /// ihn wegklickt, hat ihn beim dritten Mal nicht mehr gelesen.</para>
    /// </summary>
    private void ZeigeInfobereichHinweisEinmal()
    {
        try
        {
            var settings = Services.GetRequiredService<SettingsService>();

            if (settings.Current.Advanced.TrayHintSeen)
            {
                return;
            }

            settings.SaveViewState(settings.Current with
            {
                Advanced = settings.Current.Advanced with { TrayHintSeen = true },
            });

            _tray?.ShowHint(
                "nipp läuft weiter",
                "Das Fenster ist zu, das Telefon nicht. Anrufe kommen weiterhin an. "
                    + "Zum Beenden: Rechtsklick auf dieses Symbol, «Beenden».");
        }
        catch (Exception ex)
        {
            // Ein Hinweis ist kein Grund, das Schliessen zu gefaehrden.
            AppLog.HandlerFailed(_logger, nameof(ZeigeInfobereichHinweisEinmal), ex.GetType().Name);
        }
    }

    /// <summary>
    /// Sagt nach einer Installation oder einem Update, dass nipp bereitsteht
    /// (14.09.2026).
    ///
    /// <para><b>Der Anlass.</b> Das Setup zeigte bis dahin einen Fortschritt
    /// mit einem <b>OK-Knopf</b> — dem Standard eines Windows-Aufgabendialogs.
    /// Wer ihn drückte, brach die Installation ab, obwohl der Knopf wie eine
    /// Bestätigung aussah. Der Dialog ist seither durch ein Bild ersetzt
    /// (<c>--splashImage</c> in <c>Release-Nipp.ps1</c>), und die Rückmeldung
    /// kommt dorthin, wo sie hingehört: <b>ans Ende</b>.</para>
    ///
    /// <para><b>Über den Infobereich und nicht über ein Fenster.</b> nipp
    /// startet nach der Installation minimiert; ein Dialog stünde vor einem
    /// Fenster, das niemand gerufen hat. Bleibt die Sprechblase aus — Windows
    /// unterdrückt sie je nach Einstellung —, ist nichts verloren: die
    /// Installation ist trotzdem fertig.</para>
    /// </summary>
    private void ZeigeInstallationshinweis()
    {
        if (!Program.FrischInstalliert)
        {
            return;
        }

        try
        {
            var version = typeof(App).Assembly.GetName().Version;

            _tray?.ShowHint(
                "nipp ist eingerichtet",
                version is null
                    ? "Die Installation ist abgeschlossen. nipp läuft im Infobereich."
                    : $"Version {version.Major}.{version.Minor}.{version.Build} ist installiert. "
                        + "nipp läuft im Infobereich.");
        }
        catch (Exception ex)
        {
            // Eine Meldung ueber eine gelungene Installation ist kein Grund,
            // den Start zu gefaehrden.
            AppLog.HandlerFailed(_logger, nameof(ZeigeInstallationshinweis), ex.GetType().Name);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        args.Handled = true;
        (_window as MainWindow)?.HideToTray();
        AppLog.HiddenToTray(_logger);

        ZeigeInfobereichHinweisEinmal();
    }

    /// <summary>
    /// Reicht eine Aktion an den UI-Thread und meldet, wenn das nicht geht
    /// (W1.7, Befund B16).
    ///
    /// <para><b>Warum die Prüfung zählt.</b> <c>TryEnqueue</c> gibt
    /// <c>false</c> zurück, sobald die Warteschlange geschlossen ist — beim
    /// Herunterfahren also. Die Aktion verfällt dann, und zwar lautlos. An
    /// dreizehn Stellen wurde der Rückgabewert nirgends angesehen; bei
    /// «Beenden» und beim Tastenkürzel wäre das ein Fehlerbild, das genau wie
    /// T134 aussieht: alles sieht richtig aus, nichts geschieht, und im
    /// Protokoll steht keine Zeile.</para>
    ///
    /// <para>Die übrigen elf Stellen bleiben, wie sie sind: eine Kachel, die
    /// sich beim Beenden nicht mehr aktualisiert, ist kein Befund.</para>
    /// </summary>
    private void Eingereiht(string was, Action aktion)
    {
        if (_window?.DispatcherQueue.TryEnqueue(() => aktion()) == true)
        {
            return;
        }

        AppLog.DispatcherRejected(_logger, was);
    }

    private void BringToFront() => (_window as MainWindow)?.ShowFromTray();

    private void ExitApplication()
    {
        _exiting = true;

        // W1.5: der Taktgeber der Update-Pruefung haengt an der
        // Nachrichtenschlange des Fensters. Er stoppt hier ausdruecklich, statt
        // sich auf das Abraeumen zu verlassen — ein Timer, der waehrend des
        // Abbaus noch einmal feuert, laedt Dienste nach, die gerade
        // verschwinden.
        _updateTimer?.Stop();
        _updateTimer = null;

        // W2.4: Mutex und Lauscher freigeben, damit der naechste Start sofort
        // durchkommt statt eine Sekunde auf den Mutex zu warten.
        _singleInstance?.Dispose();
        _singleInstance = null;

        // Die Sicherung dagegen, dass "Beenden" nur so aussieht.
        //
        // Gemessen am 07.09.2026: nach einem Klick auf "Beenden" lief das
        // Herunterfahren vollstaendig durch — Ereignisschleife gestoppt, Konto
        // abgemeldet, "Core gestoppt" im Protokoll —, und der PROZESS blieb
        // danach liegen. Vier Minuten spaeter hielt er immer noch
        // Nipp.Core.dll offen, und der naechste Build scheiterte an MSB3027.
        //
        // Fuer den Alltag war das laestig. Seit ADR-038 ist es mehr: Velopack
        // ersetzt beim Update den Inhalt von current\, und ein Prozess, der
        // seine DLLs weiter offen haelt, laesst genau das scheitern. Ein
        // Update, das nach dem Neustart nicht angewandt ist, waere von
        // "kein Update da" nicht zu unterscheiden.
        //
        // Der Waechter laeuft als Hintergrundthread und kostet nichts, wenn
        // alles gut geht: endet der Prozess regulaer, stirbt er mit.
        //
        // <b>Drei Sekunden, nicht mehr acht.</b> Er war bis zum 12.09.2026 der
        // Normalweg — jedes einzelne Beenden lief ueber ihn, weil
        // Application.Exit() vom falschen Thread kam (siehe den Kommentar bei
        // ExitRequested in StartShellServices). Seit das behoben ist, ist er
        // das Netz fuer den Ausnahmefall, und drei Sekunden nach einem Abbau,
        // der gemessen eine Sekunde dauert, sind reichlich.
        //
        // <b>Es bleibt eine Sicherung, keine Ursachenbehebung</b> — fuer die
        // naechste Ursache, die noch niemand kennt. Wo es dann haengt, sagen
        // die drei Debug-Zeilen unten.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));

            // Nicht ueber den Logger: zu diesem Zeitpunkt ist er womoeglich
            // schon mit dem Container freigegeben, und dann verschwaende
            // ausgerechnet die Meldung, die den harten Abbruch belegt.
            WriteShutdownNote("Beenden hat drei Sekunden nicht genuegt — Prozess wird hart beendet");

            AppLog.ExitForced(_logger);
            Log.CloseAndFlush();
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "nipp-beenden-waechter",
        };

        watchdog.Start();

        // Erst jetzt die Abonnements des Fensters lösen, nicht schon in dessen
        // Closed-Handler: das feuert auch beim blossen Verstecken ins
        // Infobereich-Symbol und machte das Fenster danach taub.
        (_window as MainWindow)?.DetachFromServices();

        try
        {
            Services.GetRequiredService<SipPumpHost>().Stop();
            Services.GetRequiredService<ISipService>().ShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.TelephonyShutdownFailed(_logger, ex);
        }

        AppLog.ExitStep(_logger, "Telefonie beendet");

        // Diese zwei stehen nicht im Container und muessen zuerst weg: sie
        // halten native Handles (Infobereich-Symbol, Benachrichtigungen).
        _toasts?.Dispose();
        _tray?.Dispose();

        AppLog.ExitStep(_logger, "Infobereich und Benachrichtigungen freigegeben");

        // Die Dienste mit Ressourcen ausserhalb des Prozesses — einzeln und
        // mit Zeitmessung.
        //
        // <b>Warum das hier steht, obwohl der Container gleich alles freigibt.</b>
        // Am 08.09.2026 endete das Protokoll zweimal nach "Infobereich und
        // Benachrichtigungen freigegeben"; die Zeile "Dienste freigegeben" kam
        // nie, und der Waechter beendete nach acht Sekunden hart. Damit war
        // klar, DASS es in der Freigabe haengt — aber nicht, woran. Ein
        // Container-Dispose ist eine einzige Anweisung; haengt einer der
        // Dienste darin, sieht man nur das Schweigen danach.
        //
        // Das ist NICHT die handverlesene Liste von frueher, die den
        // Container ersetzen sollte und dabei die Haelfte vergass. Sie steht
        // davor, sie ist auf die Dienste beschraenkt, die ueberhaupt haengen
        // koennen (Threads, native Handles, Netz, Datei), und der Container
        // raeumt danach unveraendert alles Uebrige weg. Doppeltes Dispose ist
        // bei allen dieser Dienste idempotent.
        DisposeWithLog<SdkLogBridge>();
        DisposeWithLog<ConnectivityMonitor>();
        DisposeWithLog<GlobalHotkeyService>();
        DisposeWithLog<HeadsetCallControl>();
        DisposeWithLog<BlfService>();
        DisposeWithLog<ContactStore>();
        DisposeWithLog<IntegrationHttpClient>();
        DisposeWithLog<CallHistoryStore>();
        DisposeWithLog<SipService>();

        // Ab hier schreibt kein ILogger mehr.
        //
        // Der Container gibt den Serilog-Provider frei, und der ist mit
        // "dispose: true" registriert — er nimmt den Logger mit. Alles, was
        // danach ueber _logger laeuft, geht ins Leere.
        //
        // Genau darauf bin ich am 08.09.2026 hereingefallen: die Zeile
        // "Dienste freigegeben" fehlte zweimal im Protokoll, und ich habe das
        // als "es haengt in Services.Dispose()" gelesen. Es hing nicht — das
        // Protokoll war zu. Eine fehlende Zeile heisst nur dann "bis hierher
        // und nicht weiter", wenn ueberhaupt noch jemand schreiben KANN.
        AppLog.ExitStep(_logger, "Dienste werden freigegeben");

        (Services as IDisposable)?.Dispose();

        WriteShutdownNote("Dienste freigegeben, der Prozess endet jetzt regulaer");

        Log.CloseAndFlush();
        Exit();

        // <b>Hierher kommt niemand, und genau das ist die Aussage.</b>
        // Application.Exit() verlaesst die Nachrichtenschleife des
        // Hauptthreads; der Aufruf kehrt nicht zurueck, wenn er wirkt.
        //
        // Kehrt er doch zurueck, steht das beim naechsten Mal da. Ohne diese
        // Zeile sah der Fall vom 07.09. bis 12.09.2026 aus wie „es haengt
        // irgendwo in der Freigabe" — dabei war die Freigabe nach einer
        // Sekunde durch, und es hing in der letzten Anweisung. Eine
        // Beendigung, die nur so aussieht, darf nicht noch einmal fuenf Tage
        // brauchen.
        WriteShutdownNote(
            "Exit() ist zurückgekehrt, ohne den Prozess zu beenden — "
                + "laeuft das Beenden wieder auf dem falschen Thread?");
    }

    /// <summary>
    /// Gibt einen Dienst frei und schreibt auf, wie lange er dafür gebraucht
    /// hat.
    ///
    /// <para>Die Zeitangabe ist der Punkt: „hängt" und „dauert lange" sehen im
    /// Protokoll gleich aus, solange niemand misst. Eine Ausnahme beim
    /// Freigeben wird protokolliert und geschluckt — beim Beenden ist ein
    /// Dienst, der sich beschwert, kein Grund, die übrigen stehen zu
    /// lassen.</para>
    /// </summary>
    private void DisposeWithLog<T>()
        where T : class
    {
        var start = Stopwatch.GetTimestamp();

        try
        {
            (Services.GetService<T>() as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.ExitStepFailed(_logger, typeof(T).Name, Describe(ex));
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(start);

        // Nur nennen, was auffaellt. Zwanzig Zeilen "0 ms" beim Beenden
        // verdecken die eine, auf die es ankommt.
        if (elapsed > TimeSpan.FromMilliseconds(100))
        {
            AppLog.ExitStepSlow(_logger, typeof(T).Name, (long)elapsed.TotalMilliseconds);
        }
    }

    private void ApplyWindowsIntegration(NippSettings settings)
    {
        var integration = Services.GetRequiredService<WindowsIntegration>();
        integration.SetAutostart(settings.Advanced.StartWithWindows, settings.Advanced.StartMinimized);
        integration.RegisterProtocolHandlers(settings.Advanced.RegisterProtocolHandlers);
    }

    /// <summary>
    /// Das systemweite Tastenkuerzel (§10, AP7.6). Holt nipp nach vorn und
    /// setzt den Zeiger ins Eingabefeld — der Sinn der Sache ist, aus einer
    /// beliebigen Anwendung heraus eine Nummer waehlen zu koennen.
    /// </summary>
    private void StartHotkey(NippSettings settings)
    {
        var hotkeys = Services.GetRequiredService<GlobalHotkeyService>();

        hotkeys.Pressed += (_, rolle) =>
            // Kommt vom eigenen Nachrichtenthread; alles Weitere gehoert auf
            // den UI-Thread.
            Eingereiht($"Tastenkuerzel {rolle}", () =>
            {
                if (rolle == HotkeyRole.Stumm)
                {
                    HandleMuteHotkey();
                    return;
                }

                HandleHotkey();
            });

        hotkeys.Apply(HotkeyRole.AnnehmenAuflegen, settings.Advanced.GlobalHotkey);

        // Leer heisst «keines», und das ist die Vorgabe (C7, ADR-050).
        hotkeys.Apply(HotkeyRole.Stumm, settings.Advanced.MuteHotkey);
    }

    /// <summary>
    /// Was das systemweite Kürzel tut.
    ///
    /// §9.6 nennt es „Globaler Hotkey <b>Annehmen/Auflegen</b>", die
    /// Testmatrix führt T26 als „nimmt an und legt auf". Beides fehlte: das
    /// Kürzel holte nur das Fenster nach vorn.
    ///
    /// Die Reihenfolge ist die des Alltags: klingelt es, wird angenommen —
    /// dort zählt jede Sekunde. Läuft ein Gespräch, wird aufgelegt. Sonst
    /// kommt nipp nach vorn, damit sich aus einer beliebigen Anwendung heraus
    /// wählen lässt.
    ///
    /// <para><b>Dieselbe Regel wie am Headset</b>, und seit dem 09.09.2026
    /// auch derselbe Code (<see cref="HeadsetPolicy.Interpret"/>). Sie stand
    /// hier zweimal, und die Fassung am Headset war die falsche: sie hing am
    /// Gabelzustand des Geräts. Zwei Kopien einer Regel sind zwei
    /// Gelegenheiten, sie falsch zu haben — und eine, nur die eine zu
    /// korrigieren.</para>
    /// </summary>
    private void HandleHotkey()
    {
        var sip = Services.GetRequiredService<ISipService>();

        var (action, call) = HeadsetPolicy.Interpret(sip.ActiveCalls);

        try
        {
            if (action == HeadsetAction.Annehmen && call is not null)
            {
                sip.AcceptAsync(call.Handle).GetAwaiter().GetResult();
                AppLog.HotkeyAction(_logger, "angenommen");
                BringToFront();
                return;
            }

            if (action == HeadsetAction.Auflegen && call is not null)
            {
                sip.HangUpAsync(call.Handle).GetAwaiter().GetResult();
                AppLog.HotkeyAction(_logger, "aufgelegt");
                return;
            }
        }
        catch (Exception ex)
        {
            // Ein Kürzel, das eine Ausnahme wirft, nimmt sonst die ganze App
            // mit — und zwar aus einem Ereignishandler heraus.
            AppLog.HotkeyFailed(_logger, ex.Message);
        }

        BringToFront();
    }

    /// <summary>
    /// Was das zweite systemweite Kürzel tut: das laufende Gespräch stumm
    /// schalten und wieder zurück (C7, ADR-050).
    ///
    /// <para><b>Es holt nipp nicht nach vorn</b> — das ist der ganze Sinn.
    /// Stummschalten passiert mitten in einer anderen Anwendung, oft ohne
    /// hinzusehen; ein Fenster, das dabei aufspringt, nähme genau das weg,
    /// wofür man die Taste drückt.</para>
    ///
    /// <para><b>Und ohne Gespräch tut es nichts</b>, statt eine Meldung zu
    /// zeigen, die niemand gerufen hat — dieselbe Regel wie beim Eintrag im
    /// Infobereich. Das Protokoll hält beide Fälle fest: ohne Zeile wäre «die
    /// Taste tut nichts» nicht von «hier kommt nichts an» zu unterscheiden,
    /// und genau diese Lücke hat dieses Projekt am Headset zwei Tage
    /// gekostet.</para>
    /// </summary>
    private void HandleMuteHotkey()
    {
        var sip = Services.GetRequiredService<ISipService>();

        var call = sip.ActiveCalls.FirstOrDefault(c => c.Status == CallStatus.Connected);

        if (call is null)
        {
            AppLog.HotkeyAction(_logger, "stumm: kein Gespraech");
            return;
        }

        try
        {
            sip.SetMutedAsync(call.Handle, !call.IsMuted).GetAwaiter().GetResult();
            AppLog.HotkeyAction(_logger, call.IsMuted ? "Stummschaltung aufgehoben" : "stumm geschaltet");
        }
        catch (Exception ex)
        {
            // Wie beim anderen Kürzel: eine Ausnahme aus einem
            // Ereignishandler nimmt sonst die ganze Anwendung mit.
            AppLog.HotkeyFailed(_logger, ex.Message);
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(CreateLogger(), dispose: true);
        });

        // Telefonie (§6)
        services.AddSingleton<SdkLoadProbe>();
        services.AddSingleton<SdkLogBridge>();

        // §9.2 und §14.6: das SDK braucht die Wurzelzertifikate als Datei,
        // sonst ist „Serverzertifikat pruefen" eine Anzeige ohne Wirkung.
        services.AddSingleton<RootCertificates>();
        services.AddSingleton<SipService>();
        services.AddSingleton<ISipService>(sp => sp.GetRequiredService<SipService>());
        services.AddSingleton<ISipEventPump>(sp => sp.GetRequiredService<SipService>());
        services.AddSingleton<SipPumpHost>();
        services.AddSingleton<ConnectivityMonitor>();
        services.AddSingleton<SettingsApplier>();

        // §22.5: Annehmen und Auflegen ueber die Tasten am Headset. Nicht im
        // SDK — die Jabra-Anbindung ueber HIDAPI wurde mit 5.5.0 entfernt.
        services.AddSingleton<HeadsetCallControl>();

        // Einstellungen und Zugangsdaten (§9, §10)
        services.AddSingleton<SecretStore>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<PolicyService>();
        services.AddSingleton<ProvisioningService>();

        // Anrufliste (§8.3) und Diagnose (§9.6)
        services.AddSingleton<CallHistoryStore>();

        // Kontakte (§8.4). Die Reihenfolge der Quellen bestimmt die Reihenfolge
        // in der Liste: Team zuerst, dann Outlook.
        services.AddSingleton<IContactSource, TeamContactSource>();
        services.AddSingleton<IContactSource, OutlookContactSource>();
        services.AddSingleton<ContactStore>();
        services.AddSingleton<ClipResolver>();
        services.AddSingleton<BlfService>();
        services.AddSingleton<DiagnosticsBundle>();

        // Integrationen (§21). Der Kern kennt weder das SDK noch WinUI; ein
        // Architekturtest erzwingt das. Geladen wird die Konfiguration erst in
        // StartShellServices — nichts davon gehört vor die Telefonie.
        services.AddSingleton<IntegrationSecrets>();
        services.AddSingleton<IntegrationConfigValidator>();
        services.AddSingleton<IntegrationConfigStore>();
        services.AddSingleton<IntegrationHttpClient>();
        services.AddSingleton<IntegrationTester>();

        // Die Anbietervorlagen (ADR-040). Geladen wird NICHT hier, sondern in
        // StartShellServices: nichts an den Integrationen darf zwischen dem
        // Start und dem ersten moeglichen Anruf stehen (Paragraph 21.2).
        services.AddSingleton<ConnectorLibrary>();
        services.AddSingleton<IntegrationProvisioning>();

        // Welche Karte für welche Art gilt (ADR-032). Eine Stelle für drei
        // Empfänger — Gesprächsansicht, Toast und die Vorschau im Designer.
        services.AddSingleton<CardResolver>();

        // Der Katalog „Quelle hinzufügen" ist statisch (eingebettete
        // Ressourcen, unveränderlich); nur die Testdaten für die
        // Kartenvorschau haben Zustand — und der bleibt im Arbeitsspeicher,
        // weil dort personenbezogene Antworten fremder Systeme liegen (§21.2).
        services.AddSingleton<TestSampleStore>();

        // Kontaktsuche (§21.1). Die Reihenfolge der Anbieter ist wie bei
        // IContactSource Fachlogik: die lokale Quelle steht zuerst und
        // antwortet ohne Netz, damit beim Tippen sofort etwas erscheint.
        // Anbieter für externe Quellen kommen in I6 dazu.
        services.AddSingleton<IContactSearchProvider, LocalSnapshotSearchProvider>();

        // Die externen Quellen entstehen erst aus der Konfiguration und können
        // sich zur Laufzeit ändern — deshalb die Registry dazwischen und nicht
        // eine feste Liste im Container.
        services.AddSingleton<IntegrationRegistry>();
        services.AddSingleton<ISearchProviderRegistry>(
            sp => sp.GetRequiredService<IntegrationRegistry>());
        services.AddSingleton<ICallerContextProviderRegistry>(
            sp => sp.GetRequiredService<IntegrationRegistry>());
        services.AddSingleton<IContactMerger, ContactMerger>();
        services.AddSingleton<ContactSearchService>();

        // Anruferkontext (§21.1). Die lokalen Kontakte antworten ohne Netz und
        // stehen deshalb vorn — die Karte zeigt sofort einen Namen.
        services.AddSingleton<ICallerContextProvider, LocalContactsContextProvider>();
        services.AddSingleton<CallerContextService>();

        // Wie der Gespraechspartner heisst, beantwortet genau eine Stelle
        // (ADR-043). Sie bekommt den Kontextdienst ueber eine schmale
        // Schnittstelle — dieselbe Instanz, nur ohne dass der Auflöser die
        // ganze Klasse kennt; das haelt seine Tests bei drei Zeilen Attrappe.
        services.AddSingleton<ICallContextSnapshots>(
            static sp => sp.GetRequiredService<CallerContextService>());
        services.AddSingleton<CallPartyResolver>();
        services.AddSingleton<CallerCardViewModel>();
        services.AddSingleton<IntegrationSettingsViewModel>();

        // Update-Prüfung (§9.6, ADR-039). Das Gateway kennt Velopack, der
        // Dienst nicht — deshalb ist der Dienst prüfbar und das Gateway
        // austauschbar, wenn die Update-Quelle wandert.
        //
        // Die Gesprächssperre kommt als Func<bool> und nicht als
        // ISipService-Abhängigkeit: der Update-Dienst braucht vom Telefon
        // genau eine Auskunft, und mehr soll er nicht bekommen.
        services.AddSingleton<IUpdateGateway, VelopackUpdateGateway>();
        services.AddSingleton(sp => new UpdateService(
            sp.GetRequiredService<ILogger<UpdateService>>(),
            sp.GetRequiredService<IUpdateGateway>(),
            () => sp.GetRequiredService<ISipService>().ActiveCalls.Count > 0));

        // Windows-Integration (§10) und Erscheinungsbild (§20.4)
        services.AddSingleton<WindowsIntegration>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<TrayIconHost>();

        // ViewModels. Singletons, weil sie Ereignisse abonnieren.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ActiveCallViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Serilog für die App. Das SDK loggt separat über seinen eigenen
    /// <c>LoggingService</c> (§4) — zwei Ströme, ein gemeinsamer Log-Ordner,
    /// damit das Diagnosepaket (§9.6) beide einsammeln kann.
    /// </summary>
    /// <summary>
    /// Die Stufe des Protokolls, umschaltbar zur Laufzeit.
    ///
    /// Statisch, weil <see cref="CreateLogger"/> im Konstruktor laeuft, bevor
    /// es Dienste gibt. Bis zum 05.09.2026 stand die Stufe fest auf
    /// Information — die Einstellung „Protokollierung: Aus/Info/Debug" wurde
    /// gespeichert und nirgends gelesen.
    /// </summary>
    private static readonly Serilog.Core.LoggingLevelSwitch LogLevelSwitch =
        new(Serilog.Events.LogEventLevel.Information);

    /// <summary>
    /// §9.6: Aus, Info oder Debug.
    ///
    /// „Aus" heisst hier Warnungen und Fehler, nicht Stille: ein Absturz ohne
    /// Protokollzeile waere die falsche Sparsamkeit, und das Diagnosepaket
    /// (§9.6) lebt von genau diesen Zeilen.
    /// </summary>
    private static void ApplyLogVerbosity(LogVerbosity verbosity) =>
        LogLevelSwitch.MinimumLevel = verbosity switch
        {
            LogVerbosity.Debug => Serilog.Events.LogEventLevel.Debug,
            LogVerbosity.Off => Serilog.Events.LogEventLevel.Warning,
            _ => Serilog.Events.LogEventLevel.Information,
        };

    private static Serilog.Core.Logger CreateLogger()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nipp",
            "logs");

        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LogLevelSwitch)
            .WriteTo.File(
                Path.Combine(logDirectory, "nipp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,

                // W1.5 (Befund E13): eine Obergrenze je Datei.
                //
                // Serilogs Standard sind 1 GB am Tag. Auf Debug entstehen im
                // Gespraech sehr viele Zeilen — am 12.09.2026 waren es 8 MB
                // bei gelegentlicher Nutzung, und der SIP-Trace ist die
                // groesste Quelle davon. 64 MB reichen fuer einen vollen
                // Arbeitstag auf Debug und passen noch in ein Diagnosepaket,
                // das jemand per Mail schickt.
                fileSizeLimitBytes: 64L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Schreibt eine Ausnahme in eine Datei neben die Logs. Absichtlich ohne
    /// jede Abhängigkeit — dieser Pfad muss auch dann funktionieren, wenn der
    /// DI-Container oder Serilog nicht stehen.
    /// </summary>
    /// <summary>
    /// Eine Zeile ueber das Beenden, geschrieben ohne Logger.
    ///
    /// <para>Zwei Meldungen kommen zu spaet fuer Serilog: die nach dem
    /// Freigeben des Containers (der nimmt den Logger mit) und die des
    /// Waechters. Beide sind aber genau die, an denen sich entscheidet, ob
    /// nipp regulaer endet oder hart beendet wird — sie duerfen nicht die
    /// einzigen sein, die niemand liest.</para>
    ///
    /// <para>Eigene Datei statt der Protokolldatei: die haelt Serilog offen,
    /// und ein zweiter Schreiber darauf waere ein Wettlauf.</para>
    /// </summary>
    /// <summary>
    /// Legt eine zu gross gewordene Datei einmal beiseite (W1.5, Befund E13).
    ///
    /// <para><c>crash.txt</c> und <c>beenden.txt</c> werden nur angehängt und
    /// nie gedreht. Auf einem Arbeitsplatz, der jeden Tag ein paarmal
    /// abstürzt, wächst <c>crash.txt</c> unbegrenzt — und landet in jedem
    /// Diagnosepaket, das jemand verschicken will.</para>
    ///
    /// <para><b>Eine Sicherung, nicht viele.</b> Die vorige <c>.alt</c> wird
    /// überschrieben: zwei Dateien mit den jüngsten Einträgen genügen, und
    /// eine Verwaltung mit Nummern wäre mehr Code als die Sache wert.</para>
    /// </summary>
    private static void KappeWennZuGross(string pfad)
    {
        const long grenze = 4L * 1024 * 1024;

        try
        {
            var datei = new FileInfo(pfad);

            if (!datei.Exists || datei.Length < grenze)
            {
                return;
            }

            File.Move(pfad, pfad + ".alt", overwrite: true);
        }
        catch (Exception)
        {
            // Siehe unten: hier ist nichts mehr zu retten, und ein Absturz
            // beim Aufraeumen waere die schlechteste aller Antworten.
        }
    }

    private static void WriteShutdownNote(string text)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nipp",
                "logs");
            Directory.CreateDirectory(directory);

            var pfad = Path.Combine(directory, "beenden.txt");

            KappeWennZuGross(pfad);

            File.AppendAllText(
                pfad,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {text}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Dieselbe Haltung wie bei WriteCrashFile: geht das auch nicht
            // mehr, ist ohnehin nichts mehr zu retten.
        }
    }

    private static void WriteCrashFile(object? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nipp",
                "logs");
            Directory.CreateDirectory(directory);

            var pfad = Path.Combine(directory, "crash.txt");

            KappeWennZuGross(pfad);

            File.AppendAllText(
                pfad,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 70)}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Wenn nicht einmal das geht, ist ohnehin nichts mehr zu retten.
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.UnhandledException(_logger, e.Exception, e.Message);
        WriteCrashFile(e.Exception);

        Log.CloseAndFlush();
    }

    /// <summary>
    /// Räumt Infobereich und Benachrichtigungen ab. Wird im Normalfall über
    /// <c>ExitApplication</c> erreicht; die Schnittstelle steht hier, weil
    /// beide Dienste native Ressourcen halten (CA1001).
    /// </summary>
    public void Dispose()
    {
        _toasts?.Dispose();
        _tray?.Dispose();
        (Services as IDisposable)?.Dispose();

        GC.SuppressFinalize(this);
    }
}
