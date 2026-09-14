using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Die Gruppen der Team-Nebenstellen (ADR-041).
///
/// <para><b>Rein und ohne Zustand</b>, nach dem Vorbild von
/// <see cref="TeamOrder"/> — genau deshalb prüfbar (§13). Was hier schiefgeht,
/// fällt an der Oberfläche erst auf, wenn jemand einen Kontakt sucht, den es
/// noch gibt und der nirgends steht.</para>
///
/// <para><b>Die Invariante, an der das Sortieren hängt:</b>
/// <c>Contacts.Team</c> ist immer blockweise nach <c>Contacts.Groups</c>
/// sortiert. Nur dann gilt „Anzeige == Speicher", und
/// <see cref="TeamOrder.Apply"/> bleibt Zeile für Zeile richtig. Sortierte die
/// Anzeige nach Gruppen, während die gespeicherte Liste verschachtelt ist
/// (A, B, A), dann schriebe <b>ein einziger Ziehvorgang eine Reihenfolge
/// zurück, die niemand hergestellt hat</b>.</para>
/// </summary>
public static class TeamGroups
{
    /// <summary>
    /// Wie die erste Gruppe ab Werk heisst.
    ///
    /// <b>Nur der Anfangswert, nicht die Bedeutung.</b> „Standardgruppe"
    /// heisst <c>Groups[0]</c> — wer sie umbenennt, verliert keine Einträge.
    /// </summary>
    public const string DefaultName = "Team";

    /// <summary>
    /// Die Gruppe einer Nebenstelle, so wie sie angezeigt wird: ihre eigene,
    /// wenn sie in <paramref name="groups"/> vorkommt, sonst die erste.
    /// </summary>
    public static string NameOf(TeamExtension member, IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(member);

        return NameOf(member.Group, groups);
    }

    /// <summary>
    /// Dasselbe für einen bereits gelesenen Gruppennamen — etwa den an einem
    /// <c>Contact</c>.
    /// </summary>
    public static string NameOf(string? group, IReadOnlyList<string> groups)
    {
        var standard = First(groups);

        if (group is not { Length: > 0 } eigene)
        {
            return standard;
        }

        foreach (var gruppe in groups)
        {
            if (string.Equals(gruppe, eigene, StringComparison.OrdinalIgnoreCase))
            {
                return gruppe;
            }
        }

        // Eine Gruppe, die es nicht (mehr) gibt, ist keine Heimatlosigkeit:
        // der Eintrag steht in der Standardgruppe, bis ihn jemand verschiebt.
        return standard;
    }

    /// <summary>Die erste Gruppe, oder <see cref="DefaultName"/>, wenn es keine gibt.</summary>
    public static string First(IReadOnlyList<string> groups) =>
        groups is { Count: > 0 } && groups[0] is { Length: > 0 } erste ? erste : DefaultName;

    /// <summary>
    /// Stellt die Invariante her: die Gruppenliste ist entdoppelt und nicht
    /// leer, jeder Eintrag trägt eine Gruppe, die es gibt, und die Einträge
    /// stehen blockweise in der Reihenfolge der Gruppen.
    ///
    /// <para><b>Innerhalb einer Gruppe bleibt die Reihenfolge, wie sie war.</b>
    /// Das ist der Teil, der zählt: Normalisieren darf keine vom Benutzer
    /// hergestellte Reihenfolge umwerfen.</para>
    ///
    /// <para><b>Idempotent.</b> Ein zweiter Lauf ändert nichts mehr — darauf
    /// steht der Test, der die Divergenz zwischen Anzeige und Speicher
    /// festnagelt.</para>
    /// </summary>
    public static (List<string> Groups, List<TeamExtension> Team) Normalize(
        IReadOnlyList<string>? groups,
        IReadOnlyList<TeamExtension>? team)
    {
        var sauber = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gruppe in groups ?? [])
        {
            if (gruppe is { Length: > 0 } name && gesehen.Add(name))
            {
                sauber.Add(name);
            }
        }

        if (sauber.Count == 0)
        {
            sauber.Add(DefaultName);
            gesehen.Add(DefaultName);
        }

        var eintraege = team ?? [];
        var sortiert = new List<TeamExtension>(eintraege.Count);

        foreach (var gruppe in sauber)
        {
            foreach (var eintrag in eintraege)
            {
                if (!string.Equals(NameOf(eintrag, sauber), gruppe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Die Gruppe wird ausgeschrieben — auch bei einem Eintrag, der
                // vorher keine hatte. Sonst wanderte er beim nächsten
                // Umbenennen der ersten Gruppe mit, ohne dass jemand ihn
                // angefasst hat.
                sortiert.Add(eintrag.Group == gruppe ? eintrag : eintrag with { Group = gruppe });
            }
        }

        return (sauber, sortiert);
    }

    /// <summary>
    /// Benennt eine Gruppe um. Die Einträge wandern mit — sie tragen den Namen,
    /// nicht einen Verweis.
    /// </summary>
    public static (List<string> Groups, List<TeamExtension> Team) Rename(
        IReadOnlyList<string> groups,
        IReadOnlyList<TeamExtension> team,
        string oldName,
        string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Normalize(groups, team);
        }

        var neueGruppen = new List<string>(groups.Count);

        foreach (var gruppe in groups)
        {
            neueGruppen.Add(
                string.Equals(gruppe, oldName, StringComparison.OrdinalIgnoreCase) ? newName : gruppe);
        }

        var neueEintraege = new List<TeamExtension>(team.Count);

        foreach (var eintrag in team)
        {
            neueEintraege.Add(
                string.Equals(eintrag.Group, oldName, StringComparison.OrdinalIgnoreCase)
                    ? eintrag with { Group = newName }
                    : eintrag);
        }

        return Normalize(neueGruppen, neueEintraege);
    }

    /// <summary>
    /// Entfernt eine Gruppe.
    ///
    /// <para><b>Es wird nie ein Kontakt gelöscht.</b> Die Einträge der
    /// entfernten Gruppe wandern in die Standardgruppe — eine Gruppe zu
    /// entfernen ist eine Ordnungsfrage, kein Grund, jemanden aus dem
    /// Adressbuch zu nehmen.</para>
    ///
    /// <para><b>Die letzte Gruppe bleibt.</b> Ohne sie gäbe es keine
    /// Standardgruppe mehr, und jeder Eintrag wäre heimatlos.</para>
    /// </summary>
    public static (List<string> Groups, List<TeamExtension> Team) Remove(
        IReadOnlyList<string> groups,
        IReadOnlyList<TeamExtension> team,
        string name)
    {
        var bleibend = groups
            .Where(g => !string.Equals(g, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (bleibend.Count == 0)
        {
            return Normalize(groups, team);
        }

        var standard = First(bleibend);

        var neueEintraege = new List<TeamExtension>(team.Count);

        foreach (var eintrag in team)
        {
            neueEintraege.Add(
                string.Equals(eintrag.Group, name, StringComparison.OrdinalIgnoreCase)
                    ? eintrag with { Group = standard }
                    : eintrag);
        }

        return Normalize(bleibend, neueEintraege);
    }

    /// <summary>
    /// Die Gruppen, die in den Einträgen vorkommen — für die Vorschlagsliste im
    /// Formular und für das Einlesen eines Profils, das Gruppen nennt, die es
    /// hier noch nicht gibt.
    /// </summary>
    public static List<string> Collect(
        IReadOnlyList<string>? groups,
        IReadOnlyList<TeamExtension>? team)
    {
        var alle = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gruppe in groups ?? [])
        {
            if (gruppe is { Length: > 0 } name && gesehen.Add(name))
            {
                alle.Add(name);
            }
        }

        foreach (var eintrag in team ?? [])
        {
            if (eintrag.Group is { Length: > 0 } name && gesehen.Add(name))
            {
                alle.Add(name);
            }
        }

        if (alle.Count == 0)
        {
            alle.Add(DefaultName);
        }

        return alle;
    }
}
