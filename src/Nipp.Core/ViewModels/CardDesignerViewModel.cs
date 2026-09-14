using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Ein Eintrag der Palette links im Designer.
/// </summary>
/// <param name="Path">Wie es im Ausdruck heisst.</param>
/// <param name="Label">Die vorgeschlagene Beschriftung.</param>
/// <param name="Group">Woraus es kommt — Quelle oder „Anruf".</param>
/// <param name="Hint">Was klein darunter steht.</param>
/// <param name="IsKnown">Ob der Name im Feldkatalog steht.</param>
public sealed record PaletteEntry(
    string Path,
    string Label,
    string Group,
    string? Hint = null,
    bool IsKnown = true)
{
    /// <summary>
    /// Was eine Sprachausgabe sagt. Ohne das nimmt die Automation den
    /// <c>ToString()</c> des Datensatzes — derselbe Fehler, der im Katalog
    /// mehrere Tausend Zeichen vorlesen liess.
    /// </summary>
    public string AutomationName => $"{Label}, {Group}";

    public string Detail => IsKnown ? Path : $"{Path} — eigenes Feld";
}

/// <summary>
/// Eine Gruppe der Palette.
/// </summary>
public sealed record PaletteGroup(string Name, IReadOnlyList<PaletteEntry> Entries);

/// <summary>
/// Eine Karte, von der sich der Aufbau übernehmen lässt.
/// </summary>
/// <param name="Kind">Welche Art.</param>
/// <param name="Definition">Was dort gerade gilt, oder <c>null</c>.</param>
/// <param name="IsCustom">Ob es eine eigene Karte ist oder die mitgelieferte.</param>
public sealed record CardCopySource(CardKind Kind, CardDefinition? Definition, bool IsCustom)
{
    public string KindName => Kind switch
    {
        CardKind.ActiveExpanded => "Gespräch",
        CardKind.IncomingCompact => "Eingehender Anruf",
        CardKind.Toast => "Benachrichtigung",
        _ => "Anrufliste",
    };

    /// <summary>
    /// Was in der Auswahl steht.
    ///
    /// <b>Und was eine Sprachausgabe vorliest</b> — ohne
    /// <c>AutomationProperties.Name</c> nähme die Automation den
    /// <c>ToString()</c> des Datensatzes, und darin steckt die ganze
    /// Kartenbeschreibung. Derselbe Fehler wie im Katalog, dort mehrere
    /// Tausend Zeichen.
    /// </summary>
    public string Label => IsCustom
        ? $"{KindName} (eigene)"
        : $"{KindName} (mitgeliefert)";

    public override string ToString() => Label;
}

/// <summary>
/// Der Karten-Designer (K4, I9, ADR-032).
///
/// <para><b>Warum der ganze Zustand hier liegt und nicht im Fenster.</b>
/// <c>Nipp.App</c> hat kein Testprojekt — was dort steht, ist nur am Gerät
/// prüfbar. Beim <c>ToastComposer</c> war das schon einmal der Grund, Logik in
/// den Kern zu ziehen, und ein Editor hat mehr Zustand als alles andere in
/// nipp: Auswahl, Einfügen, Verschieben, Rückgängig, Prüfung, Vorschau. Das
/// Fenster zeichnet und ruft; entschieden wird hier.</para>
///
/// <para><b>Rückgängig über Schnappschüsse, nicht über umgekehrte
/// Befehle.</b> Vor jeder Änderung wird die <see cref="CardDefinition"/>
/// abgelegt — ein unveränderlicher <c>record</c> mit typisch zwanzig
/// Bausteinen, also nichts. Ein Befehlsmuster mit von Hand geschriebenen
/// Gegenoperationen wäre schneller und hätte je Operation eine Gelegenheit,
/// falsch zu sein; einen Schnappschuss kann man nicht falsch zurücklegen. Bei
/// dieser Datenmenge ist die Genauigkeit die bessere Wahl.</para>
///
/// <para><b>Die Vorschau ist dieselbe Engine wie im Gespräch.</b> Eine
/// Vorschau, die ihre Werte anders gewinnt als der Anruf, ist keine — und der
/// Fall, für den sie zählt, ist der häufigste beim Einrichten: der Feldpfad
/// ist falsch, und die Zeile bleibt leer. Das soll <b>hier</b> zu sehen sein
/// und nicht erst am Telefon.</para>
/// </summary>
public sealed partial class CardDesignerViewModel : ObservableObject
{
    /// <summary>
    /// Wie viele Schritte zurückgenommen werden können.
    ///
    /// Fünfzig, weil das Zusammenstellen einer Karte aus vielen kleinen
    /// Schritten besteht — Beschriftung tippen, Wert wählen, verschieben — und
    /// weil zehn zu wenig sind, um einen Irrweg zu verlassen.
    /// </summary>
    public const int MaxUndoSteps = 50;

    private readonly IntegrationConfigStore _store;
    private readonly TestSampleStore _samples;
    private readonly ILogger<CardDesignerViewModel> _logger;
    private readonly IntegrationTester? _tester;
    private readonly SettingsService? _settings;

    /// <summary>Der laufende Abruf — für die Prüfung auf die überholte Antwort.</summary>
    private CancellationTokenSource? _lookupRequest;

    private readonly Stack<CardDefinition> _undo = new();
    private readonly Stack<CardDefinition> _redo = new();

    /// <summary>Der Stand beim Öffnen — für „Verwerfen" und für die Frage, ob etwas offen ist.</summary>
    private readonly CardDefinition _opened;

    /// <summary>
    /// Sperrt das Aufzeichnen, während ein Rückgängig selbst den Entwurf
    /// austauscht.
    /// </summary>
    private bool _restoring;

    /// <param name="tester">
    /// Für den Abruf hinter der Vorschau. <c>null</c> heisst: die Vorschau
    /// bleibt bei den erfundenen Beispieldaten — so laufen die Tests des
    /// Designers ohne Netz.
    /// </param>
    /// <param name="settings">
    /// Nur für <c>Advanced.CountryPrefix</c>: ohne Präfix wird aus „079…" keine
    /// E.164-Nummer, und die Quelle bekäme eine Nummer, die sie nicht kennt.
    /// </param>
    public CardDesignerViewModel(
        CardKind kind,
        CardResolver cards,
        IntegrationConfigStore store,
        TestSampleStore samples,
        ILogger<CardDesignerViewModel> logger,
        IntegrationTester? tester = null,
        SettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(cards);

        _store = store;
        _samples = samples;
        _logger = logger;
        _tester = tester;
        _settings = settings;

        Kind = kind;
        IsCustom = cards.IsCustom(kind);

        _opened = cards.DefinitionFor(kind) ?? CardDraft.Empty(kind).ToDefinition();
        _draft = CardDraft.FromDefinition(_opened);

        BuildPalette();
        BuildCopySources(cards);
        Refresh();
    }

    /// <summary>Welche Kartenart bearbeitet wird.</summary>
    public CardKind Kind { get; }

    /// <summary>Ob beim Öffnen schon eine eigene Karte galt.</summary>
    public bool IsCustom { get; }

    public string Title => Kind switch
    {
        CardKind.ActiveExpanded => "Karte im Gespräch",
        CardKind.IncomingCompact => "Karte beim eingehenden Anruf",
        CardKind.Toast => "Benachrichtigung",
        _ => "Karte in der Anrufliste",
    };

    /// <summary>
    /// Was für diese Art gilt und was nicht — steht als Satz im Fenster.
    ///
    /// Beim Toast ist das keine Höflichkeit: Windows nimmt drei Textzeilen,
    /// und was nicht passt, ist nicht abgeschnitten, sondern <b>weg</b>.
    /// </summary>
    public string KindNote => Kind switch
    {
        CardKind.Toast =>
            "Eine Benachrichtigung nimmt genau drei Textzeilen plus die Rufnummer klein "
                + "darunter. Felder, Schaltflächen und Trennlinien erscheinen dort nicht.",

        CardKind.IncomingCompact =>
            "Kurz halten: diese Karte erscheint im Moment des Klingelns. Wer klingelt, "
                + "soll auf einen Blick sehen, wer das ist — nicht eine Aufstellung lesen.",

        _ =>
            "Das Fenster ist rund 400 Pixel breit. Zwei Werte nebeneinander gehen, drei "
                + "werden unlesbar.",
    };

    [ObservableProperty]
    private CardDraft _draft;

    /// <summary>Der ausgewählte Baustein — der Eigenschaftenbereich hängt daran.</summary>
    [ObservableProperty]
    private DraftElement? _selectedElement;

    /// <summary>Die Zeile, in die als nächstes eingefügt wird.</summary>
    [ObservableProperty]
    private DraftRow? _selectedRow;

    /// <summary>Die aufgelöste Vorschau — was der Renderer zeichnen würde.</summary>
    [ObservableProperty]
    private CardModel _preview = CardModel.Empty;

    /// <summary>Was an der Karte gerade nicht stimmt, mit Stelle und Abhilfe.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>Die Felderpalette, nach Herkunft gruppiert.</summary>
    public ObservableCollection<PaletteGroup> Palette { get; } = [];

    /// <summary>Woher die Vorschaudaten kommen — Beispiel oder echter Abruf.</summary>
    [ObservableProperty]
    private string _previewSource = string.Empty;

    /// <summary>
    /// Die Nummer, mit der die Vorschau rechnet.
    ///
    /// <b>Sie stand fest auf der Beispielnummer</b>, obwohl
    /// <c>BuildSnapshot</c> sie von Anfang an als Parameter nimmt — wer sehen
    /// wollte, wie seine Karte bei einem echten Kunden aussieht, konnte es
    /// nicht.
    /// </summary>
    [ObservableProperty]
    private string _previewNumber = TestSampleStore.DefaultPreviewNumber.National;

    /// <summary>Ob gerade abgerufen wird — für den Wartekringel und die Sperre.</summary>
    [ObservableProperty]
    private bool _isLookingUp;

    /// <summary>Was der letzte Abruf ergeben hat, oder leer.</summary>
    [ObservableProperty]
    private string _lookupResult = string.Empty;

    /// <summary>
    /// Ob ein Abruf überhaupt möglich ist — Dienst da und Quelle eingerichtet.
    ///
    /// <para><b>Ohne Prüfung auf <c>Enabled</c>, und das ist Absicht.</b> Am
    /// 13.09.2026 stand hier einmal eine, weil der Abruf nur eingeschaltete
    /// Quellen fragt. Der Ablauf beim Einrichten geht aber andersherum:
    /// hinzufügen, Testabruf, <em>dann</em> einschalten (ADR-040). Ein Knopf,
    /// der genau dabei grau ist, sperrt den Schritt, für den es ihn gibt. Wer
    /// ohne eingeschaltete Quelle drückt, bekommt den Grund als Satz.</para>
    /// </summary>
    public bool CanLookup =>
        _tester is not null && _store.Current.DataSources.Any(s => s.LookupByPhone is not null);

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Ob gespeichert werden darf.
    ///
    /// <b>Ein Befund sperrt das Speichern.</b> Eine Karte mit einem kaputten
    /// Ausdruck würde vom <see cref="CardResolver"/> abgewiesen und auf die
    /// mitgelieferte zurückfallen — der Benutzer sähe dann seine Arbeit nicht
    /// und wüsste nicht, warum. Besser hier sperren und sagen, wo es klemmt.
    /// </summary>
    public bool CanSave => Problems.Count == 0 && Draft.HasUsableId;

    /// <summary>Ob die Karte mehr als drei Textzeilen hat — nur beim Toast von Belang.</summary>
    public int TextRowCount => Draft.AllElements.Count(static e => e.Kind == DraftElementKind.Text);

    // --- Palette ---

    private void BuildPalette()
    {
        Palette.Clear();

        // Die eingebauten zuerst: sie gibt es immer, auch ohne eine einzige
        // eingerichtete Quelle, und ohne sie hätte die Palette auf einem
        // frischen Gerät nichts anzubieten.
        foreach (var gruppe in FieldCatalog.BuiltIn.GroupBy(static e => e.Group))
        {
            Palette.Add(new PaletteGroup(
                gruppe.Key,
                [.. gruppe.Select(static e => new PaletteEntry(e.Path, e.Label, e.Group, e.Hint))]));
        }

        // Dann die Bedeutungen. Sie stehen vor den Quellen, weil eine Karte,
        // die sie benutzt, bei jedem Kunden trägt — und weil das der Weg ist,
        // den die mitgelieferten Karten gehen.
        Palette.Add(new PaletteGroup(
            "Bedeutung (quellenunabhängig)",
            [
                .. FieldCatalog.RoleNames.Select(static r => new PaletteEntry(
                    $"role('{r}')",
                    FieldCatalog.NamesFor(FieldCatalog.RoleFromExpression(r)!.Value) is { Count: > 0 } namen
                        ? FieldCatalog.LabelFor(namen[0])
                        : r,
                    "Bedeutung",
                    Hint: "Die erste Quelle nach Reihenfolge, die so ein Feld liefert")),
            ]));

        // Und zuletzt, was die eingerichteten Quellen wirklich mappen.
        foreach (var quelle in _store.Current.DataSources.OrderBy(static s => s.Priority))
        {
            var namen = new List<string>();

            if (quelle.LookupByPhone is { } lookup)
            {
                namen.AddRange(lookup.Mapping.Keys);
            }

            if (namen.Count == 0)
            {
                continue;
            }

            Palette.Add(new PaletteGroup(
                quelle.DisplayName,
                [
                    .. namen.Distinct(StringComparer.Ordinal).Select(n => new PaletteEntry(
                        $"{quelle.Id}.{n}",
                        FieldCatalog.LabelFor(n),
                        quelle.DisplayName,
                        IsKnown: FieldCatalog.Find(n) is not null)),
                ]));
        }
    }

    // --- Bearbeiten ---

    /// <summary>
    /// Legt den Stand ab, bevor etwas geändert wird.
    ///
    /// <b>Jede Änderung geht durch diese Methode.</b> Wer sie umgeht, hat
    /// einen Schritt, der sich nicht zurücknehmen lässt — und das fällt erst
    /// auf, wenn jemand ihn zurücknehmen will.
    /// </summary>
    private void Record()
    {
        if (_restoring)
        {
            return;
        }

        _undo.Push(Draft.ToDefinition());
        _redo.Clear();

        while (_undo.Count > MaxUndoSteps)
        {
            // Stack kennt kein Entfernen von unten. Bei fünfzig Einträgen ist
            // ein Neuaufbau billiger als eine eigene Datenstruktur.
            var behalten = _undo.Take(MaxUndoSteps).Reverse().ToList();

            _undo.Clear();

            foreach (var stand in behalten)
            {
                _undo.Push(stand);
            }
        }

        HasUnsavedChanges = true;
    }

    /// <summary>Fügt ein Feld aus der Palette in die ausgewählte Zeile ein.</summary>
    [RelayCommand]
    public void AddField(PaletteEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        Record();

        var baustein = new DraftElement
        {
            Kind = DraftElementKind.Field,
            Label = entry.Label,
            EmptyText = null,
        };

        var wert = DraftValue.FromExpression(entry.Path);
        baustein.Value.Mode = wert.Mode;
        baustein.Value.Text = wert.Text;

        Einfuegen(baustein);
    }

    /// <summary>Fügt einen Baustein einer bestimmten Art ein.</summary>
    [RelayCommand]
    public void AddElement(DraftElementKind kind)
    {
        Record();

        var baustein = new DraftElement { Kind = kind };

        if (kind == DraftElementKind.Field)
        {
            baustein.EmptyText = null;
        }

        if (kind == DraftElementKind.Badge)
        {
            baustein.Value.Mode = DraftValueMode.Literal;
            baustein.Value.Text = "VIP";
        }

        Einfuegen(baustein);
    }

    private void Einfuegen(DraftElement element)
    {
        var zeile = SelectedRow ?? Draft.Sections.LastOrDefault()?.Rows.LastOrDefault();

        if (zeile is null)
        {
            var abschnitt = Draft.Sections.FirstOrDefault();

            if (abschnitt is null)
            {
                abschnitt = new DraftSection { Id = "kopf" };
                Draft.Sections.Add(abschnitt);
            }

            zeile = CardDraft.NewRow();
            abschnitt.Rows.Add(zeile);
        }

        var spalte = zeile.Columns.LastOrDefault();

        if (spalte is null)
        {
            spalte = new DraftColumn { Span = CardLayout.Columns };
            zeile.Columns.Add(spalte);
        }

        spalte.Elements.Add(element);

        SelectedRow = zeile;
        SelectedElement = element;

        Refresh();
    }

    /// <summary>
    /// Die Karten, von denen sich der Aufbau übernehmen lässt — alle Arten
    /// ausser der bearbeiteten.
    ///
    /// Der Zusatz sagt, ob dort eine eigene Karte gilt oder die mitgelieferte:
    /// „Gespräch (eigene)" und „Gespräch (mitgeliefert)" sind verschiedene
    /// Vorlagen, und wer übernimmt, will wissen, welche er bekommt.
    /// </summary>
    public IReadOnlyList<CardCopySource> CopySources { get; private set; } = [];

    /// <summary>
    /// Übernimmt den Aufbau einer anderen Karte.
    ///
    /// <para><b>Kennung, Name und Art bleiben.</b> Käme die Kennung mit,
    /// stünden zwei Karten mit derselben in der Datei, und der Validator meldete
    /// „Die Kennung kommt mehrfach vor" — ein Befund für etwas, das der Benutzer
    /// nicht getan hat. Übernommen werden die Abschnitte, sonst nichts.</para>
    ///
    /// <para><b>Und es wird nicht gekürzt.</b> Wer die Gesprächskarte in den
    /// Toast übernimmt, bekommt mehr als drei Textzeilen und damit einen
    /// Befund, der das Speichern sperrt und sagt, was zu viel ist. Ein
    /// stillschweigendes Kürzen wäre der schlechtere Weg: es nähme dem
    /// Benutzer die Entscheidung, welche Zeile fällt.</para>
    ///
    /// <para>Ein Schritt, kein Neuanfang — <see cref="Undo"/> holt den vorigen
    /// Stand zurück.</para>
    /// </summary>
    [RelayCommand]
    public void CopyFrom(CardCopySource? source)
    {
        if (source?.Definition is not { } vorlage)
        {
            return;
        }

        Record();

        var uebernommen = Draft.ToDefinition() with { Sections = vorlage.Sections };

        _restoring = true;

        try
        {
            Draft = CardDraft.FromDefinition(uebernommen);
            SelectedElement = null;
            SelectedRow = Draft.Sections.FirstOrDefault()?.Rows.FirstOrDefault();
        }
        finally
        {
            _restoring = false;
        }

        HasUnsavedChanges = true;

        CardDesignerLog.CopiedFrom(_logger, source.Kind.ToString(), Kind.ToString());

        Refresh();
    }

    private void BuildCopySources(CardResolver cards)
    {
        CopySources =
        [
            .. Enum.GetValues<CardKind>()
                .Where(k => k != Kind)
                .Select(k => new CardCopySource(k, cards.DefinitionFor(k), cards.IsCustom(k)))
                .Where(static q => q.Definition is not null),
        ];
    }

    /// <summary>Hängt eine neue Zeile an den Abschnitt an.</summary>
    [RelayCommand]
    public void AddRow()
    {
        Record();

        var abschnitt = Draft.Sections.LastOrDefault();

        if (abschnitt is null)
        {
            abschnitt = new DraftSection { Id = "kopf" };
            Draft.Sections.Add(abschnitt);
        }

        var zeile = CardDraft.NewRow();
        abschnitt.Rows.Add(zeile);

        SelectedRow = zeile;

        Refresh();
    }

    /// <summary>Entfernt den ausgewählten Baustein.</summary>
    [RelayCommand]
    public void RemoveSelected()
    {
        if (SelectedElement is not { } baustein)
        {
            return;
        }

        Record();

        foreach (var spalte in Draft.Sections.SelectMany(static s => s.Rows).SelectMany(static r => r.Columns))
        {
            if (spalte.Elements.Remove(baustein))
            {
                break;
            }
        }

        SelectedElement = null;

        Refresh();
    }

    /// <summary>
    /// Verschiebt den ausgewählten Baustein um eine Stelle nach oben oder
    /// unten — <b>über Spalten- und Zeilengrenzen hinweg</b>.
    ///
    /// <para>Die Karte wird als eine Folge von Plätzen betrachtet, nicht als
    /// Baum: „nach unten" heisst an den nächsten Platz, und ob der in derselben
    /// Spalte, in der Nachbarspalte oder in der nächsten Zeile liegt, soll
    /// niemanden beschäftigen. Wer einen Baustein an eine ganz andere Stelle
    /// will, zieht ihn.</para>
    /// </summary>
    [RelayCommand]
    public void MoveSelected(int direction)
    {
        if (SelectedElement is not { } baustein || direction == 0)
        {
            return;
        }

        var plaetze = Draft.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .ToList();

        var quelle = plaetze.FirstOrDefault(s => s.Elements.Contains(baustein));

        if (quelle is null)
        {
            return;
        }

        var index = quelle.Elements.IndexOf(baustein);
        var ziel = index + direction;

        Record();

        if (ziel >= 0 && ziel < quelle.Elements.Count)
        {
            quelle.Elements.Move(index, ziel);
            Refresh();

            return;
        }

        // Über die Spaltengrenze: in die nächste beziehungsweise vorige
        // Spalte, dort an den passenden Rand.
        var spaltenIndex = plaetze.IndexOf(quelle);
        var nachbarIndex = spaltenIndex + direction;

        if (nachbarIndex < 0 || nachbarIndex >= plaetze.Count)
        {
            // Ganz oben beziehungsweise ganz unten — nichts zu tun. Der
            // Schnappschuss von oben bleibt liegen; das kostet einen
            // wirkungslosen Rückgängig-Schritt und ist besser als ein
            // Sonderfall, der irgendwann falsch ist.
            return;
        }

        var nachbar = plaetze[nachbarIndex];

        quelle.Elements.Remove(baustein);

        if (direction > 0)
        {
            nachbar.Elements.Insert(0, baustein);
        }
        else
        {
            nachbar.Elements.Add(baustein);
        }

        Refresh();
    }

    /// <summary>
    /// Verschiebt einen Baustein an eine bestimmte Stelle — das Ziel eines
    /// Ziehens.
    /// </summary>
    /// <param name="element">Was verschoben wird.</param>
    /// <param name="target">In welche Spalte.</param>
    /// <param name="index">An welche Stelle darin; <c>-1</c> heisst „ans Ende".</param>
    public void MoveTo(DraftElement element, DraftColumn target, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(target);

        var quelle = Draft.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .FirstOrDefault(s => s.Elements.Contains(element));

        if (quelle is null)
        {
            return;
        }

        Record();

        quelle.Elements.Remove(element);

        var stelle = index < 0 || index > target.Elements.Count
            ? target.Elements.Count
            : index;

        target.Elements.Insert(stelle, element);

        SelectedElement = element;

        Refresh();
    }

    /// <summary>
    /// Teilt die Zeile des ausgewählten Bausteins in zwei Spalten oder führt
    /// sie wieder zusammen — „zwei Werte nebeneinander".
    ///
    /// <b>Zwei und nicht beliebig viele:</b> das Fenster ist rund 400 Pixel
    /// breit, ein Drittel davon zeigt nichts mehr. Das Raster hat sechs
    /// Einheiten, drei und drei.
    /// </summary>
    [RelayCommand]
    public void TogglePaired()
    {
        if (SelectedElement is not { } baustein)
        {
            return;
        }

        var zeile = Draft.Sections
            .SelectMany(static s => s.Rows)
            .FirstOrDefault(r => r.Columns.Any(c => c.Elements.Contains(baustein)));

        if (zeile is null)
        {
            return;
        }

        Record();

        if (zeile.Columns.Count > 1)
        {
            // Zusammenführen: alles in die erste Spalte, in der Reihenfolge,
            // in der es dasteht.
            var alle = zeile.Columns.SelectMany(static c => c.Elements).ToList();

            zeile.Columns.Clear();

            var spalte = new DraftColumn { Span = CardLayout.Columns };

            foreach (var e in alle)
            {
                spalte.Elements.Add(e);
            }

            zeile.Columns.Add(spalte);
        }
        else
        {
            var erste = zeile.Columns[0];
            erste.Span = CardLayout.Columns / 2;

            var zweite = new DraftColumn { Span = CardLayout.Columns / 2 };

            // Der ausgewählte Baustein wandert nach rechts, alles andere
            // bleibt links — das ist die Erwartung, wenn jemand auf einer
            // Zeile „nebeneinander" wählt.
            if (erste.Elements.Count > 1)
            {
                erste.Elements.Remove(baustein);
                zweite.Elements.Add(baustein);
            }

            zeile.Columns.Add(zweite);
        }

        Refresh();
    }

    // --- Rückgängig ---

    [RelayCommand]
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Draft.ToDefinition());
        Wiederherstellen(_undo.Pop());
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Draft.ToDefinition());
        Wiederherstellen(_redo.Pop());
    }

    private void Wiederherstellen(CardDefinition stand)
    {
        _restoring = true;

        try
        {
            Draft = CardDraft.FromDefinition(stand);
            SelectedElement = null;
            SelectedRow = Draft.Sections.FirstOrDefault()?.Rows.FirstOrDefault();
        }
        finally
        {
            _restoring = false;
        }

        HasUnsavedChanges = !Gleich(stand, _opened);

        Refresh();
    }

    private static bool Gleich(CardDefinition a, CardDefinition b) =>
        string.Equals(
            IntegrationConfigStore.SerializeCard(a),
            IntegrationConfigStore.SerializeCard(b),
            StringComparison.Ordinal);

    // --- Speichern ---

    /// <summary>
    /// Speichert die Karte in die Konfiguration.
    ///
    /// <b>Sie ersetzt die vorhandene ihrer Art</b> — es gilt eine je Art. Und
    /// sie geht durch dieselbe Prüfung wie eine von Hand geschriebene: der
    /// Designer bekommt keine Ausnahme.
    /// </summary>
    [RelayCommand]
    public void Save()
    {
        if (!CanSave)
        {
            return;
        }

        var definition = Draft.ToDefinition();
        var config = _store.Current;

        var karten = config.Cards
            .Where(k => k.Kind != definition.Kind)
            .Append(definition)
            .ToList();

        _store.Save(config with { Cards = karten });

        CardDesignerLog.Saved(
            _logger,
            definition.Id,
            definition.Kind.ToString(),
            definition.Sections.Count,
            Draft.AllElements.Count());

        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Setzt auf die mitgelieferte Karte zurück — <b>entfernt</b> die eigene
    /// aus der Konfiguration.
    ///
    /// Nicht „Vorlage laden": eine Kopie der mitgelieferten Karte als eigene
    /// zu speichern hiesse, dass sie bei einer Verbesserung in nipp nicht
    /// mitkommt.
    /// </summary>
    [RelayCommand]
    public void ResetToDefault()
    {
        var config = _store.Current;

        _store.Save(config with
        {
            Cards = [.. config.Cards.Where(k => k.Kind != Kind)],
        });

        _restoring = true;

        try
        {
            Draft = CardDraft.FromDefinition(
                DefaultCards.For(Kind) ?? CardDraft.Empty(Kind).ToDefinition());

            SelectedElement = null;
        }
        finally
        {
            _restoring = false;
        }

        _undo.Clear();
        _redo.Clear();

        HasUnsavedChanges = false;

        Refresh();
    }

    /// <summary>Verwirft alles und stellt den Stand beim Öffnen her.</summary>
    [RelayCommand]
    public void Discard()
    {
        _undo.Clear();
        _redo.Clear();

        Wiederherstellen(_opened);

        HasUnsavedChanges = false;
    }

    // --- Abruf für die Vorschau ---

    /// <summary>
    /// Eine andere Nummer heisst eine andere Vorschau, auch ohne Abruf:
    /// <c>formatPhone</c> und die Anruf-Felder kommen aus ihr. Billig — es ist
    /// derselbe Lauf wie nach jeder Bearbeitung, ohne Netz.
    /// </summary>
    partial void OnPreviewNumberChanged(string value) => Refresh();

    /// <summary>
    /// Holt zu <see cref="PreviewNumber"/> eine echte Antwort und legt sie der
    /// Vorschau unter.
    ///
    /// <para><b>Derselbe Weg wie der Testabruf in den Einstellungen</b>, und
    /// nicht <c>CallerContextService</c>: nur so unterscheidet
    /// <see cref="PreviewSource"/> weiterhin „erfundene Beispieldaten" von
    /// „Antwort des letzten Testabrufs", und der Designer zeigt dasselbe wie
    /// die Einrichtung. Der Kontextdienst brächte lokale Kontakte und die
    /// Prioritätsauflösung mit — und umginge die Herkunftsanzeige.</para>
    ///
    /// <para><b>Nicht aus <see cref="Refresh"/> heraus.</b> Refresh läuft nach
    /// jeder Bearbeitung auf dem Thread, der alle 20 ms <c>Core.Iterate()</c>
    /// bedient; ein Netzaufruf dort ruckelt das Telefon. Deshalb ein eigener
    /// Befehl, den jemand auslöst.</para>
    ///
    /// <para><b>Der Entwurf bleibt unberührt.</b> Ein Abruf ist kein
    /// Bearbeitungsschritt — er legt keinen Schnappschuss an, setzt
    /// <c>HasUnsavedChanges</c> nicht und ist nicht rückgängig zu machen.</para>
    /// </summary>
    [RelayCommand]
    private async Task RunLookupAsync(CancellationToken cancellationToken)
    {
        if (_tester is null)
        {
            return;
        }

        // Ein zweiter Klick überholt den ersten; dessen Antwort zählt dann
        // nicht mehr.
        if (_lookupRequest is { } laufend)
        {
            await laufend.CancelAsync().ConfigureAwait(true);
            laufend.Dispose();
        }

        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lookupRequest = request;

        var nummer = VorschauNummer();
        var praefix = _settings?.Current.Advanced.CountryPrefix;

        IsLookingUp = true;
        LookupResult = string.Empty;

        var quellen = _store.Current.DataSources
            .Where(s => s.Enabled && s.LookupByPhone is not null)
            .ToList();

        if (quellen.Count == 0)
        {
            IsLookingUp = false;
            LookupResult = "Keine eingeschaltete Quelle mit Anruferkontext — nichts abzurufen.";
            return;
        }

        var gelungen = 0;
        var gescheitert = new List<string>();

        try
        {
            foreach (var quelle in quellen)
            {
                ConnectionTestResult ergebnis;

                try
                {
                    ergebnis = await _tester
                        .TestLookupAsync(quelle, nummer.E164, praefix, request.Token)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    gescheitert.Add($"{quelle.Id}: {ex.GetType().Name}");
                    continue;
                }

                // <b>Die ungekürzte Fassung, nicht die zum Ansehen</b>
                // (13.09.2026). Hier stand die Vorschaufassung — der bei 8192 Zeichen
                // abgeschnittene Text mit «… (gekürzt)» am Ende. Der ist kein
                // gültiges JSON mehr, der Probenspeicher konnte ihn nicht
                // lesen, und die Vorschau zeigte weiter die erfundenen
                // Beispieldaten.
                if (ergebnis is { IsSuccess: true, ResponseBody: { Length: > 0 } rohdaten })
                {
                    // <b>Und gezählt wird, was übernommen wurde</b>, nicht was
                    // geantwortet hat. Vorher stand gelungen++ hier ohne
                    // Prüfung: die Statuszeile meldete Erfolg, während nichts
                    // angekommen war.
                    if (_samples.SetFromLiveCall(quelle.Id, rohdaten))
                    {
                        gelungen++;
                    }
                    else
                    {
                        gescheitert.Add($"{quelle.Id}: Antwort nicht lesbar");
                    }
                }
                else
                {
                    gescheitert.Add($"{quelle.Id}: {ergebnis.Message ?? ergebnis.Outcome.ToString()}");
                }
            }
        }
        finally
        {
            IsLookingUp = false;
        }

        // Zwischen Start und Antwort kann ein zweiter Abruf begonnen haben.
        if (!ReferenceEquals(request, _lookupRequest))
        {
            return;
        }

        // Ohne Nummer und ohne Antwortinhalt (§21.2, ADR-022): wie viele
        // Quellen geantwortet haben, genügt.
        // «Übernommen» und nicht «geantwortet»: eine Quelle kann antworten,
        // ohne dass die Vorschau davon etwas hat — und genau diesen
        // Unterschied hat die Zeile bis zum 13.09.2026 verschwiegen.
        LookupResult = gescheitert.Count == 0
            ? $"{gelungen} von {quellen.Count} Quellen übernommen."
            : $"{gelungen} von {quellen.Count} Quellen übernommen. "
                + string.Join(" · ", gescheitert);

        // Ein gescheiterter Abruf lässt stehen, was vorher zu sehen war: eine
        // leere Vorschau wäre die schlechtere Auskunft als eine alte.
        if (gelungen > 0)
        {
            Refresh();
        }
    }

    // --- Prüfung und Vorschau ---

    /// <summary>
    /// Baut Vorschau und Befunde neu.
    ///
    /// <b>Nach jeder Änderung</b>, und deshalb bewusst günstig gehalten: die
    /// Übersetzung einer Karte mit zwanzig Bausteinen ist ein Parserlauf über
    /// zwanzig kurze Ausdrücke. Der Designer läuft auf demselben Thread, der
    /// alle 20 ms <c>Core.Iterate()</c> bedient (§6) — wenn hier etwas teuer
    /// wird, ruckelt das Telefon.
    /// </summary>
    public void Refresh()
    {
        Problems.Clear();

        var definition = Draft.ToDefinition();

        foreach (var befund in CardDefinitionValidator.ValidateCard(definition))
        {
            Problems.Add(befund.Message);
        }

        if (!Draft.HasUsableId)
        {
            Problems.Add(
                "Die Kennung der Karte fehlt oder enthält Zeichen, die dort nicht hingehören. "
                    + "Erlaubt sind Buchstaben, Ziffern, Strich und Unterstrich.");
        }

        Preview = CardLayoutEngine.TryCompile(definition, out var uebersetzt, out _)
            ? CardLayoutEngine.Build(uebersetzt, Schnappschuss())
            : CardModel.Empty;

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(TextRowCount));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemText));
    }

    public bool HasProblems => Problems.Count > 0;

    public string ProblemText => string.Join(Environment.NewLine, Problems);

    /// <summary>
    /// Die eingegebene Nummer in ihren Formen — leer heisst die Beispielnummer.
    /// </summary>
    private PhoneNumberKey VorschauNummer()
    {
        if (string.IsNullOrWhiteSpace(PreviewNumber))
        {
            return TestSampleStore.DefaultPreviewNumber;
        }

        var praefix = _settings?.Current.Advanced.CountryPrefix ?? "+41";

        return PhoneNumberKey.From(PreviewNumber, new NumberNormalizer(praefix));
    }

    private Services.Integrations.Context.ContextSnapshot Schnappschuss()
    {
        var schnappschuss = _samples.BuildSnapshot(
            _store.Current,
            VorschauNummer());

        var echte = _store.Current.DataSources
            .Count(s => _samples.IsFromLiveCall(s.Id));

        PreviewSource = _store.Current.DataSources.Count == 0
            ? "Keine Quelle eingerichtet — die Vorschau zeigt nur, was aus dem Anruf selbst kommt."
            : echte > 0
                ? $"Vorschau mit der Antwort des letzten Testabrufs ({echte} von "
                    + $"{_store.Current.DataSources.Count} Quellen)."
                : "Vorschau mit erfundenen Beispieldaten. Für die eigenen Felder in den "
                    + "Einstellungen einen Testabruf machen.";

        return schnappschuss;
    }
}
