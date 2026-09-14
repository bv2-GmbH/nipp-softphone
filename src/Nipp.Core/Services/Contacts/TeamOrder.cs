using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Bringt die Team-Nebenstellen in die Reihenfolge, die der Benutzer in der
/// Kontaktliste hergestellt hat (§8.4).
///
/// <b>Rein und ohne Zustand</b> — genau deshalb prüfbar (§13). Die Regel, dass
/// beim Umsortieren nichts verlorengeht, ist keine, die man an der Oberfläche
/// nachsehen kann: ein fehlender Eintrag fällt erst auf, wenn jemand ihn
/// braucht.
/// </summary>
public static class TeamOrder
{
    /// <summary>
    /// Ordnet <paramref name="team"/> nach <paramref name="orderedIds"/> —
    /// den Kennungen aus <see cref="TeamContactSource.IdOf"/> in ihrer neuen
    /// Reihenfolge.
    ///
    /// <b>Es geht kein Eintrag verloren und keiner kommt doppelt.</b> Was in
    /// <paramref name="orderedIds"/> fehlt, hängt hinten an; was dort steht und
    /// hier unbekannt ist, wird übergangen. Stimmt die Anzahl am Ende trotzdem
    /// nicht, bleibt <paramref name="team"/> unverändert: eine halb sortierte
    /// Team-Liste zu speichern wäre schlimmer, als gar nicht zu sortieren.
    ///
    /// Die Zuordnung läuft über die Kennung und nicht über die Einträge selbst.
    /// <see cref="TeamExtension"/> ist ein Record mit Wertgleichheit — zwei
    /// gleichnamige Nebenstellen mit derselben Nummer wären ununterscheidbar,
    /// und ein Entfernen träfe immer die erste.
    /// </summary>
    public static List<TeamExtension> Apply(
        IReadOnlyList<TeamExtension> team,
        IReadOnlyList<string> orderedIds)
    {
        var kennungen = TeamContactSource.IdsOf(team);
        var byId = new Dictionary<string, int>(team.Count, StringComparer.Ordinal);

        for (var i = 0; i < team.Count; i++)
        {
            // Bei doppelten Kennungen gewinnt die erste. Vorkommen kann das
            // nicht, weil der Zähler gleicher Kurzwahlen sie unterscheidet —
            // die Zeile steht hier, damit ein Indexer-Absturz nicht die
            // Alternative ist.
            byId.TryAdd(kennungen[i], i);
        }

        var taken = new bool[team.Count];
        var result = new List<TeamExtension>(team.Count);

        foreach (var id in orderedIds)
        {
            if (byId.TryGetValue(id, out var index) && !taken[index])
            {
                taken[index] = true;
                result.Add(team[index]);
            }
        }

        for (var i = 0; i < team.Count; i++)
        {
            if (!taken[i])
            {
                result.Add(team[i]);
            }
        }

        return result.Count == team.Count ? result : [.. team];
    }

    /// <summary>
    /// Schreibt <b>Gruppe und Reihenfolge zusammen</b> — der eine Weg, den ein
    /// Ziehvorgang in der Kontaktliste nimmt (ADR-042).
    ///
    /// <para><b>Warum das nicht zwei Schritte sein dürfen.</b> Bis zum
    /// 12.09.2026 schrieb das Ziehen nur die Reihenfolge. Ein Zug über eine
    /// Gruppengrenze verschob die Zeile in der Anzeige, liess
    /// <see cref="TeamExtension.Group"/> aber stehen: die gespeicherte Liste
    /// war danach verschachtelt (A, B, A), die Invariante aus ADR-041 verletzt,
    /// und beim nächsten Laden sprang die Zeile in ihre alte Gruppe
    /// zurück.</para>
    ///
    /// <para><b>Das Ergebnis ist normalisiert.</b> Damit ist die Invariante
    /// „blockweise nach Gruppen" keine Regel mehr, an die sich der Aufrufer
    /// halten muss, sondern eine Eigenschaft dessen, was hier herauskommt.</para>
    ///
    /// <para><b>Eine unbekannte Gruppe wird nicht erfunden</b> — sie fällt über
    /// <see cref="TeamGroups.NameOf(string?, IReadOnlyList{string})"/> auf die
    /// erste zurück, wie überall sonst. Gruppen entstehen in den
    /// Einstellungen, nicht beim Ziehen.</para>
    ///
    /// <para>Stimmt die Anzahl am Ende nicht, bleibt alles, wie es war —
    /// dieselbe Zusage wie in <see cref="Apply"/>.</para>
    /// </summary>
    /// <param name="layout">
    /// Was die Liste nach dem Loslassen zeigt: je Zeile ihre Kennung und die
    /// Gruppe, in deren Sammlung sie jetzt steht.
    /// </param>
    public static (List<string> Groups, List<TeamExtension> Team) ApplyLayout(
        IReadOnlyList<string> groups,
        IReadOnlyList<TeamExtension> team,
        IReadOnlyList<TeamPlacement> layout)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(layout);

        var kennungen = TeamContactSource.IdsOf(team);
        var byId = new Dictionary<string, int>(team.Count, StringComparer.Ordinal);

        for (var i = 0; i < team.Count; i++)
        {
            byId.TryAdd(kennungen[i], i);
        }

        var genommen = new bool[team.Count];
        var ergebnis = new List<TeamExtension>(team.Count);

        foreach (var platz in layout)
        {
            if (!byId.TryGetValue(platz.Id, out var index) || genommen[index])
            {
                continue;
            }

            genommen[index] = true;

            var gruppe = TeamGroups.NameOf(platz.Group, groups);
            var eintrag = team[index];

            ergebnis.Add(string.Equals(eintrag.Group, gruppe, StringComparison.Ordinal)
                ? eintrag
                : eintrag with { Group = gruppe });
        }

        // Was die Anzeige nicht genannt hat, hängt hinten an — mit
        // unveränderter Gruppe. Vorkommen sollte es nicht; die Zusage gilt
        // trotzdem, dass nichts verlorengeht.
        for (var i = 0; i < team.Count; i++)
        {
            if (!genommen[i])
            {
                ergebnis.Add(team[i]);
            }
        }

        return ergebnis.Count == team.Count
            ? TeamGroups.Normalize(groups, ergebnis)
            : TeamGroups.Normalize(groups, team);
    }
}
