using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Nipp.App.Diagnostics;
using Nipp.App.Theming;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;
using WinRT.Interop;

namespace Nipp.App.Windows;

/// <summary>
/// Das Fenster des Karten-Designers (K4, I9, ADR-032).
///
/// <para><b>Dieses Fenster ist dumm.</b> Aller Zustand liegt in
/// <see cref="CardDesignerViewModel"/> im Kern — <c>Nipp.App</c> hat kein
/// Testprojekt, und ein Editor hat mehr Zustand als alles andere in nipp.
/// Hier wird gezeichnet und weitergereicht.</para>
///
/// <para><b>Die drei Narben, die dieses Fenster kennt:</b></para>
/// <list type="number">
///   <item>
///     <b>Es entsteht auf dem UI-Thread des Hauptfensters.</b>
///     <see cref="Show"/> geht über die <c>DispatcherQueue</c>; ein Aufruf von
///     einem Fremdthread endet in <c>COMException: „Unzulässiges Fenster"</c>
///     — genau daran hing im September der Weg zurück ins Fenster.
///   </item>
///   <item>
///     <b>Sein Schliessen fasst am Hauptfenster nichts an.</b> Der Fehler in
///     acht Zeilen, der einen eingehenden Anruf unsichtbar machte, war ein
///     <c>Closed</c>-Behandler, der Abonnements abmeldete, die noch gebraucht
///     wurden. Dieses Fenster meldet nur seine eigenen ab.
///   </item>
///   <item>
///     <b>Ein Anruf zählt mehr.</b> Kommt einer, holt <c>MainWindow</c> die
///     Gesprächsansicht wie immer nach vorn (§10, T06). Der Designer wird nie
///     das Hauptfenster, hält nichts auf und beansprucht keine Aktivierung.
///   </item>
/// </list>
///
/// <para><b>Und die Vorschau baut auf demselben Thread</b>, der alle 20 ms
/// <c>Core.Iterate()</c> bedient (§6). Sie bleibt deshalb klein: eine Karte
/// mit zwanzig Bausteinen, dieselbe Engine wie im Gespräch.</para>
/// </summary>
public sealed partial class CardDesignerWindow : Window
{
    /// <summary>
    /// Das offene Fenster, oder <c>null</c>.
    ///
    /// <b>Höchstens einer.</b> Zwei Designer auf derselben Kartenart wären zwei
    /// Entwürfe, von denen der zweite den ersten beim Speichern überschreibt —
    /// ohne dass jemand es merkt.
    /// </summary>
    private static CardDesignerWindow? _open;

    private readonly ThemeService _theme;
    private readonly ILogger<CardDesignerWindow> _log;

    /// <summary>
    /// Ob dieses Fenster ohne Rueckfrage zugehen darf (C1).
    ///
    /// <para>Gesetzt, nachdem jemand «Verwerfen» gewaehlt hat, und von
    /// <see cref="SchliesseOhneFrage"/> fuer den Weg ueber <see cref="Show"/>.
    /// Ohne dieses Feld fragte der zweite <c>Close</c> ein zweites Mal.</para>
    /// </summary>
    private bool _schliessenErlaubt;

    /// <summary>
    /// Das <see cref="AppWindow"/> dieses Fensters — gehalten, weil
    /// <c>Closing</c> daran haengt und nicht am <see cref="Window"/>.
    /// </summary>
    private AppWindow? _appWindow;

    private CardDesignerWindow(CardKind kind)
    {
        InitializeComponent();

        var services = ((App)Application.Current).Services;

        _theme = services.GetRequiredService<ThemeService>();
        _log = services.GetRequiredService<ILogger<CardDesignerWindow>>();

        ViewModel = new CardDesignerViewModel(
            kind,
            services.GetRequiredService<CardResolver>(),
            services.GetRequiredService<IntegrationConfigStore>(),
            services.GetRequiredService<TestSampleStore>(),
            services.GetRequiredService<ILogger<CardDesignerViewModel>>(),
            services.GetRequiredService<IntegrationTester>(),
            services.GetRequiredService<SettingsService>());

        PreviewNumberBox.Text = ViewModel.PreviewNumber;

        ApplyTheme();
        FocusPalette();

        ApplyWindowSize();
        BuildFixedLists();

        Closed += OnClosed;

        Refresh();
    }

    internal CardDesignerViewModel ViewModel { get; }

    /// <summary>
    /// Öffnet den Designer für eine Kartenart, oder holt den offenen nach
    /// vorn.
    ///
    /// <b>Immer über die <c>DispatcherQueue</c> des Hauptfensters.</b> Auch
    /// wenn der Aufruf heute nur aus einem Klick in den Einstellungen kommt:
    /// ein Fenster, das nur auf dem richtigen Thread entsteht, weil der
    /// Aufrufer zufällig dort läuft, ist eine Zusage ohne Prüfung.
    /// </summary>
    public static void Show(CardKind kind)
    {
        var app = (App)Application.Current;

        if (app.MainWindowRef is not { } main)
        {
            return;
        }

        main.DispatcherQueue.TryEnqueue(async () =>
        {
            if (_open is { } offen)
            {
                if (offen.ViewModel.Kind == kind)
                {
                    offen.Activate();
                    return;
                }

                // Eine andere Art: das offene Fenster schliessen — und vorher
                // fragen, wenn dort etwas offen ist (C1).
                //
                // <b>Hier und nicht in Closing:</b> wer die Frage mit
                // «Weiterbearbeiten» beantwortet, will auch das neue Fenster
                // nicht. Ein abgebrochenes Close hielte den alten Designer
                // sonst zwar offen, legte aber den neuen darueber.
                if (offen.ViewModel.HasUnsavedChanges)
                {
                    await offen.FrageUndSchliesseAsync();

                    if (ReferenceEquals(_open, offen))
                    {
                        offen.Activate();
                        return;
                    }
                }
                else
                {
                    offen.SchliesseOhneFrage();
                }
            }

            var fenster = new CardDesignerWindow(kind);
            _open = fenster;

            fenster.Activate();
        });
    }

    /// <summary>
    /// Grösse und Lage.
    ///
    /// <b><see cref="AppWindow"/> rechnet in physischen Pixeln</b>, XAML in
    /// logischen — dieselbe Falle wie beim Hauptfenster. Deshalb über
    /// <see cref="WindowPlacement"/> und nicht mit eigenen Zahlen.
    /// </summary>
    private void ApplyWindowSize()
    {
        try
        {
            var handle = WindowNative.GetWindowHandle(this);
            var fenster = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));

            _appWindow = fenster;

            // Die Rueckfrage vor dem Schliessen (C1). Am AppWindow und nicht am
            // Window: WinUI meldet ein abbrechbares Schliessen nur hier.
            fenster.Closing += OnClosing;

            WindowPlacement.ApplyDesignerSize(fenster, handle);

            if (fenster.Presenter is OverlappedPresenter presenter)
            {
                // Kein eigener Eintrag in der Taskleiste: der Designer gehört
                // zum Hauptfenster und ist kein zweites nipp.
                presenter.IsAlwaysOnTop = false;

                // Und eine Untergrenze, damit die drei Spalten nebeneinander
                // bleiben — in physischen Pixeln, wie alles am AppWindow.
                var scale = WindowPlacement.ScaleFor(handle);

                presenter.PreferredMinimumWidth =
                    (int)(WindowPlacement.DesignerMinWidth * scale);
                presenter.PreferredMinimumHeight =
                    (int)(WindowPlacement.DesignerMinHeight * scale);
            }
        }
        catch (Exception ex)
        {
            // Ein Fenster in der falschen Grösse ist unschön; ein Designer,
            // der deswegen nicht aufgeht, ist schlimmer.
            AppLog.CardDesignerSizeFailed(_log, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Setzt das Erscheinungsbild des Hauptfensters — <b>ohne</b>
    /// <c>ThemeService.Attach</c>.
    ///
    /// <para><b>Der Befund dahinter.</b> <c>Attach</c> merkt sich <b>ein</b>
    /// Wurzelelement in einem Feld. Der Designer hätte damit dem Hauptfenster
    /// das Erscheinungsbild entzogen: ein Systemwechsel oder eine Änderung in
    /// den Einstellungen wäre danach nur noch im Designer angekommen, und nach
    /// seinem Schliessen nirgends mehr. WinUI kennt kein anwendungsweites
    /// Erscheinungsbild — es hängt am Element, und dieses Fenster hat sein
    /// eigenes.</para>
    /// </summary>
    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _theme.EffectiveTheme;
        _theme.EffectiveThemeChanged += OnThemeChanged;
    }

    /// <summary>
    /// Der Fokus beim Öffnen: auf die Feldpalette (ADR-044).
    ///
    /// <para>Das Fenster setzte ihn nicht — ein neu geöffnetes Fenster ohne
    /// Fokuspunkt. Die Palette ist der Anfang jeder Arbeit hier: von dort
    /// kommen die Bausteine, und ein Doppelklick fügt sie ein.</para>
    ///
    /// <para>Im <c>Loaded</c> des Wurzelelements und nicht im Konstruktor: der
    /// visuelle Baum steht dort noch nicht.</para>
    /// </summary>
    private void FocusPalette() =>
        RootGrid.Loaded += (_, _) => _ = PaletteList.Focus(FocusState.Programmatic);

    private void OnThemeChanged(object? sender, ElementTheme theme) =>
        DispatcherQueue.TryEnqueue(() => RootGrid.RequestedTheme = theme);

    /// <summary>
    /// Fragt nach, bevor eine halbe Stunde Kartenarbeit verlorengeht (C1).
    ///
    /// <para><b>Der Befund.</b> <c>HasUnsavedChanges</c> wurde an drei Stellen
    /// gesetzt und an <b>einer</b> gelesen — in <c>OnSaveClick</c>, um zu
    /// entscheiden, ob das Fenster danach zugeht. Wer es ueber das Kreuz
    /// verliess, verlor alles, lautlos; beim naechsten Oeffnen stand die alte
    /// Karte da, und nichts erklaerte es. Der Kommentar in <see cref="Show"/>
    /// behauptete seit jeher das Gegenteil («Es fragt seinerseits nach
    /// ungespeicherten Aenderungen») — <b>eine Zusage im Kommentar, die der
    /// Code nicht einloeste.</b> Dasselbe Muster wie bei
    /// <c>CardKind.History</c>, <c>IntegrationConfig.cards</c>,
    /// <c>ClipResolver.DescribeCaller</c> und <c>App.SdkStatus</c>: eine
    /// Faehigkeit gebaut und nicht angeschlossen, zum fuenften Mal — nur kostet
    /// es hier Arbeit statt Auffindbarkeit.</para>
    ///
    /// <para><b>Warum der Umweg ueber Cancel.</b> <c>Closing</c> ist
    /// synchron, ein <c>ContentDialog</c> nicht. Also: das Schliessen
    /// abbrechen, fragen, und bei «Verwerfen» ein zweites Mal schliessen —
    /// dann mit gesetztem <see cref="_schliessenErlaubt"/>, sonst faende die
    /// Frage kein Ende.</para>
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_schliessenErlaubt || !ViewModel.HasUnsavedChanges)
        {
            return;
        }

        args.Cancel = true;

        _ = FrageUndSchliesseAsync();
    }

    private async Task FrageUndSchliesseAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Änderungen an der Karte verwerfen?",
            Content = "Was seit dem letzten Speichern entstanden ist, geht verloren. "
                + "Die Karte gilt dann weiter so, wie sie beim Öffnen war.",
            PrimaryButtonText = "Verwerfen",
            SecondaryButtonText = "Speichern",
            CloseButtonText = "Weiterbearbeiten",

            // „Weiterbearbeiten" auf der Eingabetaste, wie bei jedem
            // destruktiven Dialog (ADR-044).
            DefaultButton = ContentDialogButton.Close,
        };

        var antwort = await dialog.ShowAsync();

        if (antwort == ContentDialogResult.Secondary)
        {
            ViewModel.SaveCommand.Execute(null);
            Refresh();

            // Speichern kann an einer Pruefung scheitern; dann bleibt das
            // Fenster stehen und die Fussleiste sagt, woran es liegt.
            if (ViewModel.HasUnsavedChanges)
            {
                return;
            }
        }
        else if (antwort != ContentDialogResult.Primary)
        {
            return;
        }

        SchliesseOhneFrage();
    }

    /// <summary>
    /// Schliesst ohne Rueckfrage — fuer den Weg, auf dem schon gefragt wurde.
    /// </summary>
    private void SchliesseOhneFrage()
    {
        _schliessenErlaubt = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // NUR die eigenen Abonnements. Genau hier lag der Fehler in acht
        // Zeilen, der den eingehenden Anruf unsichtbar machte: ein
        // Closed-Behandler, der abmeldete, was noch gebraucht wurde.
        Closed -= OnClosed;
        _theme.EffectiveThemeChanged -= OnThemeChanged;

        if (_appWindow is { } fenster)
        {
            fenster.Closing -= OnClosing;
            _appWindow = null;
        }

        if (ReferenceEquals(_open, this))
        {
            _open = null;
        }
    }

    // --- Feste Auswahllisten ---

    private void BuildFixedLists()
    {
        ValueModeBox.ItemsSource = new[]
        {
            "Feld", "Bedeutung", "Erster Treffer aus", "Fester Text", "Ausdruck",
        };

        StyleBox.ItemsSource = new[] { "Überschrift", "Unterzeile", "Text", "Kleingedrucktes" };

        VisibilityBox.ItemsSource = new[]
        {
            "Immer", "Nur wenn ein Wert da ist", "Nach eigener Bedingung",
        };

        SpacerSizeBox.ItemsSource = new[] { "Klein", "Mittel", "Gross" };

        BuildCopyFromMenu();

        ValueFieldBox.ItemsSource = ViewModel.Palette
            .SelectMany(static g => g.Entries)
            .Select(static e => e.Path)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Die Palette gruppiert. CollectionViewSource ist der Weg, den WinUI
        // dafuer vorsieht: die Quelle traegt Gruppen, ItemsPath nennt die
        // Eigenschaft mit den Eintraegen, und der GroupStyle im XAML zeichnet
        // die Kopfzeilen.
        var quelle = new CollectionViewSource
        {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath("Entries"),
            Source = ViewModel.Palette,
        };

        PaletteList.ItemsSource = quelle.View;
    }

    // --- Palette ---

    private void OnPaletteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        AddSelectedField();

    private void OnAddSelectedFieldClick(object sender, RoutedEventArgs e) => AddSelectedField();

    /// <summary>
    /// Fügt das ausgewählte Feld in die ausgewählte Zeile ein.
    ///
    /// <para><b>Zwei Wege zum selben Ziel</b>, und der Knopf ist der wichtigere:
    /// ein Doppelklick ist schneller, aber niemand errät ihn. Ziehen aus der
    /// Palette auf eine Zeile ist die dritte Stufe und steht auf der
    /// Testmatrix — es ist strukturell zu bauen, nicht pixelweise (§21.2), und
    /// ein halb funktionierendes Ziehen wäre schlimmer als keines.</para>
    /// </summary>
    private void AddSelectedField()
    {
        if (PaletteList.SelectedItem is PaletteEntry eintrag)
        {
            ViewModel.AddFieldCommand.Execute(eintrag);
            Refresh();
        }
    }

    private void OnAddTextClick(object sender, RoutedEventArgs e) => Add(DraftElementKind.Text);

    private void OnAddBadgeClick(object sender, RoutedEventArgs e) => Add(DraftElementKind.Badge);

    private void OnAddDividerClick(object sender, RoutedEventArgs e) => Add(DraftElementKind.Divider);

    private void OnAddSpacerClick(object sender, RoutedEventArgs e) => Add(DraftElementKind.Spacer);

    /// <summary>
    /// Fuellt die Auswahl „Von anderer Karte uebernehmen".
    ///
    /// <para>Einmal beim Oeffnen: welche Karten es gibt, aendert sich waehrend
    /// des Bearbeitens nicht — der Designer haelt die Konfiguration, und
    /// gespeichert wird erst am Ende.</para>
    ///
    /// <para><b>AutomationProperties.Name je Eintrag</b>, weil ein
    /// MenuFlyoutItem sonst seinen Inhalt vorliest und der Zusatz
    /// „(mitgeliefert)" dabei verlorengeht — genau die Angabe, wegen der
    /// jemand den einen und nicht den anderen Eintrag waehlt.</para>
    /// </summary>
    private void BuildCopyFromMenu()
    {
        CopyFromFlyout.Items.Clear();

        foreach (var quelle in ViewModel.CopySources)
        {
            var eintrag = new MenuFlyoutItem { Text = quelle.Label, Tag = quelle };

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(eintrag, quelle.Label);
            eintrag.Click += OnCopyFromClick;

            CopyFromFlyout.Items.Add(eintrag);
        }

        CopyFromButton.IsEnabled = CopyFromFlyout.Items.Count > 0;
    }

    private void OnCopyFromClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CardCopySource quelle })
        {
            ViewModel.CopyFromCommand.Execute(quelle);
            Refresh();
        }
    }

    private void Add(DraftElementKind kind)
    {
        ViewModel.AddElementCommand.Execute(kind);
        Refresh();
    }

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddRowCommand.Execute(null);
        Refresh();
    }

    // --- Aufbau der Karte ---

    /// <summary>
    /// Zeichnet den Aufbau: je Zeile eine Reihe von Knöpfen, einer je
    /// Baustein.
    ///
    /// <b>Von Hand und nicht über eine verschachtelte <c>ItemsControl</c>.</b>
    /// Der Baum ist drei Ebenen tief und ändert sich bei jeder Bearbeitung;
    /// eine Bindung darauf wäre drei Vorlagen mit je eigenem
    /// <c>DataContext</c>-Wechsel — und ein Ressourcenwörterbuch gilt nur für
    /// den Teilbaum darunter, was beim Umbau der Gesprächsansicht schon einmal
    /// eine Seite unöffenbar gemacht hat.
    /// </summary>
    private void BuildStructure()
    {
        StructurePanel.Children.Clear();

        foreach (var abschnitt in ViewModel.Draft.Sections)
        {
            foreach (var zeile in abschnitt.Rows)
            {
                var rahmen = new Border
                {
                    Padding = new Thickness(6),
                    CornerRadius = ThemeValues.Radius("ControlCornerRadius", 4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Resource("CardOutlineBrush"),
                    Background = ReferenceEquals(zeile, ViewModel.SelectedRow)
                        ? Resource("SubtleFillColorSecondaryBrush")
                        : null,
                };

                var inhalt = new StackPanel { Spacing = 4 };

                var kopf = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                };

                foreach (var spalte in zeile.Columns)
                {
                    var spaltenPanel = new StackPanel { Spacing = 4, MinWidth = 120 };

                    foreach (var baustein in spalte.Elements)
                    {
                        spaltenPanel.Children.Add(BuildElementButton(baustein));
                    }

                    if (spalte.Elements.Count == 0)
                    {
                        spaltenPanel.Children.Add(new TextBlock
                        {
                            Text = "(leer)",
                            Style = Resource<Style>("CaptionTextBlockStyle"),
                            Foreground = Resource("TextFillColorTertiaryBrush"),
                        });
                    }

                    kopf.Children.Add(spaltenPanel);
                }

                inhalt.Children.Add(kopf);
                rahmen.Child = inhalt;

                rahmen.Tapped += (_, _) =>
                {
                    ViewModel.SelectedRow = zeile;
                    Refresh();
                };

                StructurePanel.Children.Add(rahmen);
            }
        }
    }

    private Grid BuildElementButton(DraftElement element)
    {
        var ausgewaehlt = ReferenceEquals(element, ViewModel.SelectedElement);
        var inhalt = new StackPanel { Spacing = 0 };

        inhalt.Children.Add(new TextBlock
        {
            Text = element.Headline,
            Style = Resource<Style>("BodyStrongTextBlockStyle"),
        });

        if (element.Detail.Length > 0)
        {
            // Der zweite Pinsel muss mit der Flaeche wechseln (ADR-044, Befund
            // A1-11). Die Ueberschrift tut das von selbst — sie erbt ihre Farbe
            // vom Knopf, und AccentButtonStyle setzt sie. Der Detailtext hatte
            // dagegen CardSecondaryTextBrush fest gesetzt und blieb auf der
            // blauen Flaeche stehen: gemessen 1,16:1 in beiden Themen, gegen
            // 10,47:1 der Ueberschrift daneben. Verlangt sind 4,5:1.
            //
            // ZWEI Anlaeufe waren noetig, und beide sind gemessen. Erst
            // TextOnAccentFillColorSecondaryBrush: 3,38:1, immer noch unter der
            // Schwelle — der Pinsel ist fuer abgesetzten Text auf Akzent
            // gedacht, nicht fuer lesbaren. Dann Primary: 10,47:1 im Dunkeln,
            // aber im Hellen 3,70:1 mit SCHWARZER Schrift auf Dunkelblau —
            // denn Resource(...) loest einmal auf und liefert einen festen
            // Brush, der dem Themenwechsel nicht folgt. Die Zeile war im
            // dunklen Thema gebaut worden und behielt dessen Schwarz.
            //
            // Deshalb jetzt gar kein eigener Pinsel: Erben ist die einzige
            // Variante, die beide Themen und den Wechsel dazwischen trifft.
            // Den Unterschied zwischen Ueberschrift und Detail traegt allein
            // die Schriftgroesse.
            var detail = new TextBlock
            {
                Text = element.Detail,
                Style = Resource<Style>("CaptionTextBlockStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            // Auf der Akzentflaeche wird der Vordergrund GAR NICHT gesetzt: der
            // TextBlock erbt ihn dann vom Knopf, und AccentButtonStyle fuehrt
            // ihn dem Thema nach — genau wie bei der Ueberschrift darueber, die
            // nie ein Problem hatte.
            if (!ausgewaehlt)
            {
                detail.Foreground = Resource("CardSecondaryTextBrush");
            }

            inhalt.Children.Add(detail);
        }

        var knopf = new Button
        {
            Content = inhalt,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = element,
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            knopf,
            $"{element.Headline}. {element.Detail}");

        if (ausgewaehlt)
        {
            knopf.Style = Resource<Style>("AccentButtonStyle");
        }

        knopf.Click += (_, _) => Waehlen(element);

        // Das Kontextmenü am Baustein selbst. Es ruft dieselben Befehle wie die
        // Knöpfe rechts — der Weg dorthin war das Problem, nicht der Befehl.
        var menu = new MenuFlyout();

        var entfernen = new MenuFlyoutItem { Text = "Entfernen" };
        entfernen.Click += (_, _) =>
        {
            Waehlen(element);
            ViewModel.RemoveSelectedCommand.Execute(null);
            Refresh();
        };

        var hoch = new MenuFlyoutItem { Text = "Nach oben" };
        hoch.Click += (_, _) =>
        {
            Waehlen(element);
            ViewModel.MoveSelectedCommand.Execute(-1);
            Refresh();
        };

        var runter = new MenuFlyoutItem { Text = "Nach unten" };
        runter.Click += (_, _) =>
        {
            Waehlen(element);
            ViewModel.MoveSelectedCommand.Execute(1);
            Refresh();
        };

        menu.Items.Add(entfernen);
        menu.Items.Add(hoch);
        menu.Items.Add(runter);

        knopf.ContextFlyout = menu;

        // Und ein sichtbares ✕ daneben: ein Kontextmenü findet nur, wer eines
        // vermutet. Zusammen mit der Entf-Taste und dem beschrifteten Knopf
        // rechts sind es drei Wege zu demselben Befehl — das Löschen war nie
        // kaputt, es war unauffindbar.
        var zeile = new Grid();
        zeile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        zeile.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var weg = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        ToolTipService.SetToolTip(weg, "Entfernen");

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            weg,
            $"{element.Headline} entfernen");

        weg.Click += (_, _) =>
        {
            Waehlen(element);
            ViewModel.RemoveSelectedCommand.Execute(null);
            Refresh();
        };

        Grid.SetColumn(weg, 1);

        zeile.Children.Add(knopf);
        zeile.Children.Add(weg);

        return zeile;
    }

    /// <summary>
    /// Wählt einen Baustein samt seiner Zeile aus — die Auswahl, an der alle
    /// Befehle hängen.
    /// </summary>
    private void Waehlen(DraftElement element)
    {
        ViewModel.SelectedElement = element;

        ViewModel.SelectedRow = ViewModel.Draft.Sections
            .SelectMany(static s => s.Rows)
            .FirstOrDefault(r => r.Columns.Any(c => c.Elements.Contains(element)));

        Refresh();
    }

    // --- Eigenschaften ---

    /// <summary>
    /// Sperrt das Zurückschreiben, während die Felder gefüllt werden. Ohne das
    /// löst jedes Setzen ein <c>TextChanged</c> aus, das den Wert überschreibt,
    /// den es gerade anzeigen soll.
    /// </summary>
    private bool _filling;

    private void FillProperties()
    {
        var baustein = ViewModel.SelectedElement;

        NoSelectionText.Visibility = baustein is null ? Visibility.Visible : Visibility.Collapsed;
        PropertiesPanel.Visibility = baustein is null ? Visibility.Collapsed : Visibility.Visible;

        if (baustein is null)
        {
            return;
        }

        _filling = true;

        try
        {
            LabelBox.Text = baustein.Label;
            LabelBox.Visibility = Sichtbar(baustein.Kind
                is DraftElementKind.Field or DraftElementKind.Button or DraftElementKind.Link);

            var hatWert = baustein.Kind
                is DraftElementKind.Text or DraftElementKind.Field or DraftElementKind.Badge;

            ValueModeBox.Visibility = Sichtbar(hatWert);
            ValueModeBox.SelectedIndex = (int)baustein.Value.Mode;

            ValueFieldBox.Visibility = Sichtbar(
                hatWert && baustein.Value.Mode == DraftValueMode.Field);

            ValueFieldBox.SelectedItem = baustein.Value.Text;

            ValueTextBox.Visibility = Sichtbar(
                hatWert && baustein.Value.Mode is not DraftValueMode.Field);

            ValueTextBox.Header = baustein.Value.Mode switch
            {
                DraftValueMode.Role => "Bedeutung (name, company, type, work, summary, colleague)",
                DraftValueMode.Literal => "Text",
                DraftValueMode.FirstOf => "Felder, mit Komma getrennt",
                _ => "Ausdruck",
            };

            ValueTextBox.Text = baustein.Value.Mode == DraftValueMode.FirstOf
                ? string.Join(", ", baustein.Value.Parts)
                : baustein.Value.Text;

            StyleBox.Visibility = Sichtbar(baustein.Kind == DraftElementKind.Text);
            StyleBox.SelectedIndex = (int)baustein.Style;

            VisibilityBox.SelectedIndex = (int)baustein.Visibility;
            VisibleWhenBox.Text = baustein.VisibleWhen;
            VisibleWhenBox.Visibility = Sichtbar(baustein.Visibility == DraftVisibility.Expression);

            EmptyDashBox.Visibility = Sichtbar(baustein.Kind == DraftElementKind.Field);
            EmptyDashBox.IsChecked = baustein.EmptyText is { Length: > 0 };

            ShowLabelBox.Visibility = Sichtbar(baustein.Kind == DraftElementKind.Field);
            ShowLabelBox.IsChecked = baustein.ShowLabel;

            SpacerSizeBox.Visibility = Sichtbar(baustein.Kind == DraftElementKind.Spacer);
            SpacerSizeBox.SelectedIndex = (int)baustein.SpacerSize;

            UrlBox.Text = baustein.Url;
            UrlBox.Visibility = Sichtbar(baustein.Kind
                is DraftElementKind.Button or DraftElementKind.Link);

            SourceIdBox.Text = baustein.SourceId;
            SourceIdBox.Visibility = Sichtbar(baustein.Kind == DraftElementKind.SourceStatus);
        }
        finally
        {
            _filling = false;
        }
    }

    private static Visibility Sichtbar(bool ja) => ja ? Visibility.Visible : Visibility.Collapsed;

    private void OnLabelChanged(object sender, TextChangedEventArgs e) =>
        Aendern(b => b.Label = LabelBox.Text);

    private void OnValueModeChanged(object sender, SelectionChangedEventArgs e) =>
        Aendern(b =>
        {
            if (ValueModeBox.SelectedIndex >= 0)
            {
                b.Value.Mode = (DraftValueMode)ValueModeBox.SelectedIndex;
            }
        });

    private void OnValueFieldChanged(object sender, SelectionChangedEventArgs e) =>
        Aendern(b =>
        {
            if (ValueFieldBox.SelectedItem is string pfad)
            {
                b.Value.Text = pfad;
            }
        });

    private void OnValueTextChanged(object sender, TextChangedEventArgs e) =>
        Aendern(b =>
        {
            if (b.Value.Mode == DraftValueMode.FirstOf)
            {
                b.Value.Parts.Clear();

                foreach (var teil in ValueTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries))
                {
                    b.Value.Parts.Add(teil);
                }

                return;
            }

            b.Value.Text = ValueTextBox.Text;
        });

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e) =>
        Aendern(b =>
        {
            if (StyleBox.SelectedIndex >= 0)
            {
                b.Style = (CardTextStyle)StyleBox.SelectedIndex;
            }
        });

    private void OnVisibilityChanged(object sender, SelectionChangedEventArgs e) =>
        Aendern(b =>
        {
            if (VisibilityBox.SelectedIndex >= 0)
            {
                b.Visibility = (DraftVisibility)VisibilityBox.SelectedIndex;
            }
        });

    private void OnVisibleWhenChanged(object sender, TextChangedEventArgs e) =>
        Aendern(b => b.VisibleWhen = VisibleWhenBox.Text);

    private void OnEmptyTextChanged(object sender, RoutedEventArgs e) =>
        Aendern(b => b.EmptyText = EmptyDashBox.IsChecked == true ? "—" : null);

    private void OnShowLabelChanged(object sender, RoutedEventArgs e) =>
        Aendern(b => b.ShowLabel = ShowLabelBox.IsChecked == true);

    private void OnSpacerSizeChanged(object sender, SelectionChangedEventArgs e) =>
        Aendern(b =>
        {
            if (SpacerSizeBox.SelectedIndex >= 0)
            {
                b.SpacerSize = (CardSpacerSize)SpacerSizeBox.SelectedIndex;
            }
        });

    private void OnUrlChanged(object sender, TextChangedEventArgs e) =>
        Aendern(b => b.Url = UrlBox.Text);

    private void OnSourceIdChanged(object sender, TextChangedEventArgs e) =>
        Aendern(b => b.SourceId = SourceIdBox.Text);

    /// <summary>
    /// Führt eine Änderung am ausgewählten Baustein aus und baut nach.
    ///
    /// <b>Ohne Schnappschuss</b>, und das ist Absicht: eine Eigenschaft ändert
    /// sich beim Tippen zeichenweise, und ein Rückgängig je Buchstabe wäre
    /// unbedienbar. Zurückgenommen werden die strukturellen Schritte —
    /// Einfügen, Entfernen, Verschieben, Teilen.
    /// </summary>
    private void Aendern(Action<DraftElement> aenderung)
    {
        if (_filling || ViewModel.SelectedElement is not { } baustein)
        {
            return;
        }

        aenderung(baustein);

        ViewModel.HasUnsavedChanges = true;
        ViewModel.Refresh();

        Refresh();
    }

    // --- Knöpfe ---

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveSelectedCommand.Execute(-1);
        Refresh();
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveSelectedCommand.Execute(1);
        Refresh();
    }

    private void OnTogglePairedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.TogglePairedCommand.Execute(null);
        Refresh();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RemoveSelectedCommand.Execute(null);
        Refresh();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        ViewModel.UndoCommand.Execute(null);
        Refresh();
    }

    private void OnUndoAccelerator(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        ViewModel.UndoCommand.Execute(null);
        Refresh();
    }

    private void OnRedoAccelerator(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        ViewModel.RedoCommand.Execute(null);
        Refresh();
    }

    /// <summary>
    /// Die Entf-Taste über dem Aufbau-Bereich entfernt den ausgewählten
    /// Baustein.
    ///
    /// <b>Nicht als <c>KeyboardAccelerator</c> am Fenster:</b> dort träfe sie
    /// auch, während jemand in einem Textfeld rechts tippt — und löschte dann
    /// den Baustein statt eines Zeichens.
    /// </summary>
    private void OnStructureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not global::Windows.System.VirtualKey.Delete)
        {
            return;
        }

        if (ViewModel.SelectedElement is null)
        {
            return;
        }

        e.Handled = true;

        ViewModel.RemoveSelectedCommand.Execute(null);
        Refresh();
    }

    /// <summary>
    /// Ein Klick neben die Bausteine hebt die Auswahl auf — sonst bleibt der
    /// Eigenschaftenbereich auf etwas stehen, das niemand mehr meint.
    /// </summary>
    private void OnStructureTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer and not StackPanel)
        {
            return;
        }

        ViewModel.SelectedElement = null;
        Refresh();
    }

    // --- Vorschau ---

    private void OnPreviewNumberChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.PreviewNumber = PreviewNumberBox.Text;

        // Das ViewModel baut die Vorschau selbst neu; hier nur nachzeichnen.
        Refresh();
    }


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
            AppLog.HandlerFailed(_log, handler, ex.GetType().Name);
            // Der Designer hat keine Meldungsleiste an dieser Stelle; die
            // Protokollzeile ist die Spur.
        }
    }

    private async void OnRunLookupClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnRunLookupClick), () => OnRunLookupClickAsync(sender, e));

    private async Task OnRunLookupClickAsync(object sender, RoutedEventArgs e)
    {
        // Erst starten, dann zeichnen, dann warten: der Befehl setzt
        // IsLookingUp vor seinem ersten await — wer gleich awaitet, zeichnet
        // den Wartekringel erst, wenn er nicht mehr gebraucht wird.
        var lauf = ViewModel.RunLookupCommand.ExecuteAsync(null);

        Refresh();

        await lauf;

        Refresh();
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RedoCommand.Execute(null);
        Refresh();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveCommand.Execute(null);
        Refresh();

        if (!ViewModel.HasUnsavedChanges)
        {
            SchliesseOhneFrage();
        }
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DiscardCommand.Execute(null);

        // Ohne Rueckfrage: wer «Verwerfen» drueckt, hat sie gerade beantwortet.
        SchliesseOhneFrage();
    }

    private async void OnResetClick(object sender, RoutedEventArgs e) =>
        await GuardAsync(nameof(OnResetClick), () => OnResetClickAsync(sender, e));

    private async Task OnResetClickAsync(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Auf die mitgelieferte Karte zurücksetzen?",
            Content = "Die eigene Karte wird entfernt. Danach gilt wieder die Karte, die nipp "
                + "mitbringt — sie kommt bei einer Verbesserung von nipp mit.",
            PrimaryButtonText = "Zurücksetzen",
            CloseButtonText = "Abbrechen",

            // „Abbrechen" auf der Eingabetaste, wie bei jedem destruktiven
            // Dialog (ADR-044).
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.ResetToDefaultCommand.Execute(null);
        Refresh();
    }

    // --- Nachziehen ---

    /// <summary>
    /// Führt die ganze Anzeige nach.
    ///
    /// Von Hand wie in den Einstellungen — das Muster ist hier etabliert
    /// (docs/plans/REVIEW.md F11 nennt den Umbau auf <c>x:Bind</c> als bewusst offen).
    /// </summary>
    private void Refresh()
    {
        TitleText.Text = ViewModel.Title;
        KindNoteText.Text = ViewModel.KindNote;

        // Der Punkt im Fenstertitel, solange etwas offen ist (C1) — die
        // Windows-Konvention dafuer, und die einzige Stelle, an der man es in
        // der Taskleiste sieht.
        Title = ViewModel.HasUnsavedChanges
            ? $"nipp — {ViewModel.Title} •"
            : $"nipp — {ViewModel.Title}";

        BuildStructure();
        FillProperties();

        PreviewView.Model = ViewModel.Preview;
        PreviewSourceText.Text = ViewModel.PreviewSource;

        LookupButton.IsEnabled = ViewModel.CanLookup && !ViewModel.IsLookingUp;
        LookupRing.IsActive = ViewModel.IsLookingUp;
        LookupRing.Visibility = ViewModel.IsLookingUp
            ? Visibility.Visible
            : Visibility.Collapsed;
        LookupResultText.Text = ViewModel.LookupResult;

        ProblemBar.IsOpen = ViewModel.HasProblems;
        ProblemBar.Message = ViewModel.ProblemText;

        // Welche Speicherregel gilt, steht neben den Knöpfen (C12) — nicht
        // nur im Fenstertitel, wo man es beim Arbeiten nicht sieht.
        UnsavedText.Visibility = ViewModel.HasUnsavedChanges
            ? Visibility.Visible
            : Visibility.Collapsed;

        UndoButton.IsEnabled = ViewModel.CanUndo;
        RedoButton.IsEnabled = ViewModel.CanRedo;
        SaveButton.IsEnabled = ViewModel.CanSave;
        ResetButton.IsEnabled = ViewModel.IsCustom;
    }

    private static T? Resource<T>(string key)
        where T : class =>
        Application.Current.Resources.TryGetValue(key, out var wert) ? wert as T : null;

    private static Microsoft.UI.Xaml.Media.Brush? Resource(string key) =>
        Resource<Microsoft.UI.Xaml.Media.Brush>(key);
}
