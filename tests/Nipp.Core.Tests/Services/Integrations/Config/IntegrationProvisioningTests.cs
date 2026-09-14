using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Config;

/// <summary>
/// Die Verteilung der Integrationskonfiguration über das
/// Provisionierungsprofil (§21.3, §17).
///
/// <b>Der wichtigste Test ist der, der etwas ablehnt.</b> Diese Datei
/// bestimmt, welche fremden Adressen nipp mit Rufnummern beliefert — wer sie
/// unterwegs austauschen kann, leitet die Kundendaten eines ganzen Betriebs
/// um. Deshalb nur https, ohne die Ausnahme, die es bei der Provisionierung
/// selbst gibt (ADR-012).
/// </summary>
public sealed class IntegrationProvisioningTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class Kanal(string antwort, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Aufrufe { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Aufrufe++;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(antwort, Encoding.UTF8, "application/json"),
            });
        }
    }

    private IntegrationConfigStore Speicher()
    {
        var geheimnisse = new IntegrationSecrets(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

        return new IntegrationConfigStore(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));
    }

    private const string Gueltig = """
        {
          "schemaVersion": 1,
          "dataSources": [
            {
              "id": "crm",
              "displayName": "Muster-CRM",
              "http": { "baseUrl": "https://crm.example.ch/api" }
            }
          ]
        }
        """;

    [Fact]
    public async Task Eine_gueltige_Datei_wird_uebernommen()
    {
        var speicher = Speicher();
        using var provisioning = new IntegrationProvisioning(
            speicher,
            NullLogger<IntegrationProvisioning>.Instance,
            new Kanal(Gueltig));

        Assert.True(await provisioning.ApplyAsync("https://prov.example.ch/integrations.json"));
        Assert.Single(speicher.Current.DataSources);
        Assert.Null(provisioning.LastError);
    }

    /// <summary>
    /// <b>Ohne Ausnahme.</b> Bei der Provisionierung selbst gibt es einen
    /// Schalter für Anlagen ohne TLS; hier nicht.
    /// </summary>
    [Theory]
    [InlineData("http://prov.example.ch/integrations.json")]
    [InlineData("file:///C:/integrations.json")]
    [InlineData("keine-adresse")]
    public async Task Eine_unsichere_Adresse_wird_abgelehnt_ohne_abzurufen(string adresse)
    {
        var kanal = new Kanal(Gueltig);
        var speicher = Speicher();

        using var provisioning = new IntegrationProvisioning(
            speicher,
            NullLogger<IntegrationProvisioning>.Instance,
            kanal);

        Assert.False(await provisioning.ApplyAsync(adresse));
        Assert.Equal(0, kanal.Aufrufe);
        Assert.Contains("https", provisioning.LastError!, StringComparison.Ordinal);
        Assert.Empty(speicher.Current.DataSources);
    }

    [Fact]
    public async Task Ohne_Adresse_passiert_nichts()
    {
        var kanal = new Kanal(Gueltig);

        using var provisioning = new IntegrationProvisioning(
            Speicher(),
            NullLogger<IntegrationProvisioning>.Instance,
            kanal);

        Assert.False(await provisioning.ApplyAsync(null));
        Assert.Equal(0, kanal.Aufrufe);
        Assert.Null(provisioning.LastError);
    }

    /// <summary>
    /// §17 sinngemäss: ein Fehler beim Abruf darf nichts kosten ausser dem
    /// Abruf. Es gilt weiter, was zuletzt gespeichert wurde.
    /// </summary>
    [Fact]
    public async Task Eine_unbrauchbare_Datei_laesst_das_Bisherige_stehen()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig
        {
            DataSources =
            [
                new DataSourceDefinition
                {
                    Id = "bestehend",
                    DisplayName = "Bestehend",
                    Http = new HttpConnection { BaseUrl = "https://alt.example.ch" },
                },
            ],
        });

        using var provisioning = new IntegrationProvisioning(
            speicher,
            NullLogger<IntegrationProvisioning>.Instance,
            new Kanal("{ das ist kein JSON"));

        Assert.False(await provisioning.ApplyAsync("https://prov.example.ch/integrations.json"));
        Assert.NotNull(provisioning.LastError);

        var bestehend = Assert.Single(speicher.Current.DataSources);
        Assert.Equal("bestehend", bestehend.Id);
    }

    [Fact]
    public async Task Ein_nicht_erreichbarer_Server_meldet_und_kostet_nichts()
    {
        var kanal = new WerfenderKanal();
        var speicher = Speicher();

        using var provisioning = new IntegrationProvisioning(
            speicher,
            NullLogger<IntegrationProvisioning>.Instance,
            kanal);

        Assert.False(await provisioning.ApplyAsync("https://prov.example.ch/integrations.json"));
        Assert.Contains("nicht erreichbar", provisioning.LastError!, StringComparison.Ordinal);
    }

    private sealed class WerfenderKanal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("DNS");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
