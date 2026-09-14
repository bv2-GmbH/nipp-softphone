using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Integrations.Config;

/// <summary>
/// Holt die Integrationskonfiguration von der Adresse, die im
/// Provisionierungsprofil steht (§21.3, §17).
///
/// <b>Nach dem Start, nicht im Startpfad.</b> Der Abruf des Profils selbst
/// liegt im Start, weil daraus Konten kommen können (§17); dieser hier nicht.
/// Nichts an den Integrationen darf zwischen dem Start und dem ersten
/// möglichen Anruf stehen (§21.2) — im schlimmsten Fall stehen die
/// Integrationen eine Sekunde später bereit, und das merkt niemand.
///
/// <b>Nur https</b>, ohne Ausnahme. Bei der Provisionierung selbst gibt es
/// einen Schalter für Anlagen ohne TLS (ADR-012); hier nicht. Diese Datei
/// bestimmt, welche fremden Adressen nipp mit Rufnummern beliefert — wer sie
/// unterwegs austauschen kann, leitet die Kundendaten eines ganzen Betriebs
/// um.
/// </summary>
public sealed class IntegrationProvisioning : IDisposable
{
    /// <summary>
    /// Kurz gehalten: es ist ein Hintergrundabruf, und ein nicht erreichbarer
    /// Server soll nicht minutenlang eine Verbindung offen halten.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Grösste angenommene Datei. Eine Integrationskonfiguration mit hundert
    /// Quellen bleibt weit darunter.
    /// </summary>
    private const int MaxBytes = 512 * 1024;

    private readonly IntegrationConfigStore _store;
    private readonly ILogger<IntegrationProvisioning> _logger;
    private readonly HttpClient _http;
    private bool _disposed;

    public IntegrationProvisioning(
        IntegrationConfigStore store,
        ILogger<IntegrationProvisioning> logger,
        HttpMessageHandler? handler = null)
    {
        _store = store;
        _logger = logger;

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout;
        _http.MaxResponseContentBufferSize = MaxBytes;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("nipp/1.0");
    }

    /// <summary>Was beim letzten Versuch schiefging, oder <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Holt die Datei und übernimmt sie. <b>Wirft nicht</b> — ein Fehler
    /// dabei kostet die Integrationen, sonst nichts.
    /// </summary>
    /// <returns>Ob etwas übernommen wurde.</returns>
    public async Task<bool> ApplyAsync(string? uri, CancellationToken cancellationToken = default)
    {
        LastError = null;

        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            LastError = "Die Adresse der Integrationskonfiguration muss mit https:// beginnen.";
            ConnectorLog.ProvisioningRejected(_logger);

            return false;
        }

        try
        {
            var text = await _http.GetStringAsync(parsed, cancellationToken).ConfigureAwait(false);

            if (!_store.TryRead(text, out var config, out var issues))
            {
                LastError = "Die Integrationskonfiguration vom Server ist unbrauchbar: "
                    + string.Join(" | ", issues.Select(static i => i.ToString()));

                ConnectorLog.ProvisioningBroken(_logger, parsed.Host);

                return false;
            }

            _store.Save(config!);
            ConnectorLog.ProvisioningApplied(_logger, parsed.Host, config!.DataSources.Count);

            return true;
        }
        catch (HttpRequestException ex)
        {
            // §17 sinngemäss: ein Fehler beim Abruf darf nichts kosten ausser
            // dem Abruf. Es gilt weiter, was zuletzt gespeichert wurde.
            LastError = $"Die Integrationskonfiguration ist nicht erreichbar ({ex.Message}). "
                + "Es gilt weiter, was zuletzt gespeichert wurde.";

            ConnectorLog.ProvisioningFailed(_logger, parsed.Host, ex.GetType().Name);

            return false;
        }
        catch (TaskCanceledException)
        {
            LastError = $"Der Server {parsed.Host} antwortet nicht "
                + $"(mehr als {Timeout.TotalSeconds:0} Sekunden).";

            ConnectorLog.ProvisioningFailed(_logger, parsed.Host, "Zeitueberschreitung");

            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
