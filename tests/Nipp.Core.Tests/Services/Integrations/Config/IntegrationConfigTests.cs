using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Config;

/// <summary>
/// Ablage und Prüfung der Integrationskonfiguration (ADR-017, §21.3).
///
/// <b>Zwei Dinge stehen hier auf dem Spiel.</b> Erstens darf eine kaputte oder
/// halb fertige Konfiguration nichts kosten ausser den Integrationen — das
/// Telefonieren hängt an keiner Quelle (§21.2). Zweitens sollen Fehler beim
/// Einrichten gefunden werden, nicht beim ersten Anruf: die Prüfung sammelt
/// alle Befunde auf einmal und formuliert sie nach §15 mit Ursache und
/// Abhilfe.
/// </summary>
public sealed class IntegrationConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private string Pfad => Path.Combine(_directory, "integrations.json");

    private IntegrationSecrets Geheimnisse() =>
        new(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

    private IntegrationConfigStore Speicher(IntegrationSecrets? geheimnisse = null) =>
        new(
            new IntegrationConfigValidator(geheimnisse ?? Geheimnisse()),
            NullLogger<IntegrationConfigStore>.Instance,
            Pfad);

    private static DataSourceDefinition Quelle(string id = "crm") => new()
    {
        Id = id,
        DisplayName = "Muster-CRM",
        Http = new HttpConnection { BaseUrl = "https://crm.example.ch/api" },
        LookupByPhone = new LookupByPhoneCapability
        {
            Request = new RequestDefinition
            {
                Path = "/contacts",
                Query = new Dictionary<string, string> { ["phone"] = "{{number.e164}}" },
            },
            Mapping = { ["customerName"] = new FieldMapping(Path: "$.contact.fullName") },
        },
    };

    // --- Ablage ---

    [Fact]
    public void Eine_gespeicherte_Konfiguration_kommt_unveraendert_zurueck()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig { DataSources = [Quelle()] });

        var gelesen = Speicher().Load();

        Assert.Single(gelesen.DataSources);
        Assert.Equal("crm", gelesen.DataSources[0].Id);
        Assert.Equal(
            "$.contact.fullName",
            gelesen.DataSources[0].LookupByPhone!.Mapping["customerName"].Path);
    }

    [Fact]
    public void Ohne_Datei_gibt_es_einfach_keine_Integrationen()
    {
        var geladen = Speicher().Load();

        Assert.Empty(geladen.DataSources);
        Assert.Empty(Speicher().Issues);
    }

    /// <summary>
    /// Dieselbe Haltung wie bei den Einstellungen: in einer Integrationsdatei
    /// steckt unter Umständen die Arbeit eines halben Tages. Sie wird
    /// beiseitegelegt, nicht überschrieben.
    /// </summary>
    [Fact]
    public void Eine_kaputte_Datei_wird_aufgehoben_und_kostet_nur_die_Integrationen()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Pfad, "{ das ist kein JSON");

        var geladen = Speicher().Load();

        Assert.Empty(geladen.DataSources);
        Assert.False(File.Exists(Pfad));
        Assert.NotEmpty(Directory.GetFiles(_directory, "integrations.json.kaputt-*"));
    }

    [Fact]
    public void Beim_Speichern_wird_ueber_eine_Nebendatei_geschrieben()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig { DataSources = [Quelle()] });

        Assert.True(File.Exists(Pfad));
        Assert.False(File.Exists(Pfad + ".tmp"));
    }

    [Fact]
    public void Eine_Aenderung_wird_genau_einmal_gemeldet()
    {
        var speicher = Speicher();
        var meldungen = 0;

        speicher.Changed += (_, _) => meldungen++;
        speicher.Save(new IntegrationConfig { DataSources = [Quelle()] });

        Assert.Equal(1, meldungen);
    }

    /// <summary>
    /// Kommentare und nachgestellte Kommas sind in einer von Hand gepflegten
    /// Konfigurationsdatei die Regel, nicht die Ausnahme.
    /// </summary>
    [Fact]
    public void Kommentare_in_der_Datei_stoeren_nicht()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Pfad, """
            {
              // Das CRM des Kunden
              "schemaVersion": 1,
              "dataSources": [
                {
                  "id": "crm",
                  "displayName": "Muster-CRM",
                  "http": { "baseUrl": "https://crm.example.ch/api" },
                },
              ],
            }
            """);

        Assert.Single(Speicher().Load().DataSources);
    }

    // --- Welche Quellen benutzt werden ---

    [Fact]
    public void Eine_abgeschaltete_Quelle_wird_nicht_benutzt()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig
        {
            DataSources = [Quelle() with { Enabled = false }],
        });

        Assert.Empty(speicher.UsableSources);
    }

    [Fact]
    public void Eine_Quelle_mit_Fehler_bleibt_aus_die_anderen_laufen()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig
        {
            DataSources =
            [
                Quelle("crm"),
                Quelle("kaputt") with { Http = new HttpConnection { BaseUrl = "keine-adresse" } },
            ],
        });

        Assert.Single(speicher.UsableSources);
        Assert.Equal("crm", speicher.UsableSources[0].Id);
    }

    /// <summary>
    /// Ein fehlendes Geheimnis ist eine Warnung, kein Fehler: die
    /// Konfiguration stimmt, es fehlt nur der Wert auf diesem Gerät. Die
    /// Quelle bleibt in der Liste, und der HTTP-Client sagt beim Versuch, was
    /// zu tun ist.
    /// </summary>
    [Fact]
    public void Ein_fehlendes_Geheimnis_nimmt_die_Quelle_nicht_aus_der_Liste()
    {
        var speicher = Speicher();

        var mitSchluessel = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://crm.example.ch/api",
                Auth = new AuthDefinition { Type = AuthKind.Bearer, SecretRef = "crm.token" },
            },
        };

        speicher.Save(new IntegrationConfig { DataSources = [mitSchluessel] });

        Assert.Single(speicher.UsableSources);
        Assert.Contains(speicher.Issues, i => i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void Die_Quellen_kommen_in_der_Reihenfolge_ihrer_Prioritaet()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig
        {
            DataSources =
            [
                Quelle("erp") with { Priority = 50 },
                Quelle("crm") with { Priority = 10 },
            ],
        });

        Assert.Equal(["crm", "erp"], speicher.UsableSources.Select(static s => s.Id));
    }

    // --- Prüfung ---

    private IReadOnlyList<ValidationIssue> Pruefe(DataSourceDefinition quelle) =>
        new IntegrationConfigValidator(Geheimnisse()).ValidateSource(quelle);

    [Fact]
    public void Eine_richtig_eingerichtete_Quelle_hat_keine_Befunde() =>
        Assert.Empty(Pruefe(Quelle()));

    [Theory]
    [InlineData("Grosses")]
    [InlineData("mit leerzeichen")]
    [InlineData("1zahl")]
    [InlineData("")]
    public void Eine_unbrauchbare_Kennung_wird_bemaengelt(string id)
    {
        var befunde = Pruefe(Quelle() with { Id = id });

        Assert.Contains(befunde, b => b.Path.EndsWith(".id", StringComparison.Ordinal));
    }

    /// <summary>
    /// Eine Quelle namens <c>number</c> würde auf einer Karte die Rufnummer
    /// verdecken — dort steht <c>number.e164</c> schon.
    /// </summary>
    [Theory]
    [InlineData("number")]
    [InlineData("query")]
    [InlineData("status")]
    [InlineData("contacts")]
    public void Eine_belegte_Kennung_wird_abgelehnt(string id)
    {
        var befunde = Pruefe(Quelle() with { Id = id });

        Assert.Contains(befunde, b => b.Message.Contains("vergeben", StringComparison.Ordinal));
    }

    [Fact]
    public void Unverschluesseltes_http_braucht_eine_ausdrueckliche_Erlaubnis()
    {
        var ohne = Quelle() with
        {
            Http = new HttpConnection { BaseUrl = "http://crm.example.ch/api" },
        };

        Assert.Contains(
            Pruefe(ohne),
            b => b.Severity == IssueSeverity.Error
                && b.Message.Contains("unverschlüsselt", StringComparison.Ordinal));

        var mit = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "http://crm.example.ch/api",
                AllowInsecureHttp = true,
            },
        };

        Assert.DoesNotContain(Pruefe(mit), b => b.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Zugangsdaten_in_der_Adresse_werden_abgelehnt()
    {
        var quelle = Quelle() with
        {
            Http = new HttpConnection { BaseUrl = "https://nipp:pw@crm.example.ch/api" },
        };

        Assert.Contains(
            Pruefe(quelle),
            b => b.Message.Contains("Zugangsdaten", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60_000)]
    public void Eine_unsinnige_Zeitgrenze_wird_bemaengelt(int millisekunden)
    {
        var quelle = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://crm.example.ch/api",
                TimeoutMs = millisekunden,
            },
        };

        Assert.Contains(Pruefe(quelle), b => b.Path.EndsWith(".timeoutMs", StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_kaputtes_Mapping_wird_beim_Pruefen_gefunden()
    {
        var quelle = Quelle();
        quelle.LookupByPhone!.Mapping["kaputt"] = new FieldMapping(Path: "$.[[[");

        Assert.Contains(
            Pruefe(quelle),
            b => b.Path.EndsWith(".mapping", StringComparison.Ordinal)
                && b.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Eine_kaputte_Vorlage_in_der_Anfrage_wird_gefunden()
    {
        var quelle = Quelle() with
        {
            LookupByPhone = new LookupByPhoneCapability
            {
                Request = new RequestDefinition { Path = "/contacts/{{number.e164" },
            },
        };

        Assert.Contains(
            Pruefe(quelle),
            b => b.Path.EndsWith(".request", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ohne diese Prüfung liesse sich über eine verteilte Konfigurationsdatei
    /// ein beliebiger Link unter der Beschriftung „Kontakt im CRM öffnen"
    /// unterbringen (§21.2).
    /// </summary>
    [Fact]
    public void Eine_Oeffnen_Adresse_muss_mit_einem_festen_Rechnernamen_beginnen()
    {
        var quelle = Quelle() with
        {
            OpenContact = new OpenContactCapability { UrlTemplate = "{{crm.url}}/kontakt" },
        };

        Assert.Contains(
            Pruefe(quelle),
            b => b.Severity == IssueSeverity.Error
                && b.Path.EndsWith(".urlTemplate", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_Oeffnen_Adresse_auf_einen_fremden_Rechner_wird_angemerkt()
    {
        var quelle = Quelle() with
        {
            OpenContact = new OpenContactCapability
            {
                UrlTemplate = "https://ganz-woanders.example/{{externalId}}",
            },
        };

        Assert.Contains(
            Pruefe(quelle),
            b => b.Severity == IssueSeverity.Warning
                && b.Message.Contains("ganz-woanders.example", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_Quelle_ohne_Faehigkeit_wird_angemerkt()
    {
        var quelle = Quelle() with { LookupByPhone = null };

        Assert.Contains(
            Pruefe(quelle),
            b => b.Severity == IssueSeverity.Warning
                && b.Message.Contains("nie gefragt", StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_Api_Schluessel_im_Parameter_wird_angemerkt()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm.apiKey", "x");

        var quelle = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://crm.example.ch/api",
                Auth = new AuthDefinition
                {
                    Type = AuthKind.ApiKey,
                    In = ApiKeyLocation.Query,
                    Name = "key",
                    SecretRef = "crm.apiKey",
                },
            },
        };

        var befunde = new IntegrationConfigValidator(geheimnisse).ValidateSource(quelle);

        Assert.Contains(
            befunde,
            b => b.Severity == IssueSeverity.Warning
                && b.Message.Contains("Protokollen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Wer den Schlüssel ins Schema tippt, bekommt sonst erst bei der ersten
    /// Anfrage eine Ausnahme aus <c>AuthenticationHeaderValue</c> — weit weg
    /// von der Ursache.
    /// </summary>
    [Theory]
    [InlineData("Token abc123")]
    [InlineData("Bearer:")]
    public void Ein_Schema_mit_unerlaubten_Zeichen_wird_bemaengelt(string schema)
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm", "abc");

        var quelle = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://crm.example.ch/api",
                Auth = new AuthDefinition
                {
                    Type = AuthKind.Bearer,
                    SecretRef = "crm",
                    Scheme = schema,
                },
            },
        };

        var befunde = new IntegrationConfigValidator(geheimnisse).ValidateSource(quelle);

        Assert.Contains(
            befunde,
            b => b.Severity == IssueSeverity.Error
                && b.Path.EndsWith("scheme", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ein <c>scheme</c> bei einem API-Schlüssel tut nichts — dort bestimmt
    /// <c>name</c> die Kopfzeile. Eine Angabe, die stillschweigend ignoriert
    /// wird, kostet den nächsten Betreiber eine Stunde.
    /// </summary>
    [Fact]
    public void Ein_Schema_bei_einer_anderen_Anmeldeart_wird_bemaengelt()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm.apiKey", "abc");

        var quelle = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://crm.example.ch/api",
                Auth = new AuthDefinition
                {
                    Type = AuthKind.ApiKey,
                    Name = "X-Api-Key",
                    SecretRef = "crm.apiKey",
                    Scheme = "Token",
                },
            },
        };

        var befunde = new IntegrationConfigValidator(geheimnisse).ValidateSource(quelle);

        Assert.Contains(
            befunde,
            b => b.Severity == IssueSeverity.Warning
                && b.Path.EndsWith("scheme", StringComparison.Ordinal));
    }

    [Fact]
    public void Das_Schema_Token_ist_erlaubt()
    {
        var geheimnisse = Geheimnisse();
        geheimnisse.Set("crm", "abc");

        var quelle = Quelle() with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://crm.example.ch/api",
                Auth = new AuthDefinition
                {
                    Type = AuthKind.Bearer,
                    SecretRef = "crm",
                    Scheme = "Token",
                },
            },
        };

        var befunde = new IntegrationConfigValidator(geheimnisse).ValidateSource(quelle);

        Assert.DoesNotContain(befunde, b => b.Path.EndsWith("scheme", StringComparison.Ordinal));
    }

    [Fact]
    public void Zwei_Quellen_mit_derselben_Kennung_werden_bemaengelt()
    {
        var befunde = new IntegrationConfigValidator(Geheimnisse()).Validate(
            new IntegrationConfig { DataSources = [Quelle("crm"), Quelle("crm")] });

        Assert.Contains(befunde, b => b.Message.Contains("mehrfach", StringComparison.Ordinal));
    }

    [Fact]
    public void Alle_Befunde_kommen_auf_einmal()
    {
        var kaputt = Quelle() with
        {
            Id = "Falsch Geschrieben",
            DisplayName = string.Empty,
            Http = new HttpConnection { BaseUrl = "http://crm.example.ch", TimeoutMs = 1 },
        };

        var befunde = Pruefe(kaputt);

        Assert.True(befunde.Count >= 4, $"Erwartet wurden mehrere Befunde, gefunden: {befunde.Count}");
    }

    // --- Einlesen ---

    [Fact]
    public void Eine_Datei_laesst_sich_pruefen_ohne_sie_zu_uebernehmen()
    {
        var speicher = Speicher();

        var gut = speicher.TryRead(
            """
            { "dataSources": [ { "id": "crm", "displayName": "CRM",
              "http": { "baseUrl": "https://crm.example.ch" } } ] }
            """,
            out var gelesen,
            out var befunde);

        Assert.True(gut, string.Join(" | ", befunde));
        Assert.NotNull(gelesen);

        // Übernommen wurde nichts.
        Assert.Empty(speicher.Current.DataSources);
    }

    [Fact]
    public void Kaputtes_Json_beim_Einlesen_wird_erklaert()
    {
        Assert.False(Speicher().TryRead("{ kaputt", out _, out var befunde));
        Assert.Contains(befunde, b => b.Message.Contains("JSON", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
