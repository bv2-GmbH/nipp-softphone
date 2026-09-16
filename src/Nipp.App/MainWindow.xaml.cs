using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Nipp.App.Diagnostics;
using Nipp.App.Theming;
using Nipp.App.Views;
using Nipp.App.Windows;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Windows.Graphics;
using WinRT.Interop;

namespace Nipp.App;

/// <summary>
/// Das Fenster von nipp — schmal, im Smartphone-Format (§20.1).
///
/// Kein Code-Behind ruft das SDK (§6); der Zugriff auf <c>ISipService</c>
/// dient nur dazu, bei einem Gespräch die Ansicht zu wechseln.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ISipService _sip;
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var services = ((App)Application.Current).Services;
        _sip = services.GetRequiredService<ISipService>();
        _settings = services.GetRequiredService<SettingsService>();
        _theme = services.GetRequiredService<ThemeService>();

        ApplyWindowChrome();

        // §20.4: Erscheinungsbild anwenden und bei Systemwechsel nachziehen.
        _theme.Attach(RootGrid, _settings.Current.Advanced.Theme);

        _sip.CallStateChanged += OnCallStateChanged;
        _settings.Changed += OnSettingsChanged;
        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
        Closed += OnClosed;

        ApplyTitleBarIcon();

        ContentFrame.Navigate(typeof(ShellPage));
    }

    /// <summary>
    /// <b>Ein Bild, nicht zwei</b> (W1.4, Befund D1). Hier stand bis zum
    /// 13.09.2026 eine Wahl nach Erscheinungsbild, mit der Begründung, die
    /// helle Fassung stehe auf dunklem Mica als Fleck. <b>Die beiden Dateien
    /// waren byteidentisch</b> — dieselbe Prüfsumme, seit dem Logowechsel am
    /// 11.09.2026. Die Wahl hat also nie etwas bewirkt, und der Kommentar
    /// beschrieb einen Zustand, den es nicht gab.
    ///
    /// <para><b>Warum eine Datei und nicht zwei echte.</b> Das Logo ist eine
    /// farbige Marke, kein einfarbiges Zeichen; eine Hell- und eine
    /// Dunkelfassung wären Grafikarbeit, keine Codeänderung. Der Code behauptet
    /// jetzt nichts mehr, was er nicht hält. Fällt am Gerät auf, dass es auf
    /// einer der beiden Taskleisten schlecht liest (T269), kommt die Wahl mit
    /// zwei echten Dateien zurück — sie ist zehn Zeilen.</para>
    /// </summary>
    private void ApplyTitleBarIcon()
    {
        const string name = "nipp.png";

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", name)))
        {
            return;
        }

        // ms-appx und nicht der Dateipfad: BitmapImage loest ein file:-URI in
        // WinUI nicht auf, und ein gescheitertes Decodieren meldet sich nur
        // ueber ImageFailed — die Titelleiste bliebe stumm ohne Symbol.
        // ms-appx funktioniert auch unpackaged und zeigt dann auf dasselbe
        // Verzeichnis, das oben geprueft wurde.
        var image = new BitmapImage(new Uri($"ms-appx:///Assets/{name}"));
        image.ImageFailed += (_, e) => AppLog.TitleBarIconFailed(
            (Application.Current as App)!.Services.GetRequiredService<ILogger<MainWindow>>(),
            e.ErrorMessage);

        AppTitleBar.IconSource = new ImageIconSource { ImageSource = image };
    }

    private void OnEffectiveThemeChanged(object? sender, ElementTheme e) => ApplyTitleBarIcon();

    /// <summary>
    /// Fenstergrösse, Symbol und „immer im Vordergrund" (§20.1, §20.4, §20.5).
    ///
    /// Grösse und Position kommen aus <see cref="WindowPlacement"/> — samt der
    /// Begrenzung auf den Arbeitsbereich, damit das Fenster auf einem
    /// 1080p-Bildschirm mit 150 % Skalierung vollständig sichtbar bleibt.
    /// </summary>
    private void ApplyWindowChrome()
    {
        var appWindow = AppWindowRef;

        WindowPlacement.Apply(this, _settings.Current.Advanced.WindowPlacement);
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        ApplyAlwaysOnTop(appWindow, _settings.Current.Advanced.AlwaysOnTop);
    }

    /// <summary>
    /// Merkt sich die Lage des Fensters.
    ///
    /// Beim Schliessen, nicht bei jeder Bewegung: <c>AppWindow.Changed</c>
    /// feuert während des Ziehens im Dutzendtakt, und jedes Mal die
    /// Einstellungsdatei zu schreiben wäre eine Menge Schreibzugriffe für
    /// nichts.
    /// </summary>
    private void RememberPlacement()
    {
        if (WindowPlacement.Capture(AppWindowRef) is not { } placement)
        {
            return;
        }

        var current = _settings.Current;

        if (string.Equals(current.Advanced.WindowPlacement, placement, StringComparison.Ordinal))
        {
            return;
        }

        // Reiner Anzeigezustand: die Fensterlage geht niemanden ausserhalb
        // dieser Klasse etwas an.
        _settings.SaveViewState(current with
        {
            Advanced = current.Advanced with { WindowPlacement = placement },
        });
    }

    /// <summary>
    /// §20.5: Fenster über allen anderen halten.
    ///
    /// Geht nur über <see cref="OverlappedPresenter"/> — ein Fenster im
    /// Standardzustand kennt die Eigenschaft nicht.
    /// </summary>
    private static void ApplyAlwaysOnTop(AppWindow appWindow, bool alwaysOnTop)
    {
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = alwaysOnTop;
        }
    }

    private void OnSettingsChanged(object? sender, NippSettings settings)
    {
        ApplyAlwaysOnTop(AppWindowRef, settings.Advanced.AlwaysOnTop);
        _theme.SetPreference(settings.Advanced.Theme);
    }

    /// <summary>
    /// Welche Anrufe die Gesprächsansicht schon einmal geholt haben.
    ///
    /// <b>Warum nach Kennung und nicht nach Zustandswechsel.</b> Vorher hing die
    /// Navigation an einer Liste erlaubter Übergänge („von Wählen nach
    /// Verbunden" und so fort). Die Reihenfolge, in der das SDK seine Zustände
    /// meldet, ist aber für ein- und ausgehende Anrufe verschieden: ein
    /// ausgehender geht über <c>Ringing</c>, und dieser Übergang stand nicht in
    /// der Liste — die Gesprächsansicht erschien nie. Ein eingehender wurde
    /// bereits klingelnd angelegt und war deshalb nie „neu klingelnd".
    ///
    /// Die Kennung eines Anrufs ist dagegen eindeutig und von der Reihenfolge
    /// unabhängig: <b>jeder Anruf holt die Ansicht genau einmal nach vorn</b>,
    /// beim ersten Mal, dass wir von ihm hören. Wer danach zurück zur
    /// Wähltastatur geht — für den zweiten Anruf, §8.2 —, wird nicht wieder
    /// hineingezogen.
    /// </summary>
    private readonly HashSet<CallHandle> _announcedCalls = [];

    /// <summary>
    /// Bei einem Gespräch in die Gesprächsansicht wechseln und danach zurück.
    /// §8.2 gilt inhaltlich weiter, nur ohne Navigationsleiste (§20.1).
    /// </summary>
    private void OnCallStateChanged(object? sender, CallStateEventArgs e)
    {
        var hasCall = _sip.ActiveCalls.Count > 0;

        if (!hasCall)
        {
            _announcedCalls.Clear();

            if (ContentFrame.CurrentSourcePageType == typeof(ActiveCallPage))
            {
                ContentFrame.Navigate(typeof(ShellPage));
            }

            // Der Verlauf wuchs bei jedem Anruf um zwei Einträge, und jeder
            // hielt eine Seiteninstanz am Leben. Nach dem Wechsel zurück in die
            // Shell gibt es nichts mehr, wohin „zurück" führen sollte.
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.BackStack.Clear();
            }

            return;
        }

        // Add liefert false, wenn die Kennung schon drin war — dieser Anruf hat
        // die Ansicht also bereits einmal geholt.
        if (!_announcedCalls.Add(e.Call.Handle))
        {
            return;
        }

        // <b>Im breiten Layout wird nicht navigiert.</b> Dort zeigt die
        // Hauptansicht das Gespraech selbst — in ihrer linken Spalte, waehrend
        // rechts die Nebenstellen mit ihren Lampen stehen bleiben
        // (ShellPage.ApplyCallView). Wuerde hier trotzdem navigiert, staende
        // die Gespraechsansicht zweimal im Baum, und die Shell haette
        // niemanden mehr, der ihre Breite misst.
        //
        // <b>Gefragt wird das ViewModel und nicht das Fenster</b>: welches
        // Layout gilt, entscheidet ApplyWidth an genau einer Stelle (ADR-047).
        if (!IstBreit() && ContentFrame.CurrentSourcePageType != typeof(ActiveCallPage))
        {
            ContentFrame.Navigate(typeof(ActiveCallPage));
        }

        // §8.6 wörtlich: „Klick auf Annehmen: App in den Vordergrund." Der
        // Vordergrund gehört an das ANNEHMEN, nicht an das Klingeln (C2).
        //
        // <b>Was hier bis zum 13.09.2026 stand, nahm jedem eingehenden Anruf
        // den Fokus — und nahm dabei manchmal den Anruf an.</b> ShowFromTray()
        // ruft Activate() und notfalls SetForegroundWindow; die Navigation
        // darüber setzt den Fokus auf „Annehmen" (ADR-044, B10). Wer gerade in
        // einer anderen Anwendung tippte, bekam das Fenster vor die Nase, und
        // die nächste Leertaste oder Eingabetaste nahm das Gespräch entgegen.
        // Zwei je für sich richtige Entscheidungen, zusammen ein Anruf, den
        // niemand angenommen hat.
        //
        // Das Zeichen beim Klingeln ist der Toast: er läuft im Szenario
        // IncomingCall, bleibt stehen, bis jemand reagiert, und erscheint auch
        // über einer Vollbildanwendung.
        //
        // <b>Die eine Ausnahme:</b> kommt der Toast nicht durch, ist das
        // Fenster das einzige Zeichen — dann wiegt Sichtbarkeit mehr als Ruhe.
        // Gefragt wird der Dienst, nicht eine Einstellung: wer das beantworten
        // kann, ist er.
        //
        // <b>Aber ohne den Fokus zu nehmen</b> (W1.1, 13.09.2026). Die
        // Fassung vom 13.09. rief hier ShowFromTray(), und das ist derselbe
        // Weg wie ein Klick auf das Infobereich-Symbol: Activate(), notfalls
        // SetForegroundWindow. Damit war der Befund von ADR-049 im
        // Rueckfallpfad neu gebaut — auf jedem Arbeitsplatz, auf dem die
        // Benachrichtigungen nicht durchkommen (packaged/T110, eine
        // gescheiterte Registrierung), nahm die naechste Leertaste in Word den
        // Anruf an. Und genau diese Arbeitsplaetze sind die, auf denen der
        // Rueckfall ueberhaupt greift.
        if (e.Call.Direction == CallDirection.Incoming && !ToastsWerdenGezeigt())
        {
            ShowWithoutStealingFocus();
        }
    }

    /// <summary>
    /// Ob gerade das breite Layout gilt — <b>gelesen und nicht selbst
    /// entschieden</b>.
    ///
    /// <para>Die Schwelle steht in <c>ShellViewModel.ApplyWidth</c>, und zwar
    /// dort allein (ADR-047, §23). Dieses Fenster fragt nur nach; eine eigene
    /// Breitenrechnung hier waere die zweite Wahrheit ueber dasselbe.</para>
    /// </summary>
    private static bool IstBreit() =>
        ((App)Application.Current).Services
            .GetService<Nipp.Core.ViewModels.ShellViewModel>() is { IsWide: true };

    /// <summary>
    /// Ob ein eingehender Anruf auch ohne dieses Fenster sichtbar wird (C2).
    /// </summary>
    private static bool ToastsWerdenGezeigt()
    {
        var toasts = ((App)Application.Current).Services
            .GetService<Nipp.App.Windows.ToastService>();

        return toasts is { NotificationsAvailable: true };
    }

    /// <summary>
    /// §10: „Schliessen des Fensters beendet die App nicht." Das Fenster
    /// verschwindet, die Telefonie läuft weiter.
    /// </summary>
    public void HideToTray()
    {
        RememberPlacement();
        AppWindowRef.Hide();
    }

    /// <summary>
    /// Holt das Fenster zurück nach vorn — aus dem Infobereich, vom Toast oder
    /// von einem zweiten Programmstart (§10).
    ///
    /// <para><b>Der WinUI-Weg funktioniert</b> — nachgemessen am 07.09.2026:
    /// Fenster verstecken, zweiter Programmstart, Fenster ist wieder sichtbar.
    /// Die Ursache des gemeldeten „Doppelklick öffnet nicht" lag im
    /// <c>TrayIconHost</c> und im Nachrichtenmodus des Infobereich-Symbols,
    /// nicht hier.</para>
    ///
    /// <para><b>Der Win32-Weg darunter hat sich schon bewährt.</b> Beim
    /// Befund vom 07.09.2026 warf <c>AppWindow.Show()</c> eine
    /// <c>COMException: „Unzulässiges Fenster. Es gehört zu einem anderen
    /// Thread."</c> — der Aufruf kam aus dem Nachrichtenfenster von
    /// H.NotifyIcon, das eine eigene Pumpe auf einem eigenen Thread führt.
    /// Der Rückfall hat das Fenster trotzdem gezeigt und es ins Protokoll
    /// geschrieben; erst diese Zeile hat den Thread als Ursache verraten.
    /// Behoben ist es dort, wo es hingehört (<c>App</c> reicht den Aufruf über
    /// die <c>DispatcherQueue</c>) — der Rückfall bleibt als Netz.</para>
    ///
    /// <para>Denn dies ist der einzige Weg zurück ins Fenster: nipp lebt im
    /// Infobereich (§10), und wenn er ausfällt, ist die Anwendung nur noch
    /// über den Task-Manager erreichbar. Ein Weg, dessen Erfolg niemand
    /// prüft, fällt genau dann aus, wenn er gebraucht wird.</para>
    /// </summary>
    public void ShowFromTray()
    {
        var handle = WindowNative.GetWindowHandle(this);

        try
        {
            AppWindowRef.Show();

            // Nach dem Anzeigen muss es auch nach vorne: Windows lässt ein
            // wiederhergestelltes Fenster sonst hinter dem aktiven stehen.
            if (AppWindowRef.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            Activate();
        }
        catch (Exception ex)
        {
            // Nicht durchlassen: das hier ist der einzige Weg zurueck ins
            // Fenster. Eine Ausnahme darf ihn nicht abschneiden — der
            // Win32-Weg unten kommt trotzdem noch.
            AppLog.ShowFromTrayFailed(Log, $"{ex.GetType().Name}: {ex.Message}");
        }

        if (IsWindowVisible(handle))
        {
            return;
        }

        // Der Rueckfall. SW_SHOW zeigt das Fenster unabhaengig davon, was
        // WinUI ueber seinen Zustand denkt; SetForegroundWindow holt es nach
        // vorn.
        _ = ShowWindow(handle, SW_SHOW);
        _ = SetForegroundWindow(handle);

        AppLog.ShownByFallback(Log, IsWindowVisible(handle));
    }

    /// <summary>
    /// Zeigt das Fenster, <b>ohne</b> es in den Vordergrund zu holen (W1.1).
    ///
    /// <para><b>Warum das genügt.</b> Der Fokus innerhalb eines Fensters, das
    /// nicht im Vordergrund steht, bekommt keine Tastendrücke. Die
    /// Gesprächsansicht darf also weiterhin «Annehmen» fokussieren (ADR-044) —
    /// wer dann die Leertaste drückt, trifft die Anwendung, in der er gerade
    /// tippt. Beide Regeln bleiben, wie sie sind, und widersprechen sich
    /// nicht mehr.</para>
    ///
    /// <para><b>Warum nicht über <c>AppWindow</c>.</b> <c>AppWindow.Show()</c>
    /// aktiviert, und die Überladung mit <c>activateWindow: false</c> setzt
    /// das Fenster nach dem Anzeigen trotzdem nicht sichtbar nach oben. Der
    /// Win32-Weg tut beides ausdrücklich: <c>SW_SHOWNOACTIVATE</c> zeigt,
    /// <c>SetWindowPos</c> mit <c>SWP_NOACTIVATE</c> hebt es über die anderen
    /// Fenster, ohne den Vordergrund zu wechseln.</para>
    ///
    /// <para>Der Preis, offen genannt: das Fenster erscheint hinter dem
    /// aktiven Vollbildfenster, wenn eines läuft. Ohne Toast <em>und</em> im
    /// Vollbild bleibt der Klingelton das einzige Zeichen — das ist der Grund,
    /// warum T110 (Toasts in der packaged Fassung) offen und wichtig
    /// bleibt.</para>
    /// </summary>
    public void ShowWithoutStealingFocus()
    {
        var handle = WindowNative.GetWindowHandle(this);

        try
        {
            // Den AppWindow-Zustand nachziehen, damit WinUI das Fenster nicht
            // weiterhin fuer versteckt haelt — sonst stimmt beim naechsten
            // Klick auf das Infobereich-Symbol die Buchfuehrung nicht.
            if (AppWindowRef.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }
        }
        catch (Exception ex)
        {
            AppLog.ShowFromTrayFailed(Log, $"{ex.GetType().Name}: {ex.Message}");
        }

        _ = ShowWindow(handle, SW_SHOWNOACTIVATE);

        _ = SetWindowPos(
            handle,
            HWND_TOP,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        AppLog.ShownWithoutFocus(Log, IsWindowVisible(handle));
    }

    private static ILogger<MainWindow> Log =>
        ((App)Application.Current).Services.GetRequiredService<ILogger<MainWindow>>();

    private const int SW_SHOW = 5;

    /// <summary>Zeigt ein Fenster, ohne es zu aktivieren.</summary>
    private const int SW_SHOWNOACTIVATE = 4;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private AppWindow AppWindowRef
    {
        get
        {
            var handle = WindowNative.GetWindowHandle(this);
            return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));
        }
    }

    /// <summary>
    /// <b>Nur die Fensterlage.</b> Die Abonnements werden hier absichtlich
    /// nicht gelöst — siehe <see cref="DetachFromServices"/>.
    /// </summary>
    private void OnClosed(object sender, WindowEventArgs args) => RememberPlacement();

    /// <summary>
    /// Löst die Abonnements. Gehört an das <b>tatsächliche</b> Beenden und wird
    /// von <c>App.ExitApplication</c> gerufen.
    ///
    /// <para><b>Warum nicht in <c>Closed</c>.</b> WinUI meldet <c>Closed</c> bei
    /// jedem Klick auf das Fensterkreuz, und zwar <b>bevor</b> jemand
    /// <c>Handled</c> auswertet: alle Handler laufen, auch wenn das Fenster
    /// gleich darauf nur versteckt wird (§10 — Schliessen beendet nipp nicht).
    /// <c>MainWindow</c> hatte zuerst abonniert und meldete deshalb beim ersten
    /// Verstecken alles ab. Danach war das Fenster taub: ein eingehender Anruf
    /// holte die Gesprächsansicht nicht mehr, <c>ShowFromTray</c> blieb aus,
    /// Thema und „immer im Vordergrund" wurden nicht mehr nachgezogen.</para>
    ///
    /// <para>Im Alltag lebt nipp im Infobereich, also war das der Normalfall —
    /// und weil eine Abnahme am offenen Fenster den Fehler nicht zeigt, ist
    /// das der wahrscheinliche Grund, warum T06 nie bestanden hat.</para>
    /// </summary>
    public void DetachFromServices()
    {
        _sip.CallStateChanged -= OnCallStateChanged;
        _settings.Changed -= OnSettingsChanged;
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
    }
}
