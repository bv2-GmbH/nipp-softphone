using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Search;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Search;

/// <summary>
/// Die Suche in einer HTTP-Quelle (§21.1).
///
/// <b>Der Kern ist die Übersetzung</b>: aus einer fremden Trefferliste werden
/// Kontakte, die genauso aussehen wie die aus Outlook. Die Kontaktliste soll
/// nicht wissen, woher ein Kontakt stammt — das ist die Zusage aus §21.1, und
/// hier wird sie eingelöst.
/// </summary>
public sealed class HttpContactSearchProviderTests : IDisposable
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

    private IntegrationHttpClient Client(Kanal kanal) =>
        new(
            new IntegrationSecrets(new SecretStore(
                NullLogger<SecretStore>.Instance,
                Path.Combine(_directory, "secrets.dat"))),
            NullLogger<IntegrationHttpClient>.Instance,
            kanal);

    /// <summary>Eine CRM-Quelle, wie sie in der Beispielkonfiguration steht.</summary>
    private static DataSourceDefinition Quelle(OpenContactCapability? oeffnen = null) => new()
    {
        Id = "crm",
        DisplayName = "Muster-CRM",
        Http = new HttpConnection { BaseUrl = "https://crm.example.ch/api/v2" },
        SearchContacts = new SearchContactsCapability
        {
            Request = new RequestDefinition
            {
                Method = "GET",
                Path = "/contacts",
                Query = new Dictionary<string, string>
                {
                    ["q"] = "{{query.text}}",
                    ["limit"] = "{{query.limit}}",
                },
            },
            ItemsPath = "$.items[*]",
            Mapping =
            {
                ["externalId"] = new FieldMapping(Path: "$.id"),
                ["displayName"] = new FieldMapping(Expr: "concat($.firstName, ' ', $.lastName)"),
                ["company"] = new FieldMapping(Path: "$.company.name"),
                ["email"] = new FieldMapping(Path: "$.email"),
                ["phoneBusiness"] = new FieldMapping(Path: "$.phoneBusiness"),
                ["phoneMobile"] = new FieldMapping(Path: "$.phoneMobile"),
            },
        },
        OpenContact = oeffnen,
    };

    private const string ZweiTreffer = """
        {
          "items": [
            {
              "id": "4711",
              "firstName": "Hans",
              "lastName": "Muster",
              "company": { "name": "Muster AG" },
              "email": "hans@muster.ch",
              "phoneBusiness": "044 512 84 30",
              "phoneMobile": "+41791234567"
            },
            {
              "id": "4712",
              "firstName": "Anna",
              "lastName": "Beispiel",
              "company": { "name": "Beispiel GmbH" },
              "phoneBusiness": "0442223344"
            }
          ]
        }
        """;

    private async Task<ContactSearchPage> SucheAsync(
        string antwort,
        DataSourceDefinition? quelle = null,
        int limit = 25)
    {
        var kanal = new Kanal(antwort);
        using var client = Client(kanal);

        var provider = HttpContactSearchProvider.TryCreate(quelle ?? Quelle(), client);
        Assert.NotNull(provider);

        return await provider.SearchAsync(new ContactQuery("Muster", limit), CancellationToken.None);
    }

    // --- Der Normalfall ---

    [Fact]
    public async Task Eine_Trefferliste_wird_zu_Kontakten()
    {
        var seite = await SucheAsync(ZweiTreffer);

        Assert.Equal(SearchState.Success, seite.State);
        Assert.Equal(2, seite.Contacts.Count);

        var hans = seite.Contacts[0];

        Assert.Equal("Hans Muster", hans.DisplayName);
        Assert.Equal("Muster AG", hans.Company);
        Assert.Equal("hans@muster.ch", hans.Email);
        Assert.Equal("4711", hans.ExternalId);
        Assert.Equal("crm", hans.SourceId);
        Assert.Equal(ContactSourceKind.External, hans.Source);
    }

    /// <summary>
    /// Die Nummern kommen über eine Namenskonvention: alles, was mit
    /// <c>phone</c> beginnt, wird zu einer Nummer, und der Rest des Namens
    /// bestimmt ihre Art.
    /// </summary>
    [Fact]
    public async Task Felder_die_mit_phone_beginnen_werden_zu_Nummern()
    {
        var seite = await SucheAsync(ZweiTreffer);
        var hans = seite.Contacts[0];

        Assert.Equal(2, hans.Numbers.Count);

        Assert.Contains(hans.Numbers, n =>
            n.Kind == ContactNumberKind.Business && n.Number == "044 512 84 30");

        Assert.Contains(hans.Numbers, n =>
            n.Kind == ContactNumberKind.Mobile && n.Number == "+41791234567");
    }

    [Fact]
    public async Task Der_Suchtext_geht_kodiert_an_die_Quelle()
    {
        var kanal = new Kanal(ZweiTreffer);
        using var client = Client(kanal);

        var provider = HttpContactSearchProvider.TryCreate(Quelle(), client)!;

        await provider.SearchAsync(new ContactQuery("Muster & Co", 25), CancellationToken.None);

        var query = kanal.Letzte!.RequestUri!.Query;

        Assert.Contains("q=Muster%20%26%20Co", query, StringComparison.Ordinal);
        Assert.Contains("limit=25", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Trefferzahl_wird_begrenzt()
    {
        var seite = await SucheAsync(ZweiTreffer, limit: 1);

        Assert.Single(seite.Contacts);
    }

    // --- Was verworfen wird ---

    /// <summary>
    /// Dieselbe Regel wie bei Outlook: ein Eintrag ohne wählbare Nummer ist
    /// für ein Telefon wertlos und füllt nur die Liste.
    /// </summary>
    [Fact]
    public async Task Ein_Treffer_ohne_Nummer_wird_uebergangen()
    {
        var seite = await SucheAsync("""
            { "items": [ { "id": "1", "firstName": "Ohne", "lastName": "Nummer" } ] }
            """);

        Assert.Empty(seite.Contacts);
        Assert.Equal(SearchState.Empty, seite.State);
    }

    [Fact]
    public async Task Ein_Treffer_ohne_Namen_wird_uebergangen()
    {
        var seite = await SucheAsync("""
            { "items": [ { "id": "1", "phoneBusiness": "0445128430" } ] }
            """);

        Assert.Empty(seite.Contacts);
    }

    /// <summary>
    /// Ein kaputter Eintrag kostet nicht die ganze Liste — fremde Antworten
    /// sind gemischt, und der brauchbare Teil soll ankommen.
    /// </summary>
    [Fact]
    public async Task Ein_kaputter_Eintrag_kostet_nicht_die_Liste()
    {
        var seite = await SucheAsync("""
            {
              "items": [
                { "id": "1", "firstName": "Ohne", "lastName": "Nummer" },
                { "id": "2", "firstName": "Hans", "lastName": "Muster", "phoneBusiness": "0445128430" }
              ]
            }
            """);

        var kontakt = Assert.Single(seite.Contacts);
        Assert.Equal("Hans Muster", kontakt.DisplayName);
    }

    [Fact]
    public async Task Eine_leere_Trefferliste_ist_kein_Fehler()
    {
        var seite = await SucheAsync("""{ "items": [] }""");

        Assert.Equal(SearchState.Empty, seite.State);
        Assert.Null(seite.Message);
    }

    // --- Fehlerfälle ---

    [Fact]
    public async Task Ein_Fehlerstatus_wird_zum_Zustand_mit_Meldung()
    {
        var kanal = new Kanal("{}", HttpStatusCode.Unauthorized);
        using var client = Client(kanal);

        var provider = HttpContactSearchProvider.TryCreate(Quelle(), client)!;
        var seite = await provider.SearchAsync(new ContactQuery("Muster", 25), CancellationToken.None);

        Assert.Equal(SearchState.Error, seite.State);
        Assert.Contains("Muster-CRM", seite.Message!, StringComparison.Ordinal);
        Assert.Empty(seite.Contacts);
    }

    // --- Kontakt im Fremdsystem öffnen ---

    [Fact]
    public async Task Die_Oeffnen_Adresse_wird_aus_der_Vorlage_gebildet()
    {
        var quelle = Quelle(new OpenContactCapability
        {
            UrlTemplate = "https://crm.example.ch/contacts/{{externalId}}",
        });

        var seite = await SucheAsync(ZweiTreffer, quelle);

        Assert.Equal(
            new Uri("https://crm.example.ch/contacts/4711"),
            seite.Contacts[0].OpenUri);
    }

    /// <summary>
    /// Die zweite Verteidigungslinie (§21.2): der Validator prüft den festen
    /// Anfang der Vorlage, hier wird das Ergebnis nach dem Einsetzen geprüft.
    /// Alles ausser http und https landete sonst in <c>ShellExecute</c>.
    /// </summary>
    [Fact]
    public async Task Eine_Oeffnen_Adresse_mit_fremdem_Schema_wird_verworfen()
    {
        var quelle = Quelle(new OpenContactCapability
        {
            UrlTemplate = "file:///C:/Windows/{{externalId}}",
        });

        var seite = await SucheAsync(ZweiTreffer, quelle);

        Assert.Null(seite.Contacts[0].OpenUri);
    }

    [Fact]
    public async Task Ohne_Oeffnen_Faehigkeit_gibt_es_keine_Adresse()
    {
        var seite = await SucheAsync(ZweiTreffer);

        Assert.Null(seite.Contacts[0].OpenUri);
    }

    // --- Was gar nicht erst entsteht ---

    [Fact]
    public void Ohne_Such_Faehigkeit_entsteht_kein_Anbieter()
    {
        using var client = Client(new Kanal("{}"));

        var ohne = Quelle() with { SearchContacts = null };

        Assert.Null(HttpContactSearchProvider.TryCreate(ohne, client));
    }

    [Fact]
    public void Eine_kaputte_Anfragevorlage_ergibt_keinen_Anbieter()
    {
        using var client = Client(new Kanal("{}"));

        var kaputt = Quelle() with
        {
            SearchContacts = new SearchContactsCapability
            {
                Request = new RequestDefinition { Path = "/contacts/{{query.text" },
            },
        };

        Assert.Null(HttpContactSearchProvider.TryCreate(kaputt, client));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
