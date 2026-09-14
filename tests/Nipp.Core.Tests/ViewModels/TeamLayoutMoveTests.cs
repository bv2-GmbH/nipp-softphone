using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Wohin eine gezogene Nebenstelle gehört (ADR-065).
///
/// <para><b>Der Anlass.</b> Bis zum 13.09.2026 konnte eine Zeile nur ans
/// <em>Ende</em> einer Gruppe wandern — das Kontextmenü bot nichts anderes an,
/// und für das Ziehen gab es gar keine Rechnung: es verliess sich darauf, dass
/// WinUI die Zeile beim gruppierten Umsortieren selbst umhängt.</para>
///
/// <para><b>Warum das hier geprüft wird und nicht am Fenster.</b>
/// <c>Nipp.App</c> hat kein Testprojekt. Wohin eine Zeile gehört, ist eine
/// Rechnung über eine Liste — sie gehört in den Kern, und dort ist sie
/// prüfbar.</para>
/// </summary>
public sealed class TeamLayoutMoveTests
{
    /// <summary>Drei in «Team», zwei in «Dienste» — die Ausgangslage.</summary>
    private static IReadOnlyList<TeamPlacement> Ausgangslage() =>
    [
        new("team:201", "Team"),
        new("team:202", "Team"),
        new("team:203", "Team"),
        new("team:901", "Dienste"),
        new("team:902", "Dienste"),
    ];

    private static string Reihe(IReadOnlyList<TeamPlacement> layout) =>
        string.Join(" ", layout.Select(p => $"{p.Id[5..]}@{p.Group[0]}"));

    [Fact]
    public void Ohne_Index_landet_sie_am_Ende_der_Gruppe()
    {
        // Der Weg des Kontextmenüs — er bleibt, wie er war.
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:201", "Dienste", null);

        Assert.Equal("202@T 203@T 901@D 902@D 201@D", Reihe(ergebnis));
    }

    [Fact]
    public void An_den_Anfang_der_Zielgruppe()
    {
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:201", "Dienste", 0);

        Assert.Equal("202@T 203@T 201@D 901@D 902@D", Reihe(ergebnis));
    }

    [Fact]
    public void Mitten_in_die_Zielgruppe()
    {
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:201", "Dienste", 1);

        Assert.Equal("202@T 203@T 901@D 201@D 902@D", Reihe(ergebnis));
    }

    [Fact]
    public void Ein_Index_jenseits_der_Gruppe_landet_am_Ende()
    {
        // Wer unter der letzten Zeile loslässt, meint das Ende und hat sich
        // nicht geirrt.
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:201", "Dienste", 99);

        Assert.Equal("202@T 203@T 901@D 902@D 201@D", Reihe(ergebnis));
    }

    [Fact]
    public void Innerhalb_derselben_Gruppe_an_eine_andere_Stelle()
    {
        // <b>Der Fall, den das Kontextmenü nie konnte.</b> Die Zeile wird
        // zuerst herausgenommen, der Index zählt danach.
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:203", "Team", 0);

        Assert.Equal("203@T 201@T 202@T 901@D 902@D", Reihe(ergebnis));
    }

    [Fact]
    public void In_eine_leere_Gruppe()
    {
        // Eine leere Gruppe hat keine Stelle, an der man sich orientieren
        // könnte — die Zeile geht ans Ende, und TeamGroups.Normalize sortiert
        // die Blöcke danach.
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:201", "Extern", 0);

        Assert.Equal("202@T 203@T 901@D 902@D 201@E", Reihe(ergebnis));
    }

    [Fact]
    public void Eine_unbekannte_Kennung_fuegt_trotzdem_ein()
    {
        // Die Kennung stammt aus der Anzeige; steht sie nicht im Layout, ist
        // das kein Grund, den Auftrag zu verwerfen. Der Schreibweg dahinter
        // (TeamOrder.ApplyLayout) lehnt eine falsche Zahl ohnehin ab.
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:999", "Team", 0);

        Assert.Equal(6, ergebnis.Count);
        Assert.Equal("999@T 201@T 202@T 203@T 901@D 902@D", Reihe(ergebnis));
    }

    [Fact]
    public void Die_Vorlage_bleibt_unberuehrt()
    {
        // Eine reine Funktion gibt eine neue Liste zurück. Würde sie die
        // übergebene ändern, hinge an jedem Aufruf ein Seiteneffekt, den
        // niemand sieht.
        var vorlage = Ausgangslage();

        _ = TeamLayout.Move(vorlage, "team:201", "Dienste", 0);

        Assert.Equal("201@T 202@T 203@T 901@D 902@D", Reihe(vorlage));
    }

    [Fact]
    public void Ein_negativer_Index_landet_ganz_oben()
    {
        var ergebnis = TeamLayout.Move(Ausgangslage(), "team:201", "Dienste", -5);

        Assert.Equal("202@T 203@T 201@D 901@D 902@D", Reihe(ergebnis));
    }
}
