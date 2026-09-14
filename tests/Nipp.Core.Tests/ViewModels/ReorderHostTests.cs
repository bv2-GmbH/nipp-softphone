using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Welche Gruppe den Umschalter «Reihenfolge ändern» trägt (ADR-064).
///
/// <para><b>Der Anlass.</b> Der Umschalter stand in einer eigenen Leiste über
/// der Liste — ein einzelner Knopf über einer sonst leeren Fläche. Er gehört
/// in den Kopf einer Gruppe; die Frage ist nur, in welchen.</para>
///
/// <para>Die Regel hat drei Fälle, und der dritte ist der, der zählt: sind
/// <b>alle</b> Gruppen zugeklappt, trägt ihn die erste. <c>null</c> hiesse
/// dort, dass die Funktion sich selbst wegsperrt.</para>
/// </summary>
public sealed class ReorderHostTests
{
    [Fact]
    public void Die_erste_offene_Gruppe_traegt_ihn()
    {
        var team = Gruppe("Team", offen: true);
        var dienste = Gruppe("Dienste", offen: true);

        Assert.Same(team, ReorderHost.Pick([team, dienste]));
    }

    [Fact]
    public void Ist_die_erste_zu_wandert_er_zur_naechsten_offenen()
    {
        // Der Fall aus dem Alltag: wer «Team» zuklappt und unten mit
        // «Dienste» arbeitet, sucht den Umschalter dort.
        var team = Gruppe("Team", offen: false);
        var dienste = Gruppe("Dienste", offen: true);

        Assert.Same(dienste, ReorderHost.Pick([team, dienste]));
    }

    [Fact]
    public void Er_ueberspringt_auch_mehrere_zugeklappte()
    {
        var team = Gruppe("Team", offen: false);
        var dienste = Gruppe("Dienste", offen: false);
        var extern_ = Gruppe("Extern", offen: true);

        Assert.Same(extern_, ReorderHost.Pick([team, dienste, extern_]));
    }

    [Fact]
    public void Sind_alle_zugeklappt_traegt_ihn_die_erste()
    {
        // <b>Der Fall, der zählt.</b> Ohne diesen Rückfall wäre der Umschalter
        // unerreichbar — und mit ihm das Umsortieren. Eine Funktion, die sich
        // selbst wegsperrt, ist schlimmer als eine an einem mittelguten Platz.
        var team = Gruppe("Team", offen: false);
        var dienste = Gruppe("Dienste", offen: false);

        Assert.Same(team, ReorderHost.Pick([team, dienste]));
    }

    [Fact]
    public void Eine_einzige_Gruppe_traegt_ihn_auch_zugeklappt()
    {
        var team = Gruppe("Team", offen: false);

        Assert.Same(team, ReorderHost.Pick([team]));
    }

    [Fact]
    public void Ohne_Gruppen_gibt_es_keinen_Traeger()
    {
        Assert.Null(ReorderHost.Pick([]));
    }

    private static ContactGroupRow Gruppe(string name, bool offen) =>
        new(name, [Zeile(name)], offen);

    private static ContactRow Zeile(string name) =>
        new(
            new Contact(
                Id: $"team:{name}",
                DisplayName: name,
                Numbers: [new ContactNumber("201", ContactNumberKind.Business)],
                Source: ContactSourceKind.Team),
            PresenceStatus.Unknown);
}
