using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// ADR-054: der Benutzer gewinnt bei einem provisionierten, nicht gesperrten
/// Feld.
///
/// <para><b>Der Befund.</b> <c>docs/provisioning.md</c> beschrieb drei Ebenen
/// und den Benutzer als letzte. <c>ApplyValues</c> schrieb aber bei
/// <b>jedem Start</b> jeden Profilwert, ohne die Sperre zu befragen — das Wort
/// <c>IsLocked</c> kam in der Datei nicht vor. Damit galt faktisch das Profil,
/// und die Sperre entschied nur übers Ausgrauen. Eine von Hand geänderte
/// Codec-Reihenfolge war nach dem nächsten Start lautlos weg.</para>
///
/// <para><b>Kein Zugriff auf das Benutzerprofil</b> (siehe
/// <c>TestIsolationTests</c>): alle Pfade liegen unter dem
/// Temp-Verzeichnis.</para>
/// </summary>
public sealed class ProvisioningUserOverrideTests : IDisposable
{
    private readonly string _verzeichnis;
    private readonly SettingsService _settings;
    private readonly PolicyService _policy;
    private readonly ProvisioningService _provisioning;

    public ProvisioningUserOverrideTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "nipp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_verzeichnis);

        var secrets = new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_verzeichnis, "secrets.dat"));

        _settings = new SettingsService(
            secrets,
            NullLogger<SettingsService>.Instance,
            Path.Combine(_verzeichnis, "settings.json"));

        _policy = new PolicyService();

        _provisioning = new ProvisioningService(
            _settings,
            _policy,
            secrets,
            NullLogger<ProvisioningService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_verzeichnis))
        {
            Directory.Delete(_verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Ein_von_Hand_gesetzter_Wert_ueberlebt_das_Profil()
    {
        // Der Fall aus dem Alltag: jemand stellt das Keep-Alive um, weil seine
        // Firewall NAT-Bindungen früh vergisst. Beim nächsten Start stand
        // wieder 30 da, und im Protokoll keine Zeile dazu.
        _settings.Save(_settings.Current with
        {
            Network = _settings.Current.Network with { KeepAliveSeconds = 15 },
        });

        _provisioning.ApplyProfile(Profil("""
            <set path="network.keep-alive-seconds" value="300" />
            """));

        Assert.Equal(15, _settings.Current.Network.KeepAliveSeconds);
    }

    [Fact]
    public void Ein_gesperrtes_Feld_holt_der_Administrator_zurueck()
    {
        // Die Gegenprobe, und sie ist der Grund, warum die Regel tragbar ist:
        // ohne sie hätte ein Administrator keinen Weg mehr, einen
        // Benutzerwert zu überschreiben.
        _settings.Save(_settings.Current with
        {
            Network = _settings.Current.Network with { KeepAliveSeconds = 15 },
        });

        _provisioning.ApplyProfile(Profil(
            """<set path="network.keep-alive-seconds" value="300" />""",
            gesperrt: "network.keep-alive-seconds"));

        Assert.Equal(300, _settings.Current.Network.KeepAliveSeconds);

        // Und die Markierung ist weg: sonst gälte sie beim nächsten Start
        // wieder, und die Sperre hätte genau einmal gewirkt.
        Assert.DoesNotContain(
            "network.keep-alive-seconds",
            _settings.Current.UserOverrides,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Was_der_Benutzer_nie_angefasst_hat_setzt_das_Profil()
    {
        // Die andere Gegenprobe. Eine Regel, die alles stehen lässt, wäre
        // dasselbe wie gar keine Provisionierung.
        _provisioning.ApplyProfile(Profil("""
            <set path="network.keep-alive-seconds" value="300" />
            """));

        Assert.Equal(300, _settings.Current.Network.KeepAliveSeconds);
    }

    [Fact]
    public void Das_Profil_gewinnt_weiterhin_ueber_seinen_eigenen_Wert()
    {
        // Zwei Starts hintereinander mit demselben Profil, dazwischen keine
        // Benutzeränderung: das Profil muss beim zweiten Mal wieder greifen.
        //
        // Genau hier liegt die Falle: schriebe der Dienst über den gewöhnlichen
        // Speicherweg, würde sein eigener Wert sofort als Benutzeränderung
        // vermerkt, und die Regel liefe ab dem zweiten Start leer. Deshalb gibt
        // es SaveFromProfile.
        var profil = Profil("""<set path="network.keep-alive-seconds" value="300" />""");

        _provisioning.ApplyProfile(profil);
        _provisioning.ApplyProfile(Profil("""<set path="network.keep-alive-seconds" value="90" />"""));

        Assert.Equal(90, _settings.Current.Network.KeepAliveSeconds);
        Assert.Empty(_settings.Current.UserOverrides);
    }

    [Fact]
    public void Ein_von_Hand_angelegtes_Konto_ueberlebt_das_Profil()
    {
        // Bis zum 13.09.2026 ersetzte das Profil die Kontenliste vollständig.
        // Ein zweites, selbst eingerichtetes Konto war nach jedem Start weg —
        // lautlos, und die Meldung «ich muss mein Konto jeden Morgen neu
        // eintragen» hätte niemand hierher zurückverfolgt.
        _settings.Save(_settings.Current with
        {
            Accounts = [new SipAccountSettings
            {
                Username = "eigen",
                Domain = "example.test",
                Password = string.Empty,
            }],
        });

        _provisioning.ApplyProfile(Profil("""
            <accounts>
              <account username="ausdemprofil" domain="pbx.example.test" />
            </accounts>
            """));

        Assert.Equal("eigen", _settings.Current.Accounts[0].Username);
    }

    [Fact]
    public void Eine_Datei_von_vorher_hat_keine_Markierungen()
    {
        // Die Wanderung. Eine settings.json ohne das Feld bekommt eine leere
        // Menge — das Profil gewinnt dort beim ersten Start noch einmal, wie
        // bisher. Das steht so in ADR-054 und wird am Gerät geprüft (T258).
        var pfad = Path.Combine(_verzeichnis, "alt.json");

        // Eine echte Datei nachbauen heisst: PascalCase, wie SettingsService
        // sie schreibt. Ein Test, der camelCase nachbaut, lädt schlicht null
        // Einträge und prüft dann genau das — die Lehre steht in CLAUDE.md.
        File.WriteAllText(pfad, """
            {
              "SchemaVersion": 1,
              "Network": { "KeepAliveSeconds": 15 }
            }
            """);

        var geladen = new SettingsService(
            new SecretStore(
                NullLogger<SecretStore>.Instance,
                Path.Combine(_verzeichnis, "s2.dat")),
            NullLogger<SettingsService>.Instance,
            pfad);

        // Load() ist ausdruecklich zu rufen — der Dienst liest nicht im
        // Konstruktor. Der erste Anlauf dieses Tests vergass das und pruefte
        // dann die Standardwerte gegen sich selbst.
        geladen.Load();

        Assert.Equal(15, geladen.Current.Network.KeepAliveSeconds);
        Assert.Empty(geladen.Current.UserOverrides);
    }

    [Fact]
    public void Nur_der_geaenderte_Pfad_wird_vermerkt()
    {
        // Eine Markierung, die bei jedem Speichern alles einträgt, wäre
        // dasselbe wie «der Benutzer gewinnt immer» — und damit das Ende der
        // Provisionierung.
        _settings.Save(_settings.Current with
        {
            Network = _settings.Current.Network with { KeepAliveSeconds = 15 },
        });

        Assert.Equal(["network.keep-alive-seconds"], _settings.Current.UserOverrides);
    }

    private static ProvisioningProfile Profil(string inhalt, string? gesperrt = null)
    {
        // Der Parser liest den Feldnamen aus dem INHALT des Elements, nicht
        // aus einem Attribut — nachgesehen, nicht angenommen.
        var sperre = gesperrt is null
            ? string.Empty
            : $"<locked><field>{gesperrt}</field></locked>";

        var einstellungen = inhalt.Contains("<accounts", StringComparison.Ordinal)
            ? inhalt
            : $"<settings>{inhalt}</settings>";

        var xml = $"""
            <nipp-provisioning version="1">
              {einstellungen}
              {sperre}
            </nipp-provisioning>
            """;

        Assert.True(ProvisioningParser.TryParse(xml, out var profil, out var fehler), fehler);
        return profil;
    }
}
