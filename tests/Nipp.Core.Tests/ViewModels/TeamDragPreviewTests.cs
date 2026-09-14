using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Die Vorschau beim Ziehen (ADR-066).
///
/// <para><b>Warum das hier geprüft wird.</b> Die Vorschau ist seit ADR-066
/// nicht ein Bild des Auftrags, sondern der Auftrag selbst: beim Loslassen
/// wird geschrieben, was dasteht. Damit hängt die gespeicherte Reihenfolge
/// unmittelbar an diesen Rechnungen — und <c>Nipp.App</c> hat kein
/// Testprojekt.</para>
///
/// <para><b>Der teuerste Fall ist der Abbruch.</b> Wer mit Escape abbricht,
/// erwartet seine Reihenfolge unverändert zurück. Eine Vorschau, die dabei
/// etwas liegen lässt, kostet genau das, was der Benutzer sich eingerichtet
/// hat.</para>
/// </summary>
public sealed class TeamDragPreviewTests
{
    private static ContactRow Zeile(string nummer) =>
        new(
            new Contact(
                Id: $"team:{nummer}",
                DisplayName: $"N{nummer}",
                Numbers: [new ContactNumber(nummer, ContactNumberKind.Business)],
                Source: ContactSourceKind.Team),
            PresenceStatus.Unknown);

    /// <summary>Drei in «Team», zwei in «Dienste» — dieselbe Ausgangslage wie in den Layout-Tests.</summary>
    private static List<ContactGroupRow> Ausgangslage(bool diensteOffen = true) =>
    [
        new("Team", [Zeile("201"), Zeile("202"), Zeile("203")], isExpanded: true),
        new("Dienste", [Zeile("901"), Zeile("902")], isExpanded: diensteOffen),
    ];

    /// <summary>Was der Bestand sagt — die Grundlage fürs Speichern.</summary>
    private static string Bestand(IEnumerable<ContactGroupRow> gruppen) =>
        string.Join(
            " ",
            gruppen.Select(g => $"{g.Name[0]}:{string.Join(",", g.All.Select(r => r.Contact.Id[5..]))}"));

    /// <summary>Was die Liste zeichnet.</summary>
    private static string Anzeige(IEnumerable<ContactGroupRow> gruppen) =>
        string.Join(
            " ",
            gruppen.Select(g => $"{g.Name[0]}:{string.Join(",", g.Rows.Select(r => r.Contact.Id[5..]))}"));

    private static ContactRow Finden(IEnumerable<ContactGroupRow> gruppen, string nummer) =>
        gruppen.SelectMany(g => g.All).First(r => r.Contact.Id == $"team:{nummer}");

    [Fact]
    public void Beginnt_in_der_Gruppe_in_der_die_Zeile_steht()
    {
        var gruppen = Ausgangslage();

        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "902"));

        Assert.NotNull(zug);
        Assert.Equal("Dienste", zug.Group);
        Assert.False(zug.Changed);
    }

    [Fact]
    public void Eine_fremde_Zeile_beginnt_keinen_Zug()
    {
        var gruppen = Ausgangslage();

        Assert.Null(TeamDragPreview.Start(gruppen, Zeile("777")));
    }

    [Fact]
    public void Innerhalb_der_Gruppe_weichen_die_anderen_aus()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "203"))!;

        Assert.True(zug.MoveTo("Team", 0));

        Assert.Equal("T:203,201,202 D:901,902", Bestand(gruppen));
        Assert.Equal("T:203,201,202 D:901,902", Anzeige(gruppen));
        Assert.True(zug.Changed);
    }

    [Fact]
    public void In_eine_andere_Gruppe()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        Assert.True(zug.MoveTo("Dienste", 1));

        Assert.Equal("T:202,203 D:901,201,902", Bestand(gruppen));
        Assert.Equal("T:202,203 D:901,201,902", Anzeige(gruppen));
        Assert.Equal("Dienste", zug.Group);
        Assert.True(zug.Changed);
    }

    /// <summary>
    /// <b>Dieselbe Stelle zweimal ist keine Bewegung.</b> Der Zeiger meldet
    /// beim Ziehen laufend; ohne diese Antwort schriebe jede Meldung in die
    /// Sammlungen, und die Liste zeichnete bei jeder Zeigerbewegung neu.
    /// </summary>
    [Fact]
    public void Dieselbe_Stelle_bewegt_nichts()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        Assert.True(zug.MoveTo("Dienste", 0));
        Assert.False(zug.MoveTo("Dienste", 0));
    }

    /// <summary>
    /// <b>Hin und zurück ist keine Änderung.</b> Sonst schriebe ein Zug, der
    /// nichts bewegt hat, die Einstellungen neu.
    /// </summary>
    [Fact]
    public void Zurueck_an_den_Ausgangsplatz_ist_keine_Aenderung()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "202"))!;

        zug.MoveTo("Dienste", 0);
        Assert.True(zug.Changed);

        zug.MoveTo("Team", 1);

        Assert.False(zug.Changed);
        Assert.Equal("T:201,202,203 D:901,902", Bestand(gruppen));
    }

    /// <summary>
    /// Der Fall, für den es diese Klasse gibt: <b>nach einem Abbruch steht
    /// alles exakt wie vorher</b> — auch nach einer Fahrt quer durch beide
    /// Gruppen.
    /// </summary>
    [Fact]
    public void Der_Abbruch_stellt_die_Ordnung_wieder_her()
    {
        var gruppen = Ausgangslage();
        var vorher = Bestand(gruppen);
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "202"))!;

        zug.MoveTo("Dienste", 2);
        zug.MoveTo("Dienste", 0);
        zug.MoveTo("Team", 0);
        zug.MoveTo("Dienste", 1);
        zug.MoveTo("Team", 2);

        zug.Cancel();

        Assert.Equal(vorher, Bestand(gruppen));
        Assert.Equal(vorher, Anzeige(gruppen));
        Assert.Equal("Team", zug.Group);
        Assert.False(zug.Changed);
    }

    [Fact]
    public void Der_Abbruch_holt_sie_aus_der_fremden_Gruppe_zurueck()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        zug.MoveTo("Dienste", 2);
        zug.Cancel();

        Assert.Equal("T:201,202,203 D:901,902", Bestand(gruppen));
    }

    /// <summary>
    /// <b>Eine zugeklappte Gruppe führt ihren Bestand mit und zeigt trotzdem
    /// nichts.</b> Im Sortiermodus sind zwar alle Gruppen offen; diese Prüfung
    /// hält fest, dass die Vorschau nicht darauf angewiesen ist — sonst
    /// verlöre eine zugeklappte Gruppe beim nächsten Speichern ihre Einträge
    /// ans Ende der Liste (ADR-041).
    /// </summary>
    [Fact]
    public void Eine_zugeklappte_Zielgruppe_behaelt_ihren_Bestand()
    {
        var gruppen = Ausgangslage(diensteOffen: false);
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        zug.MoveTo("Dienste", 1);

        Assert.Equal("T:202,203 D:901,201,902", Bestand(gruppen));
        Assert.Equal("T:202,203 D:", Anzeige(gruppen));
    }

    /// <summary>
    /// Eine leere Gruppe ist ein gültiges Ziel — sie steht in den
    /// Einstellungen, nicht durch ihre Mitglieder (T177).
    /// </summary>
    [Fact]
    public void Eine_leergeraeumte_Gruppe_nimmt_wieder_auf()
    {
        List<ContactGroupRow> gruppen =
        [
            new("Team", [Zeile("201")], isExpanded: true),
            new("Dienste", [], isExpanded: true),
        ];

        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        Assert.True(zug.MoveTo("Dienste", 0));
        Assert.Equal("T: D:201", Bestand(gruppen));
    }

    /// <summary>
    /// Eine Stelle jenseits der Gruppe landet am Ende — wer unter der letzten
    /// Zeile loslässt, meint das Ende und hat sich nicht geirrt
    /// (<see cref="TeamLayout.Move"/> hält es genauso).
    /// </summary>
    [Fact]
    public void Eine_Stelle_jenseits_der_Gruppe_landet_am_Ende()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        zug.MoveTo("Dienste", 99);

        Assert.Equal("T:202,203 D:901,902,201", Bestand(gruppen));
    }

    [Fact]
    public void Eine_unbekannte_Gruppe_bewegt_nichts()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "201"))!;

        Assert.False(zug.MoveTo("Gibtsnicht", 0));
        Assert.Equal("T:201,202,203 D:901,902", Bestand(gruppen));
    }

    /// <summary>
    /// <b>Der Übergabepunkt ans Speichern.</b> Was die Vorschau hinterlässt,
    /// liest <c>TeamLayout.From</c> — und genau das schreibt
    /// <c>ApplyTeamLayout()</c> nach dem Loslassen. Stimmten die beiden nicht
    /// überein, zeigte die Liste etwas anderes, als gespeichert würde.
    /// </summary>
    [Fact]
    public void Was_die_Vorschau_hinterlaesst_liest_TeamLayout()
    {
        var gruppen = Ausgangslage();
        var zug = TeamDragPreview.Start(gruppen, Finden(gruppen, "203"))!;

        zug.MoveTo("Dienste", 1);

        var layout = TeamLayout.From(gruppen);

        Assert.Equal(
            "201@Team 202@Team 901@Dienste 203@Dienste 902@Dienste",
            string.Join(" ", layout.Select(p => $"{p.Id[5..]}@{p.Group}")));
    }
}
