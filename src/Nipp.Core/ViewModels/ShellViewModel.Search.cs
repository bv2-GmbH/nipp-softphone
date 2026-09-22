using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Search;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Die Kontaktsuche über mehrere Quellen (§21.4).
///
/// <b>Warum eine eigene Datei.</b> <c>ShellViewModel</c> hat über tausend
/// Zeilen und trägt die halbe Hauptansicht; die Suche als weiteren Block
/// hineinzuschreiben hätte sie unlesbar gemacht. <c>partial</c> kostet nichts
/// und hält zusammen, was zusammengehört.
///
/// <b>Was die Suche nicht ist.</b> Die Vorschlagsliste unter dem Nummernfeld
/// bleibt, wie sie ist: lokal, sofort, höchstens fünf Treffer (§8.1). Dort
/// zählt Tempo, und eine Netzquelle brächte Latenz genau dorthin, wo jemand
/// eine Nummer eintippt, die er ohnehin kennt. Diese Suche hier ist der
/// zweite Weg, den ADR-014 entfernt hatte — jetzt wieder da, aber für einen
/// anderen Zweck: sie fragt auch fremde Systeme.
///
/// <b>Und die beiden stehen nie gleichzeitig</b> (C5, ADR-051). Bis zum
/// 13.09.2026 taten sie es, mit denselben Leuten darin: <see
/// cref="ShowSearchResults"/> entscheidet das jetzt aus derselben Regel, die
/// auch die Eingabetaste trägt.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// Die Treffer der laufenden Suche. Eigene Sammlung neben
    /// <c>TeamContacts</c> und <c>OutlookContacts</c>, weil sie etwas anderes
    /// zeigt: nicht das Adressbuch, sondern eine Antwort.
    /// </summary>
    public ObservableCollection<ContactRow> SearchResults { get; } = [];

    /// <summary>Was jede befragte Quelle gerade tut — die Zeile über der Liste.</summary>
    public ObservableCollection<ContactSourceState> SearchSources { get; } = [];

    [ObservableProperty]
    private string _contactQuery = string.Empty;

    /// <summary>Ob gerade eine Suche läuft und noch eine Quelle aussteht.</summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>
    /// Ob die Suchansicht die Abschnitte Team und Outlook ersetzt.
    ///
    /// An der <b>Eingabe</b> festgemacht, nicht am Ergebnis: sonst
    /// verschwände die Liste bei jedem Zwischenstand ohne Treffer und käme
    /// gleich wieder — ein Flackern, das aussieht wie ein Fehler.
    /// </summary>
    public bool IsSearchActive => ContactQuery.Trim().Length > 0;

    /// <summary>
    /// Ob die Trefferliste die Abschnitte Team und Outlook ersetzt — und
    /// <b>damit zugleich, ob die Vorschlagsliste unter dem Feld wegbleibt</b>
    /// (C5).
    ///
    /// <para><b>Der Befund.</b> Wer einen Namen tippte, sah zwei Listen
    /// übereinander: die Vorschläge unter dem Feld und die Treffer im Bereich
    /// darunter. Und beide zeigten dieselben Leute — <c>UpdateSuggestions</c>
    /// ruft <c>ContactStore.Search</c>, der <c>LocalSnapshotSearchProvider</c>
    /// ruft dieselbe Methode. Derselbe Kollege stand zweimal auf dem
    /// Bildschirm, mit zwei Bedeutungen für dieselbe Geste: oben übernimmt ein
    /// Klick die Nummer, unten wählt ein Doppelklick. <b>Das ist die
    /// Doppelanzeige, die ADR-046 bei den zwei Suchfeldern aufgelöst hat — sie
    /// war eine Ebene tiefer gewandert.</b></para>
    ///
    /// <para><b>Die Regel ist dieselbe wie bei der Eingabetaste</b> (C4):
    /// <c>NumberNormalizer.IsDialable</c>. Wer eine <b>Nummer</b> tippt, will
    /// wählen — dann steht die Vorschlagsliste, die Wählhilfe, die Kontakte
    /// <b>und die Anrufliste</b> kennt. Wer einen <b>Namen</b> tippt, sucht —
    /// dann steht die Trefferliste, die fremde Systeme mitfragt, die Herkunft
    /// anzeigt, ein Kontextmenü trägt und in der Zeile aufklappt. Eine Regel,
    /// zwei Wirkungen; die Listen schliessen sich aus wie Löschkreuz und
    /// Verlaufspfeil im selben Feld.</para>
    ///
    /// <para>Ohne Netzquelle gibt es keine Trefferliste — dann bleibt alles,
    /// wie es war.</para>
    /// </summary>
    public bool ShowSearchResults =>
        HasContactSearch
        && IsSearchActive
        && !Services.Telephony.NumberNormalizer.IsDialable(ContactQuery);

    /// <summary>
    /// Ob die Suche überhaupt angeboten wird.
    ///
    /// <b>Nur wenn es eine Quelle gibt, die über das Netz sucht</b> (§21.4).
    /// Ohne eine solche wäre das Feld eine zweite Art, dasselbe Adressbuch zu
    /// durchsuchen, das die Vorschlagsliste schon durchsucht — und ADR-014
    /// hat es aus genau diesem Grund entfernt.
    /// </summary>
    public bool HasContactSearch => _search?.HasRemoteSources ?? false;

    /// <summary>
    /// Der Platzhalter des <b>Nummernfelds</b> (ADR-046). Kurz genug, um
    /// hineinzupassen — die Quellen nennt <see cref="ContactSearchHint"/>.
    ///
    /// <para>Vorher stand dort „Kontakte suchen". Das sagte nicht, was dieses
    /// Feld kann und die Vorschlagsliste über dem Nummernfeld nicht: fremde
    /// Systeme fragen. Wer eine Nummer aus dem CRM suchte, tippte sie oben ins
    /// Nummernfeld, bekam nichts, und hielt sie für nicht vorhanden
    /// (ADR-025).</para>
    /// </summary>
    public string NumberBoxPlaceholder => HasContactSearch ? "Nummer oder Name" : "Nummer";

    /// <summary>
    /// Der Hinweis <b>unter</b> dem Nummernfeld, solange es leer ist — er nennt
    /// die Quellen, die mitgefragt werden.
    ///
    /// <para><b>Warum nicht im Platzhalter.</b> Dort stand er zuerst und war
    /// abgeschnitten: das Feld traegt die Rufnummer in Schriftgroesse 20 und
    /// haelt rechts 76 Pixel fuer zwei Symbole frei — „Nummer oder Name — auch
    /// in «Quelle»" lief darunter hindurch. Ein abgeschnittener Platzhalter
    /// sagt weniger als ein kurzer.</para>
    ///
    /// <para>Hier steht er in Beschriftungsgroesse, umbricht bei Bedarf, und er
    /// steht an derselben Stelle wie die normalisierte Form — beide sind sich
    /// nie gleichzeitig im Weg: die eine gilt bei leerem Feld, die andere bei
    /// gefuelltem.</para>
    /// </summary>
    public string ContactSearchHint
    {
        get
        {
            var sources = _search?.RemoteSourceNames ?? [];

            return sources.Count switch
            {
                0 => string.Empty,
                1 => $"sucht auch in {sources[0]} und Outlook",

                // Ab drei Quellen würde die Aufzählung länger als das Feld.
                <= 2 => $"sucht auch in {string.Join(" und ", sources)}",
                _ => "sucht auch in den eingerichteten Quellen",
            };
        }
    }

    /// <summary>Ob die laufende Suche nichts gefunden hat.</summary>
    public bool SearchFoundNothing =>
        IsSearchActive && !IsSearching && SearchResults.Count == 0;

    /// <summary>
    /// Ob die Trefferliste etwas zu zeigen hat — das Gegenstück zu
    /// <c>HasSuggestions</c> für die zweite Liste (Befund A1-20).
    ///
    /// <para><b>Seit ADR-051 gibt es zwei Listen und eine Antwort, die
    /// entscheidet, welche steht.</b> Wer einen Namen tippt, sieht die
    /// Trefferliste; die Vorschlagsliste bleibt leer. Die Eingabetaste fragte
    /// bis zum 22.09.2026 nur nach der Vorschlagsliste und tat deshalb bei
    /// einem Namen gar nichts — obwohl der Knopf «Anrufen» daneben sagte
    /// «Einen Treffer darunter auswählen».</para>
    /// </summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>
    /// Quellen, zu denen es etwas zu sagen gibt: eine, die lädt, oder eine,
    /// bei der etwas schiefging. Erfolgreiche lokale Quellen erwähnt niemand.
    /// </summary>
    public IReadOnlyList<ContactSourceState> NotableSearchSources =>
        [.. SearchSources.Where(static s =>
            s.State is SearchState.Loading or SearchState.Error
                or SearchState.Timeout or SearchState.Skipped)];

    partial void OnContactQueryChanged(string value)
    {
        _search?.Search(value);

        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(ShowSearchResults));
        OnPropertyChanged(nameof(SearchFoundNothing));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasSuggestions));

        // Die Kacheln filtern mit (C11) — und der Sortiermodus geht dabei aus,
        // weil eine gefilterte Ordnung zurueckzuschreiben Eintraege verloere.
        OnPropertyChanged(nameof(CanReorderTeam));
        OnPropertyChanged(nameof(ShowTeamReorder));
        OnPropertyChanged(nameof(TeamFilterHint));

        if (IsTeamReorderMode && ShowSearchResults)
        {
            IsTeamReorderMode = false;
        }

        RebuildTeamGroups();
    }

    /// <summary>§8.1: Escape leert das Feld — dieselbe Geste wie beim Wählen.</summary>
    [RelayCommand]
    private void ClearContactQuery() => ContactQuery = string.Empty;

    /// <summary>
    /// Ein Zwischenstand der Suche ist da.
    ///
    /// <b>Die Generation wird hier nicht mehr geprüft</b> — das tut der Dienst
    /// zweimal, vor dem Abschicken und vor dem Auslösen. Was hier ankommt,
    /// gehört zur laufenden Eingabe.
    /// </summary>
    private void OnSearchResultsChanged(object? sender, ContactSearchSnapshot snapshot) =>
        OnUiThread(() => ApplySearchSnapshot(snapshot));

    private void ApplySearchSnapshot(ContactSearchSnapshot snapshot)
    {
        // Die Zeilen neu aufbauen, aber nur wenn sich wirklich etwas geändert
        // hat: dieselbe Vorsicht wie bei den Kontaktabschnitten. Eine Liste,
        // die sich unter dem Zeiger neu aufbaut, verliert Auswahl und
        // Bildlauf — und in einer Liste, in der ein Doppelklick anruft, ist
        // das keine Kleinigkeit.
        var rows = snapshot.Contacts
            .Select(contact => new ContactRow(contact, _blf.StatusOf(contact.SipAddress))
            {
                SourceDisplayName = DisplayNameOf(snapshot, contact),
            })
            .ToList();

        if (!SameContacts(SearchResults, rows))
        {
            Replace(SearchResults, rows);
            OnPropertyChanged(nameof(HasSearchResults));
        }

        SearchSources.Clear();

        foreach (var source in snapshot.Sources)
        {
            SearchSources.Add(source);
        }

        IsSearching = !snapshot.IsComplete;

        OnPropertyChanged(nameof(NotableSearchSources));
        OnPropertyChanged(nameof(SearchFoundNothing));
    }

    /// <summary>
    /// Der Anzeigename der Quelle, aus der ein Treffer stammt — für das
    /// Abzeichen an der Zeile. Ohne ihn stünde dort die technische Kennung.
    /// </summary>
    private static string? DisplayNameOf(ContactSearchSnapshot snapshot, Contact contact)
    {
        if (contact.Source != ContactSourceKind.External)
        {
            return null;
        }

        var id = contact.EffectiveSourceId;

        return snapshot.Sources.FirstOrDefault(s =>
            string.Equals(s.SourceId, id, StringComparison.Ordinal))?.DisplayName;
    }
}
