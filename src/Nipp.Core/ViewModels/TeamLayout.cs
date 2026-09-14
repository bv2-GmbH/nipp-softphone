namespace Nipp.Core.ViewModels;

/// <summary>
/// Eine Zeile der Team-Liste mit der Gruppe, in der sie gerade steht.
/// </summary>
/// <param name="Id">Die Kennung des Kontakts.</param>
/// <param name="Group">Der Name der Gruppe, in deren Sammlung die Zeile liegt.</param>
public sealed record TeamPlacement(string Id, string Group);

/// <summary>
/// Ein Verschiebeauftrag aus dem Kontextmenü: diese Zeile in jene Gruppe.
/// </summary>
/// <param name="Row">Die Zeile, die wandern soll.</param>
/// <param name="Group">Der Name der Zielgruppe.</param>
/// <param name="Index">
/// Wohin innerhalb der Zielgruppe — gezählt, <b>nachdem</b> die Zeile
/// herausgenommen wurde. <c>null</c> heisst ans Ende.
///
/// <para>Das Kontextmenü schickt <c>null</c>: wohin genau, hat dort niemand
/// gesagt. Beim Ziehen sagt es der Ort, an dem losgelassen wurde.</para>
/// </param>
public sealed record ContactGroupMove(ContactRow Row, string Group, int? Index = null);

/// <summary>
/// Liest aus der Gruppensicht, was die Liste gerade zeigt (ADR-042).
///
/// <para><b>Warum es diese Projektion braucht.</b> Sie ist die Vorlage, in die
/// <see cref="Move"/> hineinrechnet: was heute in welcher Gruppe und in welcher
/// Reihenfolge steht.</para>
///
/// <para><b>Hier stand bis zum 13.09.2026 eine Behauptung, die nicht
/// stimmte:</b> die Zeile sei beim Ziehen „bereits umgehängt", WinUI verschiebe
/// sie zwischen den <see cref="ContactGroupRow.Rows"/>, bevor das Ereignis
/// feuere. Das kam aus ADR-042 und war nie gemessen. <b>Am gebauten Fenster
/// nachgestellt: WinUI hängt nichts um</b> — bei einer gruppierten
/// <c>CollectionViewSource</c> endet jeder Drop mit <c>None</c>. Der Zug wird
/// seit ADR-065 selbst ausgewertet, und die Zielstelle kommt vom Ablegeort.</para>
///
/// <para><b>Der gruppeninterne Zug fällt mit ab:</b> dieselbe Gruppe, andere
/// Reihenfolge. Ein Weg für beides.</para>
///
/// <para>Dass diese Klasse im Kern liegt, ist der Punkt: ein Test kann den
/// Ziehvorgang nachstellen, indem er eine Zeile aus der einen Sammlung entfernt
/// und in die andere einfügt — genau das, was WinUI tut. <c>Nipp.App</c> hat
/// kein Testprojekt; ohne diese Stelle wäre der Ziehvorgang ungeprüft.</para>
/// </summary>
public static class TeamLayout
{
    /// <summary>
    /// Alle Zeilen in Anzeigereihenfolge, jede mit ihrer Gruppe.
    ///
    /// <b>Eine zugeklappte Gruppe steuert ihren gespeicherten Bestand bei</b>
    /// (<see cref="ContactGroupRow.All"/>), nicht ihre leere Anzeige — sonst
    /// fehlten ihre Einträge in der Ordnung, und sie landeten beim Speichern
    /// am Ende der Liste. Im Sortiermodus sind ohnehin alle Gruppen offen;
    /// diese Zeile ist die Sicherung für jeden anderen Aufrufer.
    /// </summary>
    public static IReadOnlyList<TeamPlacement> From(IEnumerable<ContactGroupRow> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var plaetze = new List<TeamPlacement>();

        foreach (var gruppe in groups)
        {
            foreach (var zeile in gruppe.IsExpanded ? gruppe.Rows : gruppe.All)
            {
                plaetze.Add(new TeamPlacement(zeile.Contact.Id, gruppe.Name));
            }
        }

        return plaetze;
    }

    /// <summary>
    /// Setzt eine Zeile an eine bestimmte Stelle einer Gruppe.
    ///
    /// <para><b>Warum das eine reine Funktion ist.</b> Wohin eine gezogene
    /// Zeile gehört, ist eine Rechnung über eine Liste — kein Fensterthema.
    /// <c>Nipp.App</c> hat kein Testprojekt; stünde sie im Code-behind, wäre
    /// sie ungeprüft.</para>
    ///
    /// <para><paramref name="index"/> zählt <b>innerhalb der Zielgruppe</b>,
    /// nachdem die Zeile herausgenommen wurde. <c>null</c> heisst ans Ende.
    /// Ein Index jenseits der Gruppe landet ebenfalls am Ende — wer unter der
    /// letzten Zeile loslässt, meint das Ende und hat sich nicht geirrt.</para>
    /// </summary>
    public static IReadOnlyList<TeamPlacement> Move(
        IReadOnlyList<TeamPlacement> layout,
        string id,
        string group,
        int? index)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var ohne = layout
            .Where(p => !string.Equals(p.Id, id, StringComparison.Ordinal))
            .ToList();

        // Die Stellen der Zielgruppe in der flachen Liste. Sie liegen
        // beieinander, weil TeamGroups.Normalize blockweise sortiert — aber
        // darauf verlässt sich hier nichts: gesucht wird die n-te.
        var stellen = new List<int>();

        for (var i = 0; i < ohne.Count; i++)
        {
            if (string.Equals(ohne[i].Group, group, StringComparison.OrdinalIgnoreCase))
            {
                stellen.Add(i);
            }
        }

        int einfuegen;

        if (stellen.Count == 0)
        {
            // Eine leere Zielgruppe hat keine Stelle, an der man sich
            // orientieren könnte. Ans Ende — TeamGroups.Normalize sortiert
            // danach ohnehin blockweise, die Gruppe findet ihren Platz.
            einfuegen = ohne.Count;
        }
        else if (index is not { } gewuenscht || gewuenscht >= stellen.Count)
        {
            einfuegen = stellen[^1] + 1;
        }
        else
        {
            einfuegen = stellen[Math.Max(gewuenscht, 0)];
        }

        ohne.Insert(einfuegen, new TeamPlacement(id, group));

        return ohne;
    }
}
