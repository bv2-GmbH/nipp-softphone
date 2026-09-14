using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Http;

/// <summary>
/// Zwei Wege, auf denen ein Zugangsschlüssel das Haus verlassen konnte.
///
/// Beides sind keine Denkfehler, sondern Lücken zwischen Schichten: die
/// https-Pflicht stand nur im Validator, und der Testknopf in den Einstellungen
/// geht an ihm vorbei — er nimmt jede eingetragene Quelle, auch eine
/// abgelehnte. Und beim Folgen einer Weiterleitung entfernt .NET nur die
/// <c>Authorization</c>-Kopfzeile; einen API-Key in einer eigenen Kopfzeile
/// nimmt es mit, auch auf einen fremden Rechner.
/// </summary>
public sealed class IntegrationHttpSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class Kanal(Func<HttpRequestMessage, HttpResponseMessage> antwort) : HttpMessageHandler
    {
        public int Aufrufe { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Aufrufe++;
            return Task.FromResult(antwort(request));
        }
    }

    private IntegrationHttpClient Client(Kanal kanal) =>
        new(
            new IntegrationSecrets(new SecretStore(
                NullLogger<SecretStore>.Instance,
                Path.Combine(_directory, "secrets.dat"))),
            NullLogger<IntegrationHttpClient>.Instance,
            kanal);

    private static DataSourceDefinition Quelle(string baseUrl, bool unverschluesseltErlaubt = false) => new()
    {
        Id = "crm",
        DisplayName = "Muster-CRM",
        Http = new HttpConnection
        {
            BaseUrl = baseUrl,
            AllowInsecureHttp = unverschluesseltErlaubt,
        },
    };

    private static HttpRequestTemplate Anfrage() =>
        HttpRequestTemplate.Compile(new RequestDefinition { Method = "GET", Path = "/contacts" });

    private sealed class LeererBereich : IExpressionScope
    {
        public ContextValue Resolve(string path) => ContextValue.Null;

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;
    }

    private static Kanal Antwortet(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });

    [Fact]
    public async Task Ueber_http_wird_gar_nicht_erst_gesendet()
    {
        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal);

        var ergebnis = await client.SendAsync(
            Quelle("http://crm.example.ch/api"),
            Anfrage(),
            new LeererBereich());

        Assert.Equal(HttpOutcome.Skipped, ergebnis.Outcome);

        // Das Entscheidende: es ging nichts hinaus. Ein Token in einer
        // Kopfzeile wäre sonst im Klartext unterwegs gewesen.
        Assert.Equal(0, kanal.Aufrufe);
        Assert.Contains("https", ergebnis.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mit_ausdruecklicher_Erlaubnis_geht_http_doch()
    {
        // Die Ausnahme muss bleiben: es gibt Anlagen in abgeschotteten Netzen,
        // und ADR-012 hat denselben Weg für die Provisionierung freigegeben.
        // Sie soll nur ausdrücklich sein, nicht stillschweigend.
        var kanal = Antwortet(HttpStatusCode.OK);
        using var client = Client(kanal);

        var ergebnis = await client.SendAsync(
            Quelle("http://crm.example.ch/api", unverschluesseltErlaubt: true),
            Anfrage(),
            new LeererBereich());

        Assert.NotEqual(HttpOutcome.Skipped, ergebnis.Outcome);
        Assert.Equal(1, kanal.Aufrufe);
    }

    [Fact]
    public async Task Einer_Weiterleitung_wird_nicht_gefolgt()
    {
        var kanal = new Kanal(_ =>
        {
            var antwort = new HttpResponseMessage(HttpStatusCode.Found);
            antwort.Headers.Location = new Uri("https://woanders.example.com/api");
            return antwort;
        });

        using var client = Client(kanal);

        var ergebnis = await client.SendAsync(
            Quelle("https://crm.example.ch/api"),
            Anfrage(),
            new LeererBereich());

        // Ein Fehler mit Begründung, nicht ein leerer Rumpf: sonst käme die
        // 302-Antwort als „keine Daten gefunden" beim Mapping an, und die
        // eigentliche Ursache stünde nirgends.
        Assert.Equal(HttpOutcome.Error, ergebnis.Outcome);
        Assert.Contains("leitet um", ergebnis.Message!, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
