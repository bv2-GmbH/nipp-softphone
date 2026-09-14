using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Eine Quelle in der Liste der Einstellungen.
/// </summary>
public sealed partial class IntegrationSourceRow : ObservableObject
{
    public IntegrationSourceRow(DataSourceDefinition source, string host, bool secretsReady)
    {
        Source = source;
        Host = host;
        SecretsReady = secretsReady;
        _isEnabled = source.Enabled;
    }

    public DataSourceDefinition Source { get; }

    public string Id => Source.Id;

    public string DisplayName => Source.DisplayName;

    /// <summary>Nur der Rechnername — der Pfad trägt gelegentlich eine Kundenkennung.</summary>
    public string Host { get; }

    /// <summary>Ob die Zugangsdaten auf diesem Gerät hinterlegt sind.</summary>
    public bool SecretsReady { get; }

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Was diese Quelle kann, als lesbare Aufzählung.</summary>
    public string Capabilities =>
        Source.Capabilities.Count == 0
            ? "nichts eingerichtet"
            : string.Join(", ", Source.Capabilities.Select(static c => c switch
            {
                Capability.LookupByPhone => "Anruferkontext",
                Capability.SearchContacts => "Kontaktsuche",
                _ => "Kontakt öffnen",
            }));

    /// <summary>
    /// Was zu dieser Quelle zu sagen ist — §15: Ursache und Abhilfe, nicht nur
    /// ein Zustand.
    /// </summary>
    public string StatusText => SecretsReady
        ? Capabilities
        : $"{Capabilities} — Zugangsdaten fehlen auf diesem Gerät";

    /// <summary>
    /// Ob die Zeile auf ein Problem hinweist. Eine Quelle ohne Token sieht
    /// sonst aus wie eine, die läuft.
    /// </summary>
    public bool NeedsAttention => !SecretsReady;
}

/// <summary>
/// Ein Eintrag im Katalog „Quelle hinzufügen" (ADR-033).
/// </summary>
public sealed record ConnectorCatalogRow(ConnectorTemplate Template)
{
    public string Id => Template.Id;

    public string DisplayName => Template.DisplayName;

    public string Summary => Template.Summary;

    /// <summary>
    /// Woher die Vorlage kommt — „importiert" und dahinter der Hersteller,
    /// falls die Datei einen nennt. Leer bei den mitgelieferten.
    ///
    /// <b>Eine Herkunftsangabe, keine Bewertung</b> (ADR-040). Vorher stand
    /// dort „intern bv2", was zwei Dinge zugleich meinte: von uns, und nicht
    /// für Kunden gedacht.
    /// </summary>
    public string VendorNote => Template.Origin switch
    {
        ConnectorOrigin.Imported when Template.Vendor is { Length: > 0 } hersteller =>
            $"importiert · {hersteller}",
        ConnectorOrigin.Imported => "importiert",
        _ => string.Empty,
    };

    /// <summary>Ob die Vorlage über den Import kam — die Oberfläche schreibt es daneben.</summary>
    public bool IsImported => Template.IsImported;

    public string CapabilityText =>
        Template.Capabilities.Count == 0
            ? "keine Fähigkeit eingerichtet"
            : string.Join(", ", Template.Capabilities.Select(static c => c switch
            {
                Capability.LookupByPhone => "Anruferkontext",
                Capability.SearchContacts => "Kontaktsuche",
                _ => "Kontakt öffnen",
            }));

    /// <summary>
    /// Was eine Sprachausgabe zu dieser Zeile sagt.
    ///
    /// <b>Ohne diese Eigenschaft nimmt die Automation den <c>ToString()</c>
    /// des Datensatzes.</b> Am Gerät gemessen war der Name einer Katalogzeile
    /// dadurch die ganze Aufstellung — Endpunkte, Mapping-Typen und die
    /// vollständige Beispielantwort, mehrere Tausend Zeichen. Ein
    /// <c>record</c> in einer Liste ist genau diese Falle: er hat ein
    /// hilfreiches <c>ToString()</c>, und das macht ihn hier unbrauchbar.
    /// </summary>
    public string AutomationName => VendorNote.Length > 0
        ? $"{DisplayName}, {VendorNote}. {Summary}"
        : $"{DisplayName}. {Summary}";
}

/// <summary>
/// Ein Geheimnis im Detail einer Quelle — mit Beschriftung und Herkunft.
///
/// <para><b>Der Grund, warum das eine eigene Zeile ist.</b> Vorher gab es
/// <b>ein</b> Feld „Verweis" als freien Text mit dem Platzhalter
/// <c>crm.apiKey</c>, und es hing an keiner Auswahl. Wer ein Token eintragen
/// wollte, musste vorher im JSON nachlesen, dass der Verweis <c>crm</c>
/// heisst. Der Verweis ist eine technische Kennung; in ein Formular gehört
/// die Beschriftung, die sagt, <b>was</b> gebraucht wird — und der Hinweis,
/// <b>woher</b> es kommt.</para>
/// </summary>
public sealed partial class IntegrationSecretRow : ObservableObject
{
    public IntegrationSecretRow(ConnectorSecret secret, bool isConfigured)
    {
        Secret = secret;
        _isConfigured = isConfigured;
    }

    public ConnectorSecret Secret { get; }

    public string Reference => Secret.Ref;

    public string Label => Secret.Label;

    public string? Hint => Secret.Hint;

    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

    /// <summary>Ob auf diesem Gerät ein Wert hinterlegt ist.</summary>
    [ObservableProperty]
    private bool _isConfigured;

    /// <summary>
    /// Der eingetippte Wert. Wird nie angezeigt, nie protokolliert und
    /// unmittelbar nach dem Ablegen vergessen.
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>§15: Zustand mit Folge, nicht nur Zustand.</summary>
    public string StatusText => IsConfigured
        ? "hinterlegt — an dieses Gerät und Benutzerkonto gebunden"
        : "fehlt auf diesem Gerät; ohne Wert bleibt die Quelle aus";

    partial void OnIsConfiguredChanged(bool value) => OnPropertyChanged(nameof(StatusText));
}

/// <summary>
/// Eine Karte in der Übersicht der Einstellungen.
/// </summary>
public sealed record CardSummaryRow(CardKind Kind, CardDefinition? Definition, bool IsCustom)
{
    public string KindName => Kind switch
    {
        CardKind.ActiveExpanded => "Gespräch",
        CardKind.IncomingCompact => "Eingehender Anruf",
        CardKind.Toast => "Benachrichtigung",
        _ => "Anrufliste",
    };

    /// <summary>Wie viele Bausteine darauf stehen.</summary>
    public int ElementCount => Definition?.Sections
        .SelectMany(static s => s.Rows)
        .SelectMany(static r => r.Columns)
        .Sum(static c => c.Elements.Count) ?? 0;

    /// <summary>
    /// Was in der Übersicht dasteht.
    ///
    /// <b>Der Toast ohne eigene Karte ist der Sonderfall</b> (K5, ADR-034):
    /// dort gilt keine Karte, sondern die Zusammensetzung im Code — sie setzt
    /// die erste Zeile aus Name, Firma und Art zusammen und lässt Teile weg,
    /// was sich als Karte nur als unlesbarer Ausdruck schreiben liesse. „0
    /// Bausteine" wäre dort eine falsche Auskunft.
    /// </summary>
    public string Summary => Definition is null
        ? "mitgelieferte Zusammensetzung · Name, letzte Arbeit, letztes Gespräch"
        : IsCustom
            ? $"{ElementCount} Bausteine · eigene Karte"
            : $"{ElementCount} Bausteine · mitgeliefert";
}

/// <summary>
/// Die Verwaltung der Integrationen in den Einstellungen (§21.4, K3).
///
/// <para><b>Was sich am 07.09.2026 geändert hat.</b> Vorher standen hier drei
/// Geschwister-Aufklapper — Liste, Zugangsdaten, Test —, die sich alle auf
/// „die ausgewählte Quelle" bezogen, aber nur einer benutzte die Auswahl. Es
/// gab kein „Quelle hinzufügen": eine neue Quelle entstand nur, indem jemand
/// eine Datei einlas, und das ersetzte die ganze Konfiguration. Und das Feld
/// für den Verweis auf ein Geheimnis war freier Text.</para>
///
/// <para><b>Jetzt:</b> eine Liste, ein Katalog zum Hinzufügen, und je Quelle
/// <b>ein</b> Detail mit Verbindung, Anmeldung, Fähigkeiten und Test. Eine
/// Quelle kommt abgeschaltet herein und wird nach einem erfolgreichen Abruf
/// eingeschaltet — nicht davor.</para>
///
/// <para><b>Was hier weiterhin nicht passiert:</b> Endpunkte und Mappings
/// werden nicht in Formularen zusammengeklickt. Sie werden als JSON
/// bearbeitet — jetzt aber je Quelle statt für die ganze Datei. Ein Formular
/// je Feld wäre eine zweite Beschreibung derselben Sache, mit eigener Prüfung
/// und eigenen Fehlermeldungen, und müsste bei jeder Erweiterung nachgezogen
/// werden. Die Karten dagegen bekommen ihren Editor (K4).</para>
///
/// <para><b>Warum die Fähigkeiten nur angezeigt und nicht geschaltet
/// werden.</b> Eine Fähigkeit <b>ist</b> ihr Block in der Konfiguration
/// (§21.3) — sie abzuschalten hiesse, ihn zu entfernen, und damit wären
/// Endpunkt und Mapping weg. Ein Schalter, der bei jedem Umlegen die Arbeit
/// einer halben Stunde löscht, ist kein Komfort. Geschaltet wird die
/// <b>Quelle</b>; wer eine einzelne Fähigkeit loswerden will, entfernt ihren
/// Block im JSON unten.</para>
/// </summary>
public sealed partial class IntegrationSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IntegrationConfigStore _store;
    private readonly IntegrationSecrets _secrets;
    private readonly IntegrationTester _tester;
    private readonly SettingsService _settings;
    private readonly TestSampleStore _samples;
    private readonly CardResolver _cards;
    private readonly ConnectorLibrary? _library;
    private bool _disposed;

    /// <summary>
    /// Sperrt das Zurückschreiben, während die Oberfläche gerade aus dem
    /// Speicher gefüllt wird.
    ///
    /// Ohne das löst jedes Setzen einer Eigenschaft in <see cref="Reload"/>
    /// ein <c>Save</c> aus, das seinerseits <c>Reload</c> auslöst.
    /// </summary>
    private bool _applying;

    /// <summary>Die eingerichteten Quellen.</summary>
    public ObservableCollection<IntegrationSourceRow> Sources { get; } = [];

    /// <summary>Was zum Hinzufügen bereitsteht (ADR-033).</summary>
    public ObservableCollection<ConnectorCatalogRow> Catalog { get; } = [];

    /// <summary>Die Zugangsdaten der ausgewählten Quelle, mit Beschriftung.</summary>
    public ObservableCollection<IntegrationSecretRow> Secrets { get; } = [];

    /// <summary>Was die Prüfung bemängelt hat — mit Pfad und Abhilfe.</summary>
    public ObservableCollection<string> Issues { get; } = [];

    /// <summary>Was das Mapping beim Testabruf ergeben hat.</summary>
    public ObservableCollection<string> TestFields { get; } = [];

    /// <summary>Die Karten, zum Bearbeiten im Designer (K4).</summary>
    public ObservableCollection<CardSummaryRow> Cards { get; } = [];

    /// <summary>Die gerade ausgewählte Quelle.</summary>
    [ObservableProperty]
    private IntegrationSourceRow? _selected;

    // --- Detail der ausgewählten Quelle ---

    [ObservableProperty]
    private string _detailDisplayName = string.Empty;

    [ObservableProperty]
    private string _detailBaseUrl = string.Empty;

    [ObservableProperty]
    private string _detailTimeoutMs = string.Empty;

    [ObservableProperty]
    private string _detailPriority = "100";

    /// <summary>
    /// Das Schema vor dem Wert in der <c>Authorization</c>-Kopfzeile.
    ///
    /// <b>Steht als eigenes Feld hier</b>, weil es der Punkt ist, an dem beim
    /// Anbinden vom CRM Zeit verlorenging: der Server antwortet auf eine
    /// Anfrage ohne Anmeldung mit <c>WWW-Authenticate: Bearer</c> und
    /// akzeptiert dann ausschliesslich <c>Token</c>. Der Kopfzeile ist nicht zu
    /// glauben — und ohne dieses Feld tippt jemand <c>Token </c> in den
    /// Geheimniswert, womit ein Teil des Protokolls in der DPAPI-Ablage steckt,
    /// wo es niemand vermutet und niemand korrigieren kann.
    /// </summary>
    [ObservableProperty]
    private string _detailScheme = string.Empty;

    /// <summary>Die Fähigkeiten als Text — Anzeige, nicht Schalter.</summary>
    [ObservableProperty]
    private string _detailCapabilities = string.Empty;

    /// <summary>Das JSON dieser einen Quelle, zum Bearbeiten.</summary>
    [ObservableProperty]
    private string _detailJson = string.Empty;

    [ObservableProperty]
    private string _detailFeedback = string.Empty;

    // --- Testabruf ---

    [ObservableProperty]
    private string _testNumber = string.Empty;

    [ObservableProperty]
    private string _testQuery = "Muster";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _testSummary = string.Empty;

    /// <summary>Die Antwort im Rohzustand — beim Einrichten die wichtigste Information.</summary>
    [ObservableProperty]
    private string _testResponse = string.Empty;

    // --- Wann nachgeschlagen wird (§21.4) ---
    //
    // Diese vier standen bisher nur in der Datei. Wer sie ändern wollte,
    // musste die Konfiguration ausgeben, von Hand bearbeiten und einlesen —
    // für vier Schalter.

    [ObservableProperty]
    private bool _lookupIncoming = true;

    [ObservableProperty]
    private bool _lookupOutgoing = true;

    [ObservableProperty]
    private bool _lookupInternalNumbers;

    /// <summary>Ob überhaupt Quellen eingerichtet sind.</summary>
    public bool HasSources => Sources.Count > 0;

    /// <summary>Ob die Prüfung etwas zu melden hat.</summary>
    public bool HasIssues => Issues.Count > 0;

    /// <summary>Ob eine Quelle ausgewählt ist — die Detailansicht hängt daran.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>Ob die ausgewählte Quelle einen Anruferkontext liefert.</summary>
    public bool SelectedHasLookup => Selected?.Source.LookupByPhone is not null;

    /// <summary>
    /// Ob die ausgewählte Quelle sucht.
    ///
    /// <b>Beide Eigenschaften gibt es, weil der Testabruf vorher log:</b> das
    /// Feld „Suchbegriff" stand immer da, benutzt wurde es aber nie, sobald
    /// eine Quelle <c>lookupByPhone</c> hatte — der Test nahm dann immer die
    /// Nummer. Jetzt wird gezeigt, was die Quelle kann, und bei beiden
    /// Fähigkeiten werden beide geprüft.
    /// </summary>
    public bool SelectedHasSearch => Selected?.Source.SearchContacts is not null;

    /// <summary>Ob die ausgewählte Quelle Zugangsdaten braucht.</summary>
    public bool SelectedNeedsSecrets => Secrets.Count > 0;

    /// <param name="library">
    /// Die Anbietervorlagen (ADR-040). <c>null</c> heisst: kein Katalog — dann
    /// bleibt „Quelle hinzufügen" leer, und eingerichtet wird über Einlesen.
    /// Das halten die bestehenden Tests am Leben, die dieses ViewModel von Hand
    /// bauen.
    /// </param>
    public IntegrationSettingsViewModel(
        IntegrationConfigStore store,
        IntegrationSecrets secrets,
        IntegrationTester tester,
        SettingsService settings,
        TestSampleStore samples,
        CardResolver cards,
        ConnectorLibrary? library = null)
    {
        _store = store;
        _secrets = secrets;
        _tester = tester;
        _settings = settings;
        _samples = samples;
        _cards = cards;
        _library = library;

        _store.Changed += OnStoreChanged;
        _cards.Changed += OnCardsChanged;

        if (_library is not null)
        {
            _library.Changed += OnLibraryChanged;
        }

        RefreshCatalog();

        Reload();
    }

    /// <summary>
    /// Baut den Katalog neu auf — nach einem Import oder einem Entfernen.
    /// </summary>
    private void RefreshCatalog()
    {
        Catalog.Clear();

        foreach (var vorlage in _library?.Templates ?? [])
        {
            Catalog.Add(new ConnectorCatalogRow(vorlage));
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e) => RefreshCatalog();

    private void OnStoreChanged(object? sender, IntegrationConfig config) => Reload();

    private void OnCardsChanged(object? sender, EventArgs e) => ReloadCards();

    /// <summary>Baut die Liste aus dem, was gespeichert ist.</summary>
    public void Reload()
    {
        var auswahl = Selected?.Id;

        Sources.Clear();

        foreach (var source in _store.Current.DataSources)
        {
            var host = Uri.TryCreate(source.Http?.BaseUrl, UriKind.Absolute, out var url)
                ? url.Host
                : "(keine Adresse)";

            var bereit = source.Http is null
                || _secrets.FindMissing(source.Http.Auth.SecretRefs).Count == 0;

            Sources.Add(new IntegrationSourceRow(source, host, bereit));
        }

        Issues.Clear();

        foreach (var issue in _store.Issues)
        {
            Issues.Add($"{issue.Path}: {issue.Message}");
        }

        var lookup = _store.Current.CallerLookup;

        _applying = true;

        LookupIncoming = lookup.LookupIncoming;
        LookupOutgoing = lookup.LookupOutgoing;
        LookupInternalNumbers = lookup.LookupInternalNumbers;

        _applying = false;

        Selected = auswahl is null
            ? Sources.FirstOrDefault()
            : Sources.FirstOrDefault(s => s.Id == auswahl) ?? Sources.FirstOrDefault();

        ReloadCards();

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasIssues));
    }

    private void ReloadCards()
    {
        Cards.Clear();

        // Alle vier Arten, auch die ohne Karte: der Toast hat ohne eigene
        // Karte keine — dort gilt die Zusammensetzung im Code. Die Zeile muss
        // trotzdem da sein, sonst gibt es keinen Weg zu seinem Designer.
        foreach (var art in new[]
        {
            CardKind.ActiveExpanded,
            CardKind.IncomingCompact,
            CardKind.History,
            CardKind.Toast,
        })
        {
            Cards.Add(new CardSummaryRow(art, _cards.DefinitionFor(art), _cards.IsCustom(art)));
        }
    }

    /// <summary>
    /// Übernimmt die Auswahl ins Detail.
    ///
    /// <b>Der Kern von K3:</b> vorher wusste der Zugangsdaten-Block von der
    /// Auswahl nichts, und der Testabruf zeigte Felder für Fähigkeiten, die
    /// die Quelle nicht hat.
    /// </summary>
    partial void OnSelectedChanged(IntegrationSourceRow? value)
    {
        TestFields.Clear();
        TestSummary = string.Empty;
        TestResponse = string.Empty;
        DetailFeedback = string.Empty;

        Secrets.Clear();

        _applying = true;

        if (value is null)
        {
            DetailDisplayName = string.Empty;
            DetailBaseUrl = string.Empty;
            DetailTimeoutMs = string.Empty;
            DetailScheme = string.Empty;
            DetailCapabilities = string.Empty;
            DetailJson = string.Empty;
            DetailPriority = "100";
        }
        else
        {
            var source = value.Source;

            DetailDisplayName = source.DisplayName;
            DetailBaseUrl = source.Http?.BaseUrl ?? string.Empty;
            DetailTimeoutMs = (source.Http?.TimeoutMs ?? 1500)
                .ToString(CultureInfo.InvariantCulture);
            DetailScheme = source.Http?.Auth.Scheme ?? string.Empty;
            DetailPriority = source.Priority.ToString(CultureInfo.InvariantCulture);
            DetailCapabilities = value.Capabilities;
            DetailJson = IntegrationConfigStore.SerializeSource(source);

            foreach (var geheimnis in _library?.SecretsFor(source) ?? [])
            {
                Secrets.Add(new IntegrationSecretRow(
                    geheimnis,
                    _secrets.IsConfigured(geheimnis.Ref)));
            }
        }

        _applying = false;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedHasLookup));
        OnPropertyChanged(nameof(SelectedHasSearch));
        OnPropertyChanged(nameof(SelectedNeedsSecrets));
    }

    // --- Hinzufügen und Entfernen ---

    /// <summary>
    /// Fügt eine Vorlage aus dem Katalog hinzu (ADR-033).
    ///
    /// <b>Sie kommt abgeschaltet herein</b>, auch wenn die Vorlage etwas
    /// anderes sagen würde: eingeschaltet wird nach einem erfolgreichen
    /// Testabruf. Und sie ersetzt keine bestehende Quelle — eine zweite
    /// Instanz derselben Vorlage bekommt eine freie Kennung, weil das der Fall
    /// „zwei Mandanten desselben Systems" ist.
    /// </summary>
    [RelayCommand]
    public void AddFromCatalog(string? templateId)
    {
        if (_library?.Find(templateId) is not { } vorlage)
        {
            return;
        }

        var kennung = _store.FreeSourceId(vorlage.Source.Id);

        _store.AddOrReplaceSource(
            vorlage.Source with { Id = kennung, Enabled = false },
            vorlage.Cards);

        Selected = Sources.FirstOrDefault(s => s.Id == kennung);

        DetailFeedback = Secrets.Count > 0
            ? $"«{vorlage.DisplayName}» ist angelegt und noch aus. Jetzt {Secrets[0].Label} "
                + "eintragen, Verbindung testen, dann einschalten."
            : $"«{vorlage.DisplayName}» ist angelegt und noch aus. Verbindung testen, "
                + "dann einschalten.";
    }

    /// <summary>
    /// Entfernt die ausgewählte Quelle.
    ///
    /// <b>Die Zugangsdaten bleiben liegen.</b> Sie zu löschen wäre beim
    /// versehentlichen Entfernen der teurere Fehler: ein Token wird nur einmal
    /// ausgegeben, und wer es wiederbeschaffen muss, braucht einen
    /// Administrator im Zielsystem.
    /// </summary>
    [RelayCommand]
    public void RemoveSelected()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var name = row.DisplayName;

        if (_store.RemoveSource(row.Id))
        {
            DetailFeedback = $"«{name}» ist entfernt. Die hinterlegten Zugangsdaten bleiben "
                + "auf diesem Gerät und gelten wieder, wenn die Quelle erneut angelegt wird.";
        }
    }

    /// <summary>
    /// Schaltet eine Quelle ein oder aus — die häufigste Handlung überhaupt.
    /// </summary>
    [RelayCommand]
    public void ToggleEnabled(IntegrationSourceRow? row)
    {
        if (row is null)
        {
            return;
        }

        var config = _store.Current;

        var sources = config.DataSources
            .Select(s => s.Id == row.Id ? s with { Enabled = row.IsEnabled } : s)
            .ToList();

        _store.Save(config with { DataSources = sources });
    }

    // --- Detail übernehmen ---

    /// <summary>
    /// Übernimmt Verbindung und Anmeldung der ausgewählten Quelle.
    ///
    /// <b>Nur die Felder, die hier stehen</b> — Endpunkte und Mappings bleiben
    /// unberührt. Wer sie ändern will, nimmt das JSON dieser Quelle.
    /// </summary>
    [RelayCommand]
    public void ApplyDetail()
    {
        if (Selected is not { } row)
        {
            return;
        }

        if (!int.TryParse(DetailTimeoutMs, CultureInfo.InvariantCulture, out var zeitgrenze))
        {
            DetailFeedback = "Die Zeitgrenze ist keine Zahl. Erwartet werden Millisekunden, "
                + "etwa 1500.";

            return;
        }

        if (!int.TryParse(DetailPriority, CultureInfo.InvariantCulture, out var prioritaet))
        {
            DetailFeedback = "Die Reihenfolge ist keine Zahl. Kleiner gewinnt; 100 ist die "
                + "Vorgabe.";

            return;
        }

        var quelle = row.Source;
        var http = quelle.Http;

        if (http is not null)
        {
            http = http with
            {
                BaseUrl = DetailBaseUrl.Trim(),
                TimeoutMs = zeitgrenze,
                Auth = http.Auth with
                {
                    Scheme = string.IsNullOrWhiteSpace(DetailScheme) ? null : DetailScheme.Trim(),
                },
            };
        }

        _store.AddOrReplaceSource(quelle with
        {
            DisplayName = string.IsNullOrWhiteSpace(DetailDisplayName)
                ? quelle.DisplayName
                : DetailDisplayName.Trim(),
            Priority = prioritaet,
            Http = http,
        });

        DetailFeedback = FeedbackFor(quelle.Id, "Übernommen.");
    }

    /// <summary>
    /// Übernimmt das JSON <b>dieser einen</b> Quelle.
    ///
    /// <para><b>Nicht mehr die ganze Datei.</b> Wer einen JSONPath korrigieren
    /// wollte, musste vorher die gesamte Konfiguration ausgeben, im Editor die
    /// Stelle suchen und alles wieder einlesen — und das Einlesen ersetzte
    /// alles. Jetzt geht eine kaputte Bearbeitung nur auf Kosten dieser
    /// Quelle.</para>
    /// </summary>
    [RelayCommand]
    public void ApplyDetailJson()
    {
        if (Selected is null)
        {
            return;
        }

        if (!IntegrationConfigStore.TryReadSource(DetailJson, out var quelle, out var fehler))
        {
            DetailFeedback = fehler;
            return;
        }

        _store.AddOrReplaceSource(quelle!);

        Selected = Sources.FirstOrDefault(s => s.Id == quelle!.Id) ?? Sources.FirstOrDefault();

        DetailFeedback = FeedbackFor(quelle!.Id, "Übernommen. Jetzt die Verbindung testen.");
    }

    /// <summary>
    /// Was nach dem Übernehmen dasteht: die Befunde dieser Quelle, sonst die
    /// Bestätigung. §15 — eine Meldung nennt Ursache und Abhilfe, und ein
    /// stilles „gespeichert" bei einer fehlerhaften Quelle wäre eine Lüge.
    /// </summary>
    private string FeedbackFor(string sourceId, string erfolg)
    {
        var befunde = _store.Issues
            .Where(i => i.Path.StartsWith($"dataSources[{sourceId}]", StringComparison.Ordinal))
            .Where(static i => i.Severity == IssueSeverity.Error)
            .Select(static i => i.Message)
            .ToList();

        return befunde.Count == 0 ? erfolg : string.Join(Environment.NewLine, befunde);
    }

    /// <summary>Übernimmt die Nachschlage-Einstellungen (§21.4).</summary>
    [RelayCommand]
    public void ApplyLookupSettings()
    {
        if (_applying)
        {
            return;
        }

        var config = _store.Current;

        _store.Save(config with
        {
            CallerLookup = config.CallerLookup with
            {
                LookupIncoming = LookupIncoming,
                LookupOutgoing = LookupOutgoing,
                LookupInternalNumbers = LookupInternalNumbers,
            },
        });
    }

    // --- Zugangsdaten ---

    /// <summary>
    /// Legt ein Geheimnis ab.
    ///
    /// <b>Es geht direkt in den <c>SecretStore</c></b> und nie in die
    /// Konfigurationsdatei — die wird exportiert, in Tickets gelegt und über
    /// das Netz verteilt (§21.2).
    /// </summary>
    [RelayCommand]
    public void SaveSecret(IntegrationSecretRow? row)
    {
        if (row is null || row.Value.Length == 0)
        {
            return;
        }

        _secrets.Set(row.Reference, row.Value);

        // Sofort vergessen: der Wert hat im Arbeitsspeicher eines ViewModels
        // nichts verloren, sobald er abgelegt ist.
        row.Value = string.Empty;
        row.IsConfigured = true;

        var meldung = $"{row.Label} ist hinterlegt. Jetzt die Verbindung testen.";

        // Die Liste zeigt „Zugangsdaten fehlen" — das muss verschwinden.
        // Reload() baut die Auswahl neu auf und setzt DetailFeedback zurück,
        // deshalb erst danach schreiben.
        Reload();

        DetailFeedback = meldung;
    }

    // --- Testabruf ---

    /// <summary>
    /// Führt einen Testabruf gegen die ausgewählte Quelle aus (§21.4).
    ///
    /// <b>Beide Fähigkeiten, wenn die Quelle beide hat.</b> Vorher wurde immer
    /// nur die erste geprüft, und das Feld „Suchbegriff" war für jede Quelle
    /// mit <c>lookupByPhone</c> wirkungslos — es stand trotzdem da.
    /// </summary>
    [RelayCommand]
    public async Task TestAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        IsTesting = true;
        TestFields.Clear();
        TestResponse = string.Empty;

        var teile = new List<string>();

        try
        {
            if (row.Source.LookupByPhone is not null)
            {
                var ergebnis = await _tester.TestLookupAsync(
                    row.Source,
                    TestNumber,
                    _settings.Current.Advanced.CountryPrefix).ConfigureAwait(true);

                teile.Add($"Anruferkontext: {Describe(ergebnis)}");
                Show(row.Id, ergebnis, "Anruferkontext");
            }

            if (row.Source.SearchContacts is not null)
            {
                var ergebnis = await _tester.TestSearchAsync(row.Source, TestQuery)
                    .ConfigureAwait(true);

                teile.Add($"Kontaktsuche: {Describe(ergebnis)}");
                Show(row.Id, ergebnis, "Kontaktsuche");
            }

            TestSummary = teile.Count == 0
                ? "Diese Quelle hat keine Fähigkeit eingerichtet — im JSON unten fehlt ein "
                    + "Block lookupByPhone oder searchContacts."
                : string.Join(Environment.NewLine, teile);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private static string Describe(ConnectionTestResult result) => result.IsSuccess
        ? $"HTTP {result.StatusCode} in {result.Elapsed.TotalMilliseconds:F0} ms, "
            + $"{result.Fields.Count} Felder gemappt"
        : result.Message ?? $"Fehlgeschlagen ({result.Outcome}).";

    private void Show(string sourceId, ConnectionTestResult result, string was)
    {
        foreach (var (name, value) in result.Fields)
        {
            TestFields.Add($"{was} · {name} = {value}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            TestFields.Add($"⚠ {was} · {diagnostic}");
        }

        if (result.ResponsePreview is not { Length: > 0 } zumAnsehen)
        {
            return;
        }

        TestResponse = TestResponse.Length == 0
            ? zumAnsehen
            : TestResponse + Environment.NewLine + Environment.NewLine + zumAnsehen;

        // Für die Kartenvorschau gemerkt — <b>nur im Arbeitsspeicher</b>
        // (§21.2). Vorher wurde die Antwort weggeworfen, und der Designer
        // hätte nichts zu zeigen gehabt als die erfundene Beispielantwort.
        //
        // <b>Und hier stand bis zum 13.09.2026 dieselbe gekürzte Fassung, die
        // eine Zeile darüber ins Textfeld geht</b> — über 8192 Zeichen ist das
        // kein gültiges JSON mehr, und der Probenspeicher lehnte es still ab.
        // Die Anzeige nimmt die Vorschau, die Verarbeitung den Rumpf.
        if (result.IsSuccess && result.ResponseBody is { Length: > 0 } rohdaten)
        {
            _samples.SetFromLiveCall(sourceId, rohdaten);
        }
    }

    // --- Ganze Datei ---

    /// <summary>
    /// Gibt die Konfiguration aus — <b>ohne Geheimnisse</b>, weil dort keine
    /// stehen: die Datei trägt nur Verweise darauf.
    /// </summary>
    public string Export() => IntegrationConfigStore.Serialize(_store.Current);

    /// <summary>
    /// Wie viele Quellen ein Einlesen ersetzen würde — für die Rückfrage
    /// davor.
    ///
    /// <b>Einlesen ersetzt die ganze Datei.</b> Das ist für die
    /// Provisionierung richtig und beim Hinzufügen einer zweiten Quelle
    /// falsch; hinzugefügt wird über den Katalog. Wer eine Datei einliest,
    /// soll vorher lesen, was er verliert — genau darüber ist am 07.09.2026
    /// das Gesprächsjournal verschwunden.
    /// </summary>
    public int SourcesAtRisk => _store.Current.DataSources.Count;

    /// <summary>
    /// Liest eine Konfiguration ein.
    ///
    /// <b>Eine kaputte Datei ändert nichts</b> — dieselbe Haltung wie beim
    /// Einlesen der Einstellungen: sie meldet, was fehlt, statt die laufende
    /// Konfiguration halb zu überschreiben.
    /// </summary>
    public bool TryImport(string json, out IReadOnlyList<string> problems)
    {
        if (!_store.TryRead(json, out var config, out var issues))
        {
            problems = [.. issues.Select(static i => i.ToString())];
            return false;
        }

        _store.Save(config!);

        // Die Testdaten gehören zu Quellen, die es jetzt vielleicht nicht mehr
        // gibt — eine Vorschau mit der Antwort einer entfernten Quelle wäre
        // irreführend.
        _samples.Clear();

        problems = [];
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnStoreChanged;
        _cards.Changed -= OnCardsChanged;
    }
}
