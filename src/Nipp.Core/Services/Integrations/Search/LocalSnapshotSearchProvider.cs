using Nipp.Core.Services.Contacts;

namespace Nipp.Core.Services.Integrations.Search;

/// <summary>
/// Die lokalen Kontakte als Suchquelle — Team und Outlook (§21, ADR-015).
///
/// <b>Das ist die Outlook-Migration, und sie besteht aus einem Adapter.</b>
/// <see cref="OutlookContactSource"/> bleibt unangetastet: sie ist die
/// heikelste Klasse des Repos — späte COM-Bindung, eigener STA-Thread, 60 s
/// Zeitgrenze, ein aufgehobenes verspätetes Ergebnis — und sie funktioniert.
/// Sie umzubauen, damit sie eine neue Schnittstelle bedient, wäre ein Risiko
/// ohne Gegenwert.
///
/// Stattdessen liest dieser Anbieter aus dem <see cref="ContactStore"/>, den
/// es ohnehin gibt. Damit gilt weiterhin:
/// <list type="bullet">
///   <item>Outlook wird <b>nicht</b> je Tastendruck über COM gefragt — das
///   wäre mit STA-Thread und dem 20-ms-Iterate-Timer auf demselben Thread
///   nicht vertretbar (§8.4, ADR-009).</item>
///   <item>Der Zwischenspeicher von zwölf Stunden bleibt, wie er ist.</item>
///   <item>Gesucht wird im Arbeitsspeicher, also sofort.</item>
/// </list>
///
/// <b>Und das Suchverhalten bleibt wörtlich dasselbe</b>: gerufen wird
/// <see cref="ContactStore.Search"/>, nicht eine neue Fassung davon.
/// <c>ContactSearchCharacterizationTests</c> hält fest, was das heisst — bis
/// hin zu den beiden bekannten Schwächen bei nationaler Schreibweise und
/// Umlauten.
/// </summary>
public sealed class LocalSnapshotSearchProvider(ContactStore contacts) : IContactSearchProvider
{
    /// <summary>
    /// Beide lokalen Quellen zusammen, unter einer Kennung.
    ///
    /// Sie hier zu trennen brächte nichts: die Oberfläche zeigt Team und
    /// Outlook ohnehin getrennt (§8.4), und zwar über
    /// <c>Contact.Source</c> — nicht über die Kennung der Suchquelle. Zwei
    /// Anbieter über demselben Zwischenspeicher wären zwei Wege zum selben
    /// Ergebnis.
    /// </summary>
    public string SourceId => "lokal";

    public string DisplayName => "Team und Outlook";

    /// <summary>
    /// Lokal, ohne Mindestlänge und ohne Zeitgrenze: die Antwort steht im
    /// Arbeitsspeicher. Der Orchestrator fragt diese Quelle deshalb vor dem
    /// Debounce — Warten hätte hier keinen Gegenwert.
    /// </summary>
    public ContactSearchTraits Traits { get; } = new(IsLocal: true, MinQueryLength: 1);

    public Task<ContactSearchPage> SearchAsync(
        ContactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        var treffer = contacts.Search(query.Text);

        // Die Obergrenze gilt auch lokal: eine Suche nach „a" trifft sonst das
        // halbe Adressbuch, und die Liste baut jede Zeile auf dem Thread, der
        // alle 20 ms Core.Iterate() bedient (§6).
        var gekuerzt = treffer.Count > query.Limit
            ? treffer.Take(query.Limit).ToList()
            : treffer;

        var page = new ContactSearchPage(
            SourceId,
            gekuerzt.Count == 0 ? SearchState.Empty : SearchState.Success,
            gekuerzt);

        return Task.FromResult(page);
    }
}
