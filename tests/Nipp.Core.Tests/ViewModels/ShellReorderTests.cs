using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.ViewModels;
using NSubstitute;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Wer den Umschalter «Reihenfolge ändern» trägt, und wann gar niemand
/// (ADR-064).
///
/// <para><b>Warum im Kern geprüft.</b> Der Umschalter sitzt seit dem
/// 13.09.2026 im Kopf einer Gruppe, also in einem <c>DataTemplate</c> — aus
/// dem Fenster ist er weder über einen Namen erreichbar noch prüfbar, und
/// <c>Nipp.App</c> hat kein Testprojekt. Die Regel steht deshalb vollständig
/// im ViewModel, und hier steht, dass sie gilt.</para>
///
/// <para><b>Die Invariante, die jeder Test mitprüft:</b> es trägt ihn
/// <b>höchstens eine</b> Gruppe. Zwei wären zwei Wahrheiten über denselben
/// Modus.</para>
/// </summary>
public sealed class ShellReorderTests
{
    /// <summary>
    /// Ein eigenes Verzeichnis je Test — <b>und es bleibt liegen</b>.
    ///
    /// <para>Dasselbe macht <c>ShellLayoutTests.Create()</c>: die Anrufliste
    /// ist SQLite, und der Verbindungspool hält die Datei noch offen, wenn der
    /// Test längst durch ist. Ein <c>Directory.Delete</c> im Dispose scheitert
    /// daran — der Test wäre dann aus einem Grund rot, der mit seiner Aussage
    /// nichts zu tun hat.</para>
    ///
    /// <para><b>Nie <c>%APPDATA%</c>:</b> ein <c>dotnet test</c> hat hier
    /// schon einmal das SIP-Konto samt Passwort gelöscht
    /// (<c>TestIsolationTests</c>).</para>
    /// </summary>
    private readonly string _verzeichnis = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Die_erste_Gruppe_traegt_ihn()
    {
        var model = MitTeam();

        Assert.Equal("Team", Traeger(model)?.Name);
    }

    [Fact]
    public void Eine_zugeklappte_erste_Gruppe_gibt_ihn_weiter()
    {
        // Der gemeldete Fall: wer «Team» zuklappt und unten mit «Dienste»
        // arbeitet, sucht den Umschalter dort.
        var model = MitTeam();

        model.ToggleGroupExpansion(model.TeamGroupRows[0]);

        Assert.False(model.TeamGroupRows[0].IsExpanded);
        Assert.Equal("Dienste", Traeger(model)?.Name);
    }

    [Fact]
    public void Zuklappen_wird_gemerkt()
    {
        var model = MitTeam();

        model.ToggleGroupExpansion(model.TeamGroupRows[0]);

        Assert.Contains("Team", model.TeamGroupRows[0].Name, StringComparison.Ordinal);
        Assert.Single(_settings!.Current.Advanced.CollapsedTeamGroups);
    }

    /// <summary>
    /// <b>Im Sortiermodus bleibt der Detailbereich zu</b> (ADR-066).
    ///
    /// <para><b>Der Anlass.</b> Diese Regel stand bis zum 14.09.2026 nur im
    /// Kommentar an <c>IsTeamReorderMode</c> — «kein offener Detailbereich»,
    /// als eine von drei —, und es gab sie nicht: geschlossen wurde er beim
    /// <em>Einschalten</em>, ein Klick öffnete ihn danach wieder. Eine Zeile
    /// mit ausgeklapptem Innenleben ist als Ziehziel weder zu treffen noch zu
    /// erklären, und für die Vorschau ist sie eine Quelle von Sprüngen: eine
    /// Zeile, die dreimal so hoch ist wie ihre Nachbarn, verschiebt beim
    /// Ausweichen das halbe Bild.</para>
    /// </summary>
    [Fact]
    public void Im_Sortiermodus_bleibt_der_Detailbereich_zu()
    {
        var model = MitTeam();
        var zeile = model.TeamGroupRows[0].Rows[0];

        model.IsTeamReorderMode = true;
        model.ToggleContactDetails(zeile);

        Assert.False(zeile.IsDetailExpanded);
        Assert.Null(model.ExpandedContact);
    }

    /// <summary>
    /// Und ein bereits offener geht zu, sobald der Modus beginnt — sonst
    /// stünde er beim ersten Zug im Weg.
    /// </summary>
    [Fact]
    public void Der_Sortiermodus_schliesst_einen_offenen_Detailbereich()
    {
        var model = MitTeam();
        var zeile = model.TeamGroupRows[0].Rows[0];

        model.ToggleContactDetails(zeile);
        Assert.True(zeile.IsDetailExpanded);

        model.IsTeamReorderMode = true;

        Assert.False(zeile.IsDetailExpanded);
        Assert.Null(model.ExpandedContact);
    }

    [Fact]
    public void Im_Sortiermodus_bleibt_er_wo_er_war()
    {
        // <b>Der Fall, der eine Entscheidung brauchte.</b> Im Sortiermodus
        // gehen alle Gruppen auf. Ohne Anheftung spränge der Umschalter im
        // Augenblick des Klicks nach oben — unter dem Zeiger weg, als Folge
        // der eigenen Geste.
        var model = MitTeam();

        model.ToggleGroupExpansion(model.TeamGroupRows[0]);
        Assert.Equal("Dienste", Traeger(model)?.Name);

        model.IsTeamReorderMode = true;

        Assert.All(model.TeamGroupRows, g => Assert.True(g.IsExpanded));
        Assert.Equal("Dienste", Traeger(model)?.Name);
    }

    [Fact]
    public void Nach_dem_Sortieren_gilt_wieder_die_Regel()
    {
        var model = MitTeam();

        model.ToggleGroupExpansion(model.TeamGroupRows[0]);
        model.IsTeamReorderMode = true;
        model.IsTeamReorderMode = false;

        // «Team» ist wieder zugeklappt, also trägt ihn «Dienste».
        Assert.False(model.TeamGroupRows[0].IsExpanded);
        Assert.Equal("Dienste", Traeger(model)?.Name);
    }

    [Fact]
    public void Im_Sortiermodus_klappt_kein_Kopf()
    {
        // Alle Gruppen stehen offen, und ein Klick auf den Kopf trifft eine
        // Liste, in der gerade gezogen wird (ADR-042).
        var model = MitTeam();

        model.IsTeamReorderMode = true;
        model.ToggleGroupExpansion(model.TeamGroupRows[0]);

        Assert.True(model.TeamGroupRows[0].IsExpanded);
    }

    [Fact]
    public void Eine_Sperre_nimmt_ihn_weg_und_beendet_den_Modus()
    {
        // §17: legt ein Profil die Kontakte fest, gibt es nichts umzusortieren.
        // <b>Und ein laufender Modus geht dabei aus</b> — bis zum 13.09.2026
        // besorgte das ein Umweg im Fenster: ein zurückgesetzter Knopf löste
        // sein eigenes Ereignis aus.
        var model = MitTeam();

        model.IsTeamReorderMode = true;
        Assert.True(model.IsTeamReorderMode);

        _policy!.Apply(["contacts"]);

        Assert.False(model.ShowTeamReorder);
        Assert.False(model.IsTeamReorderMode);
        Assert.DoesNotContain(model.TeamGroupRows, static g => g.ShowReorderToggle);
    }

    [Fact]
    public void Eine_einzige_Nebenstelle_bekommt_keinen()
    {
        // Nichts zu sortieren — dieselbe Regel wie bisher, nur an einer Stelle
        // statt an zweien im Fenster.
        var model = MitTeam(new TeamExtension("Anna", "201", Group: "Team"));

        Assert.False(model.ShowTeamReorder);
        Assert.DoesNotContain(model.TeamGroupRows, static g => g.ShowReorderToggle);
    }

    [Fact]
    public void Der_Sortierzustand_steht_in_der_tragenden_Gruppe()
    {
        var model = MitTeam();

        model.IsTeamReorderMode = true;

        Assert.All(model.TeamGroupRows, static g => Assert.True(g.IsReordering));

        model.IsTeamReorderMode = false;

        Assert.All(model.TeamGroupRows, static g => Assert.False(g.IsReordering));
    }

    /// <summary>
    /// Genau eine Gruppe trägt ihn — und gibt sie zurück.
    ///
    /// <para>Die Invariante steckt in der Hilfsfunktion, damit jeder Test sie
    /// mitprüft, ohne sie auszuschreiben.</para>
    /// </summary>
    private static ContactGroupRow? Traeger(ShellViewModel model)
    {
        var traeger = model.TeamGroupRows.Where(static g => g.ShowReorderToggle).ToList();

        Assert.True(
            traeger.Count <= 1,
            $"{traeger.Count} Gruppen tragen den Umschalter. Es darf hoechstens eine sein.");

        return traeger.FirstOrDefault();
    }

    private ShellViewModel MitTeam(params TeamExtension[] team)
    {
        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_verzeichnis, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_verzeichnis, "settings.json"));

        einstellungen.Load();

        var nebenstellen = team.Length > 0
            ? team
            :
            [
                new TeamExtension("Anna", "201", Group: "Team"),
                new TeamExtension("Beat", "202", Group: "Dienste"),
            ];

        einstellungen.Save(einstellungen.Current with
        {
            Contacts = new ContactSettings
            {
                Groups = ["Team", "Dienste"],
                Team = [.. nebenstellen],
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

        var policy = new PolicyService();

        var model = new ShellViewModel(
            sip,
            new CallHistoryStore(
                NullLogger<CallHistoryStore>.Instance,
                Path.Combine(_verzeichnis, "history.db")),
            kontakte,
            new CallPartyResolver(new ClipResolver(kontakte)),
            new BlfService(sip, kontakte, einstellungen, NullLogger<BlfService>.Instance),
            einstellungen,
            policy,
            NullLogger<ShellViewModel>.Instance);

        _settings = einstellungen;
        _policy = policy;

        return model;
    }

    private SettingsService? _settings;
    private PolicyService? _policy;
}
