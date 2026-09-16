using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;
using NSubstitute;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Dass eine geänderte Nebenstelle in der Kontaktliste ankommt — <b>ohne
/// Neustart</b>.
///
/// <para><b>Der gemeldete Fall (16.09.2026):</b> eine Mobilnummer in den
/// Einstellungen ergänzt, zurück zu den Kontakten — die Nummer fehlte, und
/// erst ein Neustart von nipp zeigte sie. Ursache war die Bremse in
/// <c>RefreshTeamContacts</c>: sie verglich nur Kennung und Gruppe, und die
/// Kennung einer Nebenstelle ist <c>team:{zaehler}:{kurzwahl}</c> — eine
/// Mobilnummer steht darin nicht.</para>
///
/// <para><b>Die Gegenprobe ist hier die wichtigere Hälfte</b> (die Lehre aus
/// ADR-060). Ein Test, der nur zeigt, dass die Nummer jetzt erscheint,
/// bestünde auch, wenn jemand den Vergleich ersatzlos streicht — und dann
/// baute jede Einstellungsänderung und jede Präsenzmeldung die Liste neu auf,
/// mitten in einem Ziehvorgang. Deshalb prüfen die letzten drei Tests, dass
/// die Sammlung <b>unangetastet</b> bleibt, und zwar über die Identität der
/// Zeilenobjekte: <c>Replace</c> erzeugt neue.</para>
/// </summary>
public sealed class ShellTeamRefreshTests
{
    /// <summary>
    /// Ein eigenes Verzeichnis je Test — <b>und es bleibt liegen</b>; dieselbe
    /// Begründung wie in <c>ShellReorderTests</c>: die Anrufliste ist SQLite,
    /// und der Verbindungspool hält die Datei noch offen, wenn der Test längst
    /// durch ist.
    /// </summary>
    private readonly string _verzeichnis = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private SettingsService? _settings;

    [Fact]
    public void Eine_ergaenzte_Mobilnummer_erscheint_sofort()
    {
        var model = MitTeam(new TeamExtension("Anna", "201"));

        Speichere(new TeamExtension("Anna", "201", Mobile: "+41791234567"));

        var zeile = Assert.Single(model.TeamContacts);

        Assert.Contains(
            zeile.Contact.Numbers,
            n => n.Kind == ContactNumberKind.Mobile && n.Number == "+41791234567");
    }

    [Fact]
    public void Ein_geaenderter_Name_erscheint_sofort()
    {
        // Nie gemeldet, gleiche Ursache: der Name steht so wenig in der
        // Kennung wie die Mobilnummer.
        var model = MitTeam(new TeamExtension("Anna", "201"));

        Speichere(new TeamExtension("Anna Muster", "201"));

        Assert.Equal("Anna Muster", Assert.Single(model.TeamContacts).DisplayName);
    }

    [Fact]
    public void Eine_korrigierte_SIP_Adresse_erscheint_sofort()
    {
        // <b>Der unangenehmste Fall.</b> An der SIP-Adresse haengt das
        // Praesenz-Abonnement: blieb die Zeile stehen, zeigte sie die alte
        // Adresse UND bekam weiter keine Lampe — ohne jeden Hinweis, dass die
        // Korrektur nicht angekommen ist.
        var model = MitTeam(new TeamExtension("Anna", "201", SipAddress: "sip:falsch@example.test"));

        Speichere(new TeamExtension("Anna", "201", SipAddress: "sip:201@example.test"));

        var zeile = Assert.Single(model.TeamContacts);

        Assert.Equal("sip:201@example.test", zeile.Contact.SipAddress);
        Assert.True(zeile.HasPresence);
    }

    [Fact]
    public void Eine_neue_Nebenstelle_erscheint_sofort()
    {
        var model = MitTeam(new TeamExtension("Anna", "201"));

        Speichere(
            new TeamExtension("Anna", "201"),
            new TeamExtension("Beat", "202"));

        Assert.Equal(2, model.TeamContacts.Count);
    }

    [Fact]
    public void Dieselben_Daten_lassen_die_Sammlung_unangetastet()
    {
        // Die Bremse selbst: zweimal derselbe Stand darf die Zeilen nicht
        // ersetzen. Sonst verliert ein laufender Ziehvorgang seinen Gegenstand.
        var model = MitTeam(new TeamExtension("Anna", "201"));
        var vorher = model.TeamContacts[0];

        model.RefreshTeamContacts();

        Assert.Same(vorher, model.TeamContacts[0]);
    }

    [Fact]
    public void Eine_fremde_Einstellung_laesst_die_Sammlung_unangetastet()
    {
        // Ein Klingelton hat mit dem Team nichts zu tun. Wuerde die Liste auch
        // hier neu gebaut, waere die Bremse ersatzlos weg statt verfeinert.
        var model = MitTeam(new TeamExtension("Anna", "201"));
        var vorher = model.TeamContacts[0];

        _settings!.Save(_settings.Current with
        {
            Audio = _settings.Current.Audio with { RingtonePath = "C:\\klang.wav" },
        });

        Assert.Same(vorher, model.TeamContacts[0]);
    }

    [Fact]
    public void Die_Praesenz_ersetzt_die_Zeile_nicht()
    {
        // <b>Warum das hier steht.</b> Der Vergleich laeuft ueber den Kontakt,
        // nicht ueber die Zeile — die traegt die Praesenz, und die aendert sich
        // im Sekundentakt. Ein Vergleich, der sie einschliesst, baute die Liste
        // bei jeder BLF-Meldung neu auf.
        var model = MitTeam(new TeamExtension("Anna", "201", SipAddress: "sip:201@example.test"));
        var vorher = model.TeamContacts[0];

        vorher.Presence = PresenceStatus.OnCall;
        model.RefreshTeamContacts();

        Assert.Same(vorher, model.TeamContacts[0]);
        Assert.Equal(PresenceStatus.OnCall, model.TeamContacts[0].Presence);
    }

    /// <summary>
    /// Speichert einen neuen Stand der Nebenstellen — das loest
    /// <c>SettingsService.Changed</c> aus, und daran haengt der Weg, um den es
    /// hier geht: <c>ReloadTeam</c> und <c>RefreshTeamContacts</c>.
    /// </summary>
    private void Speichere(params TeamExtension[] team) =>
        _settings!.Save(_settings.Current with
        {
            Contacts = new ContactSettings
            {
                Groups = ["Team"],
                Team = [.. team],
            },
        });

    private ShellViewModel MitTeam(params TeamExtension[] team)
    {
        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_verzeichnis, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_verzeichnis, "settings.json"));

        einstellungen.Load();

        einstellungen.Save(einstellungen.Current with
        {
            Contacts = new ContactSettings
            {
                Groups = ["Team"],
                Team = [.. team],
            },
        });

        var sip = Substitute.For<ISipService>();

        sip.Accounts.Returns([]);
        sip.ActiveCalls.Returns([]);

        var kontakte = new ContactStore(
            [new TeamContactSource(einstellungen)],
            einstellungen,
            NullLogger<ContactStore>.Instance);

        kontakte.ReloadTeam();

        var model = new ShellViewModel(
            sip,
            new CallHistoryStore(
                NullLogger<CallHistoryStore>.Instance,
                Path.Combine(_verzeichnis, "history.db")),
            kontakte,
            new CallPartyResolver(new ClipResolver(kontakte)),
            new BlfService(sip, kontakte, einstellungen, NullLogger<BlfService>.Instance),
            einstellungen,
            new PolicyService(),
            NullLogger<ShellViewModel>.Instance);

        _settings = einstellungen;

        return model;
    }
}
