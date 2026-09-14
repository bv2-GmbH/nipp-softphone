using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// §13 nennt die Settings-Serialisierung als verbindlich zu testen.
///
/// <b>Jeder Test arbeitet in einem eigenen Wegwerfverzeichnis.</b> Vorher
/// liefen sie gegen die echten Pfade unter <c>%APPDATA%</c> und
/// <c>%LOCALAPPDATA%</c> mit der Begründung, das sei „ehrlicher als eine
/// Abstraktion, die nur für die Tests existiert". Diese Begründung war falsch,
/// und der Preis dafür ist am 05.09.2026 fällig geworden: ein gewöhnliches
/// <c>dotnet test</c> hat das eingerichtete SIP-Konto des angemeldeten
/// Benutzers samt Passwort gelöscht. Der Test „eine kaputte Datei kostet nicht
/// den Start" schreibt schliesslich eine kaputte Datei — und schrieb sie
/// dorthin, wo die echte lag.
///
/// Der DPAPI-Pfad wird weiterhin echt geprüft: die Verschlüsselung hängt am
/// Benutzerkonto, nicht am Ablageort. Es geht nichts verloren.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nipp-settings-test-{Guid.NewGuid():N}");

    private readonly SecretStore _secrets;
    private readonly SettingsService _service;

    private string SettingsPath => _service.SettingsPath;

    public SettingsServiceTests()
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

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Testverzeichnis im Temp-Ordner ist
            // hinnehmbar; ein fehlschlagender Aufräumschritt, der einen
            // grünen Test rot macht, nicht.
        }
    }

    [Fact]
    public void Ohne_Datei_gelten_die_Standardwerte_aus_Paragraph_9()
    {
        var settings = _service.Load();

        // Stichproben aus jeder Gruppe von §9
        Assert.Equal(5061, settings.Network.SipPort);
        Assert.Equal(7078, settings.Network.RtpPortMin);
        Assert.Equal(7178, settings.Network.RtpPortMax);
        Assert.Equal(24, settings.Network.SipDscp);
        Assert.Equal(46, settings.Network.AudioDscp);
        Assert.True(settings.NatMedia.EnableIce);
        Assert.Equal(MediaEncryptionSetting.Srtp, settings.NatMedia.Encryption);
        Assert.Equal(72, settings.Audio.PlaybackVolume);
        Assert.Equal(50, settings.Audio.MicrophoneLevel);
        Assert.Equal(DtmfMode.Rfc2833, settings.Codecs.Dtmf);
        Assert.Equal("+41", settings.Advanced.CountryPrefix);
        Assert.Equal(365, settings.Advanced.HistoryRetentionDays);
    }

    [Fact]
    public void Verschluesselung_wird_angeboten_aber_nicht_erzwungen()
    {
        // ADR-007: §9.3 gab ursprünglich "erzwingen: ein" vor. Die aktuelle
        // Anlage hat keine Verschlüsselung — mit "ein" wäre kein Gespräch
        // möglich. Dieser Test hält die Entscheidung fest.
        var settings = _service.Load();

        Assert.Equal(MediaEncryptionSetting.Srtp, settings.NatMedia.Encryption);
        Assert.False(settings.NatMedia.EncryptionMandatory);
    }

    [Fact]
    public void G722_ist_aktiv_und_G729_kommt_nicht_vor()
    {
        // §9.5 will G.722 aktiv auf Priorität 2 — im SDK ist es standardmässig
        // aus. Und G.729 ist gar nicht im Build (§3), die Zeile in §9.5 geht
        // ins Leere.
        var settings = _service.Load();

        Assert.Contains("G722", settings.Codecs.Enabled);
        Assert.Equal(1, settings.Codecs.Order.IndexOf("G722"));
        Assert.DoesNotContain(settings.Codecs.Enabled, c => c.Contains("729", StringComparison.Ordinal));
    }

    [Fact]
    public void Speichern_und_Laden_erhaelt_die_Werte()
    {
        var original = new NippSettings
        {
            Network = new NetworkSettings { SipPort = 5060, KeepAliveSeconds = 45, EnableIpv6 = true },
            Audio = new AudioSettings { PlaybackVolume = 80, MicrophoneLevel = 30, EchoCalibrationMs = 120 },
            Advanced = new AdvancedSettings { CountryPrefix = "+49", AutoAnswer = true },
        };

        _service.Save(original);
        var loaded = new SettingsService(_secrets, NullLogger<SettingsService>.Instance, SettingsPath).Load();

        Assert.Equal(5060, loaded.Network.SipPort);
        Assert.Equal(45, loaded.Network.KeepAliveSeconds);
        Assert.True(loaded.Network.EnableIpv6);
        Assert.Equal(80, loaded.Audio.PlaybackVolume);
        Assert.Equal(120, loaded.Audio.EchoCalibrationMs);
        Assert.Equal("+49", loaded.Advanced.CountryPrefix);
        Assert.True(loaded.Advanced.AutoAnswer);
    }

    [Fact]
    public void Das_Passwort_steht_niemals_in_der_Klartextdatei()
    {
        // §10: "niemals Klartext". Der wichtigste Test dieser Datei.
        const string secret = "streng-geheim-4711";

        _service.Save(new NippSettings
        {
            Accounts =
            [
                new SipAccountSettings
                {
                    Username = "151bv2",
                    Domain = "pbx.example.ch",
                    Password = secret,
                },
            ],
        });

        var onDisk = File.ReadAllText(SettingsPath);

        Assert.DoesNotContain(secret, onDisk, StringComparison.Ordinal);
        Assert.Contains("151bv2", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_Ausgabedatei_enthaelt_kein_Passwort()
    {
        // Dasselbe Versprechen wie fuer settings.json, an einer zweiten Stelle:
        // eine Ausgabedatei wird herumgereicht, angehaengt, in ein Ticket
        // gelegt. Kaeme je ein Geheimnisfeld dazu, faellt es hier auf.
        const string secret = "streng-geheim-4711";

        _service.Save(new NippSettings
        {
            Accounts =
            [
                new SipAccountSettings
                {
                    Username = "151bv2",
                    Domain = "pbx.example.ch",
                    Password = secret,
                },
            ],
        });

        var target = Path.Combine(_directory, "ausgabe.json");
        _service.Export(target);

        var content = File.ReadAllText(target);

        Assert.DoesNotContain(secret, content, StringComparison.Ordinal);
        Assert.Contains("151bv2", content, StringComparison.Ordinal);
        Assert.Contains("_hinweis", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ausgeben_und_wieder_einlesen_erhaelt_die_Werte()
    {
        _service.Save(new NippSettings
        {
            Advanced = new AdvancedSettings
            {
                CountryPrefix = "+49",
                GlobalHotkey = "Ctrl+Alt+N",
                HistoryRetentionDays = 30,
            },
            Contacts = new ContactSettings
            {
                Team = [new TeamExtension("Empfang", "100", "sip:100@pbx.example.ch")],
            },
        });

        var target = Path.Combine(_directory, "ausgabe.json");
        _service.Export(target);

        // Etwas anderes einstellen, damit sich das Einlesen zeigen muss.
        _service.Save(_service.Current with
        {
            Advanced = _service.Current.Advanced with { CountryPrefix = "+1" },
        });

        Assert.True(_service.TryImport(target, out var error), error);
        Assert.Null(error);

        Assert.Equal("+49", _service.Current.Advanced.CountryPrefix);
        Assert.Equal("Ctrl+Alt+N", _service.Current.Advanced.GlobalHotkey);
        Assert.Equal(30, _service.Current.Advanced.HistoryRetentionDays);
        Assert.Equal("Empfang", Assert.Single(_service.Current.Contacts.Team).DisplayName);
    }

    [Fact]
    public void Das_Passwort_ueberlebt_das_Einlesen()
    {
        // Wer dieselben Konten schon eingerichtet hatte, soll nichts neu
        // eintippen muessen — das Geheimnis liegt ja weiterhin lokal.
        const string secret = "streng-geheim-4711";

        _service.Save(new NippSettings
        {
            Accounts =
            [
                new SipAccountSettings
                {
                    Username = "151bv2",
                    Domain = "pbx.example.ch",
                    Password = secret,
                },
            ],
        });

        var target = Path.Combine(_directory, "ausgabe.json");
        _service.Export(target);

        Assert.True(_service.TryImport(target, out _));

        Assert.Equal(secret, Assert.Single(_service.Current.Accounts).Password);
    }

    [Fact]
    public void Eine_kaputte_Ausgabedatei_aendert_nichts()
    {
        _service.Save(new NippSettings
        {
            Advanced = new AdvancedSettings { CountryPrefix = "+41" },
        });

        var target = Path.Combine(_directory, "kaputt.json");
        File.WriteAllText(target, "{ das ist kein JSON");

        Assert.False(_service.TryImport(target, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal("+41", _service.Current.Advanced.CountryPrefix);
    }

    [Fact]
    public void Eine_Datei_ohne_PCMA_und_PCMU_wird_abgelehnt()
    {
        var target = Path.Combine(_directory, "ohne-codecs.json");
        File.WriteAllText(
            target,
            """{ "Codecs": { "Enabled": [ "opus" ], "Order": [ "opus" ] } }""");

        Assert.False(_service.TryImport(target, out var error));
        Assert.Contains("PCMA", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Zuruecksetzen_stellt_die_Standardwerte_her_und_laesst_die_Konten_stehen()
    {
        _service.Save(new NippSettings
        {
            Accounts =
            [
                new SipAccountSettings
                {
                    Username = "151bv2",
                    Domain = "pbx.example.ch",
                    Password = "streng-geheim-4711",
                },
            ],
            Advanced = new AdvancedSettings
            {
                CountryPrefix = "+49",
                HistoryRetentionDays = 7,
            },
        });

        _service.Reset();

        var standard = new NippSettings();

        Assert.Equal(standard.Advanced.CountryPrefix, _service.Current.Advanced.CountryPrefix);
        Assert.Equal(standard.Advanced.HistoryRetentionDays, _service.Current.Advanced.HistoryRetentionDays);

        // Der Punkt der Entscheidung: danach laesst sich weiter telefonieren.
        var account = Assert.Single(_service.Current.Accounts);
        Assert.Equal("151bv2", account.Username);
        Assert.Equal("streng-geheim-4711", account.Password);
    }

    [Fact]
    public void Das_Passwort_kommt_beim_Laden_aus_dem_SecretStore_zurueck()
    {
        const string secret = "streng-geheim-4711";

        _service.Save(new NippSettings
        {
            Accounts =
            [
                new SipAccountSettings { Username = "151bv2", Domain = "pbx.example.ch", Password = secret },
            ],
        });

        var loaded = new SettingsService(_secrets, NullLogger<SettingsService>.Instance, SettingsPath).Load();

        Assert.Single(loaded.Accounts);
        Assert.Equal(secret, loaded.Accounts[0].Password);
    }

    [Fact]
    public void Kaputte_Datei_verhindert_den_Start_nicht()
    {
        // §11 verlangt dasselbe fürs Provisioning: ein Fehler darf den Start
        // nicht verhindern. Hier gilt es genauso.
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ das ist kein gültiges JSON ][");

        var settings = _service.Load();

        Assert.Equal(5061, settings.Network.SipPort);
        Assert.Empty(settings.Accounts);
    }

    [Fact]
    public void Kaputte_Datei_wird_aufgehoben_statt_ueberschrieben()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ kaputt ][");

        _service.Load();

        var preserved = Directory.GetFiles(
            Path.GetDirectoryName(SettingsPath)!,
            "settings.json.kaputt-*");

        Assert.NotEmpty(preserved);

        foreach (var file in preserved)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Ohne_PCMA_und_PCMU_wird_nicht_gespeichert()
    {
        // §9.5: "PCMA und PCMU dürfen nicht beide deaktiviert werden — das UI
        // verhindert es, weil sonst die Verhandlung mit vielen Trunks
        // scheitert." Die Regel gehört in den Dienst, damit auch ein
        // Provisioning-Profil sie nicht umgehen kann (§11).
        var settings = new NippSettings
        {
            Codecs = new CodecSettings { Enabled = ["opus", "G722"] },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _service.Save(settings));

        Assert.Contains("PCMA", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PCMU", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mit_nur_einem_der_beiden_wird_gespeichert()
    {
        var settings = new NippSettings
        {
            Codecs = new CodecSettings { Enabled = ["opus", "PCMA"] },
        };

        _service.Save(settings);

        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Aenderungen_werden_gemeldet()
    {
        // AP5.5: die Dienste müssen nachziehen können, ohne zu pollen.
        NippSettings? received = null;
        _service.Changed += (_, s) => received = s;

        _service.Save(new NippSettings { Advanced = new AdvancedSettings { CountryPrefix = "+43" } });

        Assert.NotNull(received);
        Assert.Equal("+43", received.Advanced.CountryPrefix);
    }

    [Fact]
    public void Video_bleibt_aus_und_laesst_sich_nicht_setzen()
    {
        // §2 schliesst Video aus, §9.3 verlangt den Schalter ausgegraut.
        // Die Eigenschaft ist berechnet — es gibt gar keinen Weg, sie
        // einzuschalten.
        Assert.False(NatMediaSettings.VideoEnabled);
    }
}

/// <summary>
/// Verhindert, dass die Settings-Tests parallel laufen — sie teilen sich die
/// Dateien unter %APPDATA% und %LOCALAPPDATA%.
/// </summary>
[CollectionDefinition("Settings", DisableParallelization = true)]
public sealed class SettingsTestGroup;
