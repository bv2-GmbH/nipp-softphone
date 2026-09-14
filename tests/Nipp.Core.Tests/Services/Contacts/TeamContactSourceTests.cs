using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Wie aus Nebenstellen Kontakte werden (§8.4, ADR-042).
///
/// <para><b>Die Kennung ist der Punkt.</b> Sie bildet die Zeilen der
/// Kontaktliste wieder auf die Einträge der Einstellungen ab — und wenn sie
/// sich beim Umsortieren ändert, bricht diese Zuordnung, ohne dass jemand es
/// merkt: es wird schlicht nichts gespeichert.</para>
/// </summary>
public sealed class TeamContactSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private SettingsService Einstellungen()
    {
        Directory.CreateDirectory(_directory);

        return new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));
    }

    /// <summary>
    /// <b>Eine Formel, ein Ort.</b> Der Ladeweg und das Nachziehen des
    /// Zwischenspeichers müssen dieselben Kennungen ergeben — sonst zeigt die
    /// Liste etwas anderes, als gespeichert ist, und der nächste Ziehvorgang
    /// greift ins Leere.
    /// </summary>
    [Fact]
    public async Task Build_und_LoadAsync_liefern_dieselben_Kennungen()
    {
        var einstellungen = Einstellungen();

        List<TeamExtension> team =
        [
            new("Anna", "201", Group: "Team"),
            new("Beat", "202", Group: "Dienste"),
        ];

        einstellungen.Save(new NippSettings
        {
            Contacts = new ContactSettings { Groups = ["Team", "Dienste"], Team = team },
        });

        var geladen = await new TeamContactSource(einstellungen).LoadAsync();

        var gebaut = TeamContactSource.Build(
            einstellungen.Current.Contacts.Team,
            einstellungen.Current.Contacts.Groups);

        Assert.Equal(
            geladen.Select(static c => c.Id),
            gebaut.Select(static c => c.Id));
    }

    /// <summary>Die Gruppe steht am Kontakt — nur sie liest die Gruppensicht.</summary>
    [Fact]
    public async Task Die_Gruppe_steht_am_Kontakt()
    {
        var einstellungen = Einstellungen();

        einstellungen.Save(new NippSettings
        {
            Contacts = new ContactSettings
            {
                Groups = ["Team", "Dienste"],
                Team =
                [
                    new TeamExtension("Anna", "201", Group: "Team"),
                    new TeamExtension("Beat", "202", Group: "Dienste"),
                ],
            },
        });

        var kontakte = await new TeamContactSource(einstellungen).LoadAsync();

        Assert.Equal("Team", kontakte.Single(c => c.DisplayName == "Anna").Group);
        Assert.Equal("Dienste", kontakte.Single(c => c.DisplayName == "Beat").Group);
    }

    /// <summary>
    /// Eine Nebenstelle, deren Gruppe es nicht (mehr) gibt, steht in der
    /// ersten — heimatlos wird niemand.
    /// </summary>
    [Fact]
    public void Eine_unbekannte_Gruppe_wird_zur_ersten()
    {
        var kontakte = TeamContactSource.Build(
            [new TeamExtension("Anna", "201", Group: "Weg")],
            ["Team", "Dienste"]);

        Assert.Equal("Team", kontakte[0].Group);
    }

    /// <summary>
    /// <b>Die Kennung zählt gleiche Kurzwahlen, nicht Plätze.</b> Zwei
    /// Einträge mit derselben Kurzwahl bleiben unterscheidbar; dieselbe Liste
    /// in anderer Reihenfolge ergibt dieselben Kennungen.
    /// </summary>
    [Fact]
    public void Die_Kennung_zaehlt_gleiche_Kurzwahlen()
    {
        List<TeamExtension> team =
        [
            new("Empfang vorne", "150"),
            new("Anna", "201"),
            new("Empfang hinten", "150"),
        ];

        var ids = TeamContactSource.IdsOf(team);

        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("team:0:150", ids[0]);
        Assert.Equal("team:0:201", ids[1]);
        Assert.Equal("team:1:150", ids[2]);

        // Anna nach vorn: ihre Kennung bleibt, die der beiden gleichnamigen
        // Kurzwahlen ebenfalls — sie haengen an der Reihenfolge untereinander,
        // nicht an der Position in der ganzen Liste.
        List<TeamExtension> umsortiert = [team[1], team[0], team[2]];

        var neueIds = TeamContactSource.IdsOf(umsortiert);

        Assert.Equal("team:0:201", neueIds[0]);
        Assert.Equal("team:0:150", neueIds[1]);
        Assert.Equal("team:1:150", neueIds[2]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
