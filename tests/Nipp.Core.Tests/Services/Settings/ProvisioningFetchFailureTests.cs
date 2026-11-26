using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// Was nipp sagt, wenn der Abruf des Profils scheitert (L9, <c>LOGBEFUNDE-PLAN.md</c>).
///
/// Ein Arbeitsplatz hatte als Provisionierungsadresse die einer Kontaktsuche
/// eingetragen und las bei jedem Start nur "404 (Not Found)". Bei 404 und 410
/// sagt nipp jetzt, dass dort vermutlich gar kein Profil liegt. Bei Netz- und
/// Serverfehlern kann ein Profil existieren — dort bleibt die alte Meldung.
///
/// Kein Netz: der Abruf laeuft ueber einen Handler, der Antworten vorgibt.
/// </summary>
public sealed class ProvisioningFetchFailureTests : IDisposable
{
    private const string ValidProfile = """
        <nipp-provisioning version="1">
          <profile name="Test" />
        </nipp-provisioning>
        """;

    private readonly string _directory;
    private readonly SettingsService _settings;
    private readonly SecretStore _secrets;
    private readonly ListLogger _logger = new();

    public ProvisioningFetchFailureTests()
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

        _settings.Save(_settings.Current with
        {
            Advanced = _settings.Current.Advanced with { ProvisioningUri = "https://pbx.example.ch/kontakte/suche" },
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.Gone, 410)]
    public async Task Bei_404_und_410_sagt_nipp_dass_dort_vermutlich_kein_Profil_liegt(
        HttpStatusCode status, int code)
    {
        using var provisioning = Create(new StubHandler(_ => new HttpResponseMessage(status)));

        var profile = await provisioning.FetchRemoteProfileAsync();

        Assert.Null(profile);
        Assert.Contains("vermutlich gar kein Provisionierungsprofil", provisioning.LastError);
        Assert.Contains("Adresse in den Einstellungen", provisioning.LastError);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), provisioning.LastError);

        var line = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal(3521, line.EventId);
        Assert.Contains("vermutlich gar kein Provisionierungsprofil", line.Message);
        Assert.DoesNotContain("kontakte/suche", line.Message);
    }

    [Fact]
    public async Task Bei_einem_Serverfehler_bleibt_es_bei_der_alten_Meldung()
    {
        using var provisioning = Create(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var profile = await provisioning.FetchRemoteProfileAsync();

        Assert.Null(profile);
        Assert.Contains("nicht erreichbar", provisioning.LastError);
        Assert.DoesNotContain("vermutlich gar kein", provisioning.LastError);

        var line = Assert.Single(_logger.Entries);
        Assert.Equal(3512, line.EventId);
        Assert.Contains("nicht abrufbar", line.Message);
    }

    [Fact]
    public async Task Bei_einem_Netzwerkfehler_bleibt_es_bei_der_alten_Meldung()
    {
        using var provisioning = Create(
            new StubHandler(_ => throw new HttpRequestException("Der Host ist unbekannt.")));

        var profile = await provisioning.FetchRemoteProfileAsync();

        Assert.Null(profile);
        Assert.Contains("nicht erreichbar", provisioning.LastError);
        Assert.DoesNotContain("vermutlich gar kein", provisioning.LastError);

        var line = Assert.Single(_logger.Entries);
        Assert.Equal(3512, line.EventId);
    }

    [Fact]
    public async Task Ein_Erfolg_bleibt_unveraendert()
    {
        using var provisioning = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidProfile),
        }));

        var profile = await provisioning.FetchRemoteProfileAsync();

        Assert.NotNull(profile);
        Assert.Null(provisioning.LastError);

        var line = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal(3510, line.EventId);
    }

    private ProvisioningService Create(HttpMessageHandler handler) =>
        new(_settings, new PolicyService(), _secrets, _logger, handler);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed record Entry(LogLevel Level, int EventId, string Message);

    private sealed class ListLogger : ILogger<ProvisioningService>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, eventId.Id, formatter(state, exception)));
    }
}
