using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Die Gruppe in der Kontaktliste (ADR-041).
///
/// <para><b>Warum das Zuklappen geprüft wird.</b> Eine zugeklappte Gruppe leert
/// ihre angezeigten Zeilen — dann erzeugt WinUI für sie auch keine Container.
/// Genau daran hängt aber das Umsortieren: die Reihenfolge wird aus der Anzeige
/// zusammengesetzt, und eine Gruppe, die nichts zeigt, dürfte ihre Einträge
/// nicht verlieren. <c>All</c> ist die Antwort darauf, und dieser Test hält sie
/// fest.</para>
/// </summary>
public sealed class ContactGroupRowTests
{
    private static ContactRow Zeile(string name, string nummer) =>
        new(
            new Contact(
                Id: $"team:{nummer}",
                DisplayName: name,
                Numbers: [new ContactNumber(nummer, ContactNumberKind.Business)],
                Source: ContactSourceKind.Team),
            PresenceStatus.Unknown);

    [Fact]
    public void Aufgeklappt_zeigt_sie_ihre_Zeilen()
    {
        var gruppe = new ContactGroupRow(
            "Support",
            [Zeile("Anna", "201"), Zeile("Beat", "202")],
            isExpanded: true);

        Assert.Equal(2, gruppe.Rows.Count);
        Assert.Equal("Support (2)", gruppe.Header);
    }

    /// <summary>
    /// <b>Zugeklappt zeigt sie nichts und weiss trotzdem alles.</b> Ohne
    /// <c>All</c> hinge beim nächsten Ziehvorgang der ganze Block dieser Gruppe
    /// hinten an, ohne dass jemand etwas verschoben hätte.
    /// </summary>
    [Fact]
    public void Zugeklappt_bleiben_die_Eintraege_bekannt()
    {
        var gruppe = new ContactGroupRow(
            "Support",
            [Zeile("Anna", "201"), Zeile("Beat", "202")],
            isExpanded: false);

        Assert.Empty(gruppe.Rows);
        Assert.Equal(2, gruppe.All.Count);
        Assert.Equal("Support (2)", gruppe.Header);
    }

    [Fact]
    public void Auf_und_Zuklappen_fuehrt_die_Zeilen_nach()
    {
        var gruppe = new ContactGroupRow(
            "Team",
            [Zeile("Anna", "201")],
            isExpanded: true);

        gruppe.IsExpanded = false;
        Assert.Empty(gruppe.Rows);

        gruppe.IsExpanded = true;
        Assert.Single(gruppe.Rows);
        Assert.Same(gruppe.All[0], gruppe.Rows[0]);
    }

    /// <summary>
    /// Die Zeilen sind <b>dieselben Instanzen</b> wie in der flachen Sammlung.
    /// Eine Kopie hätte eine Lampe, die einfriert: die Präsenzmeldung läuft
    /// über die Zeile.
    /// </summary>
    [Fact]
    public void Die_Zeilen_sind_dieselben_Instanzen()
    {
        var anna = Zeile("Anna", "201");

        var gruppe = new ContactGroupRow("Team", [anna], isExpanded: true);

        Assert.Same(anna, gruppe.Rows[0]);

        anna.Presence = PresenceStatus.OnCall;

        Assert.Equal(PresenceStatus.OnCall, gruppe.Rows[0].Presence);
    }
    // --- Was der Ziehvorgang hinterlässt (ADR-042) ---

    /// <summary>
    /// <b>Der Ersatz für den UI-Test, den es nicht geben kann.</b>
    ///
    /// <para>WinUI sagt beim Ziehen nicht, wohin gezogen wurde — es verschiebt
    /// die Zeile zwischen den Gruppensammlungen, bevor das Ereignis feuert.
    /// Genau das wird hier nachgestellt: eine Zeile aus der einen Sammlung
    /// entfernen, in die andere einfügen. Was <c>TeamLayout.From</c> daraus
    /// liest, ist die Grundlage des ganzen Speicherns.</para>
    /// </summary>
    [Fact]
    public void Was_die_Liste_zwischen_die_Gruppen_schiebt_steht_im_Aufbau()
    {
        var anna = Zeile("Anna", "201");
        var beat = Zeile("Beat", "202");

        var team = new ContactGroupRow("Team", [anna, beat], isExpanded: true);
        var dienste = new ContactGroupRow("Dienste", [], isExpanded: true);

        // Der Zug: Beat wandert nach "Dienste".
        team.Rows.Remove(beat);
        dienste.Rows.Insert(0, beat);

        var layout = TeamLayout.From([team, dienste]);

        Assert.Equal(2, layout.Count);
        Assert.Equal("Team", layout[0].Group);
        Assert.Equal(anna.Contact.Id, layout[0].Id);
        Assert.Equal("Dienste", layout[1].Group);
        Assert.Equal(beat.Contact.Id, layout[1].Id);
    }

    /// <summary>
    /// <b>Eine zugeklappte Gruppe steuert ihren Bestand bei, nicht ihre leere
    /// Anzeige.</b> Sonst fehlten ihre Einträge in der Ordnung, und sie landeten
    /// beim Speichern am Ende der Liste — der Fall, in dem heute ein Kontakt
    /// kommentarlos ans Listenende rutscht.
    /// </summary>
    [Fact]
    public void Eine_zugeklappte_Gruppe_verliert_im_Aufbau_niemanden()
    {
        var anna = Zeile("Anna", "201");
        var beat = Zeile("Beat", "202");

        var offen = new ContactGroupRow("Team", [anna], isExpanded: true);
        var zu = new ContactGroupRow("Dienste", [beat], isExpanded: false);

        Assert.Empty(zu.Rows);

        var layout = TeamLayout.From([offen, zu]);

        Assert.Equal(2, layout.Count);
        Assert.Equal(beat.Contact.Id, layout[1].Id);
        Assert.Equal("Dienste", layout[1].Group);
    }

    /// <summary>
    /// <c>SetRows</c> führt Anzeige und Bestand nach, <b>ohne die Gruppe zu
    /// ersetzen</b> — ein <c>Clear()</c> auf der Quelle einer
    /// <c>CollectionViewSource</c> kostet Bildlauf und Auswahl.
    /// </summary>
    [Fact]
    public void SetRows_fuehrt_Anzeige_und_Bestand_nach()
    {
        var anna = Zeile("Anna", "201");
        var beat = Zeile("Beat", "202");

        var gruppe = new ContactGroupRow("Team", [anna], isExpanded: true);

        Assert.Equal("Team (1)", gruppe.Header);

        gruppe.SetRows([anna, beat]);

        Assert.Equal(2, gruppe.All.Count);
        Assert.Equal(2, gruppe.Rows.Count);
        Assert.Equal("Team (2)", gruppe.Header);
        Assert.Same(beat, gruppe.Rows[1]);
    }

    [Fact]
    public void SetRows_laesst_eine_zugeklappte_Gruppe_zugeklappt()
    {
        var anna = Zeile("Anna", "201");

        var gruppe = new ContactGroupRow("Team", [], isExpanded: false);

        gruppe.SetRows([anna]);

        Assert.Empty(gruppe.Rows);
        Assert.Single(gruppe.All);
        Assert.Equal("Team (1)", gruppe.Header);
    }
}
