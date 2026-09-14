using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Das Umsortieren der Team-Nebenstellen (§8.4).
///
/// <b>Warum das geprüft wird.</b> Ein Eintrag, der beim Umsortieren
/// verlorengeht, fällt nicht auf — er fehlt erst, wenn jemand ihn anrufen will,
/// und dann sieht es aus, als habe nipp ihn nie gehabt. Die Falle liegt bei
/// <see cref="TeamExtension"/>: der Record hat Wertgleichheit, zwei gleiche
/// Einträge sind für <c>Remove</c> oder <c>IndexOf</c> derselbe.
/// </summary>
public sealed class TeamOrderTests
{
    private static List<TeamExtension> Team(params string[] namen) =>
        [.. namen.Select(n => new TeamExtension(n, n[..1] + "00"))];

    /// <summary>
    /// <b>Über TeamContactSource, nicht selbst gerechnet.</b> Die Kennung
    /// entsteht an genau einer Stelle; ein Test, der die Formel nachbaut,
    /// prüft ab da seine eigene Kopie.
    /// </summary>
    private static List<string> IdsOf(IReadOnlyList<TeamExtension> team) =>
        [.. TeamContactSource.IdsOf(team)];

    [Fact]
    public void Zwei_Eintraege_vertauschen_ergibt_die_neue_Reihenfolge()
    {
        var team = Team("Anna", "Beat", "Cora");
        var ids = IdsOf(team);

        // Cora nach vorn.
        var gezogen = new List<string> { ids[2], ids[0], ids[1] };

        var neu = TeamOrder.Apply(team, gezogen);

        Assert.Equal(["Cora", "Anna", "Beat"], neu.Select(static m => m.DisplayName));
    }

    [Fact]
    public void Zwei_gleichnamige_Nebenstellen_gehen_nicht_verloren()
    {
        // Die eigentliche Falle: über Wertgleichheit waeren diese beiden
        // ununterscheidbar, und eine der Zeilen fiele beim Sortieren heraus.
        var team = new List<TeamExtension>
        {
            new("Empfang", "100"),
            new("Empfang", "100"),
            new("Lager", "200"),
        };

        var ids = IdsOf(team);
        var neu = TeamOrder.Apply(team, [ids[2], ids[1], ids[0]]);

        Assert.Equal(3, neu.Count);
        Assert.Equal("Lager", neu[0].DisplayName);
        Assert.Equal(2, neu.Count(static m => m.DisplayName == "Empfang"));
    }

    [Fact]
    public void Eine_unbekannte_Kennung_wird_uebergangen()
    {
        var team = Team("Anna", "Beat");
        var ids = IdsOf(team);

        var neu = TeamOrder.Apply(team, [ids[1], "team:99:999", ids[0]]);

        Assert.Equal(["Beat", "Anna"], neu.Select(static m => m.DisplayName));
    }

    [Fact]
    public void Ein_fehlender_Eintrag_haengt_hinten_an()
    {
        // Eine Zeile, die die Oberflaeche nicht mitgemeldet hat, bleibt in der
        // Liste — lieber am falschen Platz als gar nicht mehr da.
        var team = Team("Anna", "Beat", "Cora");
        var ids = IdsOf(team);

        var neu = TeamOrder.Apply(team, [ids[1]]);

        Assert.Equal(3, neu.Count);
        Assert.Equal("Beat", neu[0].DisplayName);
        Assert.Contains(neu, static m => m.DisplayName == "Anna");
        Assert.Contains(neu, static m => m.DisplayName == "Cora");
    }

    [Fact]
    public void Ohne_Nebenstellen_passiert_nichts()
    {
        Assert.Empty(TeamOrder.Apply([], ["team:0:100"]));
        Assert.Equal(2, TeamOrder.Apply(Team("Anna", "Beat"), []).Count);
    }

    // --- Die Kennung, und was an ihr hing (ADR-042) ---

    /// <summary>
    /// <b>Der Test, der bis zum 12.09.2026 fehlte — und der Fehler, den er
    /// findet, war stumm.</b>
    ///
    /// <para>Die Kennung enthielt die Position in der Liste. Nach dem ersten
    /// Ziehvorgang hatte sich die gespeicherte Liste umsortiert, die Zeilen im
    /// Speicher trugen aber die alten Kennungen — beim zweiten Zug schlugen
    /// deshalb alle Zuordnungen fehl, das Ergebnis war unverändert, und die
    /// Prüfung „nichts geändert" traf zu: <b>kein Speichern, kein
    /// Protokolleintrag</b>, während die Liste die neue Ordnung zeigte. Beim
    /// nächsten Start sprang sie zurück.</para>
    ///
    /// <para>Der Test bildet die Kennungen zwischen den Zügen bewusst
    /// <b>neu</b> — genau so, wie es die Oberfläche nach dem Nachziehen des
    /// Zwischenspeichers tut.</para>
    /// </summary>
    [Fact]
    public void Ein_zweiter_Ziehvorgang_wirkt_wie_der_erste()
    {
        var team = Team("Anna", "Beat", "Cora", "Dino");

        // Zug 1: Dino nach vorn.
        var ids = IdsOf(team);
        var nachZug1 = TeamOrder.Apply(team, [ids[3], ids[0], ids[1], ids[2]]);

        Assert.Equal(["Dino", "Anna", "Beat", "Cora"], nachZug1.Select(static m => m.DisplayName));

        // Zug 2: Cora nach vorn — auf dem Stand nach Zug 1.
        var ids2 = IdsOf(nachZug1);
        var nachZug2 = TeamOrder.Apply(nachZug1, [ids2[3], ids2[0], ids2[1], ids2[2]]);

        Assert.Equal(["Cora", "Dino", "Anna", "Beat"], nachZug2.Select(static m => m.DisplayName));
    }

    /// <summary>
    /// Die Kennung überlebt ein Umsortieren: sie zählt gleiche Kurzwahlen,
    /// nicht Plätze.
    /// </summary>
    [Fact]
    public void Die_Kennung_haengt_nicht_am_Platz()
    {
        var team = Team("Anna", "Beat", "Cora");

        var vorher = IdsOf(team);
        var sortiert = TeamOrder.Apply(team, [vorher[2], vorher[1], vorher[0]]);
        var nachher = IdsOf(sortiert);

        // Cora steht jetzt vorn — und traegt dieselbe Kennung wie vorher.
        Assert.Equal(vorher[2], nachher[0]);
        Assert.Equal(vorher[1], nachher[1]);
        Assert.Equal(vorher[0], nachher[2]);
    }

    [Fact]
    public void Zwei_Nebenstellen_mit_derselben_Kurzwahl_bleiben_unterscheidbar()
    {
        List<TeamExtension> team =
        [
            new("Empfang vorne", "150"),
            new("Empfang hinten", "150"),
        ];

        var ids = IdsOf(team);

        Assert.NotEqual(ids[0], ids[1]);

        var getauscht = TeamOrder.Apply(team, [ids[1], ids[0]]);

        Assert.Equal(["Empfang hinten", "Empfang vorne"], getauscht.Select(static m => m.DisplayName));
    }

    // --- ApplyLayout: Gruppe und Reihenfolge zusammen ---

    [Fact]
    public void ApplyLayout_schreibt_Gruppe_und_Reihenfolge()
    {
        List<string> gruppen = ["Team", "Dienste"];

        List<TeamExtension> team =
        [
            new("Anna", "201", Group: "Team"),
            new("Beat", "202", Group: "Team"),
            new("Cora", "203", Group: "Dienste"),
        ];

        var ids = IdsOf(team);

        // Beat wandert nach "Dienste", und zwar an den Anfang.
        List<TeamPlacement> layout =
        [
            new(ids[0], "Team"),
            new(ids[1], "Dienste"),
            new(ids[2], "Dienste"),
        ];

        var (neueGruppen, neu) = TeamOrder.ApplyLayout(gruppen, team, layout);

        Assert.Equal(gruppen, neueGruppen);
        Assert.Equal("Dienste", neu.Single(m => m.DisplayName == "Beat").Group);

        // Blockweise sortiert — die Invariante ist eine Eigenschaft des
        // Ergebnisses, nicht eine Bitte an den Aufrufer.
        Assert.Equal(["Anna", "Beat", "Cora"], neu.Select(static m => m.DisplayName));
    }

    /// <summary>
    /// Gruppen entstehen in den Einstellungen, nicht beim Ziehen. Ein
    /// unbekannter Name fällt auf die erste Gruppe zurück.
    /// </summary>
    [Fact]
    public void ApplyLayout_erfindet_keine_Gruppe()
    {
        List<string> gruppen = ["Team"];
        List<TeamExtension> team = [new("Anna", "201", Group: "Team")];

        var (neueGruppen, neu) = TeamOrder.ApplyLayout(
            gruppen,
            team,
            [new TeamPlacement(IdsOf(team)[0], "Gibt es nicht")]);

        Assert.Equal(["Team"], neueGruppen);
        Assert.Equal("Team", neu[0].Group);
    }

    [Fact]
    public void ApplyLayout_laesst_alles_stehen_wenn_die_Zahl_nicht_stimmt()
    {
        List<string> gruppen = ["Team", "Dienste"];

        List<TeamExtension> team =
        [
            new("Anna", "201", Group: "Team"),
            new("Beat", "202", Group: "Dienste"),
        ];

        // Ein Platz mit einer Kennung, die es nicht gibt — der andere fehlt.
        var (_, neu) = TeamOrder.ApplyLayout(
            gruppen,
            team,
            [new TeamPlacement("team:99:999", "Team")]);

        Assert.Equal(["Anna", "Beat"], neu.Select(static m => m.DisplayName));
        Assert.Equal("Dienste", neu[1].Group);
    }

    /// <summary>
    /// <b>Es geht niemand verloren</b>, auch wenn drei Gruppen im Spiel sind.
    /// </summary>
    [Fact]
    public void ApplyLayout_uebergeht_niemanden_der_im_Layout_steht()
    {
        List<string> gruppen = ["Team", "Dienste"];

        List<TeamExtension> team =
        [
            new("Anna", "201", Group: "Team"),
            new("Beat", "202", Group: "Dienste"),
            new("Cora", "203", Group: "Dienste"),
        ];

        var ids = IdsOf(team);

        var (_, neu) = TeamOrder.ApplyLayout(
            gruppen,
            team,
            [new(ids[0], "Team"), new(ids[1], "Dienste"), new(ids[2], "Dienste")]);

        Assert.Equal(3, neu.Count);
    }
}
