using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Search;

namespace Nipp.Core.Services.Integrations.Config;

/// <summary>
/// Woher die Suche ihre Quellen bekommt.
///
/// <b>Eine Abstraktion mit einem Grund:</b> die Menge der Quellen ist nicht
/// fest. Sie entsteht aus einer Konfigurationsdatei, die sich zur Laufzeit
/// ändern kann — über die Einstellungen oder über ein Provisionierungsprofil.
/// Eine über den Container eingespritzte Liste stünde beim Start fest und
/// wüsste von einer neu eingerichteten Quelle nichts.
/// </summary>
public interface ISearchProviderRegistry
{
    /// <summary>
    /// Die Anbieter, die gerade gelten — lokale zuerst, dann die externen in
    /// der Reihenfolge ihrer Priorität.
    /// </summary>
    IReadOnlyList<IContactSearchProvider> SearchProviders { get; }
}

/// <summary>
/// Baut aus der Konfiguration die Anbieter (§21.3).
///
/// <b>Neu gebaut wird nur, wenn sich die Konfiguration ändert</b>, nicht bei
/// jeder Suche: einen Anbieter zu bauen heisst, Vorlagen und Mappings zu
/// übersetzen, und das gehört einmal getan und nicht bei jedem Tastendruck.
///
/// <b>Die lokalen Anbieter stehen fest</b> und kommen über den Container —
/// sie hängen an keiner Konfiguration. Sie stehen vorn, weil sie ohne Netz
/// antworten und die Suche mit ihnen beginnt.
/// </summary>
public sealed class IntegrationRegistry
    : ISearchProviderRegistry, ICallerContextProviderRegistry, IDisposable
{
    private readonly IReadOnlyList<IContactSearchProvider> _localSearch;
    private readonly IReadOnlyList<ICallerContextProvider> _localContext;
    private readonly IntegrationConfigStore _config;
    private readonly IntegrationHttpClient _http;
    private readonly ILogger<IntegrationRegistry> _logger;

    private IReadOnlyList<IContactSearchProvider> _searchProviders;
    private IReadOnlyList<ICallerContextProvider> _contextProviders;
    private bool _disposed;

    public IntegrationRegistry(
        IEnumerable<IContactSearchProvider> localSearchProviders,
        IEnumerable<ICallerContextProvider> localContextProviders,
        IntegrationConfigStore config,
        IntegrationHttpClient http,
        ILogger<IntegrationRegistry> logger)
    {
        _localSearch = [.. localSearchProviders];
        _localContext = [.. localContextProviders];
        _config = config;
        _http = http;
        _logger = logger;

        _searchProviders = _localSearch;
        _contextProviders = _localContext;

        _config.Changed += OnConfigChanged;

        Rebuild();
    }

    public IReadOnlyList<IContactSearchProvider> SearchProviders => _searchProviders;

    public IReadOnlyList<ICallerContextProvider> ContextProviders => _contextProviders;

    private void OnConfigChanged(object? sender, IntegrationConfig config) => Rebuild();

    private void Rebuild()
    {
        // Der Schutzschalter gilt für die alte Konfiguration. Wer gerade eine
        // falsche Adresse korrigiert hat, wartet sonst bis zu 60 Sekunden auf
        // eine Quelle, die längst wieder erreichbar wäre — und sieht nur
        // „wird gerade nicht gefragt", ohne Zusammenhang zu seiner Änderung.
        _http.ResetHealth();

        var search = new List<IContactSearchProvider>(_localSearch);

        // Die lokalen Kontakte stehen vorn: sie antworten ohne Netz, und die
        // Karte zeigt damit sofort einen Namen.
        var context = new List<ICallerContextProvider>(_localContext);

        var allowInternal = _config.Current.CallerLookup.LookupInternalNumbers;

        // UsableSources liefert bereits nur, was eingeschaltet und fehlerfrei
        // ist, und zwar nach Priorität sortiert.
        foreach (var source in _config.UsableSources)
        {
            if (source.SearchContacts is not null)
            {
                var provider = HttpContactSearchProvider.TryCreate(source, _http);

                if (provider is null)
                {
                    // Der Validator hat den Grund längst gemeldet; hier zählt
                    // nur, dass eine unbrauchbare Quelle die anderen nicht
                    // mitnimmt.
                    ConnectorLog.SourceInvalid(
                        _logger, source.Id, "Die Suche liess sich nicht einrichten.");
                }
                else
                {
                    search.Add(provider);
                }
            }

            if (source.LookupByPhone is not null)
            {
                var provider = HttpCallerContextProvider.TryCreate(source, _http, allowInternal);

                if (provider is null)
                {
                    ConnectorLog.SourceInvalid(
                        _logger, source.Id, "Der Anruferkontext liess sich nicht einrichten.");
                }
                else
                {
                    context.Add(provider);
                }
            }
        }

        _searchProviders = search;
        _contextProviders = context;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _config.Changed -= OnConfigChanged;
    }
}
