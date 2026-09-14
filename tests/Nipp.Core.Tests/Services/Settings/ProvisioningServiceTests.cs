using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// Prüft das Übernehmen eines Profils (§17, ADR-012).
///
/// <b>Diese Tests gibt es wegen zweier Fehler</b>, die der Review vom
/// 05.09.2026 gefunden hat und die beide unbemerkt geblieben waren:
/// <list type="number">
///   <item>
///     Ein Profil ohne Passwort — der in <c>docs/provisioning.md</c>
///     empfohlene Weg — ersetzte die Kontoliste durch Konten mit
///     <b>leerem</b> Passwort. Das über DPAPI hinterlegte blieb liegen und
///     wurde nicht mehr gelesen: das Konto registrierte sich nie.
///   </item>
///   <item>
///     Ein Profil aus dem Netz durfte die Provisioning-Adresse selbst setzen.
///     Wer einmal antworten konnte, hätte damit dauerhaft bestimmt, woher
///     dieser Arbeitsplatz seine Konten bezieht.
///   </item>
/// </list>
///
/// <b>Kein Zugriff auf das Benutzerprofil</b> (siehe
/// <c>TestIsolationTests</c>): alle Pfade liegen unter dem Temp-Verzeichnis.
/// </summary>
public sealed class ProvisioningServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsService _settings;
    private readonly SecretStore _secrets;
    private readonly PolicyService _policy;
    private readonly ProvisioningService _provisioning;

    public ProvisioningServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nipp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _secrets = new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat"));

        _settings = new SettingsService(
            _secrets,
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

        _policy = new PolicyService();

        _provisioning = new ProvisioningService(
            _settings,
            _policy,
            _secrets,
            NullLogger<ProvisioningService>.Instance);
    }

    [Fact]
    public void Ein_Profil_ohne_Passwort_behaelt_das_hinterlegte()
    {
        // Der Ausgangszustand: ein eingerichtetes Konto, dessen Passwort über
        // DPAPI abgelegt ist.
        _secrets.Set("sip:151@pbx.example.ch", "geheim");

        var profile = Parse("""
            <nipp-provisioning version="1" profile="test">
              <accounts>
                <account username="151" domain="pbx.example.ch" transport="tls" />
              </accounts>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile);

        var account = Assert.Single(_settings.Current.Accounts);

        Assert.Equal("sip:151@pbx.example.ch", account.Identity);
        Assert.Equal("geheim", account.Password);
    }

    [Fact]
    public void Ein_Passwort_im_Profil_gewinnt_gegen_das_hinterlegte()
    {
        // Ein ausdrücklich genanntes Passwort ist eine Ansage der
        // Administration und ersetzt das lokale.
        _secrets.Set("sip:151@pbx.example.ch", "alt");

        var profile = Parse("""
            <nipp-provisioning version="1">
              <accounts>
                <account username="151" domain="pbx.example.ch" password="neu" />
              </accounts>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile);

        Assert.Equal("neu", Assert.Single(_settings.Current.Accounts).Password);
        Assert.Equal("neu", _secrets.Get("sip:151@pbx.example.ch"));
    }

    [Fact]
    public void Ein_Profil_aus_dem_Netz_darf_die_Bezugsquelle_nicht_umschreiben()
    {
        _settings.Save(_settings.Current with
        {
            Advanced = _settings.Current.Advanced with { ProvisioningUri = "https://echt.bv2.ch/p.xml" },
        });

        var profile = Parse("""
            <nipp-provisioning version="1">
              <settings>
                <set path="advanced.provisioning-uri" value="http://boeser-server.example/p.xml" />
                <set path="advanced.allow-insecure-provisioning" value="true" />
                <set path="advanced.country-prefix" value="+49" />
              </settings>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile, trusted: false);

        // Die Adresse bleibt, das Vertrauen auch — die harmlose Einstellung
        // wird trotzdem übernommen.
        Assert.Equal("https://echt.bv2.ch/p.xml", _settings.Current.Advanced.ProvisioningUri);
        Assert.False(_settings.Current.Advanced.AllowInsecureProvisioning);
        Assert.Equal("+49", _settings.Current.Advanced.CountryPrefix);
    }

    [Fact]
    public void Die_mitgelieferte_Konfiguration_darf_es()
    {
        var profile = Parse("""
            <nipp-provisioning version="1">
              <settings>
                <set path="advanced.provisioning-uri" value="https://prov.bv2.ch/kunde.xml" />
              </settings>
            </nipp-provisioning>
            """);

        // Die Factory-Config liegt unter %PROGRAMDATA% und kommt mit der
        // Installation — sie darf festlegen, woher Profile kommen.
        _provisioning.ApplyProfile(profile, trusted: true);

        Assert.Equal("https://prov.bv2.ch/kunde.xml", _settings.Current.Advanced.ProvisioningUri);
    }

    [Fact]
    public void Gesperrte_Felder_kommen_beim_Bedienschutz_an()
    {
        var profile = Parse("""
            <nipp-provisioning version="1">
              <locked>
                <field>accounts</field>
              </locked>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile);

        Assert.True(_policy.IsLocked("accounts"));
        Assert.True(_policy.IsLocked("accounts.password"));
        Assert.False(_policy.IsLocked("audio"));
    }

    [Fact]
    public void Ein_Profil_ersetzt_die_Kontoliste_und_laesst_den_Rest_stehen()
    {
        // ADR-054: das Konto kommt hier ueber SaveFromProfile herein, nicht
        // ueber Save. Seit dem 13.09.2026 gilt eine von Hand geaenderte
        // Kontenliste als Entscheidung des Benutzers und wird nicht mehr
        // ersetzt — dieser Test prueft weiterhin den Normalfall, also einen
        // Arbeitsplatz, an dem niemand von Hand eingegriffen hat.
        // Der Gegenfall steht in ProvisioningUserOverrideTests.
        _settings.SaveFromProfile(_settings.Current with
        {
            Accounts = [new SipAccountSettings { Username = "alt", Domain = "alt.ch", Password = "x" }],
        });

        _settings.Save(_settings.Current with
        {
            Audio = _settings.Current.Audio with { PlaybackVolume = 33 },
        });

        var profile = Parse("""
            <nipp-provisioning version="1">
              <accounts>
                <account username="151" domain="pbx.example.ch" />
              </accounts>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile);

        Assert.Equal("151", Assert.Single(_settings.Current.Accounts).Username);

        // Was das Profil nicht nennt, bleibt so, wie der Benutzer es
        // eingestellt hat — das ist der Unterschied zwischen einer Vorgabe und
        // einem vollständigen Einstellungssatz.
        Assert.Equal(33, _settings.Current.Audio.PlaybackVolume);
    }

    // ---- Klingelton aus einem Profil (§9.4, §16.2) ----

    [Fact]
    public void Ein_mitgelieferter_Klingelton_kommt_auch_aus_dem_Netz()
    {
        var profile = Parse($"""
            <nipp-provisioning version="1" profile="test">
              <settings>
                <set path="audio.ringtone" value="{NippSounds.SdkOldPhone}" />
              </settings>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile, trusted: false);

        Assert.Equal(NippSounds.SdkOldPhone, _settings.Current.Audio.RingtonePath);
    }

    /// <summary>
    /// Der Grund für die Einschränkung: ein Profil aus dem Netz, das einen
    /// beliebigen Pfad setzen darf, kann eine Freigabe hinterlegen — und dann
    /// fragt nipp beim nächsten Klingeln einen fremden Server, mit den
    /// Anmeldedaten des angemeldeten Benutzers.
    /// </summary>
    [Fact]
    public void Eine_Freigabe_als_Klingelton_wird_aus_dem_Netz_uebergangen()
    {
        var profile = Parse("""
            <nipp-provisioning version="1" profile="test">
              <settings>
                <set path="audio.ringtone" value="\\fremder-server\freigabe\ring.wav" />
              </settings>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile, trusted: false);

        Assert.Null(_settings.Current.Audio.RingtonePath);
    }

    [Fact]
    public void Ein_Pfad_aus_dem_Verzeichnis_hinaus_wird_immer_uebergangen()
    {
        var profile = Parse("""
            <nipp-provisioning version="1" profile="test">
              <settings>
                <set path="audio.ringtone" value="Assets/../../../ring.wav" />
              </settings>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile, trusted: true);

        Assert.Null(_settings.Current.Audio.RingtonePath);
    }

    [Fact]
    public void Die_mitgelieferte_Konfiguration_darf_einen_eigenen_Pfad_setzen()
    {
        var profile = Parse("""
            <nipp-provisioning version="1" profile="test">
              <settings>
                <set path="audio.ringtone" value="C:\bv2\klingelton.wav" />
              </settings>
            </nipp-provisioning>
            """);

        _provisioning.ApplyProfile(profile, trusted: true);

        Assert.Equal(@"C:\bv2\klingelton.wav", _settings.Current.Audio.RingtonePath);
    }

    private static ProvisioningProfile Parse(string xml)
    {
        Assert.True(ProvisioningParser.TryParse(xml, out var profile, out var error), error);
        return profile;
    }

    public void Dispose()
    {
        _provisioning.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
