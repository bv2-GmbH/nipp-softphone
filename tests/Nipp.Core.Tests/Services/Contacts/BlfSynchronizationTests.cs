using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.ViewModels;
using NSubstitute;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Wann das Besetztlampenfeld seine Abos nachzieht — und gegen welchen Stand.
///
/// <para><b>Der Befund, der diese Datei nötig gemacht hat</b> (24.09.2026, am
/// laufenden Programm gemessen). Wer die SIP-Adresse einer Nebenstelle
/// änderte, hatte danach eine Lampe, die nichts meldete: im Protokoll stand
/// «11 Nebenstellen abonniert (0 neu)» statt zwölf. Erst die <b>nächste</b>
/// beliebige Änderung holte das Abo nach — dann «12 abonniert (1 neu)».</para>
///
/// <para><b>Die Ursache war keine Rechenfehler, sondern eine Reihenfolge.</b>
/// <c>BlfService</c> und <c>ShellViewModel</c> hingen beide an
/// <c>SettingsService.Changed</c>. Der Dienst lief zuerst und zählte den
/// <c>ContactStore</c>, den erst das ViewModel über <c>ReloadTeam()</c> auf den
/// neuen Stand bringt. Was ein Dienst zu sehen bekam, entschied damit die
/// Erzeugungsreihenfolge im Container — und die steht nirgends.</para>
///
/// <para><b>Deshalb wird hier über die ganze Kette geprüft</b> und nicht am
/// <c>BlfService</c> allein: Speichern → ViewModel → Speicher → Dienst. Ein
/// Test, der <c>SynchronizeAsync</c> direkt ruft, wäre auch vor der Reparatur
/// grün gewesen.</para>
/// </summary>
public sealed class BlfSynchronizationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Die Kette, wie sie in der Anwendung steht — ohne Netz, ohne SDK.
    ///
    /// <para>Das <see cref="ShellViewModel"/> gehört dazu: es ist der zweite
    /// Abonnent von <c>Changed</c> und derjenige, der <c>ReloadTeam()</c> ruft.
    /// Ohne ihn prüfte der Test eine Kette, die es so nicht gibt.</para>
    /// </summary>
    private (SettingsService Einstellungen, ISipService Sip, ShellViewModel Shell, List<IReadOnlyList<string>> Abos) Aufbau()
    {
        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

        var sip = Substitute.For<ISipService>();
        sip.Accounts.Returns([]);
        sip.ActiveCalls.Returns([]);

        // Jeder Synchronisierungslauf hinterlässt hier einen Eintrag. Die
        // ANZAHL ist so wichtig wie der Inhalt: eine zu eifrige Reparatur
        // fällt hier auf, nicht erst im Protokoll eines Arbeitstags (ADR-060).
        var abos = new List<IReadOnlyList<string>>();
        sip.WatchPresenceAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                abos.Add(call.Arg<IReadOnlyList<string>>());
                return Task.CompletedTask;
            });

        var kontakte = new ContactStore([], einstellungen, NullLogger<ContactStore>.Instance);
        var blf = new BlfService(sip, kontakte, einstellungen, NullLogger<BlfService>.Instance);

        var shell = new ShellViewModel(
            sip,
            new CallHistoryStore(
                NullLogger<CallHistoryStore>.Instance,
                Path.Combine(_directory, "history.db")),
            kontakte,
            new CallPartyResolver(new ClipResolver(kontakte)),
            blf,
            einstellungen,
            new PolicyService(),
            NullLogger<ShellViewModel>.Instance);

        return (einstellungen, sip, shell, abos);
    }

    private static NippSettings MitTeam(params TeamExtension[] team) =>
        new()
        {
            Contacts = new ContactSettings
            {
                UseOutlook = false,
                EnableBlf = true,
                Team = [.. team],
            },
        };

    /// <summary>
    /// <b>Der Befund selbst:</b> eine nachgetragene SIP-Adresse wird sofort
    /// abonniert, nicht erst beim nächsten Speichern.
    /// </summary>
    [Fact]
    public void Eine_nachgetragene_SIP_Adresse_wird_sofort_abonniert()
    {
        var (einstellungen, _, shell, abos) = Aufbau();
        using var _ = shell;

        einstellungen.Save(MitTeam(
            new TeamExtension("Anna", "201", "sip:201@example.test"),
            new TeamExtension("Beat", "202")));

        Assert.Single(abos[^1]);

        // Beat bekommt eine SIP-Adresse — ab jetzt gehört er ans Lampenfeld.
        einstellungen.Save(MitTeam(
            new TeamExtension("Anna", "201", "sip:201@example.test"),
            new TeamExtension("Beat", "202", "sip:202@example.test")));

        Assert.Equal(2, abos[^1].Count);
        Assert.Contains("sip:202@example.test", abos[^1]);
    }

    /// <summary>
    /// <b>Dasselbe für eine geänderte Adresse</b>, und das ist der Fall aus
    /// T311: die alte darf nicht stehen bleiben, sonst hängt die Lampe an
    /// einer Nebenstelle, die es nicht mehr gibt.
    /// </summary>
    [Fact]
    public void Eine_geaenderte_SIP_Adresse_ersetzt_die_alte()
    {
        var (einstellungen, _, shell, abos) = Aufbau();
        using var _ = shell;

        einstellungen.Save(MitTeam(new TeamExtension("Anna", "201", "sip:201@example.test")));
        einstellungen.Save(MitTeam(new TeamExtension("Anna", "301", "sip:301@example.test")));

        Assert.Equal(["sip:301@example.test"], abos[^1]);
    }

    /// <summary>
    /// <b>Die Gegenprobe, und sie ist die wichtigere Hälfte</b> (ADR-060): ein
    /// Speichern erzeugt <b>genau einen</b> Synchronisierungslauf.
    ///
    /// <para>Der naheliegende Griff gegen den Befund wäre gewesen, im
    /// ViewModel zusätzlich <c>SynchronizeAsync()</c> zu rufen. Dann liefen
    /// beide Abonnenten, und im Protokoll stünden zwei gleiche Zeilen für
    /// einen Vorgang — genau das, was der Kommentar im <c>BlfService</c> seit
    /// jeher vermeiden will. Dieser Test hält das fest.</para>
    /// </summary>
    [Fact]
    public void Ein_Speichern_erzeugt_genau_einen_Lauf()
    {
        var (einstellungen, _, shell, abos) = Aufbau();
        using var _ = shell;

        einstellungen.Save(MitTeam(new TeamExtension("Anna", "201", "sip:201@example.test")));
        var nachDemErsten = abos.Count;

        einstellungen.Save(MitTeam(new TeamExtension("Anna", "201", "sip:201@example.test", "+41791234567")));

        Assert.Equal(nachDemErsten + 1, abos.Count);
    }

    /// <summary>
    /// Ein Speichern, das die Einstellungen gar nicht ändert, kostet nichts —
    /// <c>SettingsService.Write</c> vergleicht vorher (ADR-060).
    /// </summary>
    [Fact]
    public void Ein_Speichern_ohne_Aenderung_kostet_keinen_Lauf()
    {
        var (einstellungen, _, shell, abos) = Aufbau();
        using var _ = shell;

        einstellungen.Save(MitTeam(new TeamExtension("Anna", "201", "sip:201@example.test")));
        var vorher = abos.Count;

        einstellungen.Save(MitTeam(new TeamExtension("Anna", "201", "sip:201@example.test")));

        Assert.Equal(vorher, abos.Count);
    }

    /// <summary>
    /// <c>TeamReloaded</c> meldet, dass die Daten <b>stehen</b> — nicht, dass
    /// jemand gespeichert hat. Wer darauf hört, liest nie einen halben Stand.
    /// </summary>
    [Fact]
    public void TeamReloaded_kommt_erst_wenn_der_Speicher_den_neuen_Stand_traegt()
    {
        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

        using var kontakte = new ContactStore([], einstellungen, NullLogger<ContactStore>.Instance);

        var beimMelden = new List<string?>();
        kontakte.TeamReloaded += (_, _) => beimMelden.AddRange(
            kontakte.Contacts.Select(static c => c.SipAddress));

        einstellungen.Save(MitTeam(new TeamExtension("Anna", "201", "sip:201@example.test")));
        kontakte.ReloadTeam();

        Assert.Equal(["sip:201@example.test"], beimMelden);
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
