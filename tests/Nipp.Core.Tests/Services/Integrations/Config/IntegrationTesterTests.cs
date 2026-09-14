using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Config;

/// <summary>
/// Der Testabruf beim Einrichten einer Quelle (§21.4).
///
/// <b>Er beantwortet die eine Frage, die ein Administrator hier stellt:</b>
/// kommt an, was ich erwartet habe? Dafür braucht er beides — die Antwort im
/// Rohzustand, um einen Pfad schreiben zu können, und die gemappten Felder,
/// um zu sehen, ob der Pfad stimmt.
///
/// Das unterscheidet ihn vom Betrieb: dort verlässt nur das Mapping die
/// Schicht (§21.1), die Rohantwort nie.
/// </summary>
public sealed class IntegrationTesterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class Kanal(string antwort, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Letzte { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Letzte = request;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(antwort, Encoding.UTF8, "application/json"),
            });
        }
    }

    private IntegrationHttpClient Client(HttpMessageHandler kanal) =>
        new(
            new IntegrationSecrets(new SecretStore(
                NullLogger<SecretStore>.Instance,
                Path.Combine(_directory, "secrets.dat"))),
            NullLogger<IntegrationHttpClient>.Instance,
            kanal);

    private static DataSourceDefinition Quelle() => new()
    {
        Id = "crm",
        DisplayName = "Muster-CRM",
        Http = new HttpConnection { BaseUrl = "https://crm.example.ch/api" },
        LookupByPhone = new LookupByPhoneCapability
        {
            Request = new RequestDefinition
            {
                Path = "/contacts",
                Query = new Dictionary<string, string> { ["phone"] = "{{number.e164}}" },
            },
            Mapping =
            {
                ["customerName"] = new FieldMapping(Path: "$.contact.fullName"),
                ["company"] = new FieldMapping(Path: "$.contact.company.name"),
            },
        },
    };

    private const string Antwort = """
        { "contact": { "fullName": "Hans Muster", "company": { "name": "Muster AG" } } }
        """;

    [Fact]
    public async Task Ein_Testabruf_zeigt_Antwort_und_gemappte_Felder()
    {
        using var client = Client(new Kanal(Antwort));
        var tester = new IntegrationTester(client);

        var ergebnis = await tester.TestLookupAsync(Quelle(), "0791234567", "+41");

        Assert.True(ergebnis.IsSuccess);
        Assert.Equal(200, ergebnis.StatusCode);

        // Die Rohantwort — beim Einrichten die Information, aus der ein Pfad
        // entsteht.
        Assert.Contains("fullName", ergebnis.ResponsePreview!, StringComparison.Ordinal);

        // Und was das Mapping daraus gemacht hat.
        Assert.Equal("Hans Muster", ergebnis.Fields["customerName"]);
        Assert.Equal("Muster AG", ergebnis.Fields["company"]);
    }

    [Fact]
    public async Task Die_Testnummer_wird_normalisiert_und_kodiert()
    {
        var kanal = new Kanal(Antwort);
        using var client = Client(kanal);

        await new IntegrationTester(client).TestLookupAsync(Quelle(), "079 123 45 67", "+41");

        Assert.Contains(
            "phone=%2B41791234567",
            kanal.Letzte!.RequestUri!.Query,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein falscher Pfad ist der häufigste Fehler beim Einrichten — und er
    /// soll hier sichtbar werden, nicht beim ersten Anruf.
    /// </summary>
    [Fact]
    public async Task Ein_falscher_Pfad_zeigt_sich_als_leeres_Feld()
    {
        var quelle = Quelle();
        quelle.LookupByPhone!.Mapping["customerName"] = new FieldMapping(Path: "$.falsch.pfad");

        using var client = Client(new Kanal(Antwort));

        var ergebnis = await new IntegrationTester(client).TestLookupAsync(quelle, "0791234567", "+41");

        Assert.True(ergebnis.IsSuccess);
        Assert.Empty(ergebnis.Fields["customerName"]);

        // Und die Rohantwort steht daneben, damit sich der richtige Pfad
        // ablesen lässt.
        Assert.Contains("fullName", ergebnis.ResponsePreview!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Fehlerstatus_wird_erklaert()
    {
        using var client = Client(new Kanal("{}", HttpStatusCode.Unauthorized));

        var ergebnis = await new IntegrationTester(client).TestLookupAsync(Quelle(), "0791234567", "+41");

        Assert.False(ergebnis.IsSuccess);
        Assert.Contains("Anmeldung", ergebnis.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_eingerichtete_Faehigkeit_wird_gar_nicht_abgerufen()
    {
        using var client = Client(new Kanal(Antwort));

        var ohne = Quelle() with { LookupByPhone = null };

        var ergebnis = await new IntegrationTester(client).TestLookupAsync(ohne, "0791234567", "+41");

        Assert.Equal(HttpOutcome.Skipped, ergebnis.Outcome);
        Assert.Contains("kein Anruferkontext", ergebnis.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eine sehr grosse Antwort ist zum Ansehen da, nicht zum Weiterverarbeiten
    /// — in einem 400 Pixel breiten Fenster hilft ein halbes Megabyte niemandem.
    /// </summary>
    [Fact]
    public async Task Eine_grosse_Antwort_wird_fuer_die_Anzeige_gekuerzt()
    {
        var gross = "{\"contact\":{\"fullName\":\"" + new string('a', 20_000) + "\"}}";

        using var client = Client(new Kanal(gross));

        var ergebnis = await new IntegrationTester(client).TestLookupAsync(Quelle(), "0791234567", "+41");

        Assert.True(ergebnis.ResponsePreview!.Length <= IntegrationTester.MaxPreviewChars + 32);
        Assert.EndsWith("(gekürzt)", ergebnis.ResponsePreview, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Und dieselbe Antwort kommt ungekürzt mit</b> (13.09.2026).
    ///
    /// <para>Der Befund: es gab nur die gekürzte Fassung, und zwei Stellen
    /// verarbeiteten sie weiter — der Karten-Designer und die
    /// Einstellungsseite. Über der Kürzungsgrenze ist das kein gültiges JSON
    /// mehr; der Probenspeicher lehnte es still ab, die Vorschau zeigte weiter
    /// erfundene Beispieldaten, und die Statuszeile meldete trotzdem
    /// Erfolg.</para>
    ///
    /// <para>Dieser Test ist die Gegenprobe zu dem darüber: <b>beide Fassungen
    /// entstehen aus derselben Antwort, und nur eine davon ist beschnitten.</b></para>
    /// </summary>
    [Fact]
    public async Task Dieselbe_grosse_Antwort_steht_ungekuerzt_zum_Verarbeiten_bereit()
    {
        var name = new string('a', 20_000);
        var gross = "{\"contact\":{\"fullName\":\"" + name + "\"}}";

        using var client = Client(new Kanal(gross));

        var ergebnis = await new IntegrationTester(client).TestLookupAsync(Quelle(), "0791234567", "+41");

        Assert.NotNull(ergebnis.ResponseBody);
        Assert.True(
            ergebnis.ResponseBody!.Length > IntegrationTester.MaxPreviewChars,
            "Der Rumpf ist gekürzt — dann ist er als JSON unbrauchbar, und genau das war der Befund.");
        Assert.DoesNotContain("(gekürzt)", ergebnis.ResponseBody, StringComparison.Ordinal);
        Assert.Contains(name, ergebnis.ResponseBody, StringComparison.Ordinal);

        // Die Zusage, auf der alles ruht: er lässt sich lesen.
        var geparst = System.Text.Json.Nodes.JsonNode.Parse(ergebnis.ResponseBody);

        Assert.Equal(name, geparst!["contact"]!["fullName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bei_einer_Trefferliste_wird_der_erste_Treffer_gezeigt()
    {
        var quelle = Quelle() with
        {
            LookupByPhone = null,
            SearchContacts = new SearchContactsCapability
            {
                Request = new RequestDefinition
                {
                    Path = "/contacts",
                    Query = new Dictionary<string, string> { ["q"] = "{{query.text}}" },
                },
                ItemsPath = "$.items[*]",
                Mapping = { ["displayName"] = new FieldMapping(Path: "$.name") },
            },
        };

        using var client = Client(new Kanal("""{ "items": [ {"name":"Erster"}, {"name":"Zweiter"} ] }"""));

        var ergebnis = await new IntegrationTester(client).TestSearchAsync(quelle, "Muster");

        Assert.Equal("Erster", ergebnis.Fields["displayName"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
