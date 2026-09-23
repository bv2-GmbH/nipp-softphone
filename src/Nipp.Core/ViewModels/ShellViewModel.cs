using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Die Hauptansicht im Smartphone-Format (§20.1).
///
/// Aufbau von oben nach unten, wie §20.1 es vorgibt:
/// <list type="number">
///   <item>Kontoauswahl mit Status-LED (§20.2)</item>
///   <item>Eingabefeld mit ×</item>
///   <item>Wählen-Schaltfläche</item>
///   <item>Wähltastatur, ein- und ausblendbar</item>
///   <item>Inhalt: Kontakte oder Anrufliste</item>
///   <item>Umschaltleiste</item>
/// </list>
///
/// §6: kennt keine SDK-Typen.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly ISipService _sip;
    private readonly CallHistoryStore _history;
    private readonly ContactStore _contactStore;
    private readonly CallPartyResolver _party;
    private readonly BlfService _blf;

    /// <summary>
    /// Der Anruferkontext — für den Aufklappbereich der Anrufliste (§22.3).
    ///
    /// <b>Optional.</b> Ohne eingerichtete Quellen gibt es ihn nicht, und die
    /// Anrufliste funktioniert unverändert: Telefonieren hängt von keiner
    /// Integration ab (§21.2).
    /// </summary>
    private readonly Nipp.Core.Services.Integrations.Context.CallerContextService? _callerContext;
    private readonly SettingsService _settings;
    private readonly PolicyService _policy;
    private readonly ILogger<ShellViewModel> _logger;

    /// <summary>
    /// Die Suche über mehrere Quellen (§21.4). Der Teil des ViewModels, der
    /// sie bedient, steht in <c>ShellViewModel.Search.cs</c>.
    ///
    /// <b>Optional</b>, damit die bestehenden Tests dieses ViewModels
    /// unverändert weiterlaufen: sie bauen es von Hand, und keiner von ihnen
    /// hat mit der Suche zu tun. Ohne Dienst gibt es kein Suchfeld —
    /// <c>HasContactSearch</c> ist dann falsch.
    /// </summary>
    private readonly Nipp.Core.Services.Integrations.Search.ContactSearchService? _search;

    /// <summary>
    /// Welche Karte für den aufgeklappten Eintrag gilt (ADR-036).
    ///
    /// <b>Optional</b> aus demselben Grund wie die beiden darüber: die
    /// bestehenden Tests bauen dieses ViewModel von Hand. Ohne Resolver bleibt
    /// der Bereich leer statt falsch — und die Anrufliste selbst hängt an
    /// keiner Karte.
    /// </summary>
    private readonly Nipp.Core.Services.Integrations.Cards.CardResolver? _cards;

    private NumberNormalizer _normalizer;
    private bool _disposed;

    /// <summary>
    /// Der Kontext, in dem dieses Modell entstanden ist — der UI-Thread.
    ///
    /// Gebraucht, weil <see cref="ContactStore"/> im Hintergrund laedt und
    /// sein Ereignis dort ausloest. Eine <c>ObservableCollection</c> von einem
    /// anderen Thread zu aendern wirft eine COMException, deren Meldung leer
    /// ist — im ersten Start genau so aufgetreten und aus dem Protokoll allein
    /// nicht zu deuten.
    ///
    /// <c>SynchronizationContext</c> statt <c>DispatcherQueue</c>: das hier ist
    /// Nipp.Core und bleibt ohne UI-Abhaengigkeit (§6).
    /// </summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    [ObservableProperty]
    private string _dialedNumber = string.Empty;

    [ObservableProperty]
    private string _normalizedPreview = string.Empty;

    [ObservableProperty]
    private AccountStatus? _selectedAccount;

    [ObservableProperty]
    private ShellSection _section = ShellSection.Contacts;

    [ObservableProperty]
    private bool _isDialpadVisible = true;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>
    /// Ein Hinweis, der kein Fehler ist — etwa ein gewechseltes Audiogerät.
    ///
    /// Getrennt von <see cref="LastError"/>, weil beides unterschiedlich
    /// aussehen muss: ein Fehler verlangt eine Handlung, ein Hinweis erklärt
    /// nur, was gerade von selbst geschehen ist.
    /// </summary>
    [ObservableProperty]
    private string? _hint;

    [ObservableProperty]
    private int _missedCount;

    [ObservableProperty]
    private bool _isLoadingContacts;

    /// <summary>
    /// Der Anmeldezustand als <b>Text</b> (§8.4: nie nur über Farbe).
    ///
    /// Stand bisher nur in den Einstellungen. In der Hauptansicht gab es
    /// ausschliesslich die achtpixelgrosse Lampe — wer abgemeldet war, sah
    /// keinen Grund und keine Abhilfe, obwohl <c>SipErrorCatalog</c> beides
    /// längst formuliert hatte.
    /// </summary>
    [ObservableProperty]
    private string _accountStateText = string.Empty;

    /// <summary>
    /// Ob der Anmeldezustand ein Problem beschreibt — dann wird er in der
    /// Oberfläche hervorgehoben statt beiläufig angezeigt.
    /// </summary>
    [ObservableProperty]
    private bool _accountStateIsProblem;

    /// <summary>
    /// Beschriftung der Gesprächsleiste, wenn ein Gespräch läuft, während die
    /// Hauptansicht sichtbar ist (§20.1, §8.2).
    ///
    /// <b>Warum es die Leiste gibt.</b> Ein laufendes Gespräch belegte die
    /// ganze Ansicht, und aus der Gesprächsansicht führte kein Weg zurück zur
    /// Wähltastatur — damit waren ein zweiter Anruf, das Makeln und die
    /// begleitete Übergabe im Programm unerreichbar, obwohl der Dienst sie
    /// beherrscht. Jetzt lässt sich das Gespräch verlassen und über diese
    /// Leiste wiederfinden.
    /// </summary>
    [ObservableProperty]
    private string _activeCallLabel = string.Empty;

    [ObservableProperty]
    private bool _hasActiveCall;

    /// <summary>Filter der Anrufliste (§8.3). „Alle" ist der Ausgangszustand.</summary>
    [ObservableProperty]
    private CallHistoryFilter _historyFilter = CallHistoryFilter.All;

    /// <summary>Die Konten für die Auswahl oben (§20.1, §20.2).</summary>
    public ObservableCollection<AccountStatus> Accounts { get; } = [];

    /// <summary>
    /// Die Anrufliste, nach §20.3 auf das Wichtigste beschränkt.
    ///
    /// <para>Zeilen und nicht Einträge: der „gesehen"-Zustand ändert sich,
    /// während die Zeile ausgewählt ist — die Begründung steht an
    /// <see cref="HistoryRow"/>.</para>
    /// </summary>
    public ObservableCollection<HistoryRow> History { get; } = [];

    /// <summary>
    /// Die Team-Nebenstellen (§8.4).
    ///
    /// <b>Eine eigene Sammlung und nicht ein Ausschnitt aus einer gemeinsamen.</b>
    /// Die Liste in der Oberfläche sortiert sie um, und dafür muss sie eine
    /// veränderliche Sammlung sein, die die Liste selbst anfassen darf. Eine
    /// gefilterte Sicht darauf würde beim Loslassen kommentarlos
    /// zurückspringen.
    /// </summary>
    public ObservableCollection<ContactRow> TeamContacts { get; } = [];

    /// <summary>
    /// Die Kontakte aus Outlook, alphabetisch (§8.4: beide Quellen getrennt
    /// sichtbar). Werden nie umsortiert — dort gibt es nichts zu entscheiden.
    /// </summary>
    public ObservableCollection<ContactRow> OutlookContacts { get; } = [];

    /// <summary>
    /// Dieselben Team-Zeilen, nach Gruppen geordnet (ADR-041) — die Quelle der
    /// gruppierten Liste.
    ///
    /// <b>Eine Sicht und keine zweite Wahrheit:</b> die Zeilen darin sind die
    /// Instanzen aus <see cref="TeamContacts"/>, und die Reihenfolge ist
    /// dieselbe, weil die gespeicherte Liste blockweise nach Gruppen sortiert
    /// ist (<c>TeamGroups.Normalize</c>).
    /// </summary>
    public ObservableCollection<ContactGroupRow> TeamGroupRows { get; } = [];

    /// <summary>
    /// Die Namen der Gruppen — für das Kontextmenü „In Gruppe verschieben".
    ///
    /// <b>Ziehen ist für eine Sprachausgabe kein Weg</b>, und für jemanden ohne
    /// Maus auch nicht. Das Menü ist deshalb kein Rückfall, sondern der zweite
    /// gleichwertige Weg (ADR-042).
    /// </summary>
    public ObservableCollection<string> TeamGroupNames { get; } = [];

    /// <summary>Überschrift des Outlook-Abschnitts.</summary>
    public string OutlookHeader => $"Outlook ({OutlookContacts.Count})";

    /// <summary>Ob überhaupt ein Kontakt da ist — für den Platzhalter.</summary>
    public bool HasContacts => TeamContacts.Count > 0 || OutlookContacts.Count > 0;

    /// <summary>
    /// Warum der Outlook-Abschnitt leer ist, als fertiger Satz — oder
    /// <c>null</c>.
    ///
    /// <para>§8.4 verlangt „eine verständliche Anzeige statt leerer Liste".
    /// Bisher stand der Grund nur im Protokoll, und in der Oberfläche las man
    /// «Outlook (0)» — das sieht aus, als sei nipp defekt. Auf diesem Gerät
    /// läuft das neue Outlook, das keine Kontakte für andere Programme
    /// bereitstellt; ohne diesen Hinweis sucht man den Fehler bei nipp.</para>
    /// </summary>
    public string? OutlookHint => _contactStore.OutlookHint;

    /// <summary>Ob es einen Grund zu zeigen gibt.</summary>
    public bool HasOutlookHint => !string.IsNullOrWhiteSpace(OutlookHint);

    /// <summary>
    /// Vorschläge zur Eingabe (AP4.3): höchstens fünf, aus Kontakten und
    /// Anrufliste.
    ///
    /// Fünf ist keine runde Zahl, sondern die, ab der eine Liste unter dem
    /// Eingabefeld mehr verdeckt als sie hilft — sie schiebt sich über die
    /// Wähltastatur.
    /// </summary>
    public ObservableCollection<DialSuggestion> Suggestions { get; } = [];

    public ShellViewModel(
        ISipService sip,
        CallHistoryStore history,
        ContactStore contacts,
        CallPartyResolver party,
        BlfService blf,
        SettingsService settings,
        PolicyService policy,
        ILogger<ShellViewModel> logger,
        Nipp.Core.Services.Integrations.Search.ContactSearchService? search = null,
        Nipp.Core.Services.Integrations.Context.CallerContextService? callerContext = null,
        Nipp.Core.Services.Integrations.Cards.CardResolver? cards = null)
    {
        _sip = sip;
        _history = history;
        _contactStore = contacts;
        _party = party;
        _blf = blf;
        _settings = settings;
        _policy = policy;
        _logger = logger;
        _search = search;
        _callerContext = callerContext;
        _cards = cards;
        _normalizer = new NumberNormalizer(settings.Current.Advanced.CountryPrefix);

        _isDialpadVisible = settings.Current.Advanced.ShowDialpad;
        _isOutlookExpanded = settings.Current.Advanced.ShowOutlookContacts;

        _sip.AccountsChanged += OnAccountsChanged;
        _sip.CallStateChanged += OnCallStateChanged;
        _sip.AudioDevicesChanged += OnAudioDevicesChanged;
        _settings.Changed += OnSettingsChanged;
        _contactStore.Changed += OnContactsChanged;
        _contactStore.LoadingChanged += OnContactsLoadingChanged;
        _isLoadingContacts = _contactStore.IsLoading;
        _blf.PresenceChanged += OnPresenceChanged;
        _policy.Changed += OnPolicyChanged;

        // Der Name aus einem fremden System trifft erst ein, nachdem der
        // letzte Zustandswechsel des Anrufs durch ist. Ohne dieses Abonnement
        // stuende in der Leiste weiter die Nummer, waehrend im Kopf der
        // Gespraechsansicht laengst der Name steht (ADR-043).
        _party.PartyChanged += OnPartyChanged;

        if (_search is not null)
        {
            _search.ResultsChanged += OnSearchResultsChanged;
        }

        if (_cards is not null)
        {
            // Sonst zeigte ein offener Eintrag nach dem Speichern im Designer
            // weiter die Karte von vorher — der Fall, für den es dieses
            // Ereignis gibt.
            _cards.Changed += OnHistoryCardChanged;
        }

        RefreshAccounts(_sip.Accounts);
        RefreshHistory();
        RefreshContacts();
        RefreshActiveCall();
    }

    /// <summary>Ob gewählt werden kann (§8.2: höchstens zwei Gespräche).</summary>
    public bool CanDial => DialIssue is null && !string.IsNullOrWhiteSpace(DialedNumber);

    /// <summary>
    /// Warum „Anrufen" grau ist — oder <c>null</c> (ADR-045).
    ///
    /// <para>Ein toter Knopf ohne Grund ist von einem Fehler nicht zu
    /// unterscheiden. Bei „kein Konto" stand der Grund immerhin im Statustext;
    /// bei <b>zwei laufenden Gesprächen</b> stand er nirgends — und dieser
    /// Grenzfall tritt ausgerechnet unter Zeitdruck auf.</para>
    ///
    /// <para>Ein leeres Eingabefeld zählt nicht dazu: dass man ohne Nummer
    /// nicht wählen kann, braucht keine Erklärung.</para>
    /// </summary>
    public string? DialIssue
    {
        get
        {
            // Zuerst die Eingabe selbst (C4): steht dort ein Name, ist jeder
            // andere Grund nachrangig — gewählt wird er ohnehin nicht.
            if (DialedNumber.Length > 0 && !NumberNormalizer.IsDialable(DialedNumber))
            {
                return HasSuggestions || SearchResults.Count > 0
                    ? "Das ist keine Nummer. Einen Treffer darunter auswählen."
                    : "Das ist keine Nummer. Eine Nummer eingeben oder nach einem Namen suchen.";
            }

            if (_sip.ActiveCalls.Count >= 2)
            {
                return "Es laufen bereits zwei Gespräche. Erst eines beenden oder übergeben.";
            }

            if (!Accounts.Any(a => a.IsUsable))
            {
                return Accounts.Count == 0
                    ? "Noch kein Konto eingerichtet."
                    : "Das Konto ist nicht angemeldet.";
            }

            return null;
        }
    }

    /// <summary>Ob das × im Eingabefeld sichtbar ist (§20.1).</summary>
    public bool CanClear => DialedNumber.Length > 0;

    /// <summary>Ob überhaupt ein Konto eingerichtet ist.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDial))]
    private async Task DialAsync()
    {
        LastError = null;

        // §8.1: normalisiert wählen, nicht die Rohform.
        var target = _normalizer.Normalize(DialedNumber);

        try
        {
            await _sip.PlaceCallAsync(target, SelectedAccount?.Identity).ConfigureAwait(true);
            DialedNumber = string.Empty;
        }
        catch (TooManyCallsException ex)
        {
            // Die Zahl mitschreiben, nicht nur die Ablehnung. Am 23.09.2026
            // stand diese Meldung auf dem Bildschirm, waehrend genau ein
            // Gespraech lief — und das Protokoll schwieg elf Minuten lang,
            // weil hier nur LastError gesetzt wurde. Hinterher war nicht zu
            // klaeren, ob nipp falsch zaehlt oder ob etwas anderes geschah.
            ShellLog.DialRejectedTooManyCalls(_logger, _sip.ActiveCalls.Count);
            LastError = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ShellLog.DialFailed(_logger, ex.GetType().Name);
            LastError = ex.Message;
        }
    }

    /// <summary>§20.1: das × ganz rechts im Eingabefeld.</summary>
    [RelayCommand]
    private void ClearNumber()
    {
        DialedNumber = string.Empty;
        LastError = null;
    }

    [RelayCommand]
    private void AppendDigit(string digit) => DialedNumber += digit;

    /// <summary>
    /// §20.1: Wähltastatur ein- und ausblenden. Die Wahl wird gemerkt.
    ///
    /// Kein Befehl mehr, sondern die Eigenschaft selbst: in der Oberfläche
    /// sitzt dort ein <c>ToggleButton</c>, und der schreibt seinen Zustand
    /// direkt zurück. Ein Befehl daneben hätte zwei Wege für dieselbe Sache
    /// ergeben, die sich beim Klick gegenseitig aufheben.
    /// </summary>
    /// <summary>§8.4: ob der Abschnitt „Outlook" aufgeklappt ist.</summary>
    [ObservableProperty]
    private bool _isOutlookExpanded = true;

    partial void OnIsOutlookExpandedChanged(bool value) =>
        SaveExpansion(s => s with { ShowOutlookContacts = value });

    /// <summary>
    /// Legt einen Klappzustand ab — auf demselben stillen Weg wie die
    /// Wähltastatur.
    ///
    /// Die Wächterabfrage ist nicht Sparsamkeit: ein Expander meldet seinen
    /// Zustand auch dann, wenn ihn niemand angefasst hat. Ohne sie schriebe
    /// jedes Öffnen der Ansicht in die Einstellungsdatei.
    /// </summary>
    private void SaveExpansion(Func<AdvancedSettings, AdvancedSettings> change)
    {
        var current = _settings.Current;
        var updated = change(current.Advanced);

        if (updated == current.Advanced)
        {
            return;
        }

        _settings.SaveViewState(current with { Advanced = updated });
    }

    partial void OnIsDialpadVisibleChanged(bool value)
    {
        var current = _settings.Current;

        if (current.Advanced.ShowDialpad == value)
        {
            return;
        }

        // SaveViewState und nicht Save: die Wähltastatur ein- oder
        // auszublenden betrifft nichts ausserhalb der Oberfläche, und die
        // Empfänger von Changed schreiben in die Registrierung und melden
        // Tastenkürzel neu an.
        _settings.SaveViewState(current with
        {
            Advanced = current.Advanced with { ShowDialpad = value },
        });
    }

    /// <summary>§20.1: Umschaltleiste unten.</summary>
    [RelayCommand]
    private void ShowSection(string section)
    {
        if (Enum.TryParse<ShellSection>(section, ignoreCase: true, out var parsed))
        {
            Section = parsed;

            if (parsed == ShellSection.History)
            {
                RefreshHistory();
            }
        }
    }

    /// <summary>Rückruf aus der Anrufliste (§8.3).</summary>
    [RelayCommand]
    private async Task CallBackAsync(HistoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        DialedNumber = row.Number;
        await DialAsync().ConfigureAwait(true);
    }

    /// <summary>Ruft einen Kontakt an (§8.4).</summary>
    [RelayCommand]
    private async Task CallContactAsync(ContactRow? row)
    {
        if (row?.Number is not { Length: > 0 } number)
        {
            return;
        }

        DialedNumber = number;
        await DialAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Wählt <b>eine bestimmte</b> Nummer eines Kontakts.
    ///
    /// <see cref="CallContactAsync"/> nimmt immer die erste — bei einem
    /// Kontakt mit Festnetz und Mobil war die zweite damit in der ganzen
    /// Oberfläche nicht erreichbar.
    /// </summary>
    [RelayCommand]
    private async Task CallNumberAsync(ContactNumberChoice? choice)
    {
        if (choice is not { Number.Length: > 0 })
        {
            return;
        }

        DialedNumber = choice.Number;
        await DialAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Laedt die Kontakte neu. §8.4 und AP6.3: <b>nie synchron im UI-Thread</b> —
    /// Outlook ueber COM zu lesen dauert Sekunden, und solange stuende die
    /// Oberflaeche.
    /// </summary>
    [RelayCommand]
    private async Task ReloadContactsAsync()
    {
        try
        {
            // ConfigureAwait(true): nach dem Laden geht es auf dem UI-Thread
            // weiter — SynchronizeAsync ruft ueber den Telefoniedienst ins SDK,
            // und das gehoert dorthin (§6).
            //
            // Beim Start synchronisiert die App selbst; hier geschieht es nur
            // beim ausdruecklichen Neuladen. Beides zu tun ergab zwei gleiche
            // Zeilen im Protokoll fuer denselben Vorgang.
            await _contactStore.RefreshAsync(force: true).ConfigureAwait(true);
            await _blf.SynchronizeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Ohne Kontakte laesst sich weiter telefonieren; das ist kein
            // Grund fuer eine Fehlermeldung ueber die halbe Ansicht, aber
            // einer fuer einen Protokolleintrag.
            ShellLog.ContactsFailed(_logger, ex.Message);
        }
    }

    /// <summary>Baut die Kontaktliste aus dem Speicher neu auf.</summary>
    public void RefreshContacts()
    {
        RefreshTeamContacts();

        Replace(
            OutlookContacts,
            _contactStore.Contacts
                .Where(static c => c.Source != ContactSourceKind.Team)
                .Select(c => new ContactRow(c, _blf.StatusOf(c.SipAddress))));

        // Der offene Detailbereich zeigte sonst auf eine Zeile, die in keiner
        // Sammlung mehr steht — ihre Lampe bekäme keine Präsenzmeldung mehr.
        ReattachContactDetails();

        OnPropertyChanged(nameof(OutlookHeader));
        OnPropertyChanged(nameof(HasContacts));
        OnPropertyChanged(nameof(OutlookHint));
        OnPropertyChanged(nameof(HasOutlookHint));
    }

    /// <summary>
    /// Baut nur die Team-Zeilen und ihre Gruppen neu.
    ///
    /// <para><b>Getrennt von den Outlook-Kontakten</b>, seit ein Ziehvorgang
    /// hier durchläuft: die 137 Zeilen aus Outlook haben mit einem verschobenen
    /// Kollegen nichts zu tun, und sie anzufassen kostet auf dem Thread, der
    /// alle 20 ms <c>Core.Iterate()</c> bedient.</para>
    ///
    /// <para><b>Die Gruppensicht hängt nicht mehr allein an den Kontakten</b>
    /// (ADR-042). Eine neu angelegte, leere Gruppe ändert weder eine Nummer
    /// noch eine Kennung — bis zum 12.09.2026 erschien sie deshalb erst nach
    /// einem Neustart. Verglichen wird jetzt auch die Folge der Gruppennamen.
    /// </para>
    /// </summary>
    public void RefreshTeamContacts()
    {
        var team = _contactStore.Contacts
            .Where(static c => c.Source == ContactSourceKind.Team)
            .Select(c => new ContactRow(c, _blf.StatusOf(c.SipAddress)))
            .ToList();

        // Die Team-Sammlung nur anfassen, wenn sich wirklich etwas geaendert
        // hat. Sie kann gerade gezogen werden — und ein spaeter Ladelauf aus
        // Outlook, der drei Sekunden nach dem Start hereinkommt, hat mit dem
        // Team nichts zu tun.
        var zeilenGleich = SameContacts(TeamContacts, team);

        if (!zeilenGleich)
        {
            Replace(TeamContacts, team);
            OnPropertyChanged(nameof(HasContacts));
        }

        if (!zeilenGleich || !GroupNamesMatch())
        {
            RebuildTeamGroups();
        }
    }

    /// <summary>
    /// Ob die angezeigten Gruppen noch denen aus den Einstellungen entsprechen —
    /// in Namen <b>und</b> Reihenfolge.
    /// </summary>
    private bool GroupNamesMatch()
    {
        var einstellungen = _settings.Current.Contacts;
        var namen = TeamGroups.Collect(einstellungen.Groups, einstellungen.Team);

        if (namen.Count != TeamGroupRows.Count)
        {
            return false;
        }

        for (var i = 0; i < namen.Count; i++)
        {
            if (!string.Equals(namen[i], TeamGroupRows[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Baut die Gruppensicht aus <see cref="TeamContacts"/> neu auf.
    ///
    /// <para>Die Gruppen kommen aus den Einstellungen — <b>alle</b>, auch die
    /// leeren: eine Gruppe, die nur existiert, solange jemand darin steht,
    /// liesse sich nicht befüllen, weil man sie zum Zuordnen bräuchte.</para>
    ///
    /// <para>Der Klappzustand wird beim Neuaufbau übernommen, denn diese
    /// Methode läuft auch bei jeder Kontaktaktualisierung — ein spät
    /// eintreffender Outlook-Lauf darf keine Gruppe aufklappen.</para>
    /// </summary>
    /// <summary>
    /// Ob eine Nebenstelle zur laufenden Suche passt (C11).
    ///
    /// <para><b>Warum das Kachelraster mitfiltern muss.</b> Im breiten Layout
    /// stehen die Nebenstellen rechts als Kacheln, und das Nummernfeld links
    /// filterte nur die Trefferliste — das Raster blieb stehen. Bei acht
    /// Nebenstellen fällt das nicht auf; bei vierzig (T215) ist Scrollen der
    /// einzige Weg zu einer bestimmten, und dann ist die Kachelform schlechter
    /// als die Liste, die sie ersetzt hat. Dazu sah man einen gesuchten
    /// Kollegen links als Treffer <b>und</b> rechts in seiner Kachel: zwei
    /// Antworten auf dieselbe Frage, nebeneinander.</para>
    ///
    /// <para><b>Ohne Suche passt alles</b> — und im schmalen Layout ist der
    /// Abschnitt beim Suchen ohnehin versteckt, dort ändert sich also
    /// nichts.</para>
    /// </summary>
    private bool PasstZurSuche(ContactRow zeile)
    {
        if (!ShowSearchResults)
        {
            return true;
        }

        var suche = ContactQuery.Trim();

        return zeile.DisplayName.Contains(suche, StringComparison.OrdinalIgnoreCase)
            || zeile.Choices.Any(c =>
                c.Number.Contains(suche, StringComparison.OrdinalIgnoreCase))
            || (zeile.Contact.Company?.Contains(suche, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Wie viele Nebenstellen der Filter gerade übriglässt — für die Zeile
    /// über den Kacheln (C11).
    /// </summary>
    public string TeamFilterHint =>
        ShowSearchResults
            ? $"{TeamContacts.Count(PasstZurSuche)} von {TeamContacts.Count}"
            : string.Empty;

    private void RebuildTeamGroups()
    {
        var einstellungen = _settings.Current.Contacts;

        // Alle Gruppen, in der Reihenfolge der Einstellungen. Diese Liste ist
        // auch die des Kontextmenues «In Gruppe verschieben» und bleibt
        // deshalb vollstaendig — sonst liesse sich beim Suchen nicht mehr in
        // eine leere Gruppe verschieben.
        var alle = TeamGroups.Collect(einstellungen.Groups, einstellungen.Team);

        // <b>Beim Filtern verschwinden leere Gruppen</b> (T248, Befund A1-5).
        // Sonst stehen sie als «Hotline (0)» da und kosten je 48 Pixel — bei
        // einem Treffer in einer von vier Gruppen also dreimal so viel Platz
        // fuer nichts. Ohne Filter bleiben sie: eine Gruppe, die nur existiert,
        // solange jemand darin steht, liesse sich nicht befuellen, weil man sie
        // zum Zuordnen braeuchte. <b>Wer sucht, will finden, nicht zuordnen.</b>
        var namen = ShowSearchResults
            ? alle.Where(name => TeamContacts.Any(r =>
                string.Equals(
                    TeamGroups.NameOf(r.Contact.Group, alle),
                    name,
                    StringComparison.OrdinalIgnoreCase)
                && PasstZurSuche(r))).ToList()
            : alle;

        // Stimmen die Namen, werden die vorhandenen Gruppen nachgeführt statt
        // ersetzt. <b>Der Grund ist die Liste, nicht die Sparsamkeit:</b>
        // `Clear()` auf der Quelle einer CollectionViewSource setzt
        // Bildlaufposition und Auswahl zurück — beim Umsortieren also
        // unmittelbar nach jedem Loslassen (ADR-042).
        var passt = namen.Count == TeamGroupRows.Count;

        if (passt)
        {
            for (var i = 0; i < namen.Count; i++)
            {
                if (!string.Equals(namen[i], TeamGroupRows[i].Name, StringComparison.Ordinal))
                {
                    passt = false;
                    break;
                }
            }
        }

        if (!passt)
        {
            TeamGroupRows.Clear();
        }

        // Das Kontextmenue bekommt ALLE Gruppen, nicht die gefilterten.
        if (!TeamGroupNames.SequenceEqual(alle, StringComparer.Ordinal))
        {
            TeamGroupNames.Clear();

            foreach (var name in alle)
            {
                TeamGroupNames.Add(name);
            }
        }

        for (var i = 0; i < namen.Count; i++)
        {
            var name = namen[i];

            // NameOf gegen die vollstaendige Liste: sie entscheidet, in
            // welche Gruppe ein Eintrag ohne eigene faellt (die erste). Mit der
            // gefilterten Liste waere das beim Suchen eine andere.
            var zeilen = TeamContacts
                .Where(r => string.Equals(
                    TeamGroups.NameOf(r.Contact.Group, alle),
                    name,
                    StringComparison.OrdinalIgnoreCase))
                .Where(PasstZurSuche)
                .ToList();

            if (passt)
            {
                TeamGroupRows[i].SetRows(zeilen);
                TeamGroupRows[i].IsExpanded = ShouldBeExpanded(name);
            }
            else
            {
                TeamGroupRows.Add(new ContactGroupRow(name, zeilen, ShouldBeExpanded(name)));
            }
        }

        // Zuletzt: wer den Umschalter trägt (ADR-064). Im !passt-Zweig oben
        // sind die Gruppen eben ersetzt worden — jede Fahne an der alten
        // Instanz wäre damit weg.
        RefreshReorderHost();
    }

    /// <summary>
    /// Ob eine Gruppe aufgeklappt dasteht.
    ///
    /// <b>Im Sortiermodus immer.</b> Eine zugeklappte Gruppe ist kein Ziehziel,
    /// das jemand treffen könnte — und sie wäre eines, in das WinUI etwas
    /// hineinlegen kann, ohne dass man es sieht. Gespeichert wird dabei nichts;
    /// <c>Advanced.CollapsedTeamGroups</c> bleibt die Wahrheit und gilt wieder,
    /// sobald der Modus endet (ADR-042).
    /// </summary>
    private bool ShouldBeExpanded(string name) =>
        IsTeamReorderMode
        || !_settings.Current.Advanced.CollapsedTeamGroups
            .Any(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ob gerade umsortiert wird (§8.4).
    ///
    /// <b>Zustand im Kern und nicht auf der Seite:</b> an ihm hängen drei
    /// Regeln — alle Gruppen offen, kein Klappen per Klick, kein offener
    /// Detailbereich —, und <c>Nipp.App</c> hat kein Testprojekt.
    /// </summary>
    [ObservableProperty]
    private bool _isTeamReorderMode;

    partial void OnIsTeamReorderModeChanged(bool value)
    {
        // Eine Zeile mit ausgeklapptem Innenleben ist als Ziehziel weder zu
        // treffen noch zu erklären.
        CloseContactDetails();

        // Beim Einschalten gehen alle Gruppen auf. Der Umschalter bleibt
        // trotzdem, wo er stand — siehe _reorderHostGroup.
        if (!value)
        {
            _reorderHostGroup = null;
        }

        foreach (var gruppe in TeamGroupRows)
        {
            gruppe.IsExpanded = ShouldBeExpanded(gruppe.Name);
        }

        RefreshReorderHost();
    }

    /// <summary>
    /// Merkt sich, welche Gruppen zugeklappt sind — über
    /// <c>SaveViewState</c>, nicht <c>Save</c> (ADR-041).
    /// </summary>
    public void SaveGroupExpansion()
    {
        var zugeklappt = TeamGroupRows
            .Where(static g => !g.IsExpanded)
            .Select(static g => g.Name)
            .ToList();

        var current = _settings.Current;

        if (current.Advanced.CollapsedTeamGroups.SequenceEqual(zugeklappt, StringComparer.Ordinal))
        {
            return;
        }

        _settings.SaveViewState(current with
        {
            Advanced = current.Advanced with { CollapsedTeamGroups = zugeklappt },
        });
    }

    private static void Replace(ObservableCollection<ContactRow> target, IEnumerable<ContactRow> rows)
    {
        target.Clear();

        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    /// <summary>
    /// Ob zwei Zeilenfolgen dieselben Kontakte in derselben Reihenfolge zeigen.
    ///
    /// <para><b>Über den Kontakt, nicht über die Zeile.</b> Die Zeile traegt die
    /// Praesenz, und die aendert sich im Sekundentakt — waere sie Teil des
    /// Vergleichs, baute jede BLF-Meldung die Liste neu auf. Genau dafuer gibt
    /// es <see cref="ContactRow"/> (siehe <c>UpdatePresence</c>).</para>
    /// </summary>
    private static bool SameContacts(
        IReadOnlyList<ContactRow> current,
        IReadOnlyList<ContactRow> wanted)
    {
        if (current.Count != wanted.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!SameContact(current[i].Contact, wanted[i].Contact))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Ob zwei Kontakte dasselbe zeigen — <b>in allen Feldern</b>.
    ///
    /// <para><b>Warum nicht Feld fuer Feld.</b> Hier stand bis zum 16.09.2026
    /// ein Vergleich aus zwei Zeilen: Kennung und Gruppe. Die Kennung einer
    /// Nebenstelle ist <c>team:{zaehler}:{kurzwahl}</c> — <b>eine hinzugefuegte
    /// Mobilnummer aendert sie nicht</b>, und die Gruppe auch nicht. Der
    /// Vergleich meldete „gleich", die Sammlung blieb stehen, und die Nummer
    /// erschien erst nach einem Neustart von nipp. Dasselbe galt fuer einen
    /// geaenderten Namen und — am unangenehmsten — fuer eine korrigierte
    /// SIP-Adresse: die Zeile blieb unveraendert <i>und</i> bekam weiter keine
    /// Praesenz.</para>
    ///
    /// <para><b>Es war der dritte Anlauf.</b> Zuerst stand hier nur die
    /// Kennung; ADR-042 trug die Gruppe nach, weil sonst ein Zug ueber die
    /// Gruppengrenze stumm blieb. Ein Vergleich, der Feld fuer Feld
    /// nachgeruestet wird, ist beim naechsten Feld wieder zu grob — deshalb
    /// vergleicht diese Fassung den <b>ganzen</b> Datensatz. Ein Feld, das
    /// spaeter an <see cref="Contact"/> dazukommt, ist damit von selbst
    /// dabei.</para>
    ///
    /// <para><b>Die beiden Listen muessen trotzdem einzeln.</b>
    /// <c>Numbers</c> und <c>Origins</c> sind <c>IReadOnlyList</c>, und die
    /// vergleicht sich nach <i>Referenz</i> — zwei frisch gebaute, inhaltlich
    /// gleiche Kontakte waeren sonst immer „ungleich", und die Liste wuerde bei
    /// jeder Einstellungsaenderung neu aufgebaut. Das kostet Auswahl, Bildlauf
    /// und trifft einen laufenden Ziehvorgang. Ihre Elemente sind
    /// <c>record</c>s, <c>SequenceEqual</c> vergleicht also den Inhalt.</para>
    /// </summary>
    private static bool SameContact(Contact current, Contact wanted) =>
        current.Numbers.SequenceEqual(wanted.Numbers)
        && SameOrigins(current.Origins, wanted.Origins)
        && (current with { Numbers = [], Origins = null })
            == (wanted with { Numbers = [], Origins = null });

    /// <summary>
    /// Die Herkunftsliste, bei der <c>null</c> und leer dasselbe bedeuten:
    /// „nur aus <c>SourceId</c>" (siehe <see cref="Contact.Origins"/>).
    /// </summary>
    private static bool SameOrigins(
        IReadOnlyList<ContactOrigin>? current,
        IReadOnlyList<ContactOrigin>? wanted) =>
        (current ?? []).SequenceEqual(wanted ?? []);

    /// <summary>
    /// Ob die Team-Reihenfolge geändert werden darf.
    ///
    /// Legt ein Provisionierungsprofil die Kontakte fest (§17), legt es auch
    /// ihre Reihenfolge fest — sonst überschriebe der nächste Profilabruf, was
    /// der Benutzer gezogen hat, und das sähe nach einem Fehler von nipp aus.
    /// </summary>
    /// <para><b>Und nicht, solange gefiltert wird</b> (C11). Das Kachelraster
    /// zeigt bei einer Suche nur die passenden Nebenstellen;
    /// <c>TeamLayout.From</c> liest die Ordnung aber aus dem Zustand der
    /// Sammlungen (ADR-042) und schriebe damit eine Reihenfolge zurück, in der
    /// die ausgefilterten fehlen. <b>Das wäre kein Anzeigefehler, sondern
    /// Datenverlust</b> — deshalb steht der Umschalter beim Suchen gar nicht
    /// erst da.</para>
    public bool CanReorderTeam => !_policy.IsLocked("contacts") && !ShowSearchResults;

    /// <summary>
    /// Ob der Umschalter «Reihenfolge ändern» überhaupt angeboten wird.
    ///
    /// <para><b>Eine Stelle statt zwei</b> (ADR-064). Diese Regel stand bis zum
    /// 13.09.2026 zweimal im Fenster — einmal für den Abschnitt links, einmal
    /// für die Kacheln rechts —, und die zweite lief im schmalen Layout gar
    /// nicht, weil die Methode dort vorher aussteigt.</para>
    ///
    /// <para>Bei einer einzigen Nebenstelle gibt es nichts zu sortieren.</para>
    /// </summary>
    public bool ShowTeamReorder => CanReorderTeam && TeamContacts.Count > 1;

    /// <summary>
    /// Der Name der Gruppe, die den Umschalter gerade trägt — <b>angeheftet</b>,
    /// solange der Sortiermodus läuft.
    ///
    /// <para><b>Warum angeheftet.</b> Im Sortiermodus gehen alle Gruppen auf
    /// (<see cref="ShouldBeExpanded"/>). Wer bei zugeklapptem «Team» unten bei
    /// «Dienste» steht und dort den Umschalter drückt, würde ihn im selben
    /// Augenblick nach oben springen sehen — <b>unter dem Zeiger weg, als
    /// Folge des eigenen Klicks.</b></para>
    ///
    /// <para><b>Warum der Name und keine Objektreferenz.</b>
    /// <see cref="RebuildTeamGroups"/> ersetzt die Gruppen, sobald sich ihre
    /// Anzahl oder Reihenfolge ändert; eine gemerkte Instanz wäre danach eine
    /// Leiche. Ist der Name nach einem Neuaufbau nicht mehr da, greift wieder
    /// die gewöhnliche Regel.</para>
    /// </summary>
    private string? _reorderHostGroup;

    /// <summary>
    /// Bestimmt, welche Gruppe den Umschalter trägt, und spiegelt den
    /// Sortierzustand in die Gruppen (ADR-064).
    ///
    /// <para>Die Gruppenköpfe liegen in einem <c>DataTemplate</c> und sind aus
    /// dem Fenster nicht über einen Namen erreichbar — sie binden, und hier
    /// steht, woran.</para>
    /// </summary>
    private void RefreshReorderHost()
    {
        // Was nicht angeboten wird, darf auch nicht laufen. Diese Regel stand
        // bis zum 13.09.2026 im Fenster und wirkte über einen Umweg: ein
        // zurückgesetzter Knopf löste sein Ereignis aus, das den Modus beendete.
        //
        // Der Aufruf hier kehrt über OnIsTeamReorderModeChanged genau einmal
        // hierher zurück; beim zweiten Durchlauf ist der Modus aus, und die
        // Bedingung trifft nicht mehr zu.
        if (!ShowTeamReorder && IsTeamReorderMode)
        {
            IsTeamReorderMode = false;
            return;
        }

        var traeger = ErmittleSortierkopf();

        _reorderHostGroup = traeger?.Name;

        foreach (var gruppe in TeamGroupRows)
        {
            gruppe.ShowReorderToggle = ReferenceEquals(gruppe, traeger);
            gruppe.IsReordering = IsTeamReorderMode;
        }

        OnPropertyChanged(nameof(ShowTeamReorder));
    }

    private ContactGroupRow? ErmittleSortierkopf()
    {
        if (!ShowTeamReorder)
        {
            return null;
        }

        // Solange sortiert wird, bleibt er, wo er beim Einschalten stand.
        if (IsTeamReorderMode && _reorderHostGroup is { Length: > 0 } gemerkt)
        {
            var angeheftet = TeamGroupRows
                .FirstOrDefault(g => string.Equals(g.Name, gemerkt, StringComparison.Ordinal));

            if (angeheftet is not null)
            {
                return angeheftet;
            }
        }

        return ReorderHost.Pick(TeamGroupRows);
    }

    /// <summary>
    /// Klappt eine Gruppe auf oder zu — der Weg, den der Kopf in der Liste
    /// nimmt (ADR-064).
    ///
    /// <para><b>Im Kern und nicht im Fenster:</b> daran hängen drei Dinge, die
    /// zusammengehören — der Sortiermodus sperrt das Klappen (ADR-042), der
    /// Zustand wird gemerkt, und der Umschalter kann dadurch die Gruppe
    /// wechseln. <c>Nipp.App</c> hat kein Testprojekt.</para>
    /// </summary>
    public void ToggleGroupExpansion(ContactGroupRow group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Im Sortiermodus stehen alle Gruppen offen, und ein Klick auf den Kopf
        // ist dort keine Klappgeste — er träfe sonst eine Liste, in der gerade
        // gezogen wird.
        if (IsTeamReorderMode)
        {
            return;
        }

        group.IsExpanded = !group.IsExpanded;

        SaveGroupExpansion();
        RefreshReorderHost();
    }

    private void OnPolicyChanged(object? sender, EventArgs e) =>
        OnUiThread(() =>
        {
            OnPropertyChanged(nameof(CanReorderTeam));
            RefreshReorderHost();
        });

    /// <summary>
    /// Die Team-Nebenstellen wurden in der Kontaktliste umsortiert (§8.4).
    ///
    /// <b>Warum hier nichts neu aufgebaut wird.</b> Der Aufruf kommt aus dem
    /// Ende eines Ziehvorgangs, und die Liste ist damit noch nicht fertig — sie
    /// hält noch Verweise auf ihre Zeilen. Angezeigt ist ohnehin bereits das
    /// Richtige: die Liste hat die neue Reihenfolge selbst hergestellt. Zu tun
    /// bleibt nur, sie festzuhalten.
    /// </summary>
    /// <summary>
    /// Verschiebt eine Nebenstelle in eine andere Gruppe — der Weg ohne Maus
    /// (ADR-042).
    ///
    /// <para>Er baut aus der Anzeige dasselbe Layout, das ein Ziehvorgang
    /// hinterlässt, tauscht für die eine Zeile die Gruppe und läuft danach
    /// durch <see cref="ApplyTeamLayout"/> — <b>ein Weg, ein Ergebnis</b>. Eine
    /// zweite Schreibstelle wäre die zweite Gelegenheit, die Invariante zu
    /// verletzen.</para>
    ///
    /// <para>Die Zeile wird ans <b>Ende</b> der Zielgruppe gesetzt: wohin
    /// genau, hat niemand gesagt, und das Ende ist die einzige Stelle, die
    /// keine Behauptung ist.</para>
    /// </summary>
    [RelayCommand]
    private void MoveContactToGroup(ContactGroupMove? ziel)
    {
        if (ziel is not { Row: { } zeile, Group.Length: > 0 })
        {
            return;
        }

        var gruppe = ziel.Group;
        var kennung = zeile.Contact.Id;

        // <b>Ohne Zielstelle und schon in der Gruppe: nichts zu tun.</b> Das
        // ist der Klick im Kontextmenü auf die eigene Gruppe; ohne diese
        // Pruefung schriebe er eine unveraenderte Liste.
        //
        // <b>Mit</b> Zielstelle ist derselbe Fall ein gueltiger Auftrag: beim
        // Ziehen innerhalb einer Gruppe ist die Zielgruppe immer die eigene.
        if (ziel.Index is null
            && string.Equals(zeile.Contact.Group, gruppe, StringComparison.Ordinal))
        {
            return;
        }

        ApplyTeamLayout(
            TeamLayout.Move(TeamLayout.From(TeamGroupRows), kennung, gruppe, ziel.Index));
    }

    /// <summary>
    /// Schreibt, was die Liste zeigt — die Fassung für den Ziehvorgang.
    /// </summary>
    public void ApplyTeamLayout() => ApplyTeamLayout(TeamLayout.From(TeamGroupRows));

    private void ApplyTeamLayout(IReadOnlyList<TeamPlacement> layout)
    {
        if (!CanReorderTeam)
        {
            return;
        }

        // Das Layout kommt von aussen: beim Ziehen aus der Anzeige (WinUI sagt
        // nicht, wohin gezogen wurde — `DragItemsCompletedEventArgs` trägt nur
        // die gezogenen Zeilen und das Ergebnis, die Zeile ist zu diesem
        // Zeitpunkt aber schon umgehängt), beim Kontextmenü aus derselben
        // Anzeige mit einer verschobenen Zeile (ADR-042).
        var current = _settings.Current;

        var (gruppen, neu) = TeamOrder.ApplyLayout(
            current.Contacts.Groups,
            current.Contacts.Team,
            layout);

        // Wertgleichheit von TeamExtension — der eine Ort, an dem sie hilft
        // statt zu stoeren: eine Zeile, die an ihren alten Platz zurueckgezogen
        // wurde, schreibt nichts.
        //
        // <b>Und diese Pruefung ist seit dem 12.09.2026 eine ehrliche.</b>
        // Vorher traf sie auch dann zu, wenn die Zuordnung gescheitert war —
        // beim zweiten Ziehvorgang war das der Normalfall, weil die Kennungen
        // am Platz hingen. Ergebnis: kein Speichern, kein Protokolleintrag,
        // und eine Anzeige, die dem Gespeicherten davonlief.
        if (neu.SequenceEqual(current.Contacts.Team)
            && gruppen.SequenceEqual(current.Contacts.Groups, StringComparer.Ordinal))
        {
            return;
        }

        // Wie viele wirklich die Gruppe gewechselt haben — über die Kennung
        // verglichen, nicht über den Platz. Nur diese Zahl geht ins Protokoll.
        var alteGruppen = new Dictionary<string, string?>(StringComparer.Ordinal);
        var alteKennungen = TeamContactSource.IdsOf(current.Contacts.Team);

        for (var i = 0; i < current.Contacts.Team.Count; i++)
        {
            alteGruppen[alteKennungen[i]] = current.Contacts.Team[i].Group;
        }

        var neueKennungen = TeamContactSource.IdsOf(neu);
        var gewechselt = 0;

        for (var i = 0; i < neu.Count; i++)
        {
            if (alteGruppen.TryGetValue(neueKennungen[i], out var alteGruppe)
                && !string.Equals(alteGruppe, neu[i].Group, StringComparison.Ordinal))
            {
                gewechselt++;
            }
        }

        // SaveViewState und nicht Save: die Begruendung steht dort. Kurz — an
        // Changed haengen fuenf Empfaenger, die beim Umsortieren nichts zu tun
        // haben, darunter das Besetztlampenfeld mit einer offenen SEH-Ausnahme.
        // Das gilt auch fuer einen Gruppenwechsel: die Menge der beobachteten
        // SIP-Adressen aendert sich dabei um null.
        _settings.SaveViewState(current with
        {
            Contacts = current.Contacts with { Groups = gruppen, Team = neu },
        });

        // Der Zwischenspeicher bekommt frisch gebaute Kontakte, keine
        // umsortierten: `Contact.Group` ist unveraenderlich, und nach einem
        // Gruppenwechsel muss der Kontakt neu entstehen — sonst liest die
        // Gruppensicht weiter die alte Gruppe und schiebt die Zeile zurueck.
        _contactStore.ReplaceTeam(TeamContactSource.Build(neu, gruppen));

        ShellLog.TeamReordered(_logger, neu.Count);

        if (gewechselt > 0)
        {
            // Nur die Zahl (Paragraph 21.2): kein Gruppenname, kein
            // Anzeigename, keine Nummer.
            ShellLog.TeamGroupChanged(_logger, gewechselt);
        }

        // <b>Hinter der Nachricht, nicht sofort.</b> Beim Eintreffen von
        // DragItemsCompleted haelt die Liste noch Verweise auf ihre Zeilen;
        // wer ihr jetzt die Sammlungen umbaut, nimmt sie ihr unter den
        // Fingern weg.
        PostToUi(RefreshTeamContacts);
    }

    public void RefreshHistory()
    {
        History.Clear();

        // §8.3: Filter und Suche. Der Speicher konnte beides von Anfang an,
        // gerufen wurde er nur mit einer Obergrenze — §20.3 kürzt die
        // angezeigten Spalten, nicht die Funktionen.
        foreach (var entry in _history.Query(HistoryFilter, HistorySearch, limit: 200))
        {
            History.Add(new HistoryRow(entry));
        }

        MissedCount = _history.CountMissed();
    }

    /// <summary>Suchtext der Anrufliste (§8.3: Suche über Nummer und Name).</summary>
    [ObservableProperty]
    private string _historySearch = string.Empty;

    partial void OnHistoryFilterChanged(CallHistoryFilter value) => RefreshHistory();

    partial void OnHistorySearchChanged(string value) => RefreshHistory();

    /// <summary>§8.3: Filter umschalten. Die Oberfläche gibt den Namen mit.</summary>
    [RelayCommand]
    private void ShowHistoryFilter(string filter)
    {
        if (Enum.TryParse<CallHistoryFilter>(filter, ignoreCase: true, out var parsed))
        {
            HistoryFilter = parsed;
        }
    }

    /// <summary>
    /// §8.3: „Kontakt anlegen" aus der Anrufliste — als Team-Nebenstelle,
    /// denn nur die kennt nipp selbst; Outlook-Kontakte gehören nach Outlook.
    ///
    /// Ohne SIP-Adresse: die Nebenstelle erscheint in der Liste, wird aber
    /// nicht beobachtet (§14.8). Die Adresse kann in den Einstellungen
    /// nachgetragen werden.
    /// </summary>
    [RelayCommand]
    private void AddToTeam(HistoryRow? row)
    {
        if (row?.Entry is not { } entry || string.IsNullOrWhiteSpace(entry.Number))
        {
            return;
        }

        var current = _settings.Current;

        if (current.Contacts.Team.Exists(t =>
            ClipResolver.IsSameNumber(t.Extension, entry.Number)))
        {
            Hint = $"{PhoneNumberFormat.ForDisplay(entry.Number)} steht schon im Team.";
            return;
        }

        var name = entry.DisplayName is { Length: > 0 } known
            ? known
            : PhoneNumberFormat.ForDisplay(entry.Number);

        var team = new List<TeamExtension>(current.Contacts.Team)
        {
            new(DisplayName: name, Extension: entry.Number, SipAddress: null),
        };

        _settings.Save(current with
        {
            Contacts = current.Contacts with { Team = team },
        });

        Hint = $"{name} ist jetzt im Team. Für eine Präsenzlampe fehlt noch die "
            + "SIP-Adresse — sie steht in den Einstellungen bei den Kontakten.";
    }

    private void OnAccountsChanged(object? sender, IReadOnlyList<AccountStatus> accounts) =>
        RefreshAccounts(accounts);

    private void RefreshAccounts(IReadOnlyList<AccountStatus> accounts)
    {
        var previous = SelectedAccount?.Identity;

        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }

        // Die Auswahl über die Identität wiederherstellen, nicht über die
        // Objektreferenz — dieselbe Falle wie bei den Gesprächen (AccountStatus
        // ist unveränderlich und wird bei jeder Zustandsänderung ersetzt).
        SelectedAccount = previous is not null
            ? Accounts.FirstOrDefault(a => a.Identity == previous)
                ?? Accounts.FirstOrDefault(a => a.IsDefault)
                ?? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();

        OnPropertyChanged(nameof(HasAccounts));

        // §8.4: der Zustand gehört als Text neben die Lampe, nicht nur in die
        // Farbe. Und §15: im Fehlerfall mit Ursache und Abhilfe.
        var gewaehlt = SelectedAccount ?? Accounts.FirstOrDefault();
        var state = DescribeAccountState(gewaehlt);
        AccountStateText = state.Text;
        AccountStateIsProblem = state.IsProblem;

        // W1.3 (Befund C4): bei einer gescheiterten Anmeldung fuehrt der
        // Hinweis auch dorthin, wo man sie repariert.
        //
        // Bis zum 13.09.2026 stand dort ein Satz und sonst nichts — der Weg
        // zum Passwortfeld kostete vier Schritte (Strg+4, Gruppe aufklappen,
        // Stift, Feld), waehrend die Leiste «Noch kein Konto eingerichtet»
        // direkt daneben einen Knopf hatte. ADR-045 sagt, jeder Hinweis fuehrt
        // dorthin, wohin er verweist; hier galt das nicht.
        AccountNeedsFixing = gewaehlt?.Status == RegistrationStatus.Failed;

        NotifyDialState();
    }

    /// <summary>
    /// Was zum ausgewählten Konto zu sagen ist. Die Meldung im Fehlerfall
    /// stammt aus <c>SipErrorCatalog</c> — sie ist beim Auslösen des
    /// Ereignisses schon übersetzt worden.
    /// </summary>
    private static (string Text, bool IsProblem) DescribeAccountState(AccountStatus? account)
    {
        if (account is null)
        {
            return ("Kein Konto eingerichtet", true);
        }

        // Im Fehlerfall steht die erklärte Meldung für sich: sie nennt bereits
        // Ursache und Abhilfe, und ein vorangestelltes „Fehlgeschlagen — " wäre
        // dasselbe Wort zweimal in einer Zeile.
        if (account.Status == RegistrationStatus.Failed)
        {
            return (account.Message ?? "Die Anmeldung ist fehlgeschlagen. Zugangsdaten prüfen.", true);
        }

        // Der Wortlaut steht in AccountStateCatalog und nur dort (ADR-044).
        return (
            AccountStateCatalog.Line(account.Status),
            AccountStateCatalog.Consequence(account.Status).Length > 0);
    }

    /// <summary>
    /// Ob der Anmeldezustand einen Weg zu den Zugangsdaten braucht (W1.3, C4).
    /// </summary>
    [ObservableProperty]
    private bool _accountNeedsFixing;

    private void OnCallStateChanged(object? sender, CallStateEventArgs e)
    {
        NotifyDialState();
        RefreshActiveCall();

        // Ein beendetes Gespräch gehört in die Liste — und zwar hier, weil nur
        // die Oberfläche weiss, wann es wirklich vorbei ist.
        if (e.HasJustEnded)
        {
            RecordInHistory(e.Call);
            RefreshHistory();
        }
    }

    /// <summary>
    /// Trägt ein beendetes Gespräch in die Liste ein und leitet daraus ab, was
    /// passiert ist (§20.3, vierte Spalte).
    /// </summary>
    private void RecordInHistory(CallInfo call)
    {
        // Die Regel steht als reine Funktion daneben, damit sie sich
        // vollstaendig pruefen laesst (§13).
        var outcome = CallOutcomeRules.Determine(call.ConnectedAt, call.EndReason, call.Direction);

        // AP6.4: der Name kommt aus derselben Quelle wie im Toast und in der
        // Gespraechsansicht — jetzt woertlich dieselbe (ADR-043). `NameOf`
        // liefert nie eine Nummer; ein Eintrag ohne Namen ist richtig, ein
        // Eintrag, dessen Name seine eigene Nummer ist, waere es nicht.
        //
        // Entscheidung vom 12.09.2026: auch ein Name, den nur ein externes
        // System kennt, wird gespeichert. Die Anrufliste haelt weiterhin
        // keinen Gespraechsinhalt (ADR-027) — ein Name ist keiner.
        var resolved = _party.NameOf(call);

        _history.Add(new CallHistoryEntry(
            Id: 0,
            Number: call.RemoteNumber,
            DisplayName: resolved,
            Direction: call.Direction,
            Outcome: outcome,
            StartedAt: call.StartedAt,
            Duration: call.ConnectedAt is null
                ? null
                : (call.Duration ?? TimeSpan.Zero),
            Codec: call.Codec,

            // Das Konto des Anrufs, nicht das gerade ausgewaehlte: ein
            // eingehender Anruf auf Konto B, waehrend A ausgewaehlt ist, stand
            // vorher unter A in der Liste.
            AccountIdentity: call.AccountIdentity ?? SelectedAccount?.Identity,

            // §8.3: der Aufnahmepfad gehoert in den Eintrag. Ohne ihn waren
            // „HasRecording" und der Filter „Aufgenommen" toter Code, und eine
            // Aufnahme liess sich nur im Ordner wiederfinden.
            RecordingPath: call.RecordingPath));
    }

    /// <summary>
    /// Führt die Gesprächsleiste nach. Sie ist der Weg zurück ins laufende
    /// Gespräch, wenn der Benutzer die Hauptansicht aufgesucht hat, um einen
    /// zweiten Anruf aufzubauen (§8.2).
    /// </summary>
    private void RefreshActiveCall()
    {
        var calls = _sip.ActiveCalls;
        HasActiveCall = calls.Count > 0;

        if (calls.Count == 0)
        {
            ActiveCallLabel = string.Empty;
            return;
        }

        var call = calls[0];
        var party = _party.Describe(call);

        // Der Wortlaut steht in CallStateCatalog und nur dort (ADR-044).
        var state = CallStateCatalog.Of(call.Status);

        ActiveCallLabel = calls.Count > 1
            ? $"{party} · {state} · und ein zweites Gespräch"
            : $"{party} · {state}";
    }

    /// <summary>
    /// Der Name aus einer fremden Quelle ist da — die Leiste zeigt ihn.
    ///
    /// <b>Ohne Blick auf die Kennung.</b> Die Leiste beschreibt ohnehin nur
    /// den vordersten Anruf; welcher das ist, entscheidet
    /// <see cref="RefreshActiveCall"/> und nicht dieses Ereignis.
    /// </summary>
    private void OnPartyChanged(object? sender, CallHandle handle) =>
        OnUiThread(RefreshActiveCall);

    /// <summary>
    /// §9.4, AP5.6: ein Audiogerät ist gekommen oder gegangen.
    ///
    /// Nur wenn der Dienst etwas zu sagen hat — beim blossen Erscheinen eines
    /// Geräts, das niemand ausgewählt hatte, gibt es nichts zu melden.
    /// </summary>
    private void OnAudioDevicesChanged(object? sender, AudioDevicesChangedEventArgs e)
    {
        if (e.Notice is { Length: > 0 } notice)
        {
            OnUiThread(() => Hint = notice);
        }
    }

    private void OnContactsChanged(object? sender, IReadOnlyList<Contact> contacts) =>
        OnUiThread(RefreshContacts);

    /// <summary>
    /// Auch der Ladelauf beim Start soll den Fortschritt zeigen, nicht nur der
    /// über die Schaltfläche angestossene.
    /// </summary>
    private void OnContactsLoadingChanged(object? sender, bool loading) =>
        OnUiThread(() => IsLoadingContacts = loading);

    /// <summary>
    /// Fuehrt etwas auf dem Thread aus, dem die Sammlungen gehoeren. Sind wir
    /// schon dort, sofort — sonst kostete jede Aktualisierung einen Umweg
    /// ueber die Nachrichtenschlange.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (_uiContext is null || _uiContext == SynchronizationContext.Current)
        {
            Guarded(action);
            return;
        }

        _uiContext.Post(_ => Guarded(action), null);
    }

    /// <summary>
    /// Faengt, was beim Nachziehen der Oberflaeche schiefgeht (ADR-053).
    ///
    /// <para>Hierher kommen Meldungen aus Diensten, die auf dem Threadpool
    /// laufen — Kontakte, Praesenz, Anrufzustaende. Eine Ausnahme in der
    /// geposteten Aktion hat keinen Aufrufer mehr, der sie faengt: sie landet
    /// direkt in <c>App.OnUnhandledException</c>, und das protokolliert nur.
    /// Eine Zeile, die sich nicht aktualisieren laesst, darf das Telefon nicht
    /// kosten.</para>
    /// </summary>
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShellLog.UiUpdateFailed(_logger, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Wie <see cref="OnUiThread"/>, aber <b>immer</b> über die
    /// Nachrichtenschlange — auch wenn wir schon auf dem richtigen Thread sind.
    ///
    /// <para><b>Der Unterschied ist der ganze Zweck.</b> Nach einem
    /// Ziehvorgang hält die Liste noch Verweise auf ihre Zeilen; ein Umbau der
    /// Sammlungen im selben Aufruf nimmt sie ihr unter den Fingern weg. Die
    /// Arbeit gehört hinter die laufende Nachricht, nicht in sie hinein.</para>
    /// </summary>
    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            Guarded(action);
            return;
        }

        _uiContext.Post(_ => Guarded(action), null);
    }

    /// <summary>
    /// Praesenz aendert nur die betroffene Zeile. Die Liste neu aufzubauen
    /// wuerde Auswahl und Bildlauf verlieren — der Grund, warum es
    /// <see cref="ContactRow"/> ueberhaupt gibt.
    /// </summary>
    private void OnPresenceChanged(object? sender, PresenceEventArgs e) =>
        OnUiThread(() => UpdatePresence(e));

    private void UpdatePresence(PresenceEventArgs e)
    {
        // Nur das Team: Praesenz gibt es laut ContactRow.HasPresence nirgends
        // sonst, und ueber 137 Outlook-Zeilen zu laufen waere bei zehn Meldungen
        // je Sekunde blosse Last.
        foreach (var row in TeamContacts)
        {
            if (SipUri.Same(row.Contact.SipAddress, e.SipAddress))
            {
                row.Presence = e.Presence;
            }
        }
    }

    private void OnSettingsChanged(object? sender, NippSettings settings)
    {
        _normalizer = new NumberNormalizer(settings.Advanced.CountryPrefix);
        UpdatePreview();

        // <b>Die Kontaktliste zieht nach, ohne dass jemand neu laden muss</b>
        // (ADR-042). Bis zum 12.09.2026 hing der Neuaufbau der Gruppensicht an
        // einer Kontaktaenderung — eine neu angelegte, leere Gruppe ist aber
        // genau das nicht, und sie erschien deshalb erst nach einem Neustart.
        //
        // <b>Ueber OnUiThread</b>: das Ereignis kommt vom Thread, der
        // gespeichert hat, und die Sammlungen gehoeren der Oberflaeche.
        // Dieselbe Lehre wie beim Absturz der Update-Pruefung am 08.09.2026.
        //
        // <b>Und die Kette wird dabei nicht laenger</b>: ReloadTeam liest die
        // Einstellungen, baut eine Liste im Speicher und loest kein Changed
        // aus — kein COM, kein Netz, kein SDK.
        OnUiThread(() =>
        {
            _contactStore.ReloadTeam();
            RefreshTeamContacts();
        });
    }

    partial void OnDialedNumberChanged(string value)
    {
        UpdatePreview();
        UpdateSuggestions(value);

        // <b>Ein Feld, zwei Tiefen</b> (ADR-046). Unter dem Feld stehen die
        // lokalen Treffer sofort — Team, Outlook, Anrufliste, hoechstens fuenf.
        // Dieselbe Eingabe geht entprellt an die Quellen, die ueber das Netz
        // suchen, und ihre Treffer erscheinen in der Liste darunter.
        //
        // Vorher waren es zwei Felder uebereinander, und der Benutzer musste
        // wissen, welches wofuer ist: wer den Kollegen oben suchte, fand ihn;
        // wer den Kunden oben suchte, fand ihn nicht und hielt ihn fuer nicht
        // vorhanden. Genau dieser Fehlschluss hat in ADR-025 zum zweiten Feld
        // gefuehrt — zwei Felder loesen ihn nicht, sie verschieben ihn.
        //
        // Die Zusage aus ADR-025 bleibt: die lokalen Treffer stehen unveraendert
        // sofort da, das Netz liegt nicht im Tippweg.
        ContactQuery = value;

        OnPropertyChanged(nameof(CanClear));
        NotifyDialState();
    }

    /// <summary>
    /// AP4.3: Präfixsuche über Name und Nummer, während getippt wird.
    ///
    /// Quellen sind die Kontakte und die Anrufliste — wer eine Nummer schon
    /// einmal gewählt hat, tippt sie meistens wieder. Doppelte werden über die
    /// Nummer zusammengeführt, damit derselbe Anschluss nicht zweimal
    /// erscheint, nur weil er auch in der Anrufliste steht.
    ///
    /// Erst ab zwei Zeichen: bei einem einzigen passt fast alles, und eine
    /// Liste, die sofort aufspringt, stört beim Tippen einer Nummer, die man
    /// ohnehin auswendig kennt.
    /// </summary>
    private void UpdateSuggestions(string input)
    {
        Suggestions.Clear();

        var query = input.Trim();

        // §22.2: bei leerem Feld die zuletzt gewählten Nummern.
        if (query.Length == 0)
        {
            FillRecentlyDialed();
            OnPropertyChanged(nameof(HasSuggestions));
            return;
        }

        if (query.Length < 2)
        {
            OnPropertyChanged(nameof(HasSuggestions));
            return;
        }

        var digits = ClipResolver.DigitsOnly(query);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Jede Nummer, nicht nur die erste: ein Kontakt mit Festnetz und
        // Mobil erschien sonst mit seiner Geschäftsnummer, und die Mobilnummer
        // war über die Vorschlagsliste nicht zu erreichen. Die Reihenfolge des
        // Kontakts bleibt erhalten — die geschäftliche steht weiter oben.
        foreach (var contact in _contactStore.Search(query))
        {
            var quelle = contact.Source == ContactSourceKind.Team ? "Team" : "Kontakt";

            foreach (var eintrag in contact.Numbers)
            {
                if (string.IsNullOrWhiteSpace(eintrag.Number)
                    || !seen.Add(ClipResolver.DigitsOnly(eintrag.Number)))
                {
                    continue;
                }

                Suggestions.Add(DialSuggestion.Create(contact.DisplayName, eintrag.Number, quelle));

                if (Suggestions.Count == MaxSuggestions)
                {
                    break;
                }
            }

            if (Suggestions.Count == MaxSuggestions)
            {
                break;
            }
        }

        if (Suggestions.Count < MaxSuggestions && digits.Length >= 2)
        {
            foreach (var entry in History)
            {
                var normalized = ClipResolver.DigitsOnly(entry.Number);

                if (!normalized.Contains(digits, StringComparison.Ordinal) || !seen.Add(normalized))
                {
                    continue;
                }

                Suggestions.Add(DialSuggestion.Create(entry.DisplayLabel, entry.Number, "Zuletzt"));

                if (Suggestions.Count == MaxSuggestions)
                {
                    break;
                }
            }
        }

        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>
    /// Zeigt die zuletzt gewählten Nummern — gerufen, wenn das Nummernfeld den
    /// Fokus bekommt (§22.2).
    ///
    /// <para>Nur bei leerem Feld: steht schon etwas drin, gilt die gewöhnliche
    /// Vorschlagsliste, und die hier würde sie überschreiben.</para>
    /// </summary>
    public void ShowRecentlyDialed()
    {
        if (DialedNumber.Trim().Length > 0)
        {
            return;
        }

        Suggestions.Clear();
        FillRecentlyDialed();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>
    /// Räumt die Liste weg, wenn das Feld den Fokus verliert — aber nur die
    /// Wahlwiederholung, nicht eine Trefferliste zu einer Eingabe.
    /// </summary>
    public void HideRecentlyDialed()
    {
        if (DialedNumber.Trim().Length > 0)
        {
            return;
        }

        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>
    /// §22.2: die fünf zuletzt gewählten Nummern, wenn das Feld leer ist.
    ///
    /// <para><b>Nur abgehende.</b> „Zuletzt gewählt" ist das, wonach gefragt
    /// war. Angenommene eingehende mitzunehmen läge nahe — wer zurückrufen
    /// will, hätte es bequemer —, ergäbe aber zwei Listen mit ähnlichem Inhalt
    /// an zwei Stellen: diese hier und die Anrufliste mit ihren Filtern
    /// (ADR-024).</para>
    ///
    /// <para><b>Ohne Doppelte.</b> Wer dieselbe Nummer dreimal probiert hat,
    /// will nicht dreimal dieselbe Zeile sehen — und bekäme sonst statt fünf
    /// Nummern eine einzige, fünffach.</para>
    ///
    /// <para>Die Quelle ist die Anrufliste, die ohnehin geladen ist. Sie enthält
    /// je nach Filter aber nicht alle Richtungen, deshalb wird hier eigens
    /// gefragt statt <c>History</c> zu durchsuchen: sonst wäre die
    /// Wahlwiederholung davon abhängig, welchen Filter jemand zuletzt in einem
    /// ganz anderen Abschnitt gesetzt hat.</para>
    /// </summary>
    private void FillRecentlyDialed()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in _history.Query(CallHistoryFilter.Outgoing, limit: 50))
        {
            var normalized = ClipResolver.DigitsOnly(entry.Number);

            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            Suggestions.Add(DialSuggestion.Create(
                entry.DisplayLabel,
                entry.Number,
                RelativeAge(entry.StartedAt)));

            if (Suggestions.Count == MaxSuggestions)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Wie lange ein Anruf her ist, in einem Wort — er steht rechts in der
    /// Zeile, wo sonst die Quelle steht.
    ///
    /// <para>Grob und nicht genau: „vor 3 Std." hilft beim Wiedererkennen,
    /// „vor 3 Std. 14 Min." nicht mehr, und die Spalte ist schmal.</para>
    /// </summary>
    /// <para>Der Wortlaut steht in <see cref="RelativeTime"/> und nur dort
    /// (ADR-046) — die Zeile der Anrufliste braucht dieselbe Angabe für die
    /// Sprachausgabe.</para>
    private static string RelativeAge(DateTimeOffset when) =>
        RelativeTime.Describe(when, DateTimeOffset.UtcNow);

    /// <summary>AP4.3 gibt fünf vor.</summary>
    private const int MaxSuggestions = 5;

    /// <summary>Ob die Vorschlagsliste etwas zu zeigen hat.</summary>
    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Ein Vorschlag wurde gewählt: die Nummer übernehmen, nicht sofort wählen.</summary>
    [RelayCommand]
    private void UseSuggestion(DialSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        // Übernehmen und nicht gleich anrufen: ein Fehlgriff in einer Liste,
        // die beim Tippen aufspringt, wäre sonst ein Anruf bei der falschen
        // Person.
        DialedNumber = suggestion.Number;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    partial void OnSelectedAccountChanged(AccountStatus? value)
    {
        if (value is not null)
        {
            _sip.DefaultAccountIdentity = value.Identity;
        }

        NotifyDialState();
    }

    private void UpdatePreview()
    {
        var normalized = _normalizer.Normalize(DialedNumber);
        NormalizedPreview = normalized == DialedNumber.Trim() ? string.Empty : normalized;
    }

    private void NotifyDialState()
    {
        OnPropertyChanged(nameof(CanDial));
        OnPropertyChanged(nameof(DialIssue));
        DialCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sip.AccountsChanged -= OnAccountsChanged;
        _sip.CallStateChanged -= OnCallStateChanged;
        _sip.AudioDevicesChanged -= OnAudioDevicesChanged;
        _settings.Changed -= OnSettingsChanged;
        _contactStore.Changed -= OnContactsChanged;
        _contactStore.LoadingChanged -= OnContactsLoadingChanged;
        _blf.PresenceChanged -= OnPresenceChanged;
        _policy.Changed -= OnPolicyChanged;
        _party.PartyChanged -= OnPartyChanged;

        if (_search is not null)
        {
            _search.ResultsChanged -= OnSearchResultsChanged;
        }

        if (_cards is not null)
        {
            _cards.Changed -= OnHistoryCardChanged;
        }
    }
}

/// <summary>
/// Ein Vorschlag unter dem Eingabefeld (AP4.3).
/// </summary>
/// <param name="Title">Name des Kontakts oder das, was die Anrufliste weiss.</param>
/// <param name="Subtitle">Die Nummer, lesbar gruppiert.</param>
/// <param name="Number">Die Nummer, wie sie ins Eingabefeld gehört.</param>
/// <param name="Source">Woher der Vorschlag kommt — „Team", „Kontakt" oder „Zuletzt".</param>
/// <param name="Title">Der aufgelöste Name, oder die Nummer, wenn keiner bekannt ist.</param>
/// <param name="Subtitle">
/// Die formatierte Nummer — <b>leer, wenn sie dasselbe sagt wie der Titel</b>.
///
/// <para>Ohne aufgelösten Namen ist <c>Title</c> bereits die Nummer, und die
/// Zeile zeigte sie dann zweimal untereinander. Das sieht nach einem Fehler
/// aus und kostet die halbe Zeilenhöhe für nichts.</para>
/// </param>
/// <param name="Number">Die Nummer, die gewählt wird.</param>
/// <param name="Source">Woher der Vorschlag stammt, oder wie lange er her ist.</param>
public sealed record DialSuggestion(string Title, string Subtitle, string Number, string Source)
{
    /// <summary>Ob der Untertitel etwas hinzufügt.</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>
    /// Was eine Sprachausgabe vorliest (Befund A1-16).
    ///
    /// <para><b>Ohne diese Eigenschaft liest sie den <c>ToString()</c> des
    /// Records</b> — gemessen am 22.09.2026 stand im Namen der Zeile
    /// «DialSuggestion { Title = …, Subtitle = …, Number = …, Source = Team,
    /// HasSubtitle = True }»: die Nummer zweimal, dazu ein internes Feld, und
    /// das bei jedem Tastendruck fünfmal neu. Dasselbe wie der Befund vom
    /// 07.09.2026 am Katalog.</para>
    ///
    /// <para>Der Untertitel bleibt weg, wenn er nur die Nummer aus dem Titel
    /// wiederholt — <see cref="HasSubtitle"/> entscheidet das schon für die
    /// Anzeige, und was man nicht sieht, will man auch nicht hören.</para>
    /// </summary>
    public string AccessibleName => HasSubtitle
        ? $"{Title}, {Subtitle}, {Source}"
        : $"{Title}, {Source}";

    /// <summary>
    /// Baut einen Vorschlag und lässt den Untertitel weg, wenn er nur die
    /// Nummer wiederholt, die schon im Titel steht.
    /// </summary>
    public static DialSuggestion Create(string title, string number, string source)
    {
        var formatted = PhoneNumberFormat.ForDisplay(number);

        var subtitle = ClipResolver.DigitsOnly(title) == ClipResolver.DigitsOnly(formatted)
            ? string.Empty
            : formatted;

        return new DialSuggestion(title, subtitle, number, source);
    }
}

/// <summary>
/// Der Inhaltsbereich der Hauptansicht (§20.1).
///
/// Die Einstellungen stehen bewusst nicht darin: sie sind eine eigene Seite,
/// keine Kachel im Ausschnitt unter der Wähltastatur. Ein Enum-Wert für einen
/// Zustand, den es nicht gibt, führt später jemanden in die Irre — und hat es
/// schon getan: der Knopf setzte den Abschnitt und öffnete nichts.
/// </summary>
public enum ShellSection
{
    Contacts,
    History,
}
