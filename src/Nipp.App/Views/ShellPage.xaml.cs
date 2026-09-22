using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nipp.App.Diagnostics;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Search;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Nipp.App.Views;

/// <summary>
/// Die Hauptansicht im Smartphone-Format (§20.1).
///
/// §6: kein Code-Behind ruft das SDK — diese Datei kennt nur das ViewModel und
/// eigene Modelltypen.
/// </summary>
public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel { get; }

    private readonly ILogger<ShellPage> _logger;

    /// <summary>Schützt die gegenseitige Auswahl der beiden Kontaktlisten.</summary>
    private bool _syncingSelection;

    /// <summary>
    /// Der laufende Ziehvorgang samt Vorschau — <c>null</c>, wenn gerade
    /// nicht gezogen wird (ADR-066).
    ///
    /// <para><b>Der Zustand liegt im Kern.</b> Woher die Zeile kam, wo sie
    /// gerade steht und ob ein Abbruch sie exakt zurückstellen kann, ist
    /// Rechnung über Listen und kein Fensterthema — und <c>Nipp.App</c> hat
    /// kein Testprojekt. Hier steht nur, wann er beginnt und endet.</para>
    ///
    /// <para>Ein Feld und kein <c>DataPackage</c>: ein Ziehvorgang ist modal,
    /// es gibt höchstens einen zur Zeit, und beide Listen stehen auf
    /// <c>SelectionMode="Single"</c>. Ein <c>DataPackagePropertySet</c> ist
    /// ausserdem ein WinRT-Behälter; ob eine <c>ContactRow</c> dort heil
    /// durchkommt, ist nirgends zugesagt. <b>Einen Weg, den man nicht misst,
    /// nimmt man nicht.</b></para>
    /// </summary>
    private TeamDragPreview? _zug;

    /// <summary>
    /// Wo der Zeiger beim letzten Vorschauschritt stand (ADR-066).
    ///
    /// <para><b>Daran hängt, dass die Vorschau nicht zittert</b> — siehe
    /// <see cref="VorschauSchwelle"/>.</para>
    /// </summary>
    private global::Windows.Foundation.Point? _letzteVorschau;

    /// <summary>
    /// Schützt die beiden Sortier-Umschalter voreinander (ADR-047) — den am
    /// Abschnitt links und den am Kachelkopf rechts.
    /// </summary>
    /// <summary>
    /// Was <see cref="ApplyReorderMode"/> zuletzt gezeichnet hat — damit es
    /// nicht bei jeder Meldung des ViewModels erneut geschrieben wird.
    /// </summary>
    private bool? _reorderApplied;

    /// <summary>
    /// Die Höhe, auf die der Team-Abschnitt gedeckelt wird, solange darunter
    /// noch Outlook steht. Kommt aus dem XAML und wird beim ersten Auffrischen
    /// abgelesen.
    /// </summary>
    private double? _teamListCap;

    /// <summary>
    /// Die Breite der linken Spalte im breiten Layout (ADR-052), abgelesen aus
    /// dem XAML, bevor die Deckelung dort aufgehoben wird.
    ///
    /// <para>Die Zahl steht als <c>NippContentMaxWidth</c> in
    /// <c>Tokens.xaml</c> und im XAML an der Spalte — <b>hier steht sie
    /// nicht</b>. Ein Nachschlagen in <c>Application.Current.Resources</c>
    /// waere der zweite Weg zu derselben Zahl, und der Indexer dort
    /// <b>wirft</b> bei einem fehlenden Schluessel: aus dem Aufbau einer Seite
    /// heraus hat genau das nipp schon einmal am Starten gehindert.</para>
    /// </summary>
    private double? _leftColumnWidth;

    /// <summary>
    /// Der Abstand zwischen den beiden Spalten, abgelesen aus dem XAML. Er
    /// gilt im schmalen Layout nicht, weil eine Spalte der Breite 0 ihn sonst
    /// als toten Streifen am rechten Rand stehen liesse (ADR-052).
    /// </summary>
    private double? _columnGap;

    /// <summary>
    /// Die gruppierte Sicht auf die Kontakte (§8.4).
    ///
    /// Muss eine <see cref="CollectionViewSource"/> sein: nur damit kennt eine
    /// <c>ListView</c> Gruppen und zeichnet Kopfzeilen. Sie steht hier und
    /// nicht im ViewModel, weil sie zu WinUI gehört — Nipp.Core bleibt ohne
    /// UI-Abhängigkeit (§6).
    /// </summary>
    public ShellPage()
    {
        var dienste = ((App)Application.Current).Services;

        ViewModel = dienste.GetRequiredService<ShellViewModel>();
        _logger = dienste.GetRequiredService<ILogger<ShellPage>>();

        InitializeComponent();

        // <b>Die Zeigerereignisse der beiden Team-Listen kommen ueber
        // AddHandler und nicht aus dem XAML</b> (ADR-065). Das ListViewItem
        // und das GridViewItem markieren PointerPressed als behandelt — fuer
        // ihre Auswahl —, und ein behandeltes Ereignis steigt nicht weiter
        // auf. Ein XAML-Attribut an der Liste bekam deshalb nie einen Druck
        // auf eine Zeile zu sehen; am 13.09.2026 gemessen. Nur
        // handledEventsToo sieht sie trotzdem.
        //
        // Dauerhaft verbunden wie Loaded und Unloaded: die Listen gehoeren
        // zur Seite, nicht zu einem ViewModel, und die Seite lebt genau
        // einmal.
        foreach (var liste in new ListViewBase[] { TeamList, TeamTiles })
        {
            liste.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTeamPointerPressed), true);
            liste.AddHandler(PointerMovedEvent, new PointerEventHandler(OnTeamPointerMoved), true);
            liste.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTeamPointerReleased), true);
            liste.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnTeamPointerReleased), true);
            liste.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTeamPointerReleased), true);
        }

        // Die Seite bleibt erhalten, statt bei jeder Navigation neu zu
        // entstehen: die ViewModels sind Singletons, und jede neue Instanz
        // haengte sich zusaetzlich an deren Ereignisse. Bei 50 Anrufen ueber
        // acht Stunden (§2) waren das 100 Seiten, die niemand mehr freigibt.
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        // Loaded und Unloaded bleiben dauerhaft verbunden. Alles andere wird
        // in OnLoaded verbunden und in OnUnloaded geloest.
        //
        // <b>Warum das so aussehen muss.</b> Mit dem Zwischenspeicher laeuft
        // dieser Konstruktor genau einmal. Wurden die Abonnements hier
        // hergestellt und in OnUnloaded geloest — samt Loaded und Unloaded
        // selbst —, dann war die Seite nach der ersten Navigation zur
        // Gespraechsansicht taub: sie kam aus dem Zwischenspeicher zurueck,
        // niemand hatte sie neu verbunden, und Kontostand, Abzeichen,
        // Anrufliste und Meldungen standen bis zum Programmende still.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Der Tastaturweg zum Kontextmenue (ADR-044), hier und nicht im XAML:
        // der Behandler braucht keinen Instanzzustand, und der XAML-Generator
        // verdrahtet Ereignisse ausschliesslich als Instanzmitglieder — beides
        // zusammen ergaebe CS0176 oder CA1822, je nachdem, wie man es schreibt.
        // Fuenf Listen, eine Stelle — das Kachelraster eingeschlossen
        // (ADR-047): auf einer Kachel greift die Menuetaste sonst genauso ins
        // Leere wie vorher in der Kontaktliste.
        foreach (var liste in new ListViewBase[] { TeamList, OutlookList, SearchResultList, HistoryList, TeamTiles })
        {
            liste.ContextRequested += OnListContextRequested;
        }
    }

    /// <summary>
    /// Verbindet die Seite mit dem ViewModel und bringt sie auf Stand — bei
    /// <b>jeder</b> Navigation hierher, nicht nur beim ersten Mal.
    ///
    /// Der Zeiger gehört danach ins Nummernfeld: wer nipp öffnet, will
    /// wählen und muss dafür nicht erst klicken.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Erst loesen, dann verbinden: Loaded kann ohne dazwischenliegendes
        // Unloaded erneut feuern, und ein doppeltes Abonnement liesse Refresh
        // zweimal je Aenderung laufen.
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Die Breite meldet sich nur, wenn sie sich aendert — beim Zurueckkommen
        // aus der Gespraechsansicht also gar nicht. Deshalb hier einmal von
        // Hand: die Seite liegt im Zwischenspeicher, und ihr Layoutzustand
        // muesste sonst von der letzten Navigation stammen (ADR-047).
        SizeChanged -= OnPageSizeChanged;
        SizeChanged += OnPageSizeChanged;

        ViewModel.ApplyWidth(ActualWidth);

        AttachTeamGroups();

        Refresh();

        NumberBox.Focus(FocusState.Programmatic);
        NumberBox.SelectionStart = NumberBox.Text.Length;
    }

    /// <summary>
    /// Meldet die neue Breite an den Kern (ADR-047).
    ///
    /// <para><b>Hier steht keine Entscheidung.</b> Ob daraus ein Wechsel wird,
    /// beantwortet <c>ShellViewModel.ApplyWidth</c> — samt Schwelle und
    /// Hysterese, und geprüft von Tests. Diese Seite hat kein Testprojekt.</para>
    ///
    /// <para><c>Refresh</c> wird nicht von hier gerufen: ein Wechsel meldet
    /// <c>PropertyChanged</c>, und daran hängt es schon. Ein Aufruf je Pixel
    /// zeichnete sonst die ganze Seite neu, auf dem Thread, der alle 20 ms
    /// <c>Core.Iterate()</c> bedient.</para>
    /// </summary>
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        ViewModel.ApplyWidth(e.NewSize.Width);

    /// <summary>
    /// Löst die Abonnements — aber nicht <c>Loaded</c> und <c>Unloaded</c>:
    /// die Seite lebt im Zwischenspeicher weiter und wird wiederverwendet.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SizeChanged -= OnPageSizeChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>
    /// Haengt die gruppierte Quelle an die Team-Liste (ADR-041).
    ///
    /// <para><c>CollectionViewSource</c> ist der Weg, den WinUI dafuer vorsieht:
    /// die Quelle traegt die Gruppen, <c>ItemsPath</c> nennt die Eigenschaft mit
    /// den Zeilen, und der <c>GroupStyle</c> im XAML zeichnet die Koepfe. Von
    /// Hand gesetzt, weil sich <c>IsSourceGrouped</c> mit <c>x:Bind</c> nicht
    /// verdrahten laesst — dasselbe Muster wie bei der Palette im
    /// Karten-Designer.</para>
    ///
    /// <para>Es bleibt EINE ListView: N Gruppen als N Listen verloeren die
    /// Virtualisierung, und die Hoehenlogik der Seite rechnet mit genau einer
    /// Team-Liste.</para>
    /// </summary>
    private void AttachTeamGroups()
    {
        if (TeamList.ItemsSource is not null)
        {
            return;
        }

        // <b>Zwei Sichten auf dieselben Gruppen</b> (ADR-047): die Liste links
        // und das Kachelraster rechts. Sie muessen getrennt sein — ein
        // ICollectionView fuehrt ein CurrentItem, und zwei Listen, die sich
        // eines teilen, teilen damit auch ihre Auswahl. Die Gruppen und die
        // Zeilen darin sind dieselben Instanzen; nur die Sicht ist es nicht.
        _syncingSelection = true;

        TeamList.ItemsSource = GroupedTeamView();
        TeamList.SelectedIndex = -1;

        TeamTiles.ItemsSource = GroupedTeamView();
        TeamTiles.SelectedIndex = -1;

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            TeamList.SelectedIndex = -1;
            TeamTiles.SelectedIndex = -1;
            _syncingSelection = false;
        });
    }

    /// <summary>
    /// Eine gruppierte Sicht auf <c>TeamGroupRows</c>.
    ///
    /// <para><b>Die Ansicht bringt eine Auswahl mit, die niemand getroffen
    /// hat.</b> Ein <c>ICollectionView</c> führt ein <c>CurrentItem</c>, und
    /// die Liste zieht ihre Auswahl daran nach — beim Anhängen ist das die
    /// erste Zeile. Solange der Detailbereich nur Outlook betraf, sah man davon
    /// nichts; seit ADR-046 ginge er beim Start von selbst auf, mit dem Namen
    /// irgendeines Kollegen darin. Der Aufrufer löst sie deshalb wieder, und
    /// zwar zweimal: die Meldung der Zuweisung kommt erst danach an.</para>
    /// </summary>
    private object GroupedTeamView() =>
        new CollectionViewSource
        {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath("Rows"),
            Source = ViewModel.TeamGroupRows,
        }.View;

    /// <summary>
    /// §8.1: Enter wählt, Escape leert. Pfeil-runter springt in die
    /// Vorschlagsliste — sie war vorher nur mit der Maus erreichbar, obwohl
    /// sie beim Tippen aufgeht.
    /// </summary>
    /// <summary>
    /// §22.2: der Pfeil rechts im Feld zeigt die zuletzt gewählten Nummern.
    ///
    /// <para><b>Auf Klick und nicht auf Fokus.</b> Vorher erschien die Liste,
    /// sobald jemand ins leere Feld klickte — also bei genau dem Handgriff, mit
    /// dem man zu tippen anfängt. Sie schob dabei die Kontakte darunter weg und
    /// drängte sich auf, wenn niemand sie wollte.</para>
    ///
    /// <para>Ein zweiter Klick schliesst sie wieder: derselbe Knopf, dieselbe
    /// Stelle, umgekehrte Wirkung — so wie jeder Aufklappbereich sich
    /// verhält.</para>
    /// </summary>
    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasSuggestions)
        {
            ViewModel.HideRecentlyDialed();
            return;
        }

        ViewModel.ShowRecentlyDialed();

        // Der Fokus gehört ins Feld: wer eine Nummer aus der Liste nimmt, will
        // sie danach womöglich ergänzen, und wer keine findet, tippt weiter.
        NumberBox.Focus(FocusState.Programmatic);
    }

    private void OnNumberBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            // Die Eingabetaste entscheidet nach dem, was im Feld steht (C4).
            //
            // <b>Seit ADR-046 ist dieses Feld zweierlei</b> — Nummernfeld und
            // Suchfeld —, und die Standardtaste kann nur eine Bedeutung
            // tragen. Bis zum 13.09.2026 trug sie die falsche: wer «Meier»
            // tippte und Enter drueckte, loeste einen Anruf an
            // sip:Meier@… aus, weil der NumberNormalizer benannte Ziele
            // absichtlich durchreicht. Gesucht, und einen fehlgeschlagenen
            // Anruf bekommen.
            //
            // Ob die Eingabe waehlbar ist, entscheidet der Kern
            // (NumberNormalizer.IsDialable, gelesen ueber DialIssue). Hier
            // steht nur, was daraus folgt: waehlen — oder in die Trefferliste
            // springen, wo die Antwort schon steht.
            case VirtualKey.Enter:
                if (ViewModel.DialCommand.CanExecute(null))
                {
                    ViewModel.DialCommand.Execute(null);
                }
                else
                {
                    SpringeInDieListe();
                }

                e.Handled = true;
                break;

            case VirtualKey.Escape:
                ViewModel.ClearNumberCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Down when ViewModel.HasSuggestions || ViewModel.ShowSearchResults:
                SpringeInDieListe();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Setzt den Fokus in die Liste, die gerade steht (Befund A1-20).
    ///
    /// <para><b>Es sind zwei, und welche steht, entscheidet dieselbe Antwort
    /// wie über die Eingabetaste</b> (ADR-051): eine wählbare Eingabe zeigt
    /// die Vorschlagsliste, ein Name die Trefferliste. Bis zum 22.09.2026
    /// kannte diese Stelle nur die erste — wer einen Namen tippte und Enter
    /// drückte, blieb im Feld stehen, obwohl fünf Treffer darunter standen
    /// und der Knopf «Anrufen» daneben sagte «Einen Treffer darunter
    /// auswählen».</para>
    ///
    /// <para>Die Vorschlagsliste hat Vorrang, weil sie näher am Feld steht und
    /// nur erscheint, wenn die Eingabe wählbar ist — beide gleichzeitig gibt
    /// es nicht.</para>
    /// </summary>
    private void SpringeInDieListe()
    {
        // WELCHE Liste steht, entscheidet ShowSearchResults -- dieselbe
        // Antwort, die auch ueber die Eingabetaste entscheidet (ADR-051).
        //
        // NICHT "welche hat Eintraege": beide haben welche. Gemessen am
        // 22.09.2026 mit einer Spur im Protokoll -- bei einem Namen meldete
        // HasSuggestions=True UND HasSearchResults=True, und der Sprung in die
        // ausgeblendete Vorschlagsliste endete mit "Behaelter=null,
        // Fokus=False". Eine unsichtbare ListView erzeugt keine Container.
        if (ViewModel.ShowSearchResults)
        {
            FokusAufErsteZeile(SearchResultList);
            return;
        }

        if (ViewModel.HasSuggestions)
        {
            FokusAufErsteZeile(SuggestionList);
        }
    }

    /// <summary>
    /// Wählt die erste Zeile und setzt den Fokus <b>auf ihren Container</b>.
    ///
    /// <para><b>Nicht auf die Liste selbst:</b> eine <c>ListView</c> meldet
    /// <c>IsKeyboardFocusable = false</c> (am 22.09.2026 an
    /// <c>SearchResultList</c> gemessen), und ein <c>Focus()</c> darauf
    /// verpufft, ohne etwas zu melden — der Rückgabewert sagt es, und den
    /// hatte niemand angesehen. Den Fokus trägt das
    /// <c>ListViewItem</c>.</para>
    ///
    /// <para>Der Container entsteht erst mit der Auswahl; steht er wider
    /// Erwarten noch nicht, bleibt der Fokus, wo er war — lieber das als ein
    /// Sprung ins Leere.</para>
    /// </summary>
    private static void FokusAufErsteZeile(ListView liste)
    {
        if (liste.Items.Count == 0)
        {
            return;
        }

        liste.SelectedIndex = 0;

        if (liste.ContainerFromIndex(0) is Control zeile)
        {
            zeile.Focus(FocusState.Keyboard);
        }
    }

    /// <summary>
    /// Enter übernimmt den gewählten Vorschlag, Escape kehrt ins Feld
    /// zurück. Übernommen wird nur — nicht gewählt: ein Fehlgriff in einer
    /// Liste, die beim Tippen aufspringt, wäre sonst ein Anruf bei der
    /// falschen Person.
    /// </summary>
    private void OnSuggestionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when SuggestionList.SelectedItem is DialSuggestion suggestion:
                ViewModel.UseSuggestionCommand.Execute(suggestion);
                FocusNumberBox();
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                FocusNumberBox();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void FocusNumberBox()
    {
        NumberBox.Focus(FocusState.Programmatic);
        NumberBox.SelectionStart = NumberBox.Text.Length;
    }

    /// <summary>Zurück in ein laufendes Gespräch (§8.2).</summary>
    private void OnActiveCallBarClick(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ActiveCallPage));

    /// <summary>§8.3: Filter der Anrufliste.</summary>
    private void OnHistoryFilterChecked(object sender, RoutedEventArgs e)
    {
        // Beim Laden der Seite feuert Checked, bevor das ViewModel steht.
        if (sender is RadioButton { Tag: string tag } && ViewModel is not null)
        {
            ViewModel.ShowHistoryFilterCommand.Execute(tag);
        }
    }

    private void OnCallBackClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryRow row })
        {
            ViewModel.CallBackCommand.Execute(row);
        }
    }

    /// <summary>§8.3: „Nummer kopieren" aus dem Kontextmenü.</summary>
    private void OnCopyNumberClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryRow row })
        {
            CopyToClipboard(row.Number);
        }
    }

    /// <summary>§8.3: „Kontakt anlegen" — als Team-Nebenstelle.</summary>
    private void OnAddToTeamClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryRow row })
        {
            ViewModel.AddToTeamCommand.Execute(row);
        }
    }

    /// <summary>
    /// Zeigt die Aufnahme im Explorer. Der Pfad steht seit AP-Nachtrag im
    /// Verlauf; vorher war „HasRecording" toter Code.
    /// </summary>
    private void OnOpenRecordingClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryRow { Entry: var entry } })
        {
            return;
        }

        if (!entry.HasRecording)
        {
            ViewModel.Hint = "Zu diesem Anruf gibt es keine Aufnahme.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.RecordingPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ViewModel.Hint = UserMessage.WithCause(
                "Der Ordner liess sich nicht öffnen. Gibt es ihn noch?",
                ex);
        }
    }

    private void OnCallContactClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContactRow row } element)
        {
            CallOrAsk(row, element);
        }
    }

    /// <summary>
    /// Wählt den Kontakt — oder fragt zuerst, welche Nummer, wenn er mehrere
    /// hat.
    ///
    /// <b>Warum nicht immer fragen.</b> Der häufige Fall ist ein Kontakt mit
    /// genau einer Nummer; ihm einen zusätzlichen Klick aufzuerlegen, wäre
    /// eine Verschlechterung für viele, damit wenige erreichbar werden. Bis
    /// zum 06.09.2026 war es umgekehrt zu streng: gewählt wurde immer
    /// <c>Numbers[0]</c>, und eine Mobilnummer daneben war in der ganzen
    /// Oberfläche unerreichbar.
    ///
    /// Das Menü wird hier gebaut und nicht im XAML, weil
    /// <c>MenuFlyout.Items</c> sich nicht binden lässt.
    /// </summary>
    private void CallOrAsk(ContactRow row, FrameworkElement anchor)
    {
        if (!row.HasMultipleNumbers)
        {
            ViewModel.CallContactCommand.Execute(row);
            return;
        }

        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };

        foreach (var choice in row.Choices)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{choice.KindLabel} · {choice.Display}",
                Command = ViewModel.CallNumberCommand,
                CommandParameter = choice,
            };

            // Ohne das liest eine Sprachausgabe nur den Text vor, in dem die
            // Nummer als eine einzige lange Zahl steht.
            //
            // Der Wortlaut steht seit C19 an einer Stelle
            // (ContactNumberChoice.AccessibleName) und gilt damit auch für die
            // Nummernknöpfe im aufgeklappten Teil einer Zeile — dort stand
            // vorher nur die Nummer.
            AutomationProperties.SetName(item, choice.AccessibleName);

            menu.Items.Add(item);
        }

        menu.ShowAt(anchor);
    }

    private void OnCopyContactNumberClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContactRow row } && row.Number is { } number)
        {
            CopyToClipboard(number);
        }
    }

    /// <summary>
    /// Öffnet einen Kontakt im Fremdsystem (§21.3).
    ///
    /// <b>Die Adresse ist bereits geprüft</b>, bevor sie hier ankommt: nur
    /// <c>http</c> und <c>https</c>, und der Rechnername muss zur Quelle
    /// passen (<c>IntegrationConfigValidator</c>). Diese Prüfung hier ist die
    /// zweite Verteidigungslinie — ein Aufruf von <c>ShellExecute</c> mit
    /// einer beliebigen Zeichenfolge startet sonst, was auch immer Windows
    /// dahinter vermutet.
    /// </summary>
    private void OnOpenContactClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ContactRow row })
        {
            return;
        }

        if (row.Contact.OpenUri is not { } uri
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ViewModel.Hint = "Zu diesem Kontakt gibt es keine Adresse zum Öffnen.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ViewModel.Hint = UserMessage.WithCause(
                "Der Kontakt liess sich in Outlook nicht öffnen. Läuft Outlook?",
                ex);
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);

            ViewModel.Hint = $"{text} ist in der Zwischenablage.";
        }
        catch (Exception ex)
        {
            // Die Zwischenablage gehoert dem ganzen System und ist
            // gelegentlich von einem anderen Programm belegt; das quittiert
            // Windows mit einer COMException. Aus einem Ereignishandler heraus
            // wuerde sie die App beenden — fuer einen Kopiervorgang ist das
            // der falsche Preis.
            ViewModel.Hint = UserMessage.WithCause(
                "Kopieren war nicht möglich. Ein anderes Programm hält die Zwischenablage "
                    + "gerade — gleich noch einmal versuchen.",
                ex);
        }
    }

    /// <summary>
    /// Eine Taste der Wähltastatur — die Ziffer wandert ins Feld.
    ///
    /// <para><b>Der Fokus springt zurück, wenn der Druck nicht von der Tastatur
    /// kam</b> (ADR-044). Wer sich mit Tabulator auf die „5" gestellt hat, soll
    /// dort bleiben und zur „6" weitergehen können; wer mit Maus oder Finger
    /// tippt, will danach im Nummernfeld stehen.</para>
    ///
    /// <para><b>Gefragt wird die Taste und nicht der Fokus</b> — das ist der
    /// Unterschied zu vorher, und er ist gemessen (Befund A1-12, 17.09.2026).
    /// Die alte Fassung prüfte, ob der Fokus gerade auf der Tastatur liegt.
    /// Bei einem Mausklick liegt er das auch: <b>Windows setzt ihn auf den
    /// Knopf, bevor <c>Click</c> feuert</b>. Der Rücksprung unterblieb damit
    /// auch bei der Maus, und die nächste getippte Ziffer ging verloren — der
    /// Fokus stand auf einem Knopf, der mit Ziffern nichts anfängt. Der
    /// Kommentar, der hier stand, behauptete das Gegenteil; niemand hatte es
    /// nachgesehen.</para>
    /// </summary>
    private void OnKeypadKeyPressed(object? sender, Controls.KeypadPress druck)
    {
        ViewModel.AppendDigitCommand.Execute(druck.Key);

        if (!druck.VonTastatur)
        {
            FocusNumberBox();
        }
    }

    private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Nur echte Benutzerwahl weiterreichen. Ein leeres AddedItems bedeutet,
        // dass die Liste ihre Auswahl beim Austausch verloren hat — dieselbe
        // Falle wie bei den Gesprächen.
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AccountStatus account)
        {
            ViewModel.SelectedAccount = account;
        }
    }

    /// <summary>Strg+F setzt den Fokus ins Nummernfeld (ADR-046).</summary>
    private void OnFocusNumberAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusNumberBox();
        NumberBox.SelectAll();
    }

    /// <summary>
    /// „Konto einrichten" aus der Hinweisleiste (ADR-045).
    ///
    /// <para>Navigiert <b>und</b> klappt die richtige Gruppe auf. Ohne den
    /// zweiten Teil waere der Knopf nur eine Abkuerzung fuer den Tabwechsel,
    /// und der Benutzer stuende wieder vor neun zugeklappten Ueberschriften.</para>
    /// </summary>
    private void OnSetupAccountClick(object sender, RoutedEventArgs e) =>
        OpenSettings(Settings.SettingsPage.Sprungziel.Konto);

    /// <summary>„Nebenstelle anlegen" aus dem leeren Kontaktbereich.</summary>
    private void OnSetupTeamClick(object sender, RoutedEventArgs e) =>
        OpenSettings(Settings.SettingsPage.Sprungziel.Kontakte);

    private void OpenSettings(Settings.SettingsPage.Sprungziel ziel)
    {
        Refresh();
        Frame.Navigate(typeof(Settings.SettingsPage), ziel);
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag })
        {
            return;
        }

        // Die Einstellungen sind kein Inhaltsbereich, sondern eine eigene
        // Seite: sie brauchen die ganze Höhe, und im Ausschnitt unter der
        // Wähltastatur wäre davon nichts zu bedienen. Zurück geht es über den
        // Pfeil oben links (SettingsPage.OnBackClick).
        if (string.Equals(tag, "Settings", StringComparison.Ordinal))
        {
            // Refresh vor der Navigation, nicht danach: ein ToggleButton
            // schaltet sich beim Klick selbst ein, und diese Seite bleibt im
            // Zwischenspeicher stehen. Ohne diese Zeile käme man aus den
            // Einstellungen zurück und der Knopf wäre weiterhin gedrückt.
            Refresh();
            Frame.Navigate(typeof(Settings.SettingsPage));
            return;
        }

        ViewModel.ShowSectionCommand.Execute(tag);

        // Auch dann nachführen, wenn sich am Abschnitt nichts geändert hat:
        // ein Klick auf den bereits aktiven Bereich schaltet den ToggleButton
        // aus, das ViewModel meldet mangels Änderung nichts, und die Leiste
        // stünde ohne Auswahl da.
        Refresh();
    }

    /// <summary>
    /// AP4.3: ein Vorschlag wurde angetippt. Einfacher Klick genügt — die Liste
    /// verschwindet gleich wieder, ein Doppelklick wäre daneben gegangen.
    /// </summary>
    private void OnSuggestionTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DialSuggestion suggestion })
        {
            ViewModel.UseSuggestionCommand.Execute(suggestion);
            FocusNumberBox();
        }
    }

    /// <summary>
    /// §8.4: Doppelklick wählt — dieselbe Geste wie in der Anrufliste.
    ///
    /// Über den <c>DataContext</c> der getroffenen Zeile und nicht über die
    /// Auswahl der Liste: doppelt in den leeren Bereich unter der Liste geklickt
    /// rief sonst die zuletzt markierte Person an.
    /// </summary>
    private void OnContactDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContactRow row } element)
        {
            CallOrAsk(row, element);
        }
    }

    /// <summary>
    /// Bringt die beiden Kontaktabschnitte auf Stand: Überschriften, Sichtbarkeit
    /// und die Aufteilung der Höhe.
    ///
    /// <b>Warum die Zeilenhöhen umgeschaltet werden.</b> Der Stern gehört dem
    /// Abschnitt, der ihn braucht. Ist Outlook zugeklappt, bliebe darunter sonst
    /// ein leerer Streifen, und das Team drängte sich oben in seine 220 Pixel,
    /// obwohl die halbe Ansicht frei ist.
    /// </summary>
    /// <summary>
    /// Führt die Suchansicht nach (§21.4).
    ///
    /// <b>Sie ersetzt die Abschnitte Team und Outlook</b>, solange etwas im
    /// Feld steht. Nebeneinander wären es drei Listen in einem 400 Pixel
    /// breiten Fenster, und keine davon zeigte genug.
    ///
    /// Umgeschaltet wird an der <b>Eingabe</b>, nicht am Ergebnis: sonst
    /// verschwände die Liste bei jedem Zwischenstand ohne Treffer und käme
    /// gleich wieder — ein Flackern, das aussieht wie ein Fehler.
    /// </summary>
    private void RefreshSearch()
    {
        // Der Platzhalter des Nummernfelds nennt die Quellen, die es fragt
        // (ADR-025, ADR-046). Im Code gesetzt und nicht gebunden: die Menge der
        // Quellen aendert sich mit der Konfiguration, und das ViewModel meldet
        // dafuer kein PropertyChanged — eine OneWay-Bindung naehme den Wert von
        // einmal beim Start, als der Suchdienst moeglicherweise noch nichts
        // wusste.
        //
        // Ohne Netzquelle bleibt es bei „Nummer": dann ist das Feld genau das,
        // und die Vorschlagsliste sucht ohnehin nur lokal.
        NumberBox.PlaceholderText = ViewModel.NumberBoxPlaceholder;

        // Eine Liste je Eingabeart (C5). Welche gilt, entscheidet der Kern —
        // aus derselben Regel, die auch die Eingabetaste trägt.
        var searching = ViewModel.ShowSearchResults;

        SearchBody.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        ContactsBody.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;

        SearchRing.IsActive = ViewModel.IsSearching;
        SearchRing.Visibility = ViewModel.IsSearching ? Visibility.Visible : Visibility.Collapsed;

        SearchEmptyText.Visibility = ViewModel.SearchFoundNothing
            ? Visibility.Visible
            : Visibility.Collapsed;

        // W1.3 (C9): «Keine Treffer.» sagte nicht, wo gesucht wurde. Wer nicht
        // weiss, dass nipp nur die eingerichteten Quellen fragt, hält die
        // leere Liste für einen Fehler — und wer keine Quelle eingerichtet
        // hat, erfährt hier zum ersten Mal, dass es welche gäbe.
        SearchEmptyText.Text = ViewModel.HasContactSearch
            ? "Keine Treffer in den eingerichteten Quellen und im Adressbuch."
            : "Keine Treffer im Adressbuch. Weitere Quellen lassen sich in den "
                + "Einstellungen anbinden.";

        // Nur Quellen, zu denen es etwas zu sagen gibt. Eine erfolgreiche
        // lokale Suche erwähnt niemand, und eine Zeile, die bei jeder Suche
        // dasselbe sagt, liest nach dem zweiten Mal niemand mehr.
        var notable = ViewModel.NotableSearchSources;

        if (notable.Count == 0)
        {
            SearchStatusText.Visibility = Visibility.Collapsed;
            return;
        }

        SearchStatusText.Text = string.Join(
            " · ",
            notable.Select(static source => source.State switch
            {
                SearchState.Loading => $"{source.DisplayName} wird gefragt …",
                SearchState.Timeout => $"{source.DisplayName} antwortet nicht",
                SearchState.Skipped => $"{source.DisplayName} übersprungen",
                _ => source.Message ?? $"{source.DisplayName}: Fehler",
            }));

        SearchStatusText.Visibility = Visibility.Visible;
    }

    private void RefreshContactSections()
    {
        OutlookHeaderText.Text = ViewModel.OutlookHeader;

        // Ein Kopf über nichts hilft niemandem — dasselbe Argument, das vorher
        // HidesIfEmpty getragen hat. Dass keine Nebenstellen eingetragen sind,
        // sagt der Platzhalter der leeren Liste.
        //
        // <b>Und im breiten Layout steht er gar nicht hier</b> (ADR-047): die
        // Nebenstellen stehen rechts als Kacheln. Zweimal dieselbe Liste in
        // einem Fenster waere die Doppelanzeige, wegen der ADR-046 die beiden
        // Suchfelder zusammengelegt hat.
        TeamGroup.Visibility = ViewModel.TeamContacts.Count > 0 && ViewModel.ShowTeamInLeftColumn
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Auch dann zeigen, wenn nichts kam, aber ein Grund vorliegt: §8.4
        // verlangt „eine verstaendliche Anzeige statt leerer Liste", und ein
        // ganz fehlender Abschnitt ist noch weniger als eine leere Liste. Auf
        // diesem Geraet laeuft das neue Outlook, das keine Kontakte fuer andere
        // Programme bereitstellt — ohne Hinweis sucht man den Fehler bei nipp.
        OutlookGroup.Visibility = ViewModel.OutlookContacts.Count > 0 || ViewModel.HasOutlookHint
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Wer den Platz bekommt, hängt daran, wer ihn brauchen kann.
        //
        // Die Deckelung der Team-Liste ist nur dann sinnvoll, wenn darunter
        // noch etwas steht, dem der Platz sonst fehlte. Steht das Team allein
        // — Outlook zugeklappt, leer oder ausgeschaltet —, bekommt es die
        // ganze Höhe; vorher blieb es bei seinen 220 Pixeln und darunter war
        // die halbe Ansicht leer.
        var outlookVisible = OutlookGroup.Visibility == Visibility.Visible;
        var outlookOpen = outlookVisible && ViewModel.IsOutlookExpanded;
        // <b>Sichtbar heisst offen</b> (ADR-063). Der Abschnitt hatte bis
        // zum 13.09.2026 einen eigenen Aufklapper; seither klappen nur noch
        // die Gruppen darin, und die tun das in der Liste selbst.
        var teamOpen = TeamGroup.Visibility == Visibility.Visible;

        // Die Deckelung steht als Token im XAML; hier wird sie einmal
        // abgelesen, bevor sie das erste Mal aufgehoben wird. Ein Nachschlagen
        // in Application.Current.Resources wäre die zweite Stelle, an der die
        // Zahl steht — und die, die beim Umbenennen niemand findet.
        _teamListCap ??= TeamList.MaxHeight;

        TeamList.MaxHeight = outlookOpen ? _teamListCap.Value : double.PositiveInfinity;

        // Ein zugeklappter Abschnitt braucht nur seine Kopfzeile. Der Stern
        // gehört dem, der wirklich eine Liste zeigt; ist das keiner, teilen
        // sich beide den Platz nach Bedarf und unten bleibt er frei.
        TeamRow.Height = teamOpen && !outlookOpen
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;

        OutlookRow.Height = outlookOpen
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
    }

    /// <summary>
    /// Stellt her, was aus dem Layoutzustand folgt (ADR-047).
    ///
    /// <para><b>Hier wird nicht entschieden, hier wird gezeichnet.</b> Ob zwei
    /// Spalten stehen, beantwortet <c>ShellViewModel.Layout</c>.</para>
    ///
    /// <para><b>Die Deckelung ist weg, die Breite gehört dem Fenster</b>
    /// (ADR-052). Schmal füllt der Inhalt das Fenster; breit ist die linke
    /// Spalte <b>fest</b> und die rechte nimmt den ganzen Rest. Zwei
    /// Sternspalten, von denen eine an ihrer Höchstbreite abgeschnitten wird,
    /// verschenken den Unterschied — auf 1920 Pixeln waren das 480.</para>
    /// </summary>
    private void ApplyLayout()
    {
        var breit = ViewModel.IsWide;

        // Die Breite der linken Spalte steht als Token im XAML und wird hier
        // einmal abgelesen — dasselbe Muster wie _teamListCap daneben. Danach
        // ist die Deckelung aufgehoben: im schmalen Layout ist die linke die
        // einzige Spalte, und eine Deckelung waere genau der Rand, den ADR-052
        // beseitigt.
        if (_leftColumnWidth is null)
        {
            _leftColumnWidth = LeftColumn.MaxWidth;
            LeftColumn.MaxWidth = double.PositiveInfinity;
        }

        LeftColumn.Width = breit
            ? new GridLength(_leftColumnWidth.Value)
            : new GridLength(1, GridUnitType.Star);

        // Eine Spalte der Breite 0 nimmt keinen Platz und erzeugt keine
        // Elemente — die Kacheln darin sind zusaetzlich unsichtbar geschaltet,
        // weil eine Nullbreite ihre Container nicht verhindert.
        TilesColumn.Width = breit ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        // <b>Der Spaltenabstand gilt auch fuer eine Spalte der Breite 0.</b>
        // Solange die Deckelung den Inhalt in die Mitte stellte, verschwand er
        // im Rand; ohne sie waere er ein toter Streifen rechts aussen — also
        // genau der Rand, den ADR-052 wegnimmt, nur schmaler.
        _columnGap ??= RootGrid.ColumnSpacing;
        RootGrid.ColumnSpacing = breit ? _columnGap.Value : 0;

        TilesPanel.Visibility = breit ? Visibility.Visible : Visibility.Collapsed;

        // Die Umschaltleiste steuert die linke Spalte, also steht sie im
        // breiten Layout auch nur dort (ADR-052). Der Kachelbereich reicht
        // dafuer neben ihr bis an den unteren Rand — das steht als RowSpan 6
        // fest im XAML und braucht keine Umschaltung.
        Grid.SetColumnSpan(SectionBar, breit ? 1 : 2);

        if (!breit)
        {
            return;
        }

        // Was der Filter übriglässt (C11) — und der Leerzustand unterscheidet
        // jetzt zwei Fälle: keine Nebenstellen eingetragen, oder keine, die
        // zur Suche passt.
        var gefiltert = ViewModel.ShowSearchResults;

        TilesFilterHint.Visibility = gefiltert ? Visibility.Visible : Visibility.Collapsed;
        TilesFilterText.Text = ViewModel.TeamFilterHint;

        var sichtbare = ViewModel.TeamGroupRows.Sum(g => g.Rows.Count);
        var leer = sichtbare == 0;

        TeamTiles.Visibility = leer ? Visibility.Collapsed : Visibility.Visible;
        TilesEmptyText.Visibility = leer ? Visibility.Visible : Visibility.Collapsed;
        var keineEingetragen = ViewModel.TeamContacts.Count == 0;

        TilesEmptyText.Text = keineEingetragen
            ? "Keine Nebenstellen eingetragen."
            : "Keine Nebenstelle passt zur Suche.";

        // W1.3 (C9): der Weg dorthin, wo man sie einträgt — aber nur im
        // wirklich leeren Zustand. Bei einer Suche ohne Treffer wäre der
        // Verweis eine falsche Fährte: die Nebenstellen sind ja da.
        TilesEmptyAction.Visibility = leer && keineEingetragen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Schaltet das Sortieren des Teams ein und aus (§8.4).
    ///
    /// Solange es aus ist, verhält sich die Liste wie jede andere: antippen
    /// wählt aus, doppelt tippen ruft an, und nichts verrutscht. Erst mit dem
    /// Umschalter lassen sich Zeilen ziehen.
    /// </summary>
    private void OnReorderToggleClick(object sender, RoutedEventArgs e)
    {
        // <b>Der Knopf setzt nur den Zustand.</b> Alles Gezeichnete folgt
        // daraus — die Ziehbarkeit der Listen und die beiden Hinweisleisten in
        // ApplyReorderMode, der gedrueckte Zustand ueber die Bindung an der
        // Gruppe. Bis zum 13.09.2026 fuehrte diese Stelle zwei benannte
        // Knoepfe von Hand nach und brauchte dafuer ein Schutzflag gegen ihr
        // eigenes Ereignis.
        var gewuenscht = sender is ToggleButton { IsChecked: true };

        ViewModel.IsTeamReorderMode = gewuenscht && ViewModel.CanReorderTeam;

        // Lehnt der Kern ab, meldet er nichts — dann bliebe der Knopf
        // gedrueckt stehen und behauptete einen Modus, den es nicht gibt.
        if (sender is ToggleButton knopf && knopf.IsChecked != ViewModel.IsTeamReorderMode)
        {
            knopf.IsChecked = ViewModel.IsTeamReorderMode;
        }
    }

    /// <summary>
    /// Was am Sortiermodus zu zeichnen ist: die Ziehbarkeit beider Listen und
    /// die beiden Hinweisleisten (C20).
    ///
    /// <para><b>Aus <c>Refresh()</c> und nicht aus dem Knopf.</b> Der Modus
    /// kann auch ohne Klick enden — eine Suche oder eine Profilsperre beenden
    /// ihn im Kern. Wer nur beim Klick zeichnet, hat die Listen danach
    /// weiterhin ziehbar.</para>
    ///
    /// <para><c>Refresh()</c> läuft bei jeder Meldung des ViewModels, also im
    /// Takt der Präsenzmeldungen. Der Frühausstieg spart das achtfache
    /// Schreiben auf zwei Listen, das dabei sonst anfiele.</para>
    /// </summary>
    private void ApplyReorderMode()
    {
        var on = ViewModel.IsTeamReorderMode;

        if (_reorderApplied == on)
        {
            return;
        }

        _reorderApplied = on;

        // <b>Hier stand bis zum 13.09.2026 der eingebaute Umsortierweg von
        // WinUI</b> — CanDragItems, CanReorderItems, AllowDrop und ReorderMode
        // an beiden Listen. Er ist weg, weil er nichts tat: gemessen am
        // gebauten Fenster endete in der schmalen Liste jeder Drop mit
        // «None», und im Kachelraster feuerte nicht einmal der Zugbeginn
        // (ADR-065, Nachtrag zu ADR-042). Gezogen wird jetzt am Element der
        // Vorlage, und das gilt in beiden Ansichten gleich.
        //
        // Ob gezogen werden darf, liest OnTeamPointerPressed aus
        // IsTeamReorderMode — es braucht dafür nichts Gezeichnetes.

        // Der Hinweis, dass der Modus läuft (C20). Sichtbar ändert sich sonst
        // nichts ausser der Ziehbarkeit: wer den Umschalter versehentlich traf,
        // merkte es erst, wenn eine Zeile verrutschte.
        //
        // <b>Und er ist der ortsfeste Ausstieg:</b> der Umschalter sitzt seit
        // ADR-064 im Gruppenkopf und scrollt mit aus dem Bild.
        var sichtbar = on ? Visibility.Visible : Visibility.Collapsed;

        TeamReorderHint.Visibility = sichtbar;
        TilesReorderHint.Visibility = sichtbar;
    }

    /// <summary>
    /// „Filter aufheben" über den Kacheln (C11) — leert das Nummernfeld, denn
    /// von dort kommt der Filter.
    /// </summary>
    private void OnClearTileFilterClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearNumberCommand.Execute(null);
        FocusNumberBox();
    }

    /// <summary>
    /// „Fertig" aus dem Hinweis — derselbe Weg wie ein zweiter Klick auf den
    /// Umschalter (C20).
    /// </summary>
    private void OnReorderDoneClick(object sender, RoutedEventArgs e)
    {
        // Direkt am Zustand: der Umschalter sitzt seit ADR-064 im Gruppenkopf
        // und ist von hier aus nicht ansprechbar. Er zieht über seine Bindung
        // nach, und das Gezeichnete folgt aus Refresh().
        ViewModel.IsTeamReorderMode = false;
    }

    /// <summary>
    /// Ab wie vielen Pixeln aus einem Drücken ein Zug wird. Darunter ist es
    /// ein Klick — Auswahl, Doppelklick, Kontextmenü bleiben unberührt.
    /// </summary>
    private const double ZugSchwelle = 8;

    /// <summary>Wo der Zeiger gedrückt wurde, solange kein Zug läuft.</summary>
    private global::Windows.Foundation.Point? _zugStart;

    /// <summary>Das Vorlagenelement unter dem gedrückten Zeiger.</summary>
    private FrameworkElement? _zugQuelle;

    /// <summary>
    /// Um wie viele Pixel sich der Zeiger bewegt haben muss, damit die
    /// Vorschau einen Schritt weiterrückt (ADR-066).
    ///
    /// <para><b>Das ist die Bremse gegen die Rückkopplung.</b> Am 14.09.2026
    /// gemessen: ohne sie springt die Vorschau an der Gruppengrenze hin und
    /// her, in vier von sieben Zügen, mit 25 bis 50 ms zwischen Hin und
    /// Zurück. Der Grund ist nicht der Zeiger — der steht still —, sondern das
    /// Layout: die Zeile wechselt die Gruppe, alles darunter rutscht nach, und
    /// unter dem stehenden Zeiger liegt wieder die alte Nachbarschaft.</para>
    ///
    /// <para>Sechs Pixel liegen deutlich unter einer Zeilenhöhe und über dem,
    /// was ein gehaltener Zeiger wackelt. <b>Der Wert ist gewählt, nicht
    /// gemessen</b> — am Gerät zu prüfen (T293).</para>
    /// </summary>
    private const double VorschauSchwelle = 6;

    /// <summary>
    /// Der Zeiger wurde in einer der beiden Team-Listen gedrückt (ADR-065).
    ///
    /// <para><b>Warum der Zug hier beginnt und nicht bei WinUI.</b> Am
    /// 13.09.2026 am gebauten Fenster gemessen: im Kachelraster startet der
    /// eingebaute Weg keinen Zug — weder <c>CanDragItems</c> an der Liste noch
    /// <c>CanDrag</c> am Element der Vorlage lösten dort je ein Ereignis aus,
    /// bei eingeschaltetem Sortiermodus und nachweislich gesetzten
    /// Eigenschaften. In der schmalen Liste ging es, im Raster nicht — und
    /// zwei Ansichten, die sich verschieden verhalten, sind genau die
    /// Ungleichheit, die dieses Projekt schon mehrmals bezahlt hat.</para>
    ///
    /// <para><b>Deshalb ein Weg für beide:</b> die Liste merkt sich das
    /// Drücken, und sobald sich der Zeiger weit genug bewegt hat, startet das
    /// Vorlagenelement den Zug selbst (<c>StartDragAsync</c>). Die
    /// Zeigerereignisse steigen bis zur Liste auf, egal wer im Inneren den
    /// Zeiger hält — das ist der Grund, warum sie an der Liste hängen und
    /// nicht an der Zeile. <b>Aber nur über <c>AddHandler</c> mit
    /// <c>handledEventsToo</c>:</b> das Item markiert den Druck als
    /// behandelt, und ein XAML-Attribut an der Liste sah ihn deshalb nie
    /// (siehe den Konstruktor).</para>
    /// </summary>
    private void OnTeamPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _zugStart = null;
        _zugQuelle = null;

        if (!ViewModel.IsTeamReorderMode || _zug is not null)
        {
            return;
        }

        var punkt = e.GetCurrentPoint(null);

        if (!punkt.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var quelle = Vorlagenwurzel(e.OriginalSource as DependencyObject);

        if (quelle?.DataContext is not ContactRow)
        {
            return;
        }

        _zugStart = punkt.Position;
        _zugQuelle = quelle;
    }

    private void OnTeamPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_zugStart is not { } start || _zugQuelle is not { } quelle)
        {
            return;
        }

        var punkt = e.GetCurrentPoint(null);

        if (!punkt.Properties.IsLeftButtonPressed)
        {
            _zugStart = null;
            _zugQuelle = null;
            return;
        }

        var dx = punkt.Position.X - start.X;
        var dy = punkt.Position.Y - start.Y;

        if ((dx * dx) + (dy * dy) < ZugSchwelle * ZugSchwelle)
        {
            return;
        }

        _zugStart = null;
        _zugQuelle = null;

        StarteZug(quelle, e.GetCurrentPoint(quelle));
    }

    private void OnTeamPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _zugStart = null;
        _zugQuelle = null;
    }

    /// <summary>
    /// Die Wurzel der Zeilen- oder Kachelvorlage über einem Element — das
    /// Element, das <c>DragStarting</c> und die Ablegeziele trägt.
    /// </summary>
    private static FrameworkElement? Vorlagenwurzel(DependencyObject? von)
    {
        for (var knoten = von; knoten is not null; knoten = VisualTreeHelper.GetParent(knoten))
        {
            if (knoten is SelectorItem eintrag)
            {
                return eintrag.ContentTemplateRoot as FrameworkElement;
            }
        }

        return null;
    }

    /// <summary>
    /// Startet den Zug und wartet sein Ende ab — <b>ein</b> Anfang, <b>ein</b>
    /// Ende, für Zeile und Kachel gleich.
    /// </summary>
    private async void StarteZug(FrameworkElement quelle, Microsoft.UI.Input.PointerPoint punkt)
    {
        var zeile = quelle.DataContext as ContactRow;

        var zug = zeile is null
            ? null
            : TeamDragPreview.Start(ViewModel.TeamGroupRows, zeile);

        // <b>Die Zeile steht vor dem Abbruch und nicht danach.</b> Ein stiller
        // Abbruch ist von einem Zug, der gar nicht erst beginnt, nicht zu
        // unterscheiden — und genau dieser Unterschied hat am 13.09.2026
        // Stunden gekostet.
        AppLog.DragStarted(_logger, ViewModel.IsTeamReorderMode, zug is not null);

        if (zug is null)
        {
            return;
        }

        var herkunft = zug.Group;

        _zug = zug;
        _letzteVorschau = null;
        zug.Row.IsDragging = true;

        var ergebnis = DataPackageOperation.None;

        try
        {
            ergebnis = await quelle.StartDragAsync(punkt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // async void: eine Ausnahme von hier beendete die Anwendung. Ein
            // Zug, der nicht starten kann, ist ein Zug ohne Verschiebung —
            // mehr nicht.
            AppLog.DragWithoutMove(_logger, ex.GetType().Name);
        }

        // Immer leeren, auch wenn nichts verschoben wurde: ein stehen
        // gebliebenes Feld vergiftet den nächsten Zug.
        _zug = null;
        _letzteVorschau = null;
        zug.Row.IsDragging = false;

        // <b>Ein Zug ohne Ergebnis nimmt die Vorschau zurück</b> — Escape,
        // Loslassen ausserhalb des Fensters, ein Ziel, das ihn ablehnt. Die
        // Zeile steht danach exakt dort, wo sie begonnen hat.
        if (ergebnis != DataPackageOperation.Move || !zug.Changed)
        {
            zug.Cancel();

            // Auch das ist eine Meldung wert: bis zum 13.09.2026 war «nichts
            // passiert» von «nicht gezogen» nicht zu unterscheiden.
            AppLog.DragWithoutMove(_logger, ergebnis.ToString());
            return;
        }

        // <b>Hier wird nichts mehr gerechnet.</b> Was die Vorschau hinterlässt,
        // ist der Auftrag; ApplyTeamLayout() liest die Anzeige (ADR-066).
        ViewModel.ApplyTeamLayout();

        AppLog.DragEnded(_logger, !string.Equals(zug.Group, herkunft, StringComparison.Ordinal));
    }

    /// <summary>
    /// Der Zug beginnt am Vorlagenelement — hier wird nur noch festgelegt, dass
    /// nichts nach aussen geht: sortiert wird, nicht exportiert.
    ///
    /// <para>Ein Zug, den nicht <see cref="StarteZug"/> angestossen hat, wird
    /// abgebrochen. Es gibt keinen zweiten Anfang.</para>
    /// </summary>
    private void OnContactDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_zug is null)
        {
            args.Cancel = true;
            return;
        }

        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    /// <summary>
    /// Der Zeiger steht über der Liste, während gezogen wird (ADR-066).
    ///
    /// <para><b>Das Ablegeziel ist die Liste und nicht die Zeile</b> — und das
    /// ist gemessen. Am 14.09.2026 endete ein Zug ohne Ergebnis, und die Sonde
    /// hielt fest: <em>letztes Ziel Zeile, vor 703 ms</em>. Beim Loslassen
    /// stand der Zeiger über keinem Ablegeziel, weil zwischen den Zeilen totes
    /// Gebiet liegt — Abstände, Padding, der Streifen zwischen zwei Gruppen.
    /// Der Zug ging stumm verloren. Mit der Liste als Ziel gibt es dieses
    /// Gebiet nicht mehr.</para>
    ///
    /// <para>Nebenbei erledigt das den zweiten Befund desselben Tages: nach
    /// dem ersten Vorschauschritt liegt die <em>gezogene</em> Zeile unter dem
    /// Zeiger, und vier von fünf Drops kamen genau dort an. Solange die Zeile
    /// das Ziel war, musste sie sich selbst annehmen; jetzt stellt sich die
    /// Frage nicht mehr.</para>
    /// </summary>
    private void OnTeamListDragOver(object sender, DragEventArgs e)
    {
        if (_zug is null || sender is not ItemsControl liste)
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.Handled = true;

        var punkt = e.GetPosition(liste);

        // <b>Die Bremse gegen die Rückkopplung</b> (VorschauSchwelle): Bewegt
        // sich der Zeiger nicht, ändert sich die Vorschau nicht — auch dann
        // nicht, wenn das Layout unter ihm etwas anderes hergeschoben hat.
        if (_letzteVorschau is { } vorher)
        {
            var dx = punkt.X - vorher.X;
            var dy = punkt.Y - vorher.Y;

            if ((dx * dx) + (dy * dy) < VorschauSchwelle * VorschauSchwelle)
            {
                return;
            }
        }

        if (ZielStelle(liste, punkt) is not { } ziel)
        {
            return;
        }

        _letzteVorschau = punkt;
        _zug.MoveTo(ziel.Gruppe, ziel.Stelle);
    }

    /// <summary>
    /// Losgelassen über der Liste (ADR-066) — <b>hier wird nichts gerechnet</b>.
    ///
    /// <para>Die Vorschau steht schon, wo sie hingehört; das Loslassen sagt nur
    /// noch, dass sie gilt. Geschrieben wird am Ende von
    /// <see cref="StarteZug"/>, damit Zeile und Kachel denselben einen
    /// Abschluss haben.</para>
    /// </summary>
    private void OnTeamListDrop(object sender, DragEventArgs e)
    {
        if (_zug is null)
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
    }

    /// <summary>Der Zeiger steht über einem Gruppenkopf (ADR-066).</summary>
    ///
    /// <para>Der Kopf bleibt ein eigenes Ziel: bei einer <b>leeren</b> Gruppe
    /// ist er die einzige Stelle, die man überhaupt treffen kann — sie hat
    /// keine Zeile, an der sich eine Position ausrechnen liesse.</para>
    private void OnTeamGroupDragOver(object sender, DragEventArgs e)
    {
        if (_zug is null || sender is not FrameworkElement { DataContext: ContactGroupRow gruppe })
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = "Ganz oben in diese Gruppe";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;

        _letzteVorschau = null;
        _zug.MoveTo(gruppe.Name, 0);
    }

    /// <summary>Auf einem Gruppenkopf losgelassen (ADR-066).</summary>
    private void OnTeamGroupDrop(object sender, DragEventArgs e)
    {
        if (_zug is null)
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
    }

    /// <summary>
    /// Wohin die Zeile käme, wenn jetzt losgelassen würde — <b>aus der
    /// Zeigerposition</b> (ADR-066).
    ///
    /// <para>Gesucht wird die Zeile oder Kachel, die dem Zeiger am nächsten
    /// liegt; ihre Mitte entscheidet, ob es davor oder dahinter wird. In der
    /// Liste zählt dafür oben und unten, im Kachelraster links und rechts —
    /// dort steht die Nachbarin daneben.</para>
    ///
    /// <para><b>Über die Container und nicht über die Vorlagen:</b> Zeile wie
    /// Kachel sind für diese Rechnung dasselbe, sobald man sie als Rechteck
    /// nimmt. So gibt es einen Weg für beide Ansichten.</para>
    ///
    /// <para>Die Stelle zählt <b>ohne</b> die gezogene Zeile — dieselbe Zählung
    /// wie in <see cref="TeamDragPreview.MoveTo"/> und
    /// <see cref="TeamLayout.Move"/>. Steht sie in derselben Gruppe weiter
    /// oben, rückt die Zielstelle deshalb um eins zurück; ohne das driftete
    /// jeder Zug nach unten.</para>
    /// </summary>
    private (string Gruppe, int Stelle)? ZielStelle(ItemsControl liste, global::Windows.Foundation.Point punkt)
    {
        var waagrecht = liste is GridView;

        ContactGroupRow? besteGruppe = null;
        var besteStelle = 0;
        var besteEntfernung = double.MaxValue;

        foreach (var gruppe in ViewModel.TeamGroupRows)
        {
            for (var i = 0; i < gruppe.Rows.Count; i++)
            {
                if (liste.ContainerFromItem(gruppe.Rows[i]) is not FrameworkElement behaelter)
                {
                    // Virtualisiert und ausserhalb des Bildes — für einen
                    // Zeiger, der im Bild steht, nie das nächste Ziel.
                    continue;
                }

                var ecke = behaelter
                    .TransformToVisual(liste)
                    .TransformPoint(new global::Windows.Foundation.Point(0, 0));

                var breite = behaelter.ActualWidth;
                var hoehe = behaelter.ActualHeight;

                var dx = Abstand(punkt.X, ecke.X, breite);
                var dy = Abstand(punkt.Y, ecke.Y, hoehe);
                var entfernung = (dx * dx) + (dy * dy);

                if (entfernung >= besteEntfernung)
                {
                    continue;
                }

                besteEntfernung = entfernung;
                besteGruppe = gruppe;

                var dahinter = waagrecht
                    ? punkt.X > ecke.X + (breite / 2)
                    : punkt.Y > ecke.Y + (hoehe / 2);

                besteStelle = dahinter ? i + 1 : i;
            }
        }

        if (besteGruppe is null)
        {
            return null;
        }

        var eigen = besteGruppe.Rows.IndexOf(_zug!.Row);

        if (eigen >= 0 && eigen < besteStelle)
        {
            besteStelle--;
        }

        return (besteGruppe.Name, besteStelle);
    }

    /// <summary>Wie weit ein Wert neben einer Strecke liegt — null, wenn darin.</summary>
    private static double Abstand(double wert, double anfang, double laenge)
    {
        if (wert < anfang)
        {
            return anfang - wert;
        }

        var ende = anfang + laenge;

        return wert > ende ? wert - ende : 0;
    }

    /// <summary>
    /// „In Gruppe verschieben" aus dem Kontextmenü — der Weg ohne Maus.
    /// </summary>
    private void OnMoveToGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ContactRow row, Text: { Length: > 0 } gruppe })
        {
            ViewModel.MoveContactToGroupCommand.Execute(new ContactGroupMove(row, gruppe));
        }
    }

    /// <summary>
    /// Füllt das Untermenü „In Gruppe verschieben", wenn es aufgeht.
    ///
    /// <para><b>Beim Öffnen und nicht im XAML</b>: die Gruppen stehen erst zur
    /// Laufzeit fest, und ein <c>MenuFlyoutSubItem</c> nimmt keine
    /// <c>ItemsSource</c>. Dass es beim Öffnen geschieht, hat einen zweiten
    /// Grund — so steht dort immer die aktuelle Liste, auch wenn jemand
    /// nebenher eine Gruppe angelegt hat.</para>
    ///
    /// <para>Die eigene Gruppe fehlt in der Liste: sie wäre ein Eintrag, der
    /// nichts tut.</para>
    /// </summary>
    private void OnContactMenuOpening(object sender, object e)
    {
        // Der DataContext haengt am Element, auf dem das Flyout steht — die
        // Zeile selbst. Ueber das Flyout kommt man nur ueber Target dorthin.
        if (sender is not MenuFlyout flyout
            || flyout.Target is not FrameworkElement { DataContext: ContactRow row }
            || flyout.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault() is not { } menu)
        {
            return;
        }

        menu.Items.Clear();

        foreach (var gruppe in ViewModel.TeamGroupNames)
        {
            if (string.Equals(gruppe, row.Contact.Group, StringComparison.Ordinal))
            {
                continue;
            }

            var eintrag = new MenuFlyoutItem { Text = gruppe, Tag = row };

            eintrag.Click += OnMoveToGroupClick;

            menu.Items.Add(eintrag);
        }

        // Ein Untermenü ohne Einträge liesse sich aufklappen und wäre leer.
        menu.IsEnabled = menu.Items.Count > 0;
    }

    /// <summary>
    /// Zwei Listen haben zwei Auswahlen — zwei markierte Zeilen sähen aus wie
    /// ein Fehler. Die jeweils andere wird deshalb gelöst.
    ///
    /// <c>AddedItems.Count == 0</c> filtert den Fall heraus, dass eine Liste
    /// ihre Auswahl beim Austausch der Quelle von selbst verliert: dieselbe
    /// Falle wie bei der Kontoauswahl.
    /// </summary>
    private void OnContactSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || e.AddedItems.Count == 0)
        {
            return;
        }

        _syncingSelection = true;

        try
        {
            (ReferenceEquals(sender, TeamList) ? OutlookList : TeamList).SelectedIndex = -1;
        }
        finally
        {
            _syncingSelection = false;
        }

        // Der Detailbereich geht NUR aus diesem Zweig auf, also nur bei einer
        // echten Auswahl: die Quersynchronisation oben setzt der anderen Liste
        // SelectedIndex = -1 und feuert damit erneut SelectionChanged. Stuende
        // das Oeffnen ausserhalb, schlosse die Synchronisation den Bereich
        // sofort wieder.
        ViewModel.ToggleContactDetails(e.AddedItems[0] as ContactRow);
        UpdateContactDetails();
    }

    /// <summary>
    /// Haelt die aufgeklappte Zeile sichtbar (ADR-048).
    ///
    /// <para><b>Gezeichnet wird der Bereich nicht mehr hier.</b> Er steht in
    /// der Zeilenvorlage und haengt an <c>ContactRow.IsDetailExpanded</c>;
    /// <c>x:Load</c> erzeugt ihn beim Aufklappen und wirft ihn beim Zuklappen
    /// wieder weg. Bis zum 13.09.2026 stand er als ein Bereich unter der
    /// ganzen Liste und wurde hier von Hand gefuellt.</para>
    ///
    /// <para>Was bleibt, ist das Nachziehen des Bildlaufs: die Liste waechst
    /// unter der gewaehlten Zeile, und ohne das rutscht sie aus dem Blick.</para>
    /// </summary>
    private void UpdateContactDetails()
    {
        if (ViewModel.ExpandedContact is not { } row)
        {
            return;
        }

        // Die Zeile steht in genau einer der drei Listen. Welche es ist, sagt
        // ContainerFromItem — danach zu fragen ist billiger, als es aus der
        // Herkunft der Zeile abzuleiten, und es bleibt richtig, wenn eine
        // vierte Liste dazukommt.
        foreach (var liste in new ListViewBase[] { TeamList, OutlookList, SearchResultList })
        {
            if (liste.ContainerFromItem(row) is not null)
            {
                liste.ScrollIntoView(row);
                return;
            }
        }
    }

    /// <summary>Waehlt eine bestimmte Nummer aus dem Detailbereich.</summary>
    private void OnCallContactNumberClick(object sender, RoutedEventArgs e)
    {
        // Ueber einen Click-Handler und nicht x:Bind: aus einem DataTemplate
        // heraus bindet x:Bind auf das Element, nicht auf die Seite.
        if (sender is FrameworkElement { Tag: ContactNumberChoice choice })
        {
            ViewModel.CallNumberCommand.Execute(choice);
        }
    }

    /// <summary>
    /// Faengt den Doppelklick im aufgeklappten Teil einer Zeile ab (ADR-048).
    ///
    /// <para>Ohne das waehlte ein Doppelklick auf einen Nummernknopf
    /// zusaetzlich die Hauptnummer: Tapped laeuft nach aussen weiter und
    /// trifft dort <c>OnContactDoubleTapped</c>. Zweimal auf «Mobil» geklickt
    /// haette also das Festnetz gewaehlt — und gewaehlt wird bei einem Telefon
    /// sofort.</para>
    /// </summary>
    private void OnDetailDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        e.Handled = true;

    /// <summary>
    /// Klappt eine Team-Gruppe auf oder zu und merkt sich den Zustand.
    ///
    /// <para>Ueber <c>SaveViewState</c>: an <c>Changed</c> haengen fuenf
    /// Empfaenger bis hinunter zum Praesenz-Neuabo, und ein zugeklappter
    /// Abschnitt ist kein Grund, ins SDK zu greifen.</para>
    /// </summary>
    private void OnTeamGroupHeaderClick(object sender, RoutedEventArgs e)
    {
        // Was ein Klick auf den Kopf bedeutet, steht im Kern: der
        // Sortiermodus sperrt das Klappen, der Zustand wird gemerkt, und der
        // Umschalter kann dadurch die Gruppe wechseln (ADR-064).
        if (sender is not FrameworkElement { Tag: ContactGroupRow group })
        {
            return;
        }

        ViewModel.ToggleGroupExpansion(group);
    }

    /// <summary>
    /// §22.3: die Auswahl in der Anrufliste öffnet den Kontextbereich darunter.
    ///
    /// <para>An der Auswahl und nicht an einem eigenen Knopf: ein Klick auf die
    /// Zeile ist die Geste, die man ohnehin macht, und der Doppelklick bleibt
    /// dem Rückruf vorbehalten (§8.3). Wer nur zurückrufen will, klickt zweimal
    /// und hat den Bereich nebenbei gesehen; wer nachsehen will, klickt einmal
    /// und liest.</para>
    /// </summary>
    private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.ToggleHistoryDetails(HistoryList.SelectedItem as HistoryRow);
        UpdateHistoryContext();
    }

    /// <summary>
    /// Blendet den Kontextbereich ein, solange ein Eintrag gewählt ist, und
    /// beschriftet ihn mit dem Namen aus der Zeile.
    /// </summary>
    private void UpdateHistoryContext()
    {
        var row = ViewModel.ExpandedHistory;

        HistoryContextPanel.Visibility = row is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (row is null)
        {
            return;
        }

        // Der Bereich steht unter der Liste, nicht in der Zeile — die
        // Überschrift sagt deshalb, zu wem er gehört.
        HistoryContextTitle.Text = row.DisplayLabel;

        HistoryContextPlaceholderText.Visibility =
            ViewModel.HistoryContextPlaceholder.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateHistoryContextHeight();
    }

    /// <summary>
    /// Wie hoch der Kartenbereich höchstens werden darf — 60 % der Höhe des
    /// Bereichs (ADR-036).
    ///
    /// <para><b>Gerechnet und nicht festgeschrieben.</b> Vorher standen dort
    /// 220 Pixel: bei drei Feldern stand der Kasten halb leer, bei zehn
    /// scrollte er, während darüber Platz frei war. Und ein fester Wert wäre
    /// bei 150 % Skalierung zusätzlich falsch — hier wird auf
    /// <c>ActualHeight</c> gerechnet, also in logischen Pixeln, und die
    /// skalieren mit.</para>
    ///
    /// <para>Die Liste behält ihre <c>MinHeight</c>; der Bereich nimmt sich
    /// nur, was übrig bleibt.</para>
    /// </summary>
    private void UpdateHistoryContextHeight()
    {
        var verfuegbar = HistoryPanel.ActualHeight;

        if (verfuegbar <= 0)
        {
            return;
        }

        HistoryContextPanel.MaxHeight = Math.Max(120, verfuegbar * 0.6);
    }

    private void OnHistoryPanelSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateHistoryContextHeight();

    /// <summary>Schliesst den Bereich über das Kreuz — und hebt die Auswahl auf.</summary>
    private void OnCloseHistoryContextClick(object sender, RoutedEventArgs e)
    {
        // Erst die Auswahl: SelectionChanged ruft ToggleHistoryDetails mit
        // null und schliesst damit ohnehin. Ohne das bliebe die Zeile markiert,
        // und der nächste Klick auf dieselbe Zeile täte nichts.
        HistoryList.SelectedItem = null;

        ViewModel.CollapseHistoryDetails();
        UpdateHistoryContext();
    }

    /// <summary>§8.3: alles Offene als gesehen vermerken.</summary>
    private void OnMarkAllSeenClick(object sender, RoutedEventArgs e) =>
        ViewModel.MarkAllHistorySeenCommand.Execute(null);

    /// <summary>
    /// Eine Schaltfläche oder ein Verweis auf der Karte in der Anrufliste.
    ///
    /// Wortgleich zur Gesprächsansicht — die Karte kann dieselben Aktionen
    /// tragen, und was sie tun, hat die Engine längst entschieden (die Adresse
    /// ist dort bereits auf http/https geprüft).
    /// </summary>
    private async void OnHistoryCardAction(object? sender, CardResolvedAction action)
    {
        try
        {
            switch (action)
            {
                case CardOpenUrl open:
                    await global::Windows.System.Launcher.LaunchUriAsync(open.Target);
                    break;

                case CardDial dial:
                    ViewModel.DialedNumber = dial.Number;
                    await ViewModel.DialCommand.ExecuteAsync(null);
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
            // async void: eine Ausnahme von hier beendete die Anwendung, und
            // eine Karte darf ein Telefon nicht mitnehmen.
            ViewModel.Hint = UserMessage.WithCause("Die Aktion liess sich nicht ausführen.", ex);
        }
    }

    /// <summary>
    /// Menütaste und Umschalt+F10 öffnen das Kontextmenü der markierten Zeile
    /// (ADR-044).
    ///
    /// <para><b>Warum das nötig ist.</b> Die Kontextmenüs hängen am Inhalt der
    /// Zeilenvorlage und nicht am <c>ListViewItem</c> — anders geht es nicht,
    /// weil nur dort der Eintrag als <c>DataContext</c> steht. Bei
    /// Tastaturbedienung liegt der Fokus aber auf dem <c>ListViewItem</c>, und
    /// dort findet sich kein Flyout: die Menütaste griff ins Leere. Damit war
    /// „Anrufen", „In Gruppe verschieben" und „Zurückrufen" ohne Maus
    /// unerreichbar — während der Kommentar an der Gruppenverschiebung
    /// ausdrücklich das Gegenteil behauptete.</para>
    ///
    /// <para><b>Nur bei der Tastatur.</b> <c>TryGetPosition</c> liefert bei
    /// Maus und Finger einen Punkt, bei der Tastatur nicht — das ist der
    /// dokumentierte Unterschied. Bei Maus und Finger trifft der Zeiger den
    /// Inhalt, WinUI zeigt das Flyout selbst und meldet das Ereignis als
    /// behandelt; hier käme es dann gar nicht erst an.</para>
    /// </summary>
    private static void OnListContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(sender, out _)
            || args.OriginalSource is not DependencyObject quelle)
        {
            return;
        }

        // SelectorItem und nicht ListViewItem: eine Kachel steckt in einem
        // GridViewItem, und die beiden sind nur ueber diesen Basistyp
        // verwandt (ADR-047).
        if (FindAncestor<SelectorItem>(quelle) is not { } eintrag
            || FindFlyoutOwner(eintrag) is not { } traeger
            || traeger.ContextFlyout is not { } flyout)
        {
            return;
        }

        flyout.ShowAt(traeger);
        args.Handled = true;
    }

    /// <summary>
    /// Die Eingabetaste ruft an — in beiden Kontaktlisten und in der
    /// Trefferliste (ADR-044).
    ///
    /// <para>Dieselbe Handlung wie der Doppelklick, und aus demselben Grund
    /// über den <c>DataContext</c> der Zeile statt über die Auswahl der
    /// Liste.</para>
    /// </summary>
    private void OnContactListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // ListViewBase und nicht ListView: das Kachelraster ist ein GridView
        // und braucht dieselbe Taste (ADR-047).
        if (e.Key != VirtualKey.Enter
            || sender is not ListViewBase { SelectedItem: ContactRow row } liste)
        {
            return;
        }

        var element = liste.ContainerFromItem(row) as FrameworkElement ?? liste;

        CallOrAsk(row, element);
        e.Handled = true;
    }

    /// <summary>Die Eingabetaste ruft zurück — wie der Doppelklick (ADR-044).</summary>
    private void OnHistoryListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || HistoryList.SelectedItem is not HistoryRow row)
        {
            return;
        }

        ViewModel.CallBackCommand.Execute(row);
        e.Handled = true;
    }

    /// <summary>Der nächste Vorfahr einer Art im visuellen Baum.</summary>
    private static T? FindAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        for (var knoten = start; knoten is not null; knoten = VisualTreeHelper.GetParent(knoten))
        {
            if (knoten is T treffer)
            {
                return treffer;
            }
        }

        return null;
    }

    /// <summary>
    /// Das erste Element unterhalb der Zeile, das ein Kontextmenü trägt.
    ///
    /// <para>Gesucht wird, statt den Aufbau der Vorlage zu kennen: die
    /// Team-Zeile trägt seit ADR-042 einen Detailbereich und hat deshalb ein
    /// <c>StackPanel</c> als Wurzel, die Outlook-Zeile das Raster selbst. Wer
    /// hier einen festen Pfad hinterlegt, hat ihn beim nächsten Umbau der
    /// Vorlage vergessen.</para>
    /// </summary>
    private static FrameworkElement? FindFlyoutOwner(DependencyObject wurzel)
    {
        if (wurzel is FrameworkElement { ContextFlyout: not null } treffer)
        {
            return treffer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wurzel); i++)
        {
            if (FindFlyoutOwner(VisualTreeHelper.GetChild(wurzel, i)) is { } gefunden)
            {
                return gefunden;
            }
        }

        return null;
    }

    private void OnHistoryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // §8.3: Rückruf per Doppelklick.
        if (HistoryList.SelectedItem is HistoryRow row)
        {
            ViewModel.CallBackCommand.Execute(row);
        }
    }

    /// <summary>
    /// Ob die Anrufliste gerade eingeschraenkt ist — durch einen Filter oder
    /// eine Suche. Entscheidet, welcher Leerzustand stimmt.
    /// </summary>
    private bool HistoryIsFiltered() =>
        ViewModel.HistoryFilter != CallHistoryFilter.All
        || !string.IsNullOrWhiteSpace(ViewModel.HistorySearch);

    private void Refresh()
    {
        UpdateHistoryContext();

        // Kontoauswahl (§20.2)
        //
        // ADR-045: Eine unvollstaendige Installation sah aus wie „noch kein
        // Konto". App.SdkStatus wurde beim Start gesetzt und als oeffentliche
        // Eigenschaft angeboten — gelesen hat sie keine Ansicht, und die
        // fertig formulierte Meldung aus SipErrorCatalog erreichte den
        // Benutzer erst, wenn er erfolglos ein Konto anzulegen versuchte.
        // Zwei grundverschiedene Ursachen, ein Bildschirm.
        if (((App)Application.Current).SdkStatus is { IsComplete: false } sdk)
        {
            NoAccountBar.Message = Nipp.Core.Services.Telephony.SipErrorCatalog.DescribeSdkUnavailable(sdk);
            NoAccountBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
            NoAccountBar.ActionButton = null;
            NoAccountBar.IsOpen = true;
        }
        else
        {
            NoAccountBar.IsOpen = !ViewModel.HasAccounts;
        }

        AccountBox.Visibility = ViewModel.HasAccounts ? Visibility.Visible : Visibility.Collapsed;

        if (ViewModel.SelectedAccount is { } account
            && !ReferenceEquals(AccountBox.SelectedItem, account))
        {
            AccountBox.SelectedItem = account;
        }

        // Das × erscheint nur, wenn etwas zu löschen ist (§20.1)
        ClearButton.Visibility = ViewModel.CanClear ? Visibility.Visible : Visibility.Collapsed;

        // Der Pfeil für die zuletzt gewählten Nummern steht dort, wo sonst das
        // Löschkreuz steht — die beiden schliessen sich aus. Bei gefülltem Feld
        // gibt es nichts zu wiederholen, bei leerem nichts zu löschen, und so
        // bleiben es rechts im Feld immer zwei Symbole statt drei.
        RecentButton.Visibility = ViewModel.CanClear ? Visibility.Collapsed : Visibility.Visible;

        // Platz für das × freihalten, damit die Nummer nicht darunter läuft
        NumberBox.Padding = ViewModel.CanClear
            ? new Thickness(12, 6, 40, 6)
            : new Thickness(12, 6, 12, 6);

        // Die Zeile unter dem Feld trägt zweierlei, und beides schliesst sich
        // aus (ADR-046): bei gefülltem Feld die normalisierte Form (§8.1), bei
        // leerem den Hinweis auf die Quellen, die mitgefragt werden.
        //
        // Der Hinweis stand zuerst im Platzhalter und war dort abgeschnitten —
        // das Feld trägt die Rufnummer in Schriftgrösse 20 und hält rechts
        // 76 Pixel für zwei Symbole frei.
        if (ViewModel.NormalizedPreview is { Length: > 0 } preview)
        {
            PreviewText.Text = $"wählt {preview}";
            PreviewText.Visibility = Visibility.Visible;
        }
        else if (ViewModel.DialedNumber.Length == 0
            && ViewModel.ContactSearchHint is { Length: > 0 } hinweis)
        {
            PreviewText.Text = hinweis;
            PreviewText.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewText.Visibility = Visibility.Collapsed;
        }

        // AP4.3: Vorschläge nur, wenn es welche gibt — und nur, solange nicht
        // die Trefferliste steht (C5).
        //
        // <b>Bis zum 13.09.2026 standen beide gleichzeitig</b>, mit denselben
        // Leuten darin: UpdateSuggestions ruft ContactStore.Search, und der
        // LocalSnapshotSearchProvider der Trefferliste ruft dieselbe Methode.
        // Derselbe Kollege stand zweimal auf dem Bildschirm, mit zwei
        // Bedeutungen für dieselbe Geste — oben übernimmt ein Klick die
        // Nummer, unten wählt ein Doppelklick.
        SuggestionPanel.Visibility = ViewModel.HasSuggestions && !ViewModel.ShowSearchResults
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Inhaltsbereich — genau einer ist sichtbar (§20.1)
        var section = ViewModel.Section;

        // Wähltastatur (§20.1) — nur im Kontakte-Tab (ADR-026).
        //
        // In der Anrufliste nahm sie rund 200 Pixel und damit
        // ein Drittel des Fensters, ohne dort gebraucht zu werden: der Liste
        // blieben vier statt acht Zeilen. Das Nummernfeld steht weiterhin über
        // allen Abschnitten, gewählt werden kann also von überall.
        //
        // <b>Der gespeicherte Zustand bleibt unberührt.</b> Er ist die Absicht
        // des Benutzers; hier wird nur entschieden, ob sie gerade sichtbar ist.
        // Beim Zurückkommen in die Kontakte steht die Tastatur deshalb wieder
        // so, wie sie verlassen wurde — und ein Neustart ändert daran nichts.
        DialKeypad.Visibility = ViewModel.IsDialpadVisible && section == ShellSection.Contacts
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Der Umschalter gehört zur Tastatur und verschwindet mit ihr: ein
        // Knopf, der etwas ein- und ausblendet, das gar nicht da sein kann,
        // ist eine Zusage, die nicht eingehalten wird.
        DialpadToggle.Visibility = section == ShellSection.Contacts
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Der Filterkopf bleibt sichtbar, auch wenn die Liste leer ist —
        // sonst kommt man aus einer erfolglosen Suche nicht mehr heraus.
        HistoryPanel.Visibility = section == ShellSection.History
            ? Visibility.Visible
            : Visibility.Collapsed;

        HistoryList.Visibility = section == ShellSection.History && ViewModel.History.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        ContactsPanel.Visibility = section == ShellSection.Contacts
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Der Platzhalter erscheint nur, wenn der gewählte Bereich nichts zu
        // zeigen hat. Eine leere Liste ohne Erklärung sieht aus wie ein Fehler.
        var placeholder = section switch
        {
            // Zwischen „es gab noch nie Anrufe" und „dieser Filter trifft
            // keinen" unterscheiden: die Liste ist in beiden Faellen leer, aber
            // „Noch keine Anrufe." ist bei gesetztem Filter schlicht falsch —
            // und wer gerade auf «Verpasst» geklickt hat, glaubt es ihr auch
            // nicht.
            ShellSection.History when ViewModel.History.Count == 0 && HistoryIsFiltered() =>
                ("", "Keine passenden Anrufe."
                    + Environment.NewLine + "Ein anderer Filter oder ein anderer Suchbegriff"
                    + Environment.NewLine + "zeigt vielleicht mehr."),
            ShellSection.History when ViewModel.History.Count == 0 =>
                ("", "Noch keine Anrufe."),
            ShellSection.Contacts when !ViewModel.HasContacts && ViewModel.IsLoadingContacts =>
                ("", "Kontakte werden eingelesen …"),
            ShellSection.Contacts when !ViewModel.HasContacts =>
                ("", "Keine Kontakte."
                    + Environment.NewLine + "Team-Nebenstellen stehen in den Einstellungen,"
                    + Environment.NewLine + "persönliche Kontakte kommen aus Outlook."),
            _ => (string.Empty, string.Empty),
        };

        if (placeholder.Item2.Length > 0)
        {
            // Beim Laden dreht sich ein Ring statt eines Symbols — ein
            // stehendes Symbol mit „wird eingelesen" sieht nach hängender App
            // aus, und Outlook braucht dafür regelmässig drei Sekunden.
            var loading = ViewModel.IsLoadingContacts && section == ShellSection.Contacts;

            PlaceholderRing.IsActive = loading;
            PlaceholderRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            PlaceholderIcon.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;

            PlaceholderIcon.Glyph = placeholder.Item1;
            PlaceholderText.Text = placeholder.Item2;
            PlaceholderPanel.Visibility = Visibility.Visible;

            // Bei den Kontakten führt „neu einlesen" aus dem leeren Zustand
            // heraus — der Knopf im Kopf des Outlook-Abschnitts ist dann nicht
            // da, weil es den Abschnitt nicht gibt. Beim Laden nicht: dann
            // läuft schon einer.
            if (section == ShellSection.Contacts)
            {
                ContactsPanel.Visibility = Visibility.Visible;
                ContactsBody.Visibility = Visibility.Collapsed;

                PlaceholderReloadButton.Visibility = ViewModel.IsLoadingContacts
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                PlaceholderTeamButton.Visibility = ViewModel.IsLoadingContacts
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            else
            {
                PlaceholderReloadButton.Visibility = Visibility.Collapsed;
                PlaceholderTeamButton.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            PlaceholderRing.IsActive = false;
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            PlaceholderReloadButton.Visibility = Visibility.Collapsed;
            PlaceholderTeamButton.Visibility = Visibility.Collapsed;
            ContactsBody.Visibility = Visibility.Visible;
        }

        RefreshContactSections();
        ApplyLayout();
        ApplyReorderMode();
        RefreshSearch();

        // Abzeichen in der Umschaltleiste — und die Zahl in den Namen
        // (ADR-046). Das Abzeichen setzte nur seinen Wert; der Name der
        // Umschaltflaeche blieb «Anrufe», und „drei verpasste Anrufe" kam bei
        // der Sprachausgabe nie an.
        SetBadge(MissedBadge, ViewModel.MissedCount);

        AutomationProperties.SetName(HistoryTab, BadgeName("Anrufe", ViewModel.MissedCount, "verpasst"));

        // Welcher Bereich aktiv ist, war vorher nicht zu sehen: gleich
        // aussehende Schaltflaechen, der Zustand nur am Inhalt zu erraten.
        ContactsTab.IsChecked = section == ShellSection.Contacts;
        HistoryTab.IsChecked = section == ShellSection.History;
        SettingsTab.IsChecked = false;

        // §8.4: Anmeldezustand als Text, im Fehlerfall hervorgehoben.
        AccountStateText.Text = ViewModel.AccountStateText;
        AccountStateText.Visibility = ViewModel.AccountStateText.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FixAccountLink.Visibility = ViewModel.AccountNeedsFixing
            ? Visibility.Visible
            : Visibility.Collapsed;

        AccountStateText.Foreground = Brush(ViewModel.AccountStateIsProblem
            ? "StatusFailedBrush"
            : "CardSecondaryTextBrush");

        // ADR-045: Warum „Anrufen" grau ist. Als Kurzinfo UND als Hilfetext —
        // ein Tooltip ist mit der Tastatur nicht erreichbar, und wer den Knopf
        // per Sprachausgabe erreicht, bekommt sonst „Anrufen, nicht verfuegbar"
        // ohne jeden Grund.
        var dialHint = ViewModel.DialIssue ?? "Anrufen";

        ToolTipService.SetToolTip(DialButton, dialHint);
        AutomationProperties.SetHelpText(DialButton, ViewModel.DialIssue ?? string.Empty);

        // Zurueck ins laufende Gespraech (§8.2)
        ActiveCallBar.Visibility = ViewModel.HasActiveCall ? Visibility.Visible : Visibility.Collapsed;
        ActiveCallText.Text = ViewModel.ActiveCallLabel;

        // Fehlermeldung (§15)
        if (ViewModel.LastError is { Length: > 0 } error)
        {
            ErrorBar.Message = error;
            ErrorBar.IsOpen = true;
        }
        else
        {
            ErrorBar.IsOpen = false;
        }

        // Hinweis, der kein Fehler ist (§9.4): gewechseltes Audiogerät
        if (ViewModel.Hint is { Length: > 0 } hint)
        {
            HintBar.Message = hint;
            HintBar.IsOpen = true;
        }
        else
        {
            HintBar.IsOpen = false;
        }

        // <b>Zuletzt</b>, und das ist keine Geschmacksfrage: ApplyCallView
        // blendet die ganze linke Spalte aus, und alles darueber setzt
        // Sichtbarkeiten darin. Stuende es vorn, machte der Rest von Refresh
        // Teile davon wieder sichtbar.
        ApplyCallView();
    }

    /// <summary>
    /// Ob das laufende Gespraech gerade <b>in</b> der linken Spalte steht.
    /// <c>null</c> heisst „noch nie entschieden".
    /// </summary>
    private bool? _gespraechEingebettet;

    /// <summary>
    /// Zeigt ein laufendes Gespraech im breiten Layout in der linken Spalte —
    /// waehrend rechts die Nebenstellen mit ihren Lampen stehen bleiben.
    ///
    /// <para><b>Die eine Stelle, die es entscheidet</b>, und sie entscheidet
    /// aus zwei Eigenschaften des ViewModels: <c>IsWide</c> (und das gehoert
    /// laut ADR-047 ganz <c>ApplyWidth</c>) und <c>HasActiveCall</c>. Im
    /// schmalen Layout aendert sich nichts — dort navigiert <c>MainWindow</c>
    /// wie bisher auf die ganze Seite.</para>
    ///
    /// <para><b>Warum die Elemente einzeln ausgeblendet werden und nicht ein
    /// Behaelter um sie herum.</b> Ein Behaelter waere strukturell besser: ein
    /// Ding, das man ausblendet, statt einer Schleife, die alle findet. Er
    /// bedeutet aber, rund 1 270 Zeilen XAML umzuhaengen, und das Ergebnis
    /// sieht man erst am laufenden Fenster. <b>Der Umbau gehoert an einen
    /// Tag, an dem jemand davorsitzt</b> — bis dahin sammelt die Schleife
    /// zuverlaessig auch Elemente ein, die spaeter dazukommen: sie fragt die
    /// Spalte ab und nicht eine Namensliste.</para>
    ///
    /// <para><b>Das Wiederherstellen laeuft ueber <see cref="Refresh"/></b> und
    /// nicht ueber gemerkte Sichtbarkeiten. Zwei Elemente haben eine eigene
    /// Regel — die Kontoauswahl haengt an <c>HasAccounts</c>, die Waehltastatur
    /// am Abschnitt —, und ein gemerkter Zustand waere beim naechsten
    /// hinzukommenden Element wieder unvollstaendig. Der Rekursionsschutz ist
    /// der Vergleich oben: beim zweiten Durchlauf steht der Zustand schon.</para>
    /// </summary>
    private void ApplyCallView()
    {
        var eingebettet = ViewModel.IsWide && ViewModel.HasActiveCall;

        if (_gespraechEingebettet == eingebettet)
        {
            return;
        }

        _gespraechEingebettet = eingebettet;

        LinkeSpalteAnzeigen(!eingebettet);

        if (eingebettet)
        {
            CallFrame.Visibility = Visibility.Visible;

            // Der Parameter sagt der Seite, dass sie eingebettet ist: ihr
            // Zurueck-Pfeil haette hier keine Bedeutung — und er wuerde in
            // diesen Frame navigieren, also die Shell in sich selbst.
            CallFrame.Navigate(typeof(ActiveCallPage), ActiveCallPage.Eingebettet);
            return;
        }

        CallFrame.Content = null;
        CallFrame.Visibility = Visibility.Collapsed;

        // <b>Schmal geworden, waehrend das Gespraech laeuft.</b> Dann gehoert
        // es auf die ganze Seite — und niemand sonst merkt diesen Wechsel:
        // MainWindow navigiert nur, wenn ein Gespraech beginnt oder endet,
        // nicht, wenn jemand das Fenster schmaler zieht.
        //
        // <b>Das Gespraech darf davon nichts merken</b> (T314). Es haengt am
        // SipService und am ActiveCallViewModel, nicht an der Seite; beide
        // sind Singletons und ueberleben die Navigation.
        if (ViewModel.HasActiveCall)
        {
            Frame.Navigate(typeof(ActiveCallPage));
            return;
        }

        // Die beiden Elemente mit eigener Sichtbarkeitsregel stehen danach
        // richtig, ohne dass diese Stelle sie kennen muss.
        Refresh();
    }

    /// <summary>
    /// Blendet die Inhalte der linken Spalte aus oder wieder ein — <b>ueber die
    /// Spalte und nicht ueber eine Namensliste</b>, damit ein spaeter
    /// hinzugefuegtes Element nicht vergessen wird.
    ///
    /// <para><b>Zwei Ausnahmen, und beide sind begruendet:</b> der
    /// Gespraechsrahmen selbst, und die Meldungszeile. Eine Fehlermeldung
    /// gehoert gesehen, gerade im Gespraech — wer eine dritte Nebenstelle
    /// anklickt, bekommt die Ablehnung aus §8.2, und ohne diese Ausnahme
    /// passierte scheinbar nichts. Dasselbe gilt fuer den Hinweis, dass ein
    /// Audiogeraet gewechselt hat: im Gespraech ist er <i>wichtiger</i> als
    /// sonst.</para>
    /// </summary>
    private void LinkeSpalteAnzeigen(bool sichtbar)
    {
        var wert = sichtbar ? Visibility.Visible : Visibility.Collapsed;

        foreach (var kind in RootGrid.Children)
        {
            if (kind is FrameworkElement element
                && !ReferenceEquals(element, CallFrame)
                && !ReferenceEquals(element, MessagePanel)
                && Grid.GetColumn(element) == 0)
            {
                element.Visibility = wert;
            }
        }
    }

    /// <summary>
    /// Der Name einer Umschaltflaeche mit Abzeichen — mit Zahl, wenn eine da
    /// ist (ADR-046).
    /// </summary>
    private static string BadgeName(string basis, int anzahl, string was) =>
        anzahl > 0 ? $"{basis}, {anzahl} {was}" : basis;

    private static void SetBadge(InfoBadge badge, int count)
    {
        badge.Value = count;
        badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Ein Pinsel aus den Anwendungsressourcen.
    ///
    /// Die Farbe stimmt auch bei abweichender Themenwahl (§20.4), weil
    /// <c>ThemeService</c> die Pinselinstanzen beim Wechsel umfärbt — die
    /// Ressourcenauflösung auf Anwendungsebene würde dem Systemthema folgen.
    /// </summary>
    private static Microsoft.UI.Xaml.Media.Brush Brush(string key) =>
        Nipp.App.Converters.ThemeBrushes.Get(key);
}
