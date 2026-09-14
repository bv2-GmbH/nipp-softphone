using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Die Reihenfolge der Kontaktliste (§8.4).
///
/// <b>Warum das geprüft wird.</b> Die Team-Reihenfolge ist eine Entscheidung
/// des Benutzers — wer seine drei wichtigsten Nebenstellen nach oben zieht,
/// will sie oben haben. Eine pauschale alphabetische Sortierung über alle
/// Quellen hat sie stillschweigend wieder weggeräumt; am Bildschirm sah das
/// aus, als habe das Ziehen nicht funktioniert.
/// </summary>
public sealed class ContactStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class Quelle(ContactSourceKind kind, params Contact[] contacts) : IContactSource
    {
        public ContactSourceKind Kind => kind;

        public bool IsAvailable => true;

        public Task<IReadOnlyList<Contact>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Contact>>(contacts);
    }

    private static Contact Kontakt(string id, string name, ContactSourceKind kind) =>
        new(id, name, [new ContactNumber("100", ContactNumberKind.Business)], kind);

    /// <summary>
    /// §10 und die Lehre aus dem 04.09.2026: ein Test fasst nie das Profil des
    /// Benutzers an. Ein <c>dotnet test</c> hat dort schon ein SIP-Konto samt
    /// Passwort gelöscht.
    /// </summary>
    private SettingsService Einstellungen() =>
        new(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

    [Fact]
    public async Task Das_Team_behaelt_seine_Reihenfolge_und_Outlook_wird_sortiert()
    {
        var team = new Quelle(
            ContactSourceKind.Team,
            Kontakt("team:0:100", "Zoe", ContactSourceKind.Team),
            Kontakt("team:1:101", "Anna", ContactSourceKind.Team),
            Kontakt("team:2:102", "Max", ContactSourceKind.Team));

        var outlook = new Quelle(
            ContactSourceKind.Outlook,
            Kontakt("o:1", "Zacharias", ContactSourceKind.Outlook),
            Kontakt("o:2", "Berta", ContactSourceKind.Outlook));

        var store = new ContactStore(
            [team, outlook],
            Einstellungen(),
            NullLogger<ContactStore>.Instance);

        await store.RefreshAsync(force: true);

        Assert.Equal(
            ["Zoe", "Anna", "Max", "Berta", "Zacharias"],
            store.Contacts.Select(static c => c.DisplayName));
    }

    [Fact]
    public async Task Umsortieren_wirkt_sofort_im_Zwischenspeicher()
    {
        // Ohne diesen Schritt käme die naechste Aktualisierung der Liste mit
        // der alten Reihenfolge zurueck, und das Ziehen sähe aus, als sei es
        // zurueckgeschnappt.
        var team = new Quelle(
            ContactSourceKind.Team,
            Kontakt("team:0:100", "Anna", ContactSourceKind.Team),
            Kontakt("team:0:101", "Beat", ContactSourceKind.Team));

        var outlook = new Quelle(
            ContactSourceKind.Outlook,
            Kontakt("outlook:1", "Zoe", ContactSourceKind.Outlook));

        var store = new ContactStore([team, outlook], Einstellungen(), NullLogger<ContactStore>.Instance);
        await store.RefreshAsync(force: true);

        store.ReplaceTeam(
        [
            Kontakt("team:0:101", "Beat", ContactSourceKind.Team),
            Kontakt("team:0:100", "Anna", ContactSourceKind.Team),
        ]);

        // Der Team-Block folgt der neuen Ordnung, Outlook bleibt stehen.
        Assert.Equal(["Beat", "Anna", "Zoe"], store.Contacts.Select(static c => c.DisplayName));
    }

    /// <summary>
    /// <b>Eine Gruppe anlegen ändert keine einzige Nummer</b> — und muss
    /// trotzdem in der Liste ankommen (ADR-042). Bis zum 12.09.2026 half
    /// dagegen nur ein Neustart.
    /// </summary>
    [Fact]
    public async Task ReloadTeam_nimmt_neue_Nebenstellen_auf_und_laesst_Outlook_stehen()
    {
        var einstellungen = Einstellungen();

        einstellungen.Save(new NippSettings
        {
            Contacts = new ContactSettings
            {
                Groups = ["Team"],
                Team = [new TeamExtension("Anna", "201")],
            },
        });

        var outlook = new Quelle(
            ContactSourceKind.Outlook,
            Kontakt("outlook:1", "Zoe", ContactSourceKind.Outlook));

        var store = new ContactStore(
            [new TeamContactSource(einstellungen), outlook],
            einstellungen,
            NullLogger<ContactStore>.Instance);

        await store.RefreshAsync(force: true);

        Assert.Equal(["Anna", "Zoe"], store.Contacts.Select(static c => c.DisplayName));

        // Eine zweite Nebenstelle und eine zweite, leere Gruppe.
        einstellungen.Save(new NippSettings
        {
            Contacts = new ContactSettings
            {
                Groups = ["Team", "Dienste"],
                Team =
                [
                    new TeamExtension("Anna", "201"),
                    new TeamExtension("Beat", "202"),
                ],
            },
        });

        store.ReloadTeam();

        Assert.Equal(["Anna", "Beat", "Zoe"], store.Contacts.Select(static c => c.DisplayName));

        // Und die Gruppe steht am Kontakt, ohne dass jemand neu geladen hat.
        Assert.All(
            store.Contacts.Where(static c => c.Source == ContactSourceKind.Team),
            c => Assert.Equal("Team", c.Group));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
