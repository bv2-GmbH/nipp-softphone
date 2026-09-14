using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Diagnostics;

/// <summary>
/// §12 (M7) verlangt ausdrücklich: „Diagnose-ZIP enthält Logs und <b>keine</b>
/// Passwörter (im Test explizit prüfen)."
///
/// Das ist der einzige Punkt am Diagnosepaket, bei dem ein Fehler teuer wird —
/// so ein Paket geht per Mail an den Support und bleibt in Postfächern liegen.
/// </summary>
[Collection("Settings")]
public sealed class DiagnosticsBundleTests : IDisposable
{
    private const string Secret = "streng-geheimes-passwort-4711";
    private const string TurnSecret = "turn-geheimnis-0815";

    /// <summary>
    /// Eigenes Wegwerfverzeichnis — aus demselben Grund wie in
    /// <c>SettingsServiceTests</c>: die Tests haben sonst die Konfiguration
    /// des angemeldeten Benutzers gelöscht.
    /// </summary>
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nipp-diag-settings-{Guid.NewGuid():N}");

    private readonly SecretStore _secrets;
    private readonly SettingsService _settings;
    private readonly IntegrationConfigStore _integrations;
    private readonly DiagnosticsBundle _bundle;
    private readonly string _output = Path.Combine(
        Path.GetTempPath(),
        $"nipp-diag-test-{Guid.NewGuid():N}");

    public DiagnosticsBundleTests()
    {
        Directory.CreateDirectory(_directory);

        _secrets = new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat"));

        _settings = new SettingsService(
            _secrets,
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));
        _settings.Load();
        _settings.Save(new NippSettings
        {
            Accounts =
            [
                new SipAccountSettings
                {
                    Username = "151bv2",
                    Domain = "pbx.example.ch",
                    Password = Secret,
                    DisplayName = "nipp Testgerät",
                },
            ],
            NatMedia = new NatMediaSettings
            {
                TurnUsername = "turnuser",
                StunServer = "stun.example.ch",
            },
        });

        // Die Integrationen (§21.3) gehen denselben Weg wie die Einstellungen:
        // eigener Pfad im Testverzeichnis, damit kein Test das Profil des
        // Benutzers anfasst (TestIsolationTests).
        var secrets = new IntegrationSecrets(
            new SecretStore(
                NullLogger<SecretStore>.Instance,
                Path.Combine(_directory, "integration-secrets.dat")));

        _integrations = new IntegrationConfigStore(
            new IntegrationConfigValidator(secrets),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));

        // Ein eigenes, leeres Logverzeichnis: sonst lesen die Tests die
        // Protokolle der laufenden Anwendung mit und pruefen bei jedem Lauf
        // etwas anderes.
        _bundle = new DiagnosticsBundle(
            _settings,
            _integrations,
            secrets,
            NullLogger<DiagnosticsBundle>.Instance,
            Path.Combine(_directory, "logs"));
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _directory, _output })
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Ein liegengebliebenes Testverzeichnis ist hinnehmbar,
                // ein daran scheiternder Test nicht.
            }
        }
    }

    private async Task<string> CreateAndReadAllAsync()
    {
        var path = await _bundle.CreateAsync(_output);

        using var archive = ZipFile.OpenRead(path);
        var everything = new System.Text.StringBuilder();

        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            everything.AppendLine(entry.FullName);
            everything.AppendLine(await reader.ReadToEndAsync());
        }

        return everything.ToString();
    }

    [Fact]
    public async Task Das_Passwort_steht_nirgends_im_Paket()
    {
        var content = await CreateAndReadAllAsync();

        Assert.DoesNotContain(Secret, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auch_kein_TURN_Geheimnis()
    {
        _settings.Save(_settings.Current with
        {
            NatMedia = _settings.Current.NatMedia with { TurnUsername = TurnSecret },
        });

        var content = await CreateAndReadAllAsync();

        Assert.DoesNotContain(TurnSecret, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Konfiguration_ist_trotzdem_brauchbar()
    {
        // Ein Paket ohne Inhalt wäre sicher, aber nutzlos. Was für die
        // Fehlersuche zählt, muss drin sein.
        var content = await CreateAndReadAllAsync();

        Assert.Contains("151bv2", content, StringComparison.Ordinal);
        Assert.Contains("pbx.example.ch", content, StringComparison.Ordinal);
        Assert.Contains("stun.example.ch", content, StringComparison.Ordinal);
        Assert.Contains("einstellungen.json", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Es_steht_drin_ob_ein_Passwort_gesetzt_ist()
    {
        // Für den Support ist „hat eines" die eigentliche Information —
        // welches, muss er nicht wissen.
        var content = await CreateAndReadAllAsync();

        Assert.Contains("HatPasswort", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dasselbe für die Integrationen (§21.2). Ein Endpunkt trägt im Pfad
    /// gelegentlich eine Mandantenkennung, ein Anfrageparameter die Vorlage
    /// für die Rufnummer — beides hat in einem Paket nichts zu suchen, das per
    /// Mail an den Support geht.
    /// </summary>
    [Fact]
    public async Task Von_den_Integrationen_steht_nur_das_Unbedenkliche_im_Paket()
    {
        const string ApiSchluessel = "streng-geheimer-api-schluessel-4711";

        var geheimnisse = new IntegrationSecrets(_secrets);
        geheimnisse.Set("crm.apiKey", ApiSchluessel);

        _integrations.Save(new Nipp.Core.Services.Integrations.Config.IntegrationConfig
        {
            DataSources =
            [
                new DataSourceDefinition
                {
                    Id = "crm",
                    DisplayName = "Muster-CRM",
                    Http = new HttpConnection
                    {
                        BaseUrl = "https://crm.example.ch/api/v2/mandant-4711",
                        Headers = { ["X-Mandant"] = "geheime-mandantennummer" },
                        Auth = new AuthDefinition
                        {
                            Type = AuthKind.ApiKey,
                            Name = "X-Api-Key",
                            SecretRef = "crm.apiKey",
                        },
                    },
                    LookupByPhone = new LookupByPhoneCapability
                    {
                        Request = new RequestDefinition
                        {
                            Path = "/contacts",
                            Query = new Dictionary<string, string> { ["phone"] = "{{number.e164}}" },
                        },
                    },
                },
            ],
        });

        var content = await CreateAndReadAllAsync();

        // Nichts Vertrauliches.
        Assert.DoesNotContain(ApiSchluessel, content, StringComparison.Ordinal);
        Assert.DoesNotContain("geheime-mandantennummer", content, StringComparison.Ordinal);
        Assert.DoesNotContain("mandant-4711", content, StringComparison.Ordinal);
        Assert.DoesNotContain("number.e164", content, StringComparison.Ordinal);

        // Aber genug, um einer Fehlersuche zu helfen.
        Assert.Contains("integrationen.json", content, StringComparison.Ordinal);
        Assert.Contains("Muster-CRM", content, StringComparison.Ordinal);
        Assert.Contains("crm.example.ch", content, StringComparison.Ordinal);
        Assert.Contains("ZugangsdatenHinterlegt", content, StringComparison.Ordinal);
        Assert.Contains("LookupByPhone", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Systeminfo_ist_dabei()
    {
        var content = await CreateAndReadAllAsync();

        Assert.Contains("systeminfo.txt", content, StringComparison.Ordinal);
        Assert.Contains("Betriebssystem", content, StringComparison.Ordinal);
        Assert.Contains(".NET", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Systeminfo_nennt_keinen_Benutzer_und_keinen_Rechner()
    {
        // Geprüft wird die Systeminfo, nicht das ganze Paket: die beigelegten
        // Logs enthalten Dateipfade unter %LOCALAPPDATA%, und darin steht der
        // Benutzername zwangsläufig. Das Paket ist sparsam, nicht anonym — und
        // sagt das auch.
        var path = await _bundle.CreateAsync(_output);

        using var archive = ZipFile.OpenRead(path);
        var entry = Assert.Single(archive.Entries, e => e.FullName == "systeminfo.txt");

        using var reader = new StreamReader(entry.Open());
        var systemInfo = await reader.ReadToEndAsync();

        Assert.DoesNotContain(Environment.MachineName, systemInfo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserDomainName, systemInfo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nicht anonym", systemInfo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Das_Paket_liegt_wo_es_soll_und_laesst_sich_oeffnen()
    {
        var path = await _bundle.CreateAsync(_output);

        Assert.True(File.Exists(path));
        Assert.StartsWith(_output, path, StringComparison.Ordinal);
        Assert.EndsWith(".zip", path, StringComparison.Ordinal);

        using var archive = ZipFile.OpenRead(path);
        Assert.NotEmpty(archive.Entries);
    }
}
