using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Die Gruppen der Team-Nebenstellen (ADR-041).
///
/// <para><b>Warum das geprüft wird.</b> Eine Gruppe zu entfernen darf keinen
/// Kontakt kosten, und ein Umbenennen keinen Eintrag heimatlos machen — beides
/// fällt an der Oberfläche erst auf, wenn jemand einen Kollegen sucht, den es
/// noch gibt und der nirgends steht.</para>
///
/// <para><b>Die wichtigste Zeile ist die Invariante:</b> die gespeicherte Liste
/// muss blockweise nach Gruppen sortiert sein, sonst schreibt ein einziger
/// Ziehvorgang in der Kontaktliste eine Reihenfolge zurück, die niemand
/// hergestellt hat.</para>
/// </summary>
public sealed class TeamGroupsTests
{
    private static TeamExtension Person(string name, string? gruppe = null) =>
        new(name, name[..1] + "00", Group: gruppe);

    [Fact]
    public void Ohne_eigene_Gruppe_steht_ein_Eintrag_in_der_ersten()
    {
        var gruppen = new List<string> { "Verkauf", "Support" };

        Assert.Equal("Verkauf", TeamGroups.NameOf(Person("Anna"), gruppen));
    }

    /// <summary>
    /// Der Standard ist die <b>erste Gruppe</b> und nicht der Name „Team".
    /// Wer sie umbenennt, verliert keine Einträge — sie wandern mit.
    /// </summary>
    [Fact]
    public void Die_erste_Gruppe_darf_heissen_wie_sie_will()
    {
        var (gruppen, team) = TeamGroups.Rename(
            ["Team", "Support"],
            [Person("Anna"), Person("Beat", "Support")],
            "Team",
            "Innendienst");

        Assert.Equal(["Innendienst", "Support"], gruppen);
        Assert.Equal("Innendienst", team.Single(m => m.DisplayName == "Anna").Group);
        Assert.Equal("Support", team.Single(m => m.DisplayName == "Beat").Group);
    }

    [Fact]
    public void Eine_Gruppe_die_es_nicht_gibt_ist_die_erste()
    {
        var gruppen = new List<string> { "Verkauf" };

        Assert.Equal("Verkauf", TeamGroups.NameOf(Person("Anna", "Weg"), gruppen));
    }

    /// <summary>
    /// <b>Die Invariante.</b> Nach dem Normalisieren stehen die Einträge
    /// blockweise in der Reihenfolge der Gruppen — und ein zweiter Lauf ändert
    /// nichts mehr.
    /// </summary>
    [Fact]
    public void Normalize_sortiert_blockweise_und_ist_idempotent()
    {
        var (gruppen, team) = TeamGroups.Normalize(
            ["A", "B"],
            [Person("Anna", "A"), Person("Beat", "B"), Person("Cora", "A")]);

        Assert.Equal(["Anna", "Cora", "Beat"], team.Select(static m => m.DisplayName));

        var (gruppen2, team2) = TeamGroups.Normalize(gruppen, team);

        Assert.Equal(gruppen, gruppen2);
        Assert.Equal(team, team2);
    }

    /// <summary>
    /// Innerhalb einer Gruppe bleibt die Reihenfolge, wie sie war —
    /// Normalisieren darf keine vom Benutzer hergestellte Ordnung umwerfen.
    /// </summary>
    [Fact]
    public void Innerhalb_einer_Gruppe_bleibt_die_Reihenfolge()
    {
        var (_, team) = TeamGroups.Normalize(
            ["A"],
            [Person("Cora", "A"), Person("Anna", "A"), Person("Beat", "A")]);

        Assert.Equal(["Cora", "Anna", "Beat"], team.Select(static m => m.DisplayName));
    }

    [Fact]
    public void Normalize_verliert_keinen_Eintrag_und_entdoppelt_die_Gruppen()
    {
        var (gruppen, team) = TeamGroups.Normalize(
            ["A", "a", "", "B"],
            [Person("Anna", "B"), Person("Beat"), Person("Cora", "A")]);

        Assert.Equal(["A", "B"], gruppen);
        Assert.Equal(3, team.Count);
    }

    /// <summary>
    /// Ohne Gruppen entsteht genau eine — das ist der Fall einer
    /// <c>settings.json</c> von vor dieser Änderung.
    /// </summary>
    [Fact]
    public void Ohne_Gruppen_entsteht_die_Standardgruppe()
    {
        var (gruppen, team) = TeamGroups.Normalize(
            null,
            [Person("Anna"), Person("Beat")]);

        Assert.Equal([TeamGroups.DefaultName], gruppen);
        Assert.All(team, m => Assert.Equal(TeamGroups.DefaultName, m.Group));
        Assert.Equal(["Anna", "Beat"], team.Select(static m => m.DisplayName));
    }

    /// <summary>
    /// <b>Entfernen löscht nie einen Kontakt.</b> Die Einträge der entfernten
    /// Gruppe wandern in die Standardgruppe.
    /// </summary>
    [Fact]
    public void Remove_verliert_keinen_Eintrag()
    {
        var (gruppen, team) = TeamGroups.Remove(
            ["Team", "Support"],
            [Person("Anna"), Person("Beat", "Support"), Person("Cora", "Support")],
            "Support");

        Assert.Equal(["Team"], gruppen);
        Assert.Equal(3, team.Count);
        Assert.All(team, m => Assert.Equal("Team", m.Group));
    }

    /// <summary>
    /// Die letzte Gruppe bleibt: ohne sie gäbe es keine Standardgruppe mehr,
    /// und jeder Eintrag wäre heimatlos.
    /// </summary>
    [Fact]
    public void Die_letzte_Gruppe_laesst_sich_nicht_entfernen()
    {
        var (gruppen, team) = TeamGroups.Remove(
            ["Team"],
            [Person("Anna")],
            "Team");

        Assert.Equal(["Team"], gruppen);
        Assert.Single(team);
    }

    [Fact]
    public void Collect_nennt_auch_Gruppen_die_nur_an_Eintraegen_stehen()
    {
        var alle = TeamGroups.Collect(
            ["Team"],
            [Person("Anna"), Person("Beat", "Aus einem Profil")]);

        Assert.Equal(["Team", "Aus einem Profil"], alle);
    }

    /// <summary>
    /// <b>Die Zeile, an der das Umsortieren hängt.</b> Die Anzeigereihenfolge
    /// durch <see cref="TeamOrder.Apply"/> gejagt ergibt eine Liste, die
    /// <see cref="TeamGroups.Normalize"/> nicht mehr verändert. Bricht das,
    /// bricht das Umsortieren still: die Anzeige sortiert nach Gruppen, der
    /// Speicher wäre verschachtelt, und ein einziger Ziehvorgang schriebe eine
    /// Reihenfolge zurück, die niemand hergestellt hat.
    /// </summary>
    [Fact]
    public void Anzeigereihenfolge_und_Speicherreihenfolge_bleiben_dieselbe()
    {
        var (gruppen, team) = TeamGroups.Normalize(
            ["A", "B"],
            [Person("Anna", "A"), Person("Beat", "B"), Person("Cora", "A"), Person("Dino", "B")]);

        // So sieht die Liste aus, und so zieht der Benutzer: Cora vor Anna,
        // innerhalb ihrer Gruppe.
        // Ueber TeamContactSource.IdsOf: die Kennung entsteht an genau einer
        // Stelle, und ein Test, der die Formel nachbaut, prueft ab da seine
        // eigene Kopie (ADR-042).
        var ids = TeamContactSource.IdsOf(team);
        var gezogen = new List<string> { ids[1], ids[0], ids[2], ids[3] };

        var sortiert = TeamOrder.Apply(team, gezogen);

        Assert.Equal(["Cora", "Anna", "Beat", "Dino"], sortiert.Select(static m => m.DisplayName));

        var (gruppen2, team2) = TeamGroups.Normalize(gruppen, sortiert);

        Assert.Equal(gruppen, gruppen2);
        Assert.Equal(sortiert, team2);
    }

    /// <summary>
    /// <b>Die Invariante gilt auch über eine Gruppengrenze hinweg</b>
    /// (ADR-042): was <c>ApplyLayout</c> zurückgibt, verändert
    /// <see cref="TeamGroups.Normalize"/> nicht mehr. Damit ist sie eine
    /// Eigenschaft des Ergebnisses und keine Bitte an den Aufrufer.
    /// </summary>
    [Fact]
    public void Nach_einem_Gruppenwechsel_gilt_die_Invariante_weiter()
    {
        List<string> gruppen = ["A", "B"];

        List<TeamExtension> team =
        [
            Person("Anna", "A"),
            Person("Beat", "A"),
            Person("Cora", "B"),
        ];

        var ids = TeamContactSource.IdsOf(team);

        // Beat wandert nach B, mitten hinein.
        var (neueGruppen, neu) = TeamOrder.ApplyLayout(
            gruppen,
            team,
            [new(ids[0], "A"), new(ids[2], "B"), new(ids[1], "B")]);

        Assert.Equal(["Anna", "Cora", "Beat"], neu.Select(static m => m.DisplayName));

        var (gruppen2, team2) = TeamGroups.Normalize(neueGruppen, neu);

        Assert.Equal(neueGruppen, gruppen2);
        Assert.Equal(neu, team2);
    }
}
