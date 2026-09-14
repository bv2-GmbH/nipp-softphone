using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// Was eine <c>settings.json</c> von <b>vor</b> den Gruppen und der Mobilnummer
/// beim Laden ergibt (ADR-041).
///
/// <para><b>Warum das empirisch festgehalten wird und nicht zitiert.</b> Die
/// Vorgabe von <c>TeamExtension.Group</c> ist <c>null</c> und nicht
/// <c>"Team"</c> — genau, weil nicht sicher ist, ob System.Text.Json bei einem
/// fehlenden Member den deklarierten Parameterwert einsetzt oder
/// <c>default(T)</c>. Bei einem nicht-nullable <c>string</c> stünde im zweiten
/// Fall <c>null</c> darin, ohne Compilerwarnung. Dieser Test beantwortet die
/// Frage, statt sie zu glauben.</para>
///
/// <para><b>Und er ist die Gegenprobe zu T162:</b> alle Nebenstellen da, alle
/// in einer Gruppe, keine <c>.kaputt-…</c>-Datei daneben.</para>
/// </summary>
public sealed class TeamExtensionJsonTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nipp-teamjson-test-{Guid.NewGuid():N}");

    private readonly SecretStore _secrets;
    private readonly SettingsService _service;

    public TeamExtensionJsonTests()
    {
        Directory.CreateDirectory(_directory);

        _secrets = new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat"));

        _service = new SettingsService(
            _secrets,
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));
    }

    /// <summary>
    /// Eine Datei, wie sie vor dem 11.09.2026 geschrieben wurde.
    ///
    /// <b>PascalCase, wie der Store schreibt</b> — <c>SettingsService</c> setzt
    /// keine Benennungsregel. Ein camelCase-Test hier wäre grün gewesen, ohne
    /// je eine echte Datei zu beschreiben.
    /// </summary>
    private const string AlteDatei = """
        {
          "SchemaVersion": 1,
          "Contacts": {
            "UseOutlook": true,
            "Team": [
              { "DisplayName": "Anna Muster", "Extension": "201", "SipAddress": "sip:201@pbx.example.ch" },
              { "DisplayName": "Beat Meier", "Extension": "202" }
            ],
            "EnableBlf": true
          }
        }
        """;

    [Fact]
    public void Eine_alte_Datei_laedt_verlustfrei()
    {
        File.WriteAllText(_service.SettingsPath, AlteDatei);

        var geladen = _service.Load();

        Assert.Equal(2, geladen.Contacts.Team.Count);
        Assert.Equal("Anna Muster", geladen.Contacts.Team[0].DisplayName);
        Assert.Equal("sip:201@pbx.example.ch", geladen.Contacts.Team[0].SipAddress);
        Assert.Equal("Beat Meier", geladen.Contacts.Team[1].DisplayName);

        // Keine Mobilnummern, eine Gruppe, und die Reihenfolge steht.
        Assert.All(geladen.Contacts.Team, m => Assert.Null(m.Mobile));
        Assert.Equal([TeamGroups.DefaultName], geladen.Contacts.Groups);
        Assert.All(geladen.Contacts.Team, m => Assert.Equal(TeamGroups.DefaultName, m.Group));

        // Und die Datei daneben ist unangetastet: eine beiseitegelegte
        // `.kaputt-…` hiesse, dass das Laden gescheitert ist.
        Assert.Empty(Directory.GetFiles(_directory, "*.kaputt-*"));
    }

    /// <summary>
    /// Die Antwort auf die STJ-Frage, schwarz auf weiss: ein fehlendes
    /// <c>group</c> ergibt <c>null</c> und keinen leeren String — und
    /// <c>NameOf</c> macht daraus die erste Gruppe.
    /// </summary>
    [Fact]
    public void Ein_fehlendes_Feld_ergibt_null_und_nicht_leer()
    {
        var gelesen = System.Text.Json.JsonSerializer.Deserialize<TeamExtension>(
            """{ "DisplayName": "Anna", "Extension": "201" }""")!;

        Assert.Null(gelesen.Group);
        Assert.Null(gelesen.Mobile);
        Assert.Null(gelesen.SipAddress);
        Assert.Equal("Team", TeamGroups.NameOf(gelesen, ["Team"]));
    }

    /// <summary>
    /// Was nicht gesetzt ist, steht auch nicht in der Datei — sonst stünde in
    /// jeder ausgegebenen Konfiguration eine Gruppe, die niemand vergeben hat.
    /// </summary>
    [Fact]
    public void Eine_Nebenstelle_ohne_Zusaetze_schreibt_keine_leeren_Felder()
    {
        _service.Save(new NippSettings
        {
            Contacts = new ContactSettings
            {
                Team = [new TeamExtension("Anna Muster", "201")],
            },
        });

        var text = File.ReadAllText(_service.SettingsPath);

        Assert.DoesNotContain("\"Mobile\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SipAddress\"", text, StringComparison.Ordinal);

        // `group` steht dagegen da: Normalize schreibt die Gruppe aus, damit
        // ein Eintrag beim Umbenennen der ersten Gruppe nicht stumm mitwandert.
        var wieder = new SettingsService(
            _secrets,
            NullLogger<SettingsService>.Instance,
            _service.SettingsPath).Load();

        Assert.Equal("Team", TeamGroups.NameOf(wieder.Contacts.Team[0], wieder.Contacts.Groups));
    }

    /// <summary>
    /// <b>Ein Zurücksetzen behält die Gruppen.</b> Ohne das verlöre es die
    /// Ordnung, während die Einträge bleiben — und alles landete stumm in
    /// „Team". Das ist die eine Stelle, an der diese Änderung sonst unbemerkt
    /// danebengeht.
    /// </summary>
    [Fact]
    public void Zuruecksetzen_behaelt_Team_und_Gruppen()
    {
        _service.Save(new NippSettings
        {
            Contacts = new ContactSettings
            {
                Groups = ["Innendienst", "Support"],
                Team =
                [
                    new TeamExtension("Anna", "201", Group: "Innendienst"),
                    new TeamExtension("Beat", "202", Group: "Support"),
                ],
            },
        });

        _service.Reset();

        Assert.Equal(["Innendienst", "Support"], _service.Current.Contacts.Groups);
        Assert.Equal(2, _service.Current.Contacts.Team.Count);
        Assert.Equal("Support", _service.Current.Contacts.Team[1].Group);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis ist kein Testfehler.
        }
    }
}
