using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Nipp.App.Controls;
using Nipp.App.Diagnostics;
using Nipp.App.Windows;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.App.Views.Settings;

/// <summary>
/// Einstellungen (§9, §20). §6: kein Zugriff auf das SDK.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly PolicyService _policy;

    public SettingsViewModel ViewModel { get; }

    /// <summary>Die Verwaltung der Integrationen (§21.4).</summary>
    public IntegrationSettingsViewModel IntegrationViewModel { get; }

    public SettingsPage()
    {
        var services = ((App)Application.Current).Services;

        ViewModel = services.GetRequiredService<SettingsViewModel>();
        IntegrationViewModel = services.GetRequiredService<IntegrationSettingsViewModel>();
        _policy = services.GetRequiredService<PolicyService>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        IntegrationViewModel.PropertyChanged += OnIntegrationPropertyChanged;
        _policy.Changed += OnPolicyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Freitext- und Zahlenfelder wirken beim Verlassen (ADR-045).
        //
        // Ueber FocusManager und nicht ueber ein LostFocus je Feld: die Seite
        // hat 25 Textfelder und 4 Zahlenfelder, und beim naechsten waere genau
        // eines vergessen. AddHandler auf UIElement.LostFocusEvent gaebe es
        // nicht — WinUI legt fuer LostFocus kein statisches RoutedEvent an.
        //
        // LostFocus und nicht LosingFocus (Befund A1-7, 17.09.2026): das
        // zweite laeuft VOR der Uebertragung, und ApplyEdits las dann den
        // alten Stand. Siehe OnLostFocus.
        FocusManager.LostFocus += OnLostFocus;

        // §20.4: das Symbol folgt dem Erscheinungsbild, wie in der Titelleiste
        // und im Infobereich.
        ActualThemeChanged += OnActualThemeChanged;
        ApplyAboutLogo();

        ViewModel.Load();
        SelectComboBoxes();

        ApplyPolicy();
        Refresh();
        RefreshIntegrations();
    }

    private void OnIntegrationPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshIntegrations();

    /// <summary>
    /// AP8.4: gesperrte Gruppen ausgrauen.
    ///
    /// Gesperrt wird auf Gruppenebene, nicht Feld für Feld. Der Grund ist
    /// praktischer Natur: ein Profil, das jedes einzelne Feld aufzählen müsste,
    /// wäre bei jeder neuen Einstellung unvollständig — und niemand merkt es,
    /// bis jemand das neue Feld verstellt. Die Pfade sind dieselben wie im
    /// Profil, ein Oberpfad sperrt alles darunter.
    ///
    /// <b>Bedienschutz, keine Sicherheitsgrenze</b> — siehe
    /// <see cref="PolicyService"/>. Ein ausgegrautes Bedienelement hält
    /// niemanden davon ab, settings.json mit einem Texteditor zu öffnen.
    /// </summary>
    private void ApplyPolicy()
    {
        Lock(AccountsGroup, "accounts");
        Lock(AppearanceGroup, "advanced.theme");
        Lock(AudioGroup, "audio");
        Lock(NetworkGroup, "nat");
        Lock(CodecsGroup, "codecs");
        Lock(ContactsGroup, "contacts");
        Lock(IntegrationsGroup, "integrations");

        // Die Karte steht seit C14 als eigene Gruppe oben, gehört aber
        // weiterhin zur Integrationskonfiguration — ein Profil, das
        // «integrations» sperrt, meint sie mit.
        Lock(CardsGroup, "integrations");

        // Die Gruppe „Erweitert" ist seit ADR-046 aufgeloest: was der Benutzer
        // angeht, steht unter „Start und Bedienung", der Rest unter „Fuer
        // Administratoren". Beide fallen unter denselben Profilpfad.
        Lock(UsageGroup, "advanced");
        Lock(DiagnosticsGroup, "advanced");

        // AP8.4: zusätzlich jede SettingCard einzeln fragen. Sie kennt ihren
        // eigenen Pfad und zeigt ein Schloss, wenn er gesperrt ist — feiner
        // als die Gruppensperre darüber und der eigentlich gemeinte Zustand
        // aus §17.
        foreach (var card in FindSettingCards(this))
        {
            card.ApplyPolicy(_policy);
        }

        PolicyBar.Message = _policy.HasAnyLock
            ? "Einige Einstellungen sind von der Administration festgelegt und lassen sich hier nicht ändern."
            : string.Empty;

        PolicyBar.IsOpen = _policy.HasAnyLock;

        void Lock(Expander group, string path)
        {
            var locked = _policy.IsLocked(path);

            group.IsEnabled = !locked;
            ToolTipService.SetToolTip(group, locked ? PolicyService.LockedHint : null);
        }
    }

    private void OnPolicyChanged(object? sender, EventArgs e) => ApplyPolicy();

    /// <summary>
    /// Sucht alle <see cref="SettingCard"/> im Baum.
    ///
    /// Über den visuellen Baum und nicht über eine Liste im Code-Behind: eine
    /// Liste müsste bei jedem neuen Feld nachgeführt werden, und genau das
    /// vergisst man. Der Baum weiss es von selbst.
    ///
    /// <b>Grenze:</b> der Inhalt eines noch nie aufgeklappten
    /// <see cref="Expander"/> ist unter Umständen noch nicht erzeugt. Deshalb
    /// wird zusätzlich beim Aufklappen erneut gefragt.
    /// </summary>
    private static IEnumerable<SettingCard> FindSettingCards(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is SettingCard card)
            {
                yield return card;
            }

            foreach (var nested in FindSettingCards(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Beim Aufklappen die Karten der Gruppe nach ihrer Sperre fragen.
    ///
    /// <para><b>Gewartet wird auf das <c>Loaded</c> des Inhalts und nicht auf
    /// einen Dispatcher-Durchlauf</b> (Befund A1-6, 17.09.2026). Vorher stand
    /// hier ein <c>TryEnqueue</c> mit dem Kommentar «der Inhalt steht erst
    /// jetzt im Baum» — <b>er stand es nicht</b>: beim ersten Aufklappen fand
    /// <see cref="FindSettingCards"/> nichts, und das Schloss erschien erst,
    /// wenn man die Gruppe zu- und wieder aufklappte. Die Sperre selbst wirkte
    /// von Anfang an; es ging allein um die Anzeige.</para>
    ///
    /// <para><c>Loaded</c> feuert genau dann, wenn der Inhalt wirklich im Baum
    /// steht — beim zweiten Mal ist er es schon, dann greift der erste
    /// Zweig.</para>
    /// </summary>
    private void OnGroupExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (sender.Content is not FrameworkElement inhalt)
        {
            return;
        }

        if (inhalt.IsLoaded)
        {
            Anwenden();
            return;
        }

        inhalt.Loaded += BeimLaden;

        void BeimLaden(object s, RoutedEventArgs e)
        {
            inhalt.Loaded -= BeimLaden;
            Anwenden();
        }

        void Anwenden()
        {
            foreach (var card in FindSettingCards(sender))
            {
                card.ApplyPolicy(_policy);
            }
        }
    }

    private void OnRemoveTeamMemberClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TeamExtension member })
        {
            ViewModel.RemoveTeamMemberCommand.Execute(member);
        }
    }

    private void OnEditTeamMemberClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TeamExtension member })
        {
            ViewModel.EditTeamMemberCommand.Execute(member);
        }
    }

    /// <summary>
    /// Entfernt eine Gruppe — <b>mit Rückfrage, die sagt, wohin die Einträge
    /// wandern</b>.
    ///
    /// Es geht kein Kontakt verloren, und genau das muss dastehen: „Gruppe
    /// entfernen?" allein liest sich, als nähme man auch die Leute mit.
    /// </summary>

    /// <summary>
    /// Der Wächter für alles, was aus <c>async void</c> heraus läuft
    /// (ADR-053).
    ///
    /// <para>Ein Ereignisbehandler ist <c>async void</c>; das ist keine Wahl,
    /// sondern das, was WinUI verlangt. Eine Ausnahme daraus hat keinen
    /// Aufrufer mehr, der sie fängt — sie geht direkt an
    /// <c>App.OnUnhandledException</c>, und das protokolliert nur. Hier fängt
    /// deshalb <b>jede</b> Ausnahme: was diese Stelle erreicht, ist per
    /// Definition das Unerwartete.</para>
    /// </summary>
    private async Task GuardAsync(string handler, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLog.HandlerFailed(((App)Application.Current).Services.GetRequiredService<ILogger<SettingsPage>>(), handler, ex.GetType().Name);
            ViewModel.Error = UserMessage.WithCause("Die Aktion liess sich nicht ausführen.", ex);
        }
    }

    private async void OnRemoveGroupClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnRemoveGroupClick), () => OnRemoveGroupClickAsync(sender, e));

    private async Task OnRemoveGroupClickAsync(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is not { Length: > 0 } gruppe)
        {
            return;
        }

        if (ViewModel.Groups.Count <= 1)
        {
            // Die letzte Gruppe bleibt: ohne sie gäbe es keine Standardgruppe
            // mehr, und jeder Eintrag wäre heimatlos.
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Gruppe «{gruppe}» entfernen?",
            Content = $"Die Einträge wechseln in die Gruppe «{ViewModel.DefaultGroupName}». "
                + "Es geht keine Nebenstelle verloren.",
            PrimaryButtonText = "Entfernen",
            CloseButtonText = "Abbrechen",

            // Bei allem Destruktiven steht „Abbrechen" auf der Eingabetaste
            // (ADR-044). Ohne Angabe ist es ContentDialogButton.None, und
            // dann tut Enter gar nichts — in vier von neun Dialogen war das
            // so, waehrend die anderen fuenf reagierten.
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveGroupCommand.Execute(null);
        }
    }

    /// <summary>
    /// Übernimmt ein Konto ins Formular. Ohne Rückfrage — es wird nichts
    /// verändert, nur angezeigt; wer sich verklickt, drückt „Abbrechen".
    /// </summary>
    private void OnEditAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountStatus account })
        {
            ViewModel.EditAccountCommand.Execute(account);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        IntegrationViewModel.PropertyChanged -= OnIntegrationPropertyChanged;
        _policy.Changed -= OnPolicyChanged;
        ActualThemeChanged -= OnActualThemeChanged;

        // FocusManager.LostFocus ist ein statisches, anwendungsweites
        // Ereignis — ohne dieses Loesen liefe der Behandler weiter, auch wenn
        // die Seite laengst nicht mehr zu sehen ist.
        FocusManager.LostFocus -= OnLostFocus;

        Unloaded -= OnUnloaded;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        ZeigeProvisionierungsproblem();

        CapacityText.Text = ViewModel.AccountCapacity;
        HotkeyStatusText.Text = ViewModel.HotkeyStatus;
        MuteHotkeyStatusText.Text = ViewModel.MuteHotkeyStatus;

        // Die Überschrift sagt, was das Formular gerade ist. Ohne sie sieht ein
        // gefülltes Formular unter „Konto hinzufügen" so aus, als lege man ein
        // zweites Konto mit denselben Angaben an.
        AccountFormTitle.Text = ViewModel.IsEditingAccount
            ? "Konto bearbeiten"
            : "Konto hinzufügen";

        CalibrationText.Text = ViewModel.EchoCalibrationMs is { } ms
            ? $"zuletzt gemessen: {ms} ms"
            : "noch nicht kalibriert";

        // Die letzte Gruppe bleibt — und der Knopf sagt es, statt sich
        // drücken zu lassen und nichts zu tun (C10).
        var letzteGruppe = ViewModel.Groups.Count <= 1;

        RemoveGroupButton.IsEnabled = !letzteGruppe;

        ToolTipService.SetToolTip(
            RemoveGroupButton,
            letzteGruppe
                ? "Die letzte Gruppe bleibt — sie nimmt die Nebenstellen ohne eigene Gruppe auf."
                : null);

        AutomationProperties.SetHelpText(
            RemoveGroupButton,
            letzteGruppe
                ? "Die letzte Gruppe bleibt — sie nimmt die Nebenstellen ohne eigene Gruppe auf."
                : string.Empty);

        // Der Grund steht nur da, wenn es einen gibt — und nicht beim leeren
        // Formular, solange niemand etwas getan hat (ADR-045).
        AccountIssueText.Visibility =
            ViewModel.AccountFormIssue is { Length: > 0 }
            && (ViewModel.NewUsername.Length > 0 || ViewModel.NewDomain.Length > 0)
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (ViewModel.Error is { Length: > 0 } error)
        {
            FeedbackBar.Severity = InfoBarSeverity.Error;
            FeedbackBar.Message = error;
            FeedbackBar.IsOpen = true;
        }
        else if (ViewModel.RestartHint is { Length: > 0 } restart)
        {
            FeedbackBar.Severity = InfoBarSeverity.Warning;
            FeedbackBar.Message = restart;
            FeedbackBar.IsOpen = true;
        }
        else if (ViewModel.Message is { Length: > 0 } message)
        {
            FeedbackBar.Severity = InfoBarSeverity.Success;
            FeedbackBar.Message = message;
            FeedbackBar.IsOpen = true;
        }
        else
        {
            FeedbackBar.IsOpen = false;
        }
    }

    /// <summary>
    /// Bringt die Auswahlfelder auf den Stand des ViewModels.
    ///
    /// Sie hängen an <c>SelectionChanged</c> statt an einer Bindung — nach dem
    /// Einlesen einer Datei oder einem Zurücksetzen müssen sie deshalb von Hand
    /// nachgezogen werden, sonst zeigen sie weiter die alte Wahl.
    /// </summary>
    private void SelectComboBoxes()
    {
        SelectByTag(ThemeBox, ViewModel.Theme.ToString());
        SelectByTag(DtmfBox, ViewModel.Dtmf.ToString());
        SelectByTag(LoggingBox, ViewModel.Logging.ToString());
        SelectByTag(TransportBox, ViewModel.NewTransport.ToString());
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private static string? TagOf(object? selectedItem) =>
        (selectedItem as ComboBoxItem)?.Tag as string;

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagOf(ThemeBox.SelectedItem) is { } tag
            && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            ViewModel.Theme = theme;
        }
    }

    private void OnDtmfChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagOf(DtmfBox.SelectedItem) is { } tag
            && Enum.TryParse<DtmfMode>(tag, out var mode))
        {
            ViewModel.Dtmf = mode;
        }
    }

    private void OnLoggingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagOf(LoggingBox.SelectedItem) is { } tag
            && Enum.TryParse<LogVerbosity>(tag, out var level))
        {
            ViewModel.Logging = level;
        }
    }

    private void OnTransportChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagOf(TransportBox.SelectedItem) is { } tag
            && Enum.TryParse<SipTransport>(tag, out var transport))
        {
            ViewModel.NewTransport = transport;
        }
    }

    /// <summary>
    /// Entfernt ein Konto — nach Rückfrage.
    ///
    /// Die Seite arbeitet sonst auf einer Kopie und wirkt erst beim Speichern;
    /// das Entfernen war die Ausnahme und sofort endgültig, samt Passwort.
    /// Ein Fehlgriff auf das Papierkorbsymbol kostete damit die Zugangsdaten.
    /// </summary>
    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnRemoveAccountClick), () => OnRemoveAccountClickAsync(sender, e));

    private async Task OnRemoveAccountClickAsync(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountStatus account })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Konto entfernen?",
            Content = $"{account.ShortLabel} wird abgemeldet und aus nipp entfernt, "
                + "zusammen mit dem gespeicherten Passwort. Das lässt sich nicht "
                + "rückgängig machen.",
            PrimaryButtonText = "Entfernen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.RemoveAccountCommand.ExecuteAsync(account);
    }

    /// <summary>
    /// §9.6: „Konfiguration jetzt abrufen". Der Dienst wirft nicht; was
    /// schiefging, steht in <c>LastError</c> (§17).
    /// </summary>
    /// <summary>
    /// Was beim letzten Profilabruf schiefging (W1.3, Befund E9).
    ///
    /// <para>§11 verlangt für den fehlgeschlagenen Abruf beim Start einen
    /// Hinweis im UI. Bis zum 13.09.2026 stand er nur im Protokoll: nipp
    /// arbeitete mit den zuletzt gespeicherten Einstellungen weiter — richtig
    /// —, sagte es aber niemandem. Wer sich wunderte, warum ein geändertes
    /// Profil nicht ankommt, fand nirgends eine Antwort.</para>
    /// </summary>
    private void ZeigeProvisionierungsproblem()
    {
        try
        {
            var provisioning = ((App)Application.Current).Services
                .GetRequiredService<ProvisioningService>();

            if (provisioning.LastError is { Length: > 0 } fehler)
            {
                ProvisioningProblemBar.Message =
                    "Das Profil war beim letzten Versuch nicht erreichbar. nipp arbeitet mit "
                        + "den zuletzt gespeicherten Einstellungen weiter."
                        + Environment.NewLine + Environment.NewLine
                        + "Technische Ursache: " + fehler;

                ProvisioningProblemBar.IsOpen = true;
                return;
            }

            ProvisioningProblemBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            AppLog.HandlerFailed(
                ((App)Application.Current).Services.GetRequiredService<ILogger<SettingsPage>>(),
                nameof(ZeigeProvisionierungsproblem),
                ex.GetType().Name);
        }
    }

    private async void OnFetchProvisioningClick(object sender, RoutedEventArgs e)
    {
        var services = ((App)Application.Current).Services;
        var provisioning = services.GetRequiredService<ProvisioningService>();

        // Erst uebernehmen: eine gerade eingetippte Adresse soll auch die sein,
        // von der abgerufen wird. Sie steht in einem Freitextfeld und wirkt
        // deshalb erst beim Verlassen (ADR-045) — der Klick auf diesen Knopf
        // ist nicht zwingend einer.
        ViewModel.ApplyEdits();

        if (ViewModel.Error is { Length: > 0 })
        {
            return;
        }

        ProvisioningRing.IsActive = true;

        try
        {
            // Holen und Anwenden getrennt: das Anwenden speichert, und daran
            // haengt eine Kette bis in den SDK-Core. Sie gehoert auf den
            // UI-Thread (§6), also erst zurueckkommen, dann anwenden.
            var profile = await provisioning.FetchRemoteProfileAsync().ConfigureAwait(true);
            var applied = profile is not null;

            if (applied)
            {
                provisioning.ApplyProfile(profile!);
            }

            if (provisioning.LastError is { Length: > 0 } error)
            {
                FeedbackBar.Severity = InfoBarSeverity.Error;
                FeedbackBar.Message = error;
            }
            else if (applied)
            {
                ViewModel.Load();
                ApplyPolicy();

                FeedbackBar.Severity = InfoBarSeverity.Success;
                FeedbackBar.Message = provisioning.LastProfileName is { Length: > 0 } name
                    ? $"Profil «{name}» übernommen."
                    : "Konfiguration übernommen.";
            }
            else
            {
                FeedbackBar.Severity = InfoBarSeverity.Informational;
                FeedbackBar.Message = "Es gab nichts zu übernehmen. Ohne Provisioning-Adresse "
                    + "und ohne mitgelieferte Konfiguration bleibt alles, wie es ist.";
            }

            FeedbackBar.IsOpen = true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Ein async void-Handler nimmt bei einer Ausnahme die App mit.
            // Der Dienst selbst wirft nicht, aber das Uebernehmen speichert —
            // und ein nicht beschreibbares Verzeichnis ist ein Fall, der
            // vorkommt.
            FeedbackBar.Severity = InfoBarSeverity.Error;
            FeedbackBar.Message = UserMessage.WithCause(
                "Die Konfiguration liess sich nicht übernehmen. Die bisherige gilt weiter; "
                    + "die Datei und ihre Zugriffsrechte prüfen.",
                ex);
            FeedbackBar.IsOpen = true;
        }
        finally
        {
            ProvisioningRing.IsActive = false;
        }
    }

    private void OnCodecUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CodecChoice codec })
        {
            ViewModel.MoveCodecUpCommand.Execute(codec);
        }
    }

    private void OnCodecDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CodecChoice codec })
        {
            ViewModel.MoveCodecDownCommand.Execute(codec);
        }
    }

    /// <summary>§9.6: „Log-Ordner öffnen".</summary>
    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nipp",
            "logs");

        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            FeedbackBar.Severity = InfoBarSeverity.Error;
            FeedbackBar.Message = UserMessage.WithCause(
                "Der Ordner mit den Protokollen liess sich nicht öffnen. Dieselben Dateien "
                    + "stecken im Diagnosepaket — der Knopf dafür steht gleich darunter.",
                ex);
            FeedbackBar.IsOpen = true;
        }
    }

    /// <summary>§9.6: Diagnosepaket. Der Dienst dahinter kommt in P8.</summary>
    private async void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var diagnostics = ((App)Application.Current).Services
            .GetRequiredService<Nipp.Core.Diagnostics.DiagnosticsBundle>();

        try
        {
            var path = await diagnostics.CreateAsync().ConfigureAwait(true);

            FeedbackBar.Severity = InfoBarSeverity.Success;
            FeedbackBar.Message = $"Diagnosepaket erstellt: {path}";
            FeedbackBar.IsOpen = true;

            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            // Auch UnauthorizedAccessException: das Paket wird geschrieben und
            // der Ordner danach geoeffnet, beides kann an Rechten scheitern.
            // In einem async void-Handler waere das sonst ein Absturz.
            FeedbackBar.Severity = InfoBarSeverity.Error;
            FeedbackBar.Message = UserMessage.WithCause(
                "Das Diagnosepaket liess sich nicht erstellen. Ist auf dem Laufwerk noch Platz, "
                    + "und ist der Zielordner beschreibbar?",
                ex);
            FeedbackBar.IsOpen = true;
        }
    }

    /// <summary>Gibt die Einstellungen als Datei aus — ohne Passwörter.</summary>
    /// <summary>
    /// Eine Meldung mit einer Schaltfläche. Dieselbe Form wie die Rückfragen
    /// dieser Seite, nur ohne Wahl.
    /// </summary>
    /// <summary>
    /// Ob gerade ein Dialog dieser Seite offen ist (Befund A1-19).
    ///
    /// <para><b>WinUI lässt genau einen zu</b> und wirft beim zweiten eine
    /// <c>COMException</c> mit «Only a single ContentDialog can be open at any
    /// time» — aus einem <c>async void</c> heraus, und damit war nipp weg.
    /// Gemessen am 22.09.2026: zwei Importversuche, bei denen die Meldung des
    /// ersten noch offen stand.</para>
    /// </summary>
    private bool _dialogOffen;

    private async Task ShowAsync(string title, string message)
    {
        // Lieber keine zweite Meldung als kein Programm mehr. Die erste steht
        // noch da und sagt dasselbe Thema; wer sie schliesst, bekommt den
        // nächsten Versuch.
        if (_dialogOffen)
        {
            AppLog.DialogUebersprungen(
                ((App)Application.Current).Services.GetRequiredService<ILogger<SettingsPage>>(),
                title);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Schliessen",
        };

        _dialogOffen = true;

        try
        {
            _ = await dialog.ShowAsync();
        }
        finally
        {
            _dialogOffen = false;
        }
    }

    // --- Integrationen (§21.4, K3) ---

    private void OnIntegrationSourceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is IntegrationSourceRow row)
        {
            IntegrationViewModel.Selected = row;
        }

        RefreshIntegrations();
    }

    /// <summary>
    /// Der Katalog „Quelle hinzufügen" (ADR-033).
    ///
    /// <para><b>Als eigener Dialog und nicht als Aufklapper in der Seite:</b>
    /// die Einstellungsseite ist rund 400 Pixel breit und schon lang. Ein
    /// Katalog, der sich darin aufklappt, schiebt alles andere weg — und die
    /// Auswahl ist ein Moment, kein Zustand.</para>
    ///
    /// <para><b>Die bv2-Einträge sind gekennzeichnet.</b> Ein Kunde sieht
    /// „das CRM" und „das Gesprächsjournal" im Katalog; das ist entschieden
    /// (ADR-033) und soll erkennbar sein.</para>
    /// </summary>
    private async void OnAddSourceClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnAddSourceClick), () => OnAddSourceClickAsync(sender, e));

    private async Task OnAddSourceClickAsync(object sender, RoutedEventArgs e)
    {
        // ADR-045: Ein Dialog, der eine Wahl zwischen EINER Moeglichkeit
        // anbietet, ist ein Zwischenschritt ohne Entscheidung. Seit ADR-040
        // liegt genau eine Vorlage bei — „Eigene REST-API" —, und alles andere
        // kommt ueber „API-Anbieter importieren". Bis zum 12.09.2026 ging
        // trotzdem ein Auswahldialog mit einer Zeile auf, und der erste
        // Eindruck war „hier gibt es nichts".
        if (IntegrationViewModel.Catalog.Count == 1)
        {
            IntegrationViewModel.AddFromCatalogCommand.Execute(IntegrationViewModel.Catalog[0].Id);
            RefreshIntegrations();
            return;
        }

        var liste = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = IntegrationViewModel.Catalog,
            ItemTemplate = (DataTemplate)Resources["ConnectorCatalogTemplate"],
        };

        if (IntegrationViewModel.Catalog.Count > 0)
        {
            liste.SelectedIndex = 0;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Quelle hinzufügen",
            Content = liste,
            PrimaryButtonText = "Hinzufügen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (liste.SelectedItem is not ConnectorCatalogRow gewaehlt)
        {
            return;
        }

        IntegrationViewModel.AddFromCatalogCommand.Execute(gewaehlt.Id);

        RefreshIntegrations();
    }

    /// <summary>
    /// Entfernt die ausgewählte Quelle — <b>mit Rückfrage</b>. Anders als beim
    /// Bearbeiten geht hier Arbeit verloren: Endpunkte und Mappings sind
    /// danach weg.
    /// </summary>
    private async void OnRemoveSourceClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnRemoveSourceClick), () => OnRemoveSourceClickAsync(sender, e));

    private async Task OnRemoveSourceClickAsync(object sender, RoutedEventArgs e)
    {
        if (IntegrationViewModel.Selected is not { } row)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{row.DisplayName} entfernen?",
            Content = "Endpunkte und Feldzuordnungen dieser Quelle gehen verloren. Die "
                + "hinterlegten Zugangsdaten bleiben auf diesem Gerät.",
            PrimaryButtonText = "Entfernen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        IntegrationViewModel.RemoveSelectedCommand.Execute(null);

        RefreshIntegrations();
    }

    private void OnSaveSecretClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IntegrationSecretRow row })
        {
            IntegrationViewModel.SaveSecretCommand.Execute(row);
            RefreshIntegrations();
        }
    }

    private void OnLookupSettingToggled(object sender, RoutedEventArgs e)
    {
        // Ein Schalter, der beim Aufbau der Seite gesetzt wird, meldet
        // ebenfalls Toggled. Das ViewModel erkennt das an seinem eigenen
        // Zustand und schreibt dann nicht zurück.
        IntegrationViewModel.ApplyLookupSettingsCommand.Execute(null);
    }

    /// <summary>
    /// Öffnet den Karten-Designer (K4).
    ///
    /// Ein eigenes Fenster: 400 Pixel tragen keine Felderpalette, und die
    /// Karte selbst bleibt trotzdem 400 Pixel breit — die Vorschau dort ist
    /// deshalb genau so breit wie das Original.
    /// </summary>
    private void OnEditCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardSummaryRow row })
        {
            CardDesignerWindow.Show(row.Kind);
        }
    }

    /// <summary>
    /// Führt die Integrationsanzeige nach.
    ///
    /// Von Hand wie der Rest dieser Seite — das Refresh-Muster ist hier
    /// etabliert (docs/plans/REVIEW.md F11 nennt den Umbau auf <c>x:Bind</c> als bewusst
    /// offen).
    /// </summary>
    private void RefreshIntegrations()
    {
        NoIntegrationsText.Visibility = IntegrationViewModel.HasSources
            ? Visibility.Collapsed
            : Visibility.Visible;

        IntegrationSourceList.Visibility = IntegrationViewModel.HasSources
            ? Visibility.Visible
            : Visibility.Collapsed;

        IntegrationIssuesBar.IsOpen = IntegrationViewModel.HasIssues;
        IntegrationIssuesBar.Message = string.Join(
            Environment.NewLine,
            IntegrationViewModel.Issues);

        // Das Detail gibt es nur mit Auswahl. Ohne diesen Schritt stünden bei
        // leerer Liste lauter leere Felder da, die aussehen, als liesse sich
        // damit etwas einrichten.
        SourceDetailPanel.Visibility = IntegrationViewModel.HasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;

        RemoveSourceButton.IsEnabled = IntegrationViewModel.HasSelection;

        if (IntegrationViewModel.Selected is { } row)
        {
            SourceDetailHeader.Text = $"{row.DisplayName} · {row.Host}";
            SourceDetailCapabilities.Text = IntegrationViewModel.DetailCapabilities;
        }

        SourceDetailBar.IsOpen = IntegrationViewModel.DetailFeedback.Length > 0;
        SourceDetailBar.Message = IntegrationViewModel.DetailFeedback;

        // Nur die Testfelder, die die Quelle wirklich hat. Vorher stand
        // „Suchbegriff" immer da und war für jede Quelle mit lookupByPhone
        // wirkungslos.
        TestNumberBox.Visibility = IntegrationViewModel.SelectedHasLookup
            ? Visibility.Visible
            : Visibility.Collapsed;

        TestQueryBox.Visibility = IntegrationViewModel.SelectedHasSearch
            ? Visibility.Visible
            : Visibility.Collapsed;

        SecretList.Visibility = IntegrationViewModel.SelectedNeedsSecrets
            ? Visibility.Visible
            : Visibility.Collapsed;

        IntegrationTestSummary.Text = IntegrationViewModel.TestSummary;
        IntegrationTestResponse.Text = IntegrationViewModel.TestResponse;

        IntegrationFieldsLabel.Visibility = IntegrationViewModel.TestFields.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnExportIntegrationsClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainWindowRef is not { } window)
        {
            return;
        }

        try
        {
            var file = await FilePickers.SaveAsync(
                window,
                $"nipp-integrationen-{DateTimeOffset.Now:yyyy-MM-dd}",
                "nipp-Integrationen",
                ".json");

            if (file is null)
            {
                return;
            }

            await File.WriteAllTextAsync(file.Path, IntegrationViewModel.Export());

            // §21.2: in der Datei stehen nur Verweise auf Geheimnisse, nie die
            // Werte. Das gehört gesagt, damit niemand sie für vertraulich hält
            // — und niemand erwartet, dass sie auf dem Zielgerät genügt.
            await ShowAsync(
                "Ausgegeben",
                $"Die Integrationen stehen in {file.Name}."
                    + Environment.NewLine + Environment.NewLine
                    + "Zugangsdaten sind nicht enthalten — die Datei trägt nur ihre Namen. "
                    + "Auf einem anderen Gerät sind sie einmal neu einzutragen.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(
                "Ausgabe fehlgeschlagen",
                UserMessage.WithCause(
                    "Die Datei liess sich nicht schreiben. Einen anderen Ordner wählen — "
                        + "auf dem Schreibtisch klappt es fast immer.",
                    ex));
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => ApplyAboutLogo();

    /// <summary>
    /// Das Symbol im Info-Bereich (§9, §20.4).
    ///
    /// <para>Dieselbe Stelle wie in <c>MainWindow.ApplyTitleBarIcon</c>, und
    /// aus denselben zwei Gründen: <c>BitmapImage</c> löst kein
    /// <c>file:</c>-URI auf — es muss <c>ms-appx:///</c> sein, das auch
    /// unpackaged auf das Ausgabeverzeichnis zeigt —, und ein gescheitertes
    /// Decodieren meldet sich <b>nur</b> über <c>ImageFailed</c>. Ohne das
    /// bliebe an dieser Stelle einfach eine Lücke, ohne eine Zeile im
    /// Protokoll.</para>
    /// </summary>
    private void ApplyAboutLogo()
    {
        // Ein Bild, nicht zwei (W1.4, Befund D1) — die beiden Dateien waren
        // byteidentisch. Die Begruendung steht bei ApplyTitleBarIcon.
        const string name = "nipp.png";

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", name)))
        {
            return;
        }

        var image = new BitmapImage(new Uri($"ms-appx:///Assets/{name}"));

        image.ImageFailed += (_, e) => AppLog.AboutLogoFailed(
            ((App)Application.Current).Services.GetRequiredService<ILogger<SettingsPage>>(),
            e.ErrorMessage);

        AboutLogo.Source = image;
    }

    /// <summary>
    /// Die Mailadresse aus dem Info-Bereich (§9).
    ///
    /// <c>Launcher</c> und nicht <c>Process.Start</c>: das ist der Weg, den
    /// diese Anwendung schon in der Gesprächsansicht nimmt, und er kommt ohne
    /// Shell-Ausführung aus.
    /// </summary>
    private async void OnContactMailClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnContactMailClick), async () => await OpenAsync($"mailto:{SettingsViewModel.ContactMail}"));

    private async void OnWebsiteClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnWebsiteClick), async () => await OpenAsync(SettingsViewModel.Website));

    private async Task OpenAsync(string uri)
    {
        try
        {
            await global::Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
        }
        catch (Exception ex)
        {
            // Kein Mailprogramm, kein Browser: eine Meldung ist besser als ein
            // Knopf, der nichts tut.
            await ShowAsync(
                "Öffnen nicht möglich",
                UserMessage.WithCause(
                    "Windows hat kein Programm dafür. Für eine Adresse braucht es einen "
                        + "Browser, für eine Mailadresse ein Mailprogramm.",
                    ex));
        }
    }

    /// <summary>
    /// Einen eigenen Klingelton wählen (§9.4).
    ///
    /// Nur <c>.wav</c>: das SDK verlangt für <c>Core.Ring</c> ausdrücklich
    /// eine WAV-Datei, und die sechs <c>.mkv</c>-Klänge des Pakets sind ohne
    /// <c>bcmatroska2.dll</c> nicht abspielbar. Ein Filter, der mehr anbietet,
    /// führte zu einem Klingeln, das stumm bleibt.
    /// </summary>
    private async void OnPickRingtone(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainWindowRef is not { } window)
        {
            return;
        }

        try
        {
            var file = await FilePickers.OpenAsync(window, ".wav");

            if (file is not null)
            {
                ViewModel.UseCustomRingtone(file.Path);
            }
        }
        catch (Exception ex)
        {
            await ShowAsync(
                "Klingelton",
                UserMessage.WithCause(
                    "Die Datei liess sich nicht übernehmen. Es muss eine WAV-Datei sein, und "
                        + "sie muss lesbar bleiben — nipp spielt sie bei jedem Anruf von dort.",
                    ex));
        }
    }

    private async void OnImportIntegrationsClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainWindowRef is not { } window)
        {
            return;
        }

        try
        {
            var file = await FilePickers.OpenAsync(window, ".json");

            if (file is null)
            {
                return;
            }

            var json = await File.ReadAllTextAsync(file.Path);

            if (IntegrationViewModel.TryImport(json, out var probleme))
            {
                RefreshIntegrations();

                await ShowAsync(
                    "Eingelesen",
                    "Die Integrationen sind übernommen. Zugangsdaten sind einzutragen, "
                        + "soweit sie auf diesem Gerät noch fehlen.");

                return;
            }

            // Eine kaputte Datei ändert nichts — dieselbe Haltung wie beim
            // Einlesen der Einstellungen.
            await ShowAsync(
                "Nicht übernommen",
                "An der Datei stimmt etwas nicht, die bisherigen Integrationen bleiben:"
                    + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, probleme.Take(10)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(
                "Einlesen fehlgeschlagen",
                UserMessage.WithCause(
                    "Die Datei liess sich nicht lesen. Nichts wurde geändert — die "
                        + "bisherigen Einstellungen gelten weiter.",
                    ex));
        }
    }

    /// <summary>
    /// Eine Anbietervorlage einlesen (ADR-040).
    ///
    /// <para><b>Der Handler ist bewusst eine Kopie von
    /// <see cref="OnImportIntegrationsClick"/></b> und kein gemeinsamer Weg:
    /// die beiden Importe tun verschiedene Dinge. Dieser legt eine Vorlage ab
    /// und ändert an den eingerichteten Quellen nichts; jener ersetzt die
    /// ganze Konfiguration.</para>
    /// </summary>
    private async void OnImportConnectorClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainWindowRef is not { } window)
        {
            return;
        }

        try
        {
            // .json und keine eigene Endung: FilePickers nimmt einen einzelnen
            // Endungs-Token, und unterschieden wird ohnehin am Inhalt.
            var file = await FilePickers.OpenAsync(window, ".json");

            if (file is null)
            {
                return;
            }

            var text = await File.ReadAllTextAsync(file.Path);

            var bibliothek = ((App)Application.Current).Services
                .GetRequiredService<ConnectorLibrary>();

            if (bibliothek.TryImport(text, replaceExisting: false, out var vorlage, out var fehler))
            {
                RefreshIntegrations();

                await ShowAsync(
                    "Importiert",
                    $"«{vorlage!.DisplayName}» steht jetzt unter «Quelle hinzufügen». "
                        + "Danach: Zugangsschlüssel eintragen, Verbindung testen, einschalten.");

                return;
            }

            // Eine schon vorhandene Kennung ist kein Fehler, sondern eine
            // Rückfrage — und sie sagt dazu, was NICHT passiert.
            if (fehler is not null && fehler.Contains("schon da", StringComparison.Ordinal))
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Vorlage ersetzen?",
                    Content = "Eine Vorlage mit dieser Kennung ist schon da. Die bereits "
                        + "eingerichteten Quellen bleiben unberührt — ersetzt wird nur die "
                        + "Vorlage, aus der neue Quellen entstehen.",
                    PrimaryButtonText = "Ersetzen",
                    CloseButtonText = "Abbrechen",
                    DefaultButton = ContentDialogButton.Close,
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                if (bibliothek.TryImport(text, replaceExisting: true, out var ersetzt, out var zweiterFehler))
                {
                    RefreshIntegrations();

                    await ShowAsync("Ersetzt", $"«{ersetzt!.DisplayName}» ist aktualisiert.");
                    return;
                }

                fehler = zweiterFehler;
            }

            // Eine kaputte Datei ändert nichts — dieselbe Haltung wie überall
            // sonst beim Einlesen.
            await ShowAsync("Nicht übernommen", fehler ?? "Die Datei liess sich nicht lesen.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(
                "Einlesen fehlgeschlagen",
                UserMessage.WithCause(
                    "Die Datei liess sich nicht lesen. Nichts wurde geändert — die "
                        + "bisherigen Einstellungen gelten weiter.",
                    ex));
        }
    }

    private async void OnExportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainWindowRef is not { } window)
        {
            return;
        }

        try
        {
            var file = await FilePickers.SaveAsync(
                window,
                $"nipp-einstellungen-{DateTimeOffset.Now:yyyy-MM-dd}",
                "nipp-Einstellungen",
                ".json");

            if (file is null)
            {
                return;
            }

            ViewModel.TryExport(file.Path);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException)
        {
            // In einem async void-Handler waere das sonst ein Absturz.
            FeedbackBar.Severity = InfoBarSeverity.Error;
            FeedbackBar.Message = UserMessage.WithCause(
                "Die Einstellungen liessen sich nicht ausgeben. Einen anderen Ordner "
                    + "wählen — auf dem Schreibtisch klappt es fast immer.",
                ex);
            FeedbackBar.IsOpen = true;
        }
    }

    /// <summary>
    /// Liest eine Ausgabedatei ein — nach Rückfrage, denn sie überschreibt
    /// alles Eingestellte auf einmal.
    /// </summary>
    private async void OnImportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainWindowRef is not { } window)
        {
            return;
        }

        try
        {
            var file = await FilePickers.OpenAsync(window, ".json");

            if (file is null)
            {
                return;
            }

            var hint = ViewModel.HasProvisioningLocks
                ? Environment.NewLine + Environment.NewLine
                    + "Hinweis: Für dieses Gerät legt ein Provisionierungsprofil "
                    + "Felder fest. Beim nächsten Abruf gewinnt wieder das Profil."
                : string.Empty;

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Einstellungen einlesen?",
                Content = $"Die Einstellungen aus {file.Name} ersetzen die jetzigen. "
                    + "Die SIP-Konten und ihre Passwörter bleiben erhalten." + hint,
                PrimaryButtonText = "Einlesen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (ViewModel.TryImport(file.Path))
            {
                // Die Bedienelemente ausserhalb der Bindungen nachziehen —
                // dieselben, die auch der Konstruktor setzt.
                SelectComboBoxes();
                ApplyPolicy();
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException)
        {
            FeedbackBar.Severity = InfoBarSeverity.Error;
            FeedbackBar.Message = UserMessage.WithCause(
                "Die Datei liess sich nicht einlesen. Nichts wurde geändert.",
                ex);
            FeedbackBar.IsOpen = true;
        }
    }

    /// <summary>Setzt alle Einstellungen zurück — nach Rückfrage.</summary>
    private async void OnResetSettingsClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnResetSettingsClick), () => OnResetSettingsClickAsync(sender, e));

    private async Task OnResetSettingsClickAsync(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Alle Einstellungen zurücksetzen?",
            Content = "Darstellung, Audio, Netzwerk, Codecs, Kontakte und die "
                + "erweiterten Einstellungen gehen auf die Vorgaben zurück. Die "
                + "SIP-Konten samt Passwörtern und die Anrufliste bleiben. Das "
                + "lässt sich nicht rückgängig machen.",
            PrimaryButtonText = "Zurücksetzen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.ResetAll();
        SelectComboBoxes();
        ApplyPolicy();
    }

    /// <summary>
    /// Wohin die Einstellungsseite springen soll, wenn sie von einem Hinweis
    /// aus geoeffnet wird (ADR-045).
    /// </summary>
    public enum Sprungziel
    {
        /// <summary>Nichts Bestimmtes — der gewoehnliche Weg ueber die Umschaltleiste.</summary>
        Keines,

        /// <summary>Die Gruppe „SIP-Konten", mit dem Fokus im ersten Feld.</summary>
        Konto,

        /// <summary>Die Gruppe „Kontakte", fuer eine neue Nebenstelle.</summary>
        Kontakte,

    }

    /// <summary>
    /// Nimmt das Sprungziel entgegen und klappt die Gruppe auf.
    ///
    /// <para><b>Und ohne Ziel dasselbe, wenn kein Konto da ist</b> (ADR-045):
    /// beim allerersten Start sah der Benutzer neun zugeklappte Ueberschriften
    /// und musste raten, welche er braucht — ausgerechnet in dem Moment, in
    /// dem das Programm ihm gerade gesagt hatte, dass ihm ein Konto fehlt.</para>
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var ziel = e.Parameter as Sprungziel?
            ?? (ViewModel.Accounts.Count == 0 ? Sprungziel.Konto : Sprungziel.Keines);

        _sprungziel = ziel;

        switch (ziel)
        {
            case Sprungziel.Konto:
                AccountsGroup.IsExpanded = true;
                break;

            case Sprungziel.Kontakte:
                ContactsGroup.IsExpanded = true;
                break;

            default:
                break;
        }
    }

    /// <summary>Wohin beim Anzeigen der Fokus gehoert. Gilt einmal je Navigation.</summary>
    private Sprungziel _sprungziel;

    /// <summary>
    /// Ein Eingabefeld hat den Fokus verloren — was darin steht, wirkt jetzt
    /// (ADR-045).
    ///
    /// <para>Nur fuer die Felder, die einen fertigen Wert brauchen. Schalter,
    /// Auswahllisten und Regler wirken laengst mit der Aenderung; sie kaemen
    /// hier ein zweites Mal an und schrieben denselben Inhalt noch einmal.</para>
    ///
    /// <para><b>LostFocus und nicht LosingFocus</b> — das ist der Unterschied
    /// zwischen „wirkt" und „ist weg" (Befund A1-7, 17.09.2026).
    /// <c>LosingFocus</c> laeuft, waehrend der Fokus noch wechselt; eine
    /// <c>TextBox</c> und eine <c>NumberBox</c> uebertragen ihren Inhalt aber
    /// erst mit <c>LostFocus</c> in die Bindung. <c>ApplyEdits</c> las dort
    /// also den <b>alten</b> Stand und schrieb ihn zurueck — und weil genau
    /// diese neun Felder in <c>ErstBeimVerlassen</c> stehen und damit vom
    /// automatischen Speichern ausgenommen sind, gab es keinen zweiten Weg:
    /// bei einer <c>NumberBox</c> kam der Wert erst mit dem naechsten
    /// Speicheranlass, bei einem Textfeld <b>gar nicht</b> — auch nicht beim
    /// Beenden.</para>
    /// </summary>
    private void OnLostFocus(object? sender, FocusManagerLostFocusEventArgs e)
    {
        if (e.OldFocusedElement is TextBox or NumberBox)
        {
            ViewModel.ApplyEdits();
        }
    }

    /// <summary>
    /// Beim Anzeigen: der Fokus auf die erste Gruppe.
    ///
    /// <para>Im <c>Loaded</c> und nicht im Konstruktor — dort steht der
    /// visuelle Baum noch nicht, und <c>Focus</c> liefe ins Leere.</para>
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        switch (_sprungziel)
        {
            // Der Fokus ins erste Pflichtfeld: wer „Konto einrichten" geklickt
            // hat, will tippen und nicht erst suchen.
            case Sprungziel.Konto:
                _ = UsernameBox.Focus(FocusState.Programmatic);
                break;

            case Sprungziel.Kontakte:
                _ = TeamNameBox.Focus(FocusState.Programmatic);
                break;

            default:
                FocusFirstGroup();
                break;
        }

        _sprungziel = Sprungziel.Keines;
    }

    /// <summary>
    /// Der Fokus beim Öffnen: auf die erste Gruppe (ADR-044).
    ///
    /// <para>Die Seite setzte ihn gar nicht — in 1 100 Zeilen Code-behind stand
    /// kein einziger <c>Focus</c>-Aufruf. Nach dem Klick auf „Einstellungen"
    /// lag er auf dem Rahmen, und wer mit der Tastatur arbeitet, tabbte durch
    /// die ganze Kopfzeile, bevor er die erste Gruppe erreichte.</para>
    /// </summary>
    private void FocusFirstGroup() => _ = AccountsGroup.Focus(FocusState.Programmatic);

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(ShellPage));
        }
    }

    /// <summary>
    /// Escape und Alt+Links gehen zurück (C9) — dieselbe Handlung wie der
    /// Pfeil oben links.
    ///
    /// <para>Es gibt kein Speichern zu vergessen: jede Änderung wirkt, sobald
    /// sie gemacht ist, und ein Freitextfeld beim Verlassen des Feldes
    /// (ADR-045). Genau das löst der Fokuswechsel beim Zurücknavigieren
    /// aus.</para>
    /// </summary>
    private void OnBackAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OnBackClick(sender, new RoutedEventArgs());
    }
}
