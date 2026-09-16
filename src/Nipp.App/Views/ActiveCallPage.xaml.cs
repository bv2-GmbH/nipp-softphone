using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nipp.App.Diagnostics;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.App.Views;

/// <summary>
/// Aktives Gespräch (§8.2).
///
/// §6: kein Code-Behind ruft das SDK — diese Datei kennt nur das ViewModel und
/// eigene Modelltypen.
/// </summary>
public sealed partial class ActiveCallPage : Page
{
    /// <summary>
    /// Eigener Timer nur für die Dauer-Anzeige. Die Gesprächsdauer läuft
    /// weiter, ohne dass sich am Zustand etwas ändert — sie über die
    /// SDK-Ereignisse zu aktualisieren würde also nicht funktionieren, und den
    /// 20-ms-Takt dafür zu benutzen wäre 50-mal zu häufig.
    /// </summary>
    private readonly DispatcherQueueTimer _durationTimer;

    public ActiveCallViewModel ViewModel { get; }

    /// <summary>
    /// Was die angebundenen Systeme über die Gegenstelle wissen (§21.1).
    ///
    /// <c>null</c>, wenn keine Integrationen eingerichtet sind — dann bleibt
    /// die Karte weg, und die Ansicht sieht aus wie bisher.
    /// </summary>
    public CallerCardViewModel? CallerCard { get; }

    /// <summary>Für die Protokollzeile, wenn ein Handler scheitert (ADR-053).</summary>
    private readonly ILogger<ActiveCallPage> _logger;

    /// <summary>
    /// Der Navigationsparameter, mit dem die Hauptansicht diese Seite in ihre
    /// linke Spalte holt (breites Layout).
    ///
    /// <para><b>Warum ueberhaupt ein Parameter.</b> Eingebettet gilt zweierlei
    /// anders: der Zurueck-Pfeil hat keine Bedeutung — die Waehltastatur ist
    /// nicht weg, sie steht nur hinter dem Gespraech —, und er wuerde in den
    /// <i>inneren</i> Frame navigieren, also die Hauptansicht in sich selbst.
    /// Ein <c>const string</c> und kein <c>bool</c>, weil der Parameter im
    /// Protokoll und im Fehlerfall lesbar sein soll.</para>
    /// </summary>
    public const string Eingebettet = "eingebettet";

    /// <summary>
    /// Ob die Seite in der linken Spalte der Hauptansicht steht.
    /// </summary>
    private bool _eingebettet;

    public ActiveCallPage()
    {
        var services = ((App)Application.Current).Services;

        ViewModel = services.GetRequiredService<ActiveCallViewModel>();
        CallerCard = services.GetService<CallerCardViewModel>();
        _logger = services.GetRequiredService<ILogger<ActiveCallPage>>();

        InitializeComponent();

        // Die Seite bleibt erhalten, statt bei jeder Navigation neu zu
        // entstehen: die ViewModels sind Singletons, und jede neue Instanz
        // haengte sich zusaetzlich an deren Ereignisse. Bei 50 Anrufen ueber
        // acht Stunden (§2) waren das 100 Seiten, die niemand mehr freigibt.
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        _durationTimer = DispatcherQueue.CreateTimer();
        _durationTimer.Interval = TimeSpan.FromSeconds(1);
        _durationTimer.IsRepeating = true;
        _durationTimer.Tick += (_, _) => UpdateDuration();

        // Nur Loaded und Unloaded dauerhaft; alles Weitere in OnLoaded.
        //
        // <b>Warum.</b> Mit dem Zwischenspeicher laeuft dieser Konstruktor
        // genau einmal. Das erste Gespraech endet, die Seite wird entladen —
        // und beim zweiten Gespraech kam die alte Instanz zurueck, ohne dass
        // jemand sie wieder verbunden haette: die Dauer stand still, und die
        // Ansicht zeigte weiterhin die Gegenstelle des vorherigen Gespraechs.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Nimmt entgegen, ob diese Seite eingebettet steht (<see cref="Eingebettet"/>).
    ///
    /// <para>Die Seite wird zwischengespeichert (<c>NavigationCacheMode</c>),
    /// die Instanz ueberlebt also den Wechsel — deshalb wird der Zustand bei
    /// <b>jeder</b> Navigation neu gesetzt und nicht nur beim ersten Mal.</para>
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _eingebettet = e?.Parameter as string == Eingebettet;

        BackButton.Visibility = _eingebettet ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (CallerCard is not null)
        {
            CallerCard.PropertyChanged -= OnViewModelPropertyChanged;
            CallerCard.PropertyChanged += OnViewModelPropertyChanged;

            // Die Karte kann Angaben zu einem Anruf haben, der schon lief, als
            // diese Seite zuletzt entladen wurde — mit dem Zwischenspeicher
            // (NavigationCacheMode.Required) ist das der Normalfall.
            CallerCard.Refresh();

            CallerCardView.Model = CallerCard.Layout;
            CallerCardFields.ItemsSource = CallerCard.Fields;
            CallerCardSources.ItemsSource = CallerCard.Sources;
        }

        Refresh();
        _durationTimer.Start();

        // ADR-044: Der Fokus gehoert auf die Handlung, um die es hier geht.
        //
        // Die Seite setzte ihn gar nicht. Bei einem eingehenden Anruf erschien
        // „Annehmen" ueber die volle Breite, erreichbar mit der Tastatur aber
        // erst nach mehreren Tabulatorschritten durch Zurueck-Pfeil und
        // Auflegen — die zeitkritischste Handlung des Programms war die am
        // schlechtesten erreichbare.
        FocusPrimaryAction();
    }

    /// <summary>
    /// Das Wiedergabegerät wählen, ohne das Gespräch zu verlassen (ADR-046).
    ///
    /// <para>Das Menü wird bei jedem Klick neu gebaut: Geräte kommen und gehen,
    /// und genau dann, wenn jemand hier klickt, hat sich meistens gerade etwas
    /// geändert. Ein gehaltenes Menü zeigte die Liste von vorhin.</para>
    /// </summary>
    /// <summary>
    /// Die Rückmeldung des Auflegen-Knopfes beim Überfahren und Drücken
    /// (ADR-067).
    ///
    /// <para><b>Warum von Hand und nicht über das Thema.</b> Der übliche Weg
    /// dafür ist Lightweight-Styling — die Zustands-Schlüssel des Knopfes in
    /// seinen eigenen Ressourcen überschreiben. Genau das hat nipp am
    /// 14.09.2026 siebenmal beendet, jedes Mal beim blossen Überfahren, mit
    /// Ausnahmecode 0xc000027b in <c>combase.dll</c> und ohne verwaltete
    /// Ausnahme. Flach, mit Verweisen nach <c>Tokens.xaml</c> und in
    /// <c>ThemeDictionaries</c> — alle drei Formen stürzten ab, ohne sie blieb
    /// der Knopf stehen.</para>
    ///
    /// <para><b>Ein Behandler für alle sechs Ereignisse</b>, und der Zustand
    /// steht in zwei Feldern: gedrückt schlägt überfahren, und ein verlorener
    /// Zeiger (<c>PointerCaptureLost</c>) räumt beides ab. Wer je Ereignis
    /// eine eigene Zuweisung schriebe, hätte den halbdurchsichtigen Knopf
    /// stehen, sobald eine Meldung ausbleibt — und sie bleibt aus, wenn der
    /// Zeiger den Knopf verlässt, während die Taste gedrückt ist.</para>
    /// </summary>
    private void OnHangUpPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hangUpHover = true;
        UpdateHangUpFill();
    }

    private void OnHangUpPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _hangUpPressed = true;
        UpdateHangUpFill();
    }

    private void OnHangUpPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _hangUpPressed = false;
        UpdateHangUpFill();
    }

    /// <summary>
    /// Verlassen, abgebrochen, Zeiger verloren — <b>ein Weg für alle drei</b>.
    /// Jeder davon endet damit, dass der Knopf nichts mehr zu melden hat.
    /// </summary>
    private void OnHangUpPointerLeft(object sender, PointerRoutedEventArgs e)
    {
        _hangUpHover = false;
        _hangUpPressed = false;
        UpdateHangUpFill();
    }

    private void UpdateHangUpFill()
    {
        if (HangUpFill is not null)
        {
            HangUpFill.Opacity = _hangUpPressed ? 0.8 : _hangUpHover ? 0.9 : 1.0;
        }
    }

    private bool _hangUpHover;
    private bool _hangUpPressed;

    private void OnAudioDeviceClick(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        var aktiv = ViewModel.SelectedPlaybackDeviceId;

        // Seit W2.5 stehen beide Geräte im Menü, also braucht jede Hälfte
        // eine Überschrift — sonst sieht die Liste aus wie ein Gerät zu viel.
        menu.Items.Add(new MenuFlyoutItem { Text = "Wiedergabe", IsEnabled = false });

        // „Wie Windows" zuerst: das ist der Normalfall und der Weg zurück,
        // wenn ein eingestelltes Gerät nicht mehr da ist.
        menu.Items.Add(Eintrag("Windows-Standard", null, aktiv is null));

        foreach (var geraet in ViewModel.PlaybackDevices)
        {
            menu.Items.Add(Eintrag(
                geraet.Name,
                geraet.Id,
                string.Equals(geraet.Id, aktiv, StringComparison.Ordinal)));
        }

        // W2.5 (C5): das Mikrofon im selben Menü.
        //
        // <b>Der Befund.</b> Hier stand nur die Wiedergabe. Wer vom
        // Notebook-Audio aufs Headset wechselte, hörte am Headset und sprach
        // ins Notebook — und das Mikrofon lag hinter den Einstellungen, also
        // ausserhalb des Gesprächs. Eine halbe Antwort ist hier schlimmer als
        // keine: sie sieht aus, als wäre das Gerät umgestellt.
        //
        // In einem Menü und nicht in zweien: die Frage ist «welches Gerät»,
        // und wer eines wechselt, wechselt meistens beide.
        var mikrofone = ViewModel.CaptureDevices;

        if (mikrofone.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = "Mikrofon", IsEnabled = false });

            var aktivesMikrofon = ViewModel.SelectedCaptureDeviceId;

            menu.Items.Add(MikrofonEintrag("Windows-Standard", null, aktivesMikrofon is null));

            foreach (var geraet in mikrofone)
            {
                menu.Items.Add(MikrofonEintrag(
                    geraet.Name,
                    geraet.Id,
                    string.Equals(geraet.Id, aktivesMikrofon, StringComparison.Ordinal)));
            }
        }

        menu.ShowAt(AudioDeviceButton);

        ToggleMenuFlyoutItem Eintrag(string text, string? id, bool gewaehlt)
        {
            var eintrag = new ToggleMenuFlyoutItem { Text = text, IsChecked = gewaehlt };

            eintrag.Click += (_, _) => ViewModel.UsePlaybackDevice(id);

            return eintrag;
        }

        ToggleMenuFlyoutItem MikrofonEintrag(string text, string? id, bool gewaehlt)
        {
            var eintrag = new ToggleMenuFlyoutItem { Text = text, IsChecked = gewaehlt };

            eintrag.Click += (_, _) => ViewModel.UseCaptureDevice(id);

            return eintrag;
        }
    }

    /// <summary>
    /// Klingelt es, gehoert der Fokus auf „Annehmen"; sonst auf „Auflegen".
    ///
    /// <para><c>FocusState.Programmatic</c> und nicht <c>Keyboard</c>: der
    /// Fokusring soll nicht bei jedem Anruf aufblitzen, wenn jemand ohnehin
    /// mit der Maus arbeitet. Wer die Tastatur benutzt, ist mit dem ersten
    /// Tabulator dort, wo er hinwollte.</para>
    /// </summary>
    private void FocusPrimaryAction()
    {
        if (AcceptButton.Visibility == Visibility.Visible)
        {
            _ = AcceptButton.Focus(FocusState.Programmatic);
            return;
        }

        if (HangUpButton.Visibility == Visibility.Visible)
        {
            _ = HangUpButton.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// Hält den Takt an und löst das Abonnement — <c>Loaded</c> und
    /// <c>Unloaded</c> bleiben, die Seite wird wiederverwendet.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _durationTimer.Stop();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (CallerCard is not null)
        {
            CallerCard.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>
    /// Der Wächter für alles, was aus <c>async void</c> heraus läuft
    /// (ADR-053).
    ///
    /// <para><b>Warum es ihn braucht.</b> Ein Ereignisbehandler ist
    /// <c>async void</c> — das ist keine Wahl, sondern das, was WinUI
    /// verlangt. Eine Ausnahme daraus hat keinen Aufrufer mehr, der sie
    /// fängt: sie geht direkt an <c>App.OnUnhandledException</c>, und das
    /// protokolliert nur. Ein Klick auf «Stumm» im Moment, in dem die
    /// Gegenseite auflegt, beendete bis zum 13.09.2026 den Prozess.</para>
    ///
    /// <para><b>Alles, nicht nur <c>InvalidOperationException</c>.</b> Dieser
    /// Wächter ist die letzte Linie vor dem Absturz; welche Ausnahme ihn
    /// erreicht, ist dafür gleichgültig. Der Benutzer bekommt einen Satz, das
    /// Protokoll bekommt den Typ.</para>
    /// </summary>
    private async Task GuardAsync(string handler, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ViewModel.LastError = ex is InvalidOperationException
                ? ex.Message
                : UserMessage.WithCause("Die Aktion liess sich nicht ausführen.", ex);

            AppLog.HandlerFailed(_logger, handler, ex.GetType().Name);
        }
    }

    private async void OnMuteClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnMuteClick),
            () => ViewModel.ToggleMuteCommand.ExecuteAsync(null));

    private async void OnHoldClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnHoldClick),
            () => ViewModel.ToggleHoldCommand.ExecuteAsync(null));

    /// <summary>
    /// Die Aufnahme — <b>beim ersten Einschalten je Gespräch mit Rückfrage</b>
    /// (C3).
    ///
    /// <para><b>Warum überhaupt.</b> Ein Klick startete die Aufzeichnung
    /// sofort; die Warnleiste erschien danach. In der Schweiz ist das
    /// Mitschneiden ohne Kenntnis der Gegenseite strafbar (Art. 179<sup>ter</sup>
    /// StGB) — ein Fehlgriff ist hier kein Ärgernis. Bis zum 13.09.2026 lag der
    /// Knopf ausserdem zwei Spalten neben «Stumm», dem Knopf, den man ohne
    /// hinzusehen drückt.</para>
    ///
    /// <para><b>Und warum nicht jedes Mal.</b> Wer bewusst aufzeichnet, tut es
    /// oft in Serie; eine Rückfrage bei jedem Ein- und Ausschalten wäre eine
    /// Zumutung. Gefragt wird einmal je Gespräch — beim Ausschalten nie.</para>
    /// </summary>
    private async void OnRecordClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnRecordClick), () => RecordAsync());

    private async Task RecordAsync()
    {
        var call = ViewModel.SelectedCall;

        // Ausschalten und ein bereits bestätigtes Gespräch gehen ohne Frage
        // durch.
        if (call is { IsRecording: false } && _aufnahmeGefragt.Add(call.Handle))
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Gespräch aufzeichnen?",
                Content = "Die Gegenseite muss davon wissen — ohne ihre Kenntnis ist "
                    + "das Mitschneiden strafbar. Die Aufnahme läuft, bis sie beendet "
                    + "oder das Gespräch aufgelegt wird.",
                PrimaryButtonText = "Aufzeichnen",
                CloseButtonText = "Abbrechen",

                // „Abbrechen" auf der Eingabetaste, wie bei allem mit Folgen
                // (ADR-044).
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                // Der Umschalter hat sich beim Klick selbst eingeschaltet.
                RecordToggle.IsChecked = false;
                return;
            }
        }

        await ViewModel.ToggleRecordingCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Für welche Gespräche die Aufnahmefrage schon beantwortet wurde (C3).
    ///
    /// <para>Je Gespräch und nicht je Sitzung: das nächste Gespräch hat eine
    /// andere Gegenseite, und die muss es wieder wissen.</para>
    /// </summary>
    private readonly HashSet<CallHandle> _aufnahmeGefragt = [];

    private async void OnDtmfKeyPressed(object? sender, string key) =>
        await GuardAsync(nameof(OnDtmfKeyPressed),
            () => ViewModel.SendDtmfCommand.ExecuteAsync(key));

    /// <summary>
    /// Ziffern von der Tastatur als Tastenton (W2.5, Befund C6).
    ///
    /// <para><b>Der Befund.</b> DTMF ging nur mit der Maus: Aufklappen, dann
    /// ein Klick je Ziffer. «Drücken Sie die 1, dann Ihre sechsstellige
    /// Kundennummer» ist damit acht Klicks und ein Aufklappen — an einem
    /// Telefon, das eine Tastatur vor sich hat.</para>
    ///
    /// <para><b>Nur, wenn kein Textfeld den Fokus hat.</b> Sonst würde die
    /// Nummer, die jemand ins Weiterleitungsfeld tippt, gleichzeitig an die
    /// Gegenseite gehen — und die hörte eine Zifferfolge, die niemand für sie
    /// gemeint hat.</para>
    ///
    /// <para><c>CharacterReceived</c> und nicht <c>KeyDown</c>: der Nummernblock
    /// und die Zifferreihe liefern verschiedene <c>VirtualKey</c>-Werte, und
    /// <c>*</c> und <c>#</c> hängen zusätzlich am Tastaturlayout. Das Zeichen
    /// ist die Antwort auf beides.</para>
    /// </summary>
    private async void OnPageCharacterReceived(
        UIElement sender,
        CharacterReceivedRoutedEventArgs args)
    {
        if (!ViewModel.HasCall || ViewModel.SelectedCall?.Status != CallStatus.Connected)
        {
            return;
        }

        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox)
        {
            return;
        }

        var zeichen = args.Character;

        if (!char.IsAsciiDigit(zeichen) && zeichen is not ('*' or '#'))
        {
            return;
        }

        args.Handled = true;

        await GuardAsync(
            nameof(OnPageCharacterReceived),
            () => ViewModel.SendDtmfCommand.ExecuteAsync(zeichen.ToString()));
    }

    /// <summary>
    /// Zurück zur Wähltastatur, ohne das Gespräch zu beenden (§8.2).
    /// Der Weg zurück führt über die Gesprächsleiste der Hauptansicht.
    /// </summary>
    /// <summary>
    /// „Karte bearbeiten …" aus dem Kontextmenü der Anruferkarte (C14).
    ///
    /// <para>Der Weg dorthin war vier Ebenen tief — und die Karte ist das, was
    /// der Benutzer bei jedem Anruf sieht. Wem hier auffällt, dass eine Zeile
    /// fehlt, soll sie ändern können, ohne sich das bis zum Ende des Gesprächs
    /// zu merken.</para>
    ///
    /// <para>Der Designer läuft auf demselben Thread, der alle 20 ms
    /// <c>Core.Iterate()</c> bedient — das ist seit K4 so und wird hier nicht
    /// anders (T99, T159).</para>
    /// </summary>
    private void OnEditCallerCardClick(object sender, RoutedEventArgs e) =>
        // Die Art, die hier gerade zu sehen ist: die ausführliche Karte des
        // laufenden Gesprächs. Nicht IncomingCompact — die gehört zum Toast.
        Nipp.App.Windows.CardDesignerWindow.Show(CardKind.ActiveExpanded);

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        Frame.Navigate(typeof(ShellPage));
    }

    /// <summary>
    /// Escape und Alt+Links gehen zurück (C9).
    ///
    /// <para><b>Escape beendet kein Gespräch</b> — es tut, was der Pfeil oben
    /// links tut, und dessen Kurzinfo sagt es wörtlich: «zurück zur
    /// Wähltastatur, das Gespräch läuft weiter».</para>
    ///
    /// <para><b>Ein offener Bereich zuerst.</b> Steht das Weiterleiten-Feld
    /// oder die Zehnertastatur offen, schliesst Escape erst die — das ist die
    /// innerste Ebene, und so erwartet es jeder Windows-Benutzer. Alt+Links
    /// meint dagegen immer die Navigation.</para>
    /// </summary>
    /// <summary>
    /// Strg+M stumm, Strg+H halten, Strg+E auflegen (C7).
    ///
    /// <para><b>Warum es das braucht.</b> Diese Seite hatte kein einziges
    /// Tastenkürzel. Stummschalten ist im Alltag der häufigste Griff, und er
    /// hiess: Fenster suchen, nach vorne holen, hinsehen, klicken — drei
    /// Sekunden für etwas, das eine halbe dauern sollte.</para>
    ///
    /// <para>Ein Behandler für alle drei, wie in der Hauptansicht: drei fast
    /// gleiche Methoden wären drei Gelegenheiten, eine davon
    /// stehenzulassen.</para>
    ///
    /// <para><b>Was hier <em>nicht</em> steht, ist die Bedeutung.</b> Die
    /// Befehle sind dieselben, die auch die Knöpfe ausführen — ein Kürzel, das
    /// seine eigene Fassung von «stumm schalten» mitbrächte, wäre die zweite
    /// Wahrheit, an der dieses Projekt schon am Headset gelitten hat.</para>
    /// </summary>
    private async void OnCallAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        try
        {
            switch (sender.Key)
            {
                case global::Windows.System.VirtualKey.M:
                    await ViewModel.ToggleMuteCommand.ExecuteAsync(null);
                    break;

                case global::Windows.System.VirtualKey.H:
                    await ViewModel.ToggleHoldCommand.ExecuteAsync(null);
                    break;

                case global::Windows.System.VirtualKey.E:
                    if (ViewModel.HangUpCommand.CanExecute(null))
                    {
                        await ViewModel.HangUpCommand.ExecuteAsync(null);
                    }

                    break;

                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            // async void: eine Ausnahme von hier beendete die Anwendung, und
            // ein Tastendruck darf ein Telefon nicht mitnehmen. Seit ADR-053
            // faengt diese Stelle jede Ausnahme, nicht nur die erwartete —
            // was hier durchkommt, ist per Definition das Unerwartete.
            ViewModel.LastError = ex is InvalidOperationException
                ? ex.Message
                : UserMessage.WithCause("Die Aktion liess sich nicht ausführen.", ex);

            AppLog.HandlerFailed(_logger, nameof(OnCallAccelerator), ex.GetType().Name);
        }
    }

    private void OnBackAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // global:: ist Pflicht: der eigene Namensraum Nipp.App.Windows verdeckt
        // Windows.* innerhalb von Nipp.App, und der Compiler meldet dann einen
        // fehlenden Assemblyverweis.
        if (sender.Key == global::Windows.System.VirtualKey.Escape
            && (TransferCard.Visibility == Visibility.Visible
                || DtmfCard.Visibility == Visibility.Visible))
        {
            CloseActionPanes();
            return;
        }

        OnBackClick(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// Jemand hat auf der Anruferkarte etwas betätigt (§21).
    ///
    /// <b>Hier wird nicht mehr geprüft, ob eine Adresse zulässig ist</b> — das
    /// hat die <c>CardLayoutEngine</c> getan, und nur was durchkam, ist
    /// überhaupt als Aktion angekommen. Diese Stelle führt aus.
    /// </summary>
    private async void OnCardAction(object? sender, CardResolvedAction action)
    {
        try
        {
            switch (action)
            {
                case CardOpenUrl open:
                    await global::Windows.System.Launcher.LaunchUriAsync(open.Target);
                    break;

                case CardDial dial:
                    Dial(dial.Number);
                    break;

                case CardCopy copy:
                    CopyToClipboard(copy.Value);
                    break;

                default:
                    break;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Eine Karte darf ein Gespräch nicht mitnehmen — und dieser
            // Handler ist ein async void, aus dem eine Ausnahme die
            // Anwendung beenden würde.
            ViewModel.LastError = $"Die Aktion liess sich nicht ausführen: {ex.Message}";
        }
    }

    /// <summary>
    /// Wählt aus der Karte heraus — über die Hauptansicht, damit dieselben
    /// Regeln gelten wie überall: Normalisierung (§8.1) und die Grenze von
    /// zwei Gesprächen (§8.2).
    /// </summary>
    private static void Dial(string number)
    {
        var shell = ((App)Application.Current).Services.GetRequiredService<ShellViewModel>();

        shell.DialedNumber = number;

        if (shell.DialCommand.CanExecute(null))
        {
            shell.DialCommand.Execute(null);
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);

            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            // Die Zwischenablage gehoert dem ganzen System und ist gelegentlich
            // von einem anderen Programm belegt; Windows quittiert das mit
            // einer COMException. Dieselbe Behandlung wie in der Hauptansicht.
            ViewModel.LastError = $"Kopieren nicht möglich: {ex.Message}";
        }
    }

    /// <summary>
    /// Enter im Zielfeld <b>übernimmt</b> — es gibt das Gespräch nicht ab
    /// (ADR-044).
    ///
    /// <para><b>Was hier stand.</b> Enter führte unmittelbar die blinde
    /// Weiterleitung aus. Der Tooltip derselben Schaltfläche sagt: „sofort
    /// abgeben, ohne Rückfrage. Das Gespräch ist danach weg." Damit lag die
    /// einzige unwiderrufliche Handlung des Programms auf der Taste, die man
    /// beim Tippen einer Nummer zuletzt drückt — und ein Anrufer landete bei
    /// jemandem, der ihn nicht erwartet, während der Übergebende aus der
    /// Leitung war.</para>
    ///
    /// <para><b>Das Muster gibt es schon.</b> Die Vorschlagsliste unter dem
    /// Nummernfeld übernimmt bei Enter und wählt nicht, mit genau dieser
    /// Begründung: „ein Fehlgriff in einer Liste, die beim Tippen aufspringt,
    /// wäre sonst ein Anruf bei der falschen Person." Hier ist es dieselbe
    /// Lage, nur mit höherem Einsatz.</para>
    ///
    /// <para>Enter übernimmt also den obersten Vorschlag und setzt den Fokus
    /// auf „Sofort abgeben". Das zweite Enter führt aus — auf einer
    /// Schaltfläche, die sichtbar den Fokus trägt und beschriftet ist mit dem,
    /// was sie tut.</para>
    /// </summary>
    private void OnTransferTargetKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // global:: und voll qualifiziert, gleich zwei Namensfallen auf einmal:
        // ein using auf Windows.System macht DispatcherQueueTimer mehrdeutig
        // (den Typ gibt es dort und in Microsoft.UI.Dispatching), und ein
        // blankes "Windows" loest innerhalb von Nipp.App auf den eigenen
        // Namespace Nipp.App.Windows auf.
        if (e.Key != global::Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;

        // Steht die Liste offen und ist noch nichts gewaehlt, uebernimmt Enter
        // den obersten Vorschlag — sonst bleibt stehen, was getippt wurde.
        if (TransferSuggestionList.Visibility == Visibility.Visible
            && ViewModel.TransferSuggestions.FirstOrDefault() is { } vorschlag
            && string.IsNullOrWhiteSpace(ViewModel.TransferTarget))
        {
            ViewModel.PickTransferTarget(vorschlag);
        }

        BlindButton.Focus(FocusState.Keyboard);
    }

    /// <summary>
    /// Meldet die Auswahl ans ViewModel. Ein leeres AddedItems bedeutet, dass
    /// die Liste ihre Auswahl beim Austausch einer Instanz verloren hat — das
    /// ist keine Benutzeraktion und wird nicht weitergereicht.
    /// </summary>
    private void OnCallSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is CallRow row)
        {
            ViewModel.SelectRow(row);
        }
    }

    private void Refresh()
    {
        var call = ViewModel.SelectedCall;
        var visible = call is not null;

        // Die Liste der Auswahl nachführen, ohne dass ihr SelectionChanged
        // etwas zurückschreibt — deshalb kein TwoWay-Binding. Die Zeile bleibt
        // über Zustandswechsel hinweg dieselbe Instanz (ADR-043), der
        // Vergleich stimmt also auch nach „Halten".
        if (ViewModel.SelectedRow is { } row && !ReferenceEquals(CallList.SelectedItem, row))
        {
            CallList.SelectedItem = row;
        }

        EmptyState.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;

        // §15: gescheitertes Weiterleiten oder eine Aufnahme, die nicht
        // startet, war vorher nur im ViewModel zu sehen.
        if (ViewModel.LastError is { Length: > 0 } error)
        {
            CallErrorBar.Message = error;
            CallErrorBar.IsOpen = true;
        }
        else
        {
            CallErrorBar.IsOpen = false;
        }

        CallHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ActionPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // Auflegen sitzt seit dem 06.09.2026 oben in der Kopfleiste, damit es
        // beim Scrollen nicht verschwindet. Ohne Gespräch hat es dort nichts
        // zu suchen — sonst stünde ein toter Knopf über der Wähltastatur.
        HangUpButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BottomGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SwapCard.Visibility = ViewModel.CanSwap ? Visibility.Visible : Visibility.Collapsed;

        // Weiterleiten und Tastentöne erst anbieten, wenn das Gespräch steht —
        // vorher gibt es nichts zu übergeben und niemanden, der Töne hört.
        var canAct = call is { Status: CallStatus.Connected or CallStatus.OnHold };

        TransferToggle.IsEnabled = canAct;
        DtmfToggle.IsEnabled = canAct;

        if (!canAct)
        {
            // Ein Gespräch, das endet, während einer der Bereiche offen steht,
            // liesse ihn sonst leer stehen.
            CloseActionPanes();
        }

        AttendedButton.IsEnabled = ViewModel.CanTransferAttended;
        UpdateTransferSuggestions();

        // Den Hinweis erst zeigen, wenn jemand wirklich dabei ist
        // weiterzuleiten: der Bereich ist offen UND ein Ziel steht drin.
        //
        // Vorher stand er, sobald der Bereich sichtbar war — also praktisch
        // immer, denn ein zweites Gespräch ist die Ausnahme. Vier Zeilen für
        // eine Erklärung, die man einmal liest, und sie schoben die
        // Anruferkarte aus dem Bild.
        AttendedHint.IsOpen = !ViewModel.CanTransferAttended
            && TransferCard.Visibility == Visibility.Visible
            && !string.IsNullOrWhiteSpace(ViewModel.TransferTarget);

        if (call is null)
        {
            RecordingBar.IsOpen = false;
            return;
        }

        // §21.1: der Name, wenn einer bekannt ist, sonst die formatierte
        // Nummer. Die Regel dahinter steht in CallPartyResolver (ADR-043) —
        // hier steht eine Zuweisung, denn Nipp.App hat kein Testprojekt.
        PartyText.Text = CallerCard?.Headline ?? string.Empty;

        StateText.Text = CallerCard?.Subtitle is { Length: > 0 } firma
            ? $"{firma} · {Describe(call.Status)}"
            : Describe(call.Status);

        // Die konfigurierbare Karte nachfuehren; sie blendet sich selbst aus,
        // wenn nichts sichtbar ist.
        if (CallerCard is not null)
        {
            CallerCardView.Model = CallerCard.Layout;
        }

        // Die Karte nur zeigen, wenn sie etwas zu sagen hat.
        CallerCardPanel.Visibility = CallerCard?.HasContext == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        CodecText.Text = call.Codec is { Length: > 0 } codec ? codec : "Codec wird verhandelt";

        // §8.2 und §8.4: die Verschlüsselung steht als Text da, nicht nur als
        // Farbe. "None" ist gegen die aktuelle Anlage der Regelfall (ADR-007)
        // und wird deshalb ausgeschrieben, nicht beschönigt.
        EncryptionText.Text = call.Encryption switch
        {
            MediaEncryptionMode.None => "unverschlüsselt",
            MediaEncryptionMode.Unknown => "Verschlüsselung unbekannt",
            var mode => $"verschlüsselt ({mode})",
        };
        // Der Ton faerbt die SCHRIFT, nicht die Flaeche (ADR-044). Als
        // Hintergrund erbte der Text seine Farbe vom Thema und stand im
        // dunklen Erscheinungsbild fast weiss auf Gelb — rund 1,4:1. Genau
        // die Angabe, die Paragraph 8.2 als Pflichtanzeige fuehrt, war damit
        // unlesbar. Der Auflegen-Knopf zwei Dutzend Zeilen darueber macht es
        // richtig: wer eine Tonflaeche setzt, setzt den Vordergrund mit.
        var ton = Brush(call.Encryption is MediaEncryptionMode.None or MediaEncryptionMode.Unknown
            ? "EncryptionInsecureBrush"
            : "EncryptionSecureBrush");

        EncryptionText.Foreground = ton;
        EncryptionChip.BorderBrush = ton;
        EncryptionChip.BorderThickness = new Thickness(1);

        MuteToggle.IsChecked = call.IsMuted;
        HoldToggle.IsChecked = call.Status == CallStatus.OnHold;
        RecordToggle.IsChecked = call.IsRecording;
        AcceptButton.Visibility = call.Status == CallStatus.Incoming ? Visibility.Visible : Visibility.Collapsed;

        // §8.2: der Aufnahmeindikator ist Pflicht.
        RecordingBar.IsOpen = call.IsRecording;

        // Solange sie laeuft, traegt der Knopf den Aufnahmeton — in Schrift,
        // Symbol und Rand, nie in der Flaeche (ADR-044). Als Flaeche erbte der
        // Text seine Farbe vom Thema und stuende im Dunkeln bei rund 1,4:1.
        var aufnahmeton = Brush(call.IsRecording
            ? "RecordingIndicatorBrush"
            : "TextFillColorPrimaryBrush");

        RecordIcon.Foreground = aufnahmeton;
        RecordLabel.Foreground = aufnahmeton;
        RecordToggle.BorderBrush = aufnahmeton;

        UpdateDuration();
        UpdateQuality();
    }

    private void UpdateDuration()
    {
        var duration = ViewModel.SelectedCall?.Duration;
        // W1.6 (A3): dieselbe Form wie in Anrufliste und Karte. Hier stand
        // mm:ss — dieselbe Dauer sah im Gespraech anders aus als hinterher.
        var text = RelativeTime.Duration(duration);

        DurationText.Text = text.Length > 0 ? text : "--:--";
    }

    private void UpdateQuality()
    {
        var quality = ViewModel.SelectedQuality;

        if (quality is null)
        {
            RatingText.Text = string.Empty;
            RttText.Text = "Round-Trip: —";
            JitterText.Text = "Jitter-Puffer: —";
            LossText.Text = "Paketverlust: —";
            BandwidthText.Text = "Bandbreite: —";
            MosText.Text = "Sprachqualität: —";
            return;
        }

        RatingText.Text = quality.Rating switch
        {
            CallQualityRating.Good => "gut",
            CallQualityRating.Fair => "brauchbar",
            _ => "schlecht",
        };
        RatingText.Foreground = Brush(quality.Rating switch
        {
            CallQualityRating.Good => "StatusRegisteredBrush",
            CallQualityRating.Fair => "StatusProgressBrush",
            _ => "StatusFailedBrush",
        });

        RttText.Text = $"Round-Trip: {quality.RoundTripSeconds * 1000:F0} ms";

        // „Jitter-Puffer" und nicht „Jitter": angezeigt wird die Groesse des
        // Puffers, nicht die Schwankung der Laufzeit.
        JitterText.Text = $"Jitter-Puffer: {quality.JitterBufferMilliseconds:F0} ms";
        LossText.Text = $"Paketverlust: {quality.ReceiverLossPercent:F1} %";
        BandwidthText.Text = $"Bandbreite: {quality.DownloadKbitPerSecond:F0} kbit/s";

        // §8.2: geschaetzter MOS. 0 heisst „noch kein Wert" — das SDK
        // braucht einige Sekunden.
        MosText.Text = quality.Mos > 0
            ? string.Create(CultureInfo.CurrentCulture, $"Sprachqualität: {quality.Mos:F1} von 5")
            : "Sprachqualität: wird gemessen …";
    }

    /// <summary>
    /// Zeigt den Weiterleiten-Bereich und schliesst dabei die Tastentöne.
    ///
    /// <para><b>Immer höchstens einer offen.</b> Bei 400 Pixeln Breite ist die
    /// Höhe die knappe Grösse: ein offenes Übergabefeld und eine offene
    /// Zehnertastatur zusammen sind über 300 Pixel, und danach ist von der
    /// Anruferkarte darunter nichts mehr zu sehen. Wer beides gleichzeitig
    /// braucht, gibt es nicht — man verbindet weiter <b>oder</b> man drückt
    /// eine Ziffer.</para>
    /// </summary>
    private void OnTransferToggleClick(object sender, RoutedEventArgs e)
    {
        var open = TransferToggle.IsChecked == true;

        DtmfToggle.IsChecked = false;
        DtmfCard.Visibility = Visibility.Collapsed;

        TransferCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            // Beim Öffnen die Vorschläge bauen: bei leerem Feld sind das die
            // Team-Nebenstellen, und die sind der häufige Fall (§22.1).
            ViewModel.RefreshTransferSuggestions();
            UpdateTransferSuggestions();

            // Der Zielbereich ist der Grund, warum jemand hier geklickt hat.
            TransferTargetBox.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Ein Vorschlag wurde angetippt — die Nummer wandert ins Feld.</summary>
    private void OnTransferSuggestionTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContactRow row })
        {
            ViewModel.PickTransferTarget(row);
            TransferTargetBox.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// Blendet die Vorschlagsliste ein, solange es welche gibt.
    ///
    /// <para>Eine leere Liste bekommt keinen Platz: bei 400 Pixeln zählt jede
    /// Zeile, und ein leerer Kasten unter dem Feld sähe aus, als suche nipp
    /// noch.</para>
    /// </summary>
    private void UpdateTransferSuggestions() =>
        TransferSuggestionList.Visibility = ViewModel.HasTransferSuggestions
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnDtmfToggleClick(object sender, RoutedEventArgs e)
    {
        var open = DtmfToggle.IsChecked == true;

        TransferToggle.IsChecked = false;
        TransferCard.Visibility = Visibility.Collapsed;

        DtmfCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Schliesst beide aufklappbaren Bereiche und ihre Umschalter.</summary>
    private void CloseActionPanes()
    {
        TransferToggle.IsChecked = false;
        DtmfToggle.IsChecked = false;
        TransferCard.Visibility = Visibility.Collapsed;
        DtmfCard.Visibility = Visibility.Collapsed;
    }

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key) =>
        Nipp.App.Converters.ThemeBrushes.Get(key);

    private static string Describe(CallStatus status) => CallStateCatalog.Of(status);
}
