using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Die zweite Nummer einer Team-Nebenstelle (ADR-041).
///
/// <para><b>Der Gewinn ergibt sich von selbst</b>, sobald sie in
/// <c>Contact.Numbers</c> steht: der Doppelklick zeigt das Nummern-Flyout, die
/// Vorschlagsliste im Wählfeld nimmt sie auf, und <c>ClipResolver</c> erkennt
/// den Kollegen, wenn er vom Handy anruft — alles ohne eine Zeile
/// Zusatzarbeit.</para>
///
/// <para><b>Was dabei gleich bleiben muss und hier festgenagelt wird:</b> die
/// Nebenstelle bleibt <c>PrimaryNumber</c>, und die Zahl der
/// Präsenz-Abonnements ändert sich nicht. Das Besetztlampenfeld liest
/// <c>SipAddress</c>, nicht <c>Numbers</c> — eine zweite Nummer darf die
/// Anlage nichts kosten (§14.8).</para>
/// </summary>
public sealed class TeamMobileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nipp-teammobile-test-{Guid.NewGuid():N}");

    private readonly SettingsService _settings;

    public TeamMobileTests()
    {
        Directory.CreateDirectory(_directory);

        _settings = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));
    }

    private IReadOnlyList<Contact> Laden(params TeamExtension[] team)
    {
        _settings.Save(new NippSettings
        {
            Contacts = new ContactSettings { Team = [.. team] },
        });

        return new TeamContactSource(_settings).LoadAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void Eine_Nebenstelle_mit_Handy_hat_zwei_waehlbare_Nummern()
    {
        var kontakte = Laden(new TeamExtension("Anna Muster", "201", Mobile: "+41791234567"));

        var anna = Assert.Single(kontakte);

        Assert.Equal(2, anna.Numbers.Count);
        Assert.Equal("201", anna.Numbers[0].Number);
        Assert.Equal(ContactNumberKind.Business, anna.Numbers[0].Kind);
        Assert.Equal("+41791234567", anna.Numbers[1].Number);
        Assert.Equal(ContactNumberKind.Mobile, anna.Numbers[1].Kind);
    }

    /// <summary>
    /// <b>Die Nebenstelle bleibt vorn.</b> Ein Klick auf den Kollegen wählt
    /// weiterhin intern und nicht aufs Handy — <c>PrimaryNumber</c> ist
    /// <c>Numbers[0]</c>.
    /// </summary>
    [Fact]
    public void Die_Nebenstelle_bleibt_die_erste_Nummer()
    {
        var kontakte = Laden(new TeamExtension("Anna Muster", "201", Mobile: "+41791234567"));

        Assert.Equal("201", kontakte[0].PrimaryNumber);
    }

    /// <summary>
    /// Ohne Handynummer entsteht <b>keine leere</b> zweite Nummer.
    /// <c>ClipResolver</c> und <c>ContactStore.Search</c> laufen roh über
    /// <c>Numbers</c>; ein leerer Eintrag wäre dort toter Ballast.
    /// </summary>
    [Fact]
    public void Ohne_Handynummer_bleibt_es_bei_einer()
    {
        var kontakte = Laden(
            new TeamExtension("Anna Muster", "201"),
            new TeamExtension("Beat Meier", "202", Mobile: "   "));

        Assert.All(kontakte, k => Assert.Single(k.Numbers));
    }

    /// <summary>
    /// <b>CLIP erkennt den Kollegen auch vom Handy.</b> Das ist der Alltagsfall,
    /// für den diese Etappe gebaut wurde: bisher war eine Handynummer nur über
    /// Outlook aufzulösen, und auf dem neuen Outlook gibt es kein COM (ADR-018).
    /// </summary>
    [Fact]
    public async Task Ein_Anruf_von_der_Handynummer_findet_den_Kollegen()
    {
        _settings.Save(new NippSettings
        {
            Contacts = new ContactSettings
            {
                UseOutlook = false,
                Team = [new TeamExtension("Anna Muster", "201", "sip:201@pbx.example.ch", "+41791234567")],
            },
        });

        using var speicher = new ContactStore(
            [new TeamContactSource(_settings)],
            _settings,
            NullLogger<ContactStore>.Instance);

        await speicher.RefreshAsync();

        var gefunden = new ClipResolver(speicher).Resolve("+41 79 123 45 67");

        Assert.NotNull(gefunden);
        Assert.Equal("Anna Muster", gefunden.DisplayName);
    }

    /// <summary>
    /// <b>Die Last auf der Anlage ändert sich um null.</b> Das
    /// Besetztlampenfeld abonniert <c>SipAddress</c>; eine zweite Nummer
    /// erzeugt kein zweites SUBSCRIBE (§14.8). Geprüft wird die Menge, die
    /// <c>BlfService.SynchronizeAsync</c> bildet.
    /// </summary>
    [Fact]
    public void Eine_zweite_Nummer_erzeugt_kein_zweites_Praesenz_Abo()
    {
        var kontakte = Laden(
            new TeamExtension("Anna", "201", "sip:201@pbx.example.ch", "+41791234567"),
            new TeamExtension("Beat", "202", "sip:202@pbx.example.ch"),
            new TeamExtension("Cora", "203", Mobile: "+41791112233"));

        var adressen = kontakte
            .Where(static c => c.Source == ContactSourceKind.Team)
            .Select(static c => c.SipAddress)
            .Where(static a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Zwei Einträge mit SIP-Adresse, also zwei Abonnements — Coras
        // Handynummer bleibt unbeobachtet, und das ist richtig.
        Assert.Equal(2, adressen.Count);
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
