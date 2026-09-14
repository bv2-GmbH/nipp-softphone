using System.Reflection;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Services.Integrations.Catalog;

/// <summary>
/// Die Anbietervorlagen: die mitgelieferten und die importierten (ADR-040).
///
/// <para><b>Ein Dienst und kein statischer Katalog mehr.</b> Der alte
/// <c>ConnectorCatalog</c> war statisch mit <c>Lazy</c> — vertretbar, solange er
/// <b>unveränderlich</b> war. Mit dem Import ist er es nicht mehr, und §15
/// verlangt Dependency Injection statt statischer Singletons.</para>
///
/// <para><b>Der Ablageort kommt über den Konstruktor</b>, wie bei
/// <c>SettingsService</c>, <c>SecretStore</c> und <c>IntegrationConfigStore</c>.
/// <c>TestIsolationTests</c> erzwingt das, und der Grund steht im Projekt fest:
/// ein gewöhnliches <c>dotnet test</c> hat hier schon einmal das SIP-Konto des
/// angemeldeten Benutzers samt Passwort gelöscht.</para>
///
/// <para><b>Kein <c>FileSystemWatcher</c>.</b> Änderungen entstehen nur über
/// <see cref="TryImport"/> und <see cref="Remove"/>, und die lösen
/// <see cref="Changed"/> selbst aus. Ein Watcher auf einem Roaming-Profil wäre
/// eine Fehlerquelle ohne Gegenwert.</para>
///
/// <para><b>Wirft nie.</b> Eine kaputte Datei fehlt in der Liste und steht im
/// Protokoll; ausführlich wird es nur auf dem Importweg, wo jemand zuschaut.
/// Der Katalog ist Beiwerk — ohne ihn lässt sich immer noch eine Konfiguration
/// einlesen.</para>
/// </summary>
public sealed class ConnectorLibrary
{
    /// <summary>Der Ressourcenpräfix der mitgelieferten Vorlagen.</summary>
    private const string BuiltInPrefix =
        "Nipp.Core.Services.Integrations.Catalog.templates.";

    private readonly ILogger<ConnectorLibrary> _logger;
    private readonly List<ConnectorTemplate> _templates = [];
    /// <summary>
    /// Schützt die Liste: <see cref="Reload"/> kann laufen, während eine
    /// Oberfläche <see cref="Templates"/> liest.
    /// </summary>
    private readonly object _gate = new();

    /// <param name="directory">
    /// Wo importierte Vorlagen liegen. <c>null</c> heisst
    /// <see cref="DefaultDirectory"/>.
    /// </param>
    public ConnectorLibrary(ILogger<ConnectorLibrary> logger, string? directory = null)
    {
        _logger = logger;
        Directory = directory ?? DefaultDirectory;
    }

    /// <summary>§10: neben <c>integrations.json</c> unter %APPDATA%\nipp.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "nipp",
        "connectors");

    /// <summary>Der Ordner, mit dem diese Instanz arbeitet.</summary>
    public string Directory { get; }

    /// <summary>Wird ausgelöst, wenn sich die Liste geändert hat.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Alle Vorlagen — die mitgelieferten zuerst, dann die importierten in
    /// alphabetischer Reihenfolge.
    ///
    /// <b>Die Reihenfolge ist eine Aussage:</b> „Eigene REST-API" steht oben,
    /// weil sie der Normalfall für jeden ist, der noch nichts eingerichtet hat.
    /// </summary>
    public IReadOnlyList<ConnectorTemplate> Templates
    {
        get
        {
            lock (_gate)
            {
                return [.. _templates];
            }
        }
    }

    /// <summary>Eine Vorlage über ihre Kennung, oder <c>null</c>.</summary>
    public ConnectorTemplate? Find(string? id) =>
        id is null ? null : Templates.FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// Lädt die mitgelieferten und die importierten Vorlagen neu.
    ///
    /// <b>Gehört hinter den Start</b> (§21.2): nichts an den Integrationen darf
    /// zwischen dem Start und dem ersten möglichen Anruf stehen.
    /// </summary>
    public void Reload()
    {
        var geladen = new List<ConnectorTemplate>();

        geladen.AddRange(LoadBuiltIn());
        geladen.AddRange(LoadImported());

        lock (_gate)
        {
            _templates.Clear();
            _templates.AddRange(geladen);
        }

        CatalogLog.Loaded(_logger, geladen.Count);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Liest eine Vorlagendatei ein und legt sie ab.
    ///
    /// <para><b>Es entsteht dabei keine Quelle.</b> Die Vorlage bleibt liegen,
    /// und eingerichtet wird danach über „Quelle hinzufügen" — der bekannte
    /// Weg. Das hat zwei Gründe: <see cref="SecretsFor"/> sucht Beschriftung
    /// und Herkunftshinweis eines Geheimnisses über <b>alle</b> Vorlagen,
    /// verschwände sie nach dem Anlegen, stünde im Formular wieder das
    /// generische „API-Token"; und dieselbe Vorlage muss für zwei Mandanten
    /// zweimal anwendbar sein.</para>
    /// </summary>
    /// <param name="text">Der Dateiinhalt.</param>
    /// <param name="replaceExisting">
    /// Ob eine vorhandene Vorlage gleicher Kennung ersetzt werden darf.
    /// <b>Angelegte Quellen bleiben davon unberührt</b> — die stehen in
    /// <c>integrations.json</c>.
    /// </param>
    /// <param name="template">Was gelesen wurde, auch im Konfliktfall.</param>
    /// <param name="error">Warum es nicht ging.</param>
    public bool TryImport(
        string? text,
        bool replaceExisting,
        out ConnectorTemplate? template,
        out string? error)
    {
        if (!ConnectorTemplateReader.TryRead(text, out template, out error))
        {
            return false;
        }

        var gelesen = template!;

        if (!replaceExisting && Find(gelesen.Id) is { } vorhanden)
        {
            error = vorhanden.Origin == ConnectorOrigin.BuiltIn
                ? $"«{vorhanden.DisplayName}» bringt nipp bereits mit. Eine mitgelieferte "
                    + "Vorlage lässt sich nicht ersetzen."
                : $"Eine Vorlage mit der Kennung «{gelesen.Id}» ist schon da.";

            return false;
        }

        if (Find(gelesen.Id) is { Origin: ConnectorOrigin.BuiltIn })
        {
            error = "Eine mitgelieferte Vorlage lässt sich nicht ersetzen.";
            return false;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            // Der Dateiname folgt der Kennung, damit ein erneuter Import
            // dieselbe Datei trifft statt eine zweite anzulegen.
            File.WriteAllText(PathFor(gelesen.Id), text!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Die Vorlage liess sich nicht ablegen: {ex.Message}";
            CatalogLog.ImportFailed(_logger, gelesen.Id, ex.Message);
            return false;
        }

        Reload();

        CatalogLog.Imported(_logger, gelesen.Id);

        return true;
    }

    /// <summary>
    /// Entfernt eine importierte Vorlage.
    ///
    /// <b>Eine eingerichtete Quelle bleibt.</b> Sie steht in
    /// <c>integrations.json</c> und telefoniert weiter; verloren geht nur die
    /// Beschriftung der Zugangsdaten.
    /// </summary>
    public bool Remove(string id)
    {
        if (Find(id) is not { Origin: ConnectorOrigin.Imported })
        {
            return false;
        }

        try
        {
            File.Delete(PathFor(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CatalogLog.RemoveFailed(_logger, id, ex.Message);
            return false;
        }

        Reload();

        return true;
    }

    /// <summary>
    /// Die Geheimnisse, die eine <b>eingerichtete</b> Quelle braucht, mit
    /// Beschriftung und Herkunft.
    ///
    /// <para>Gefragt wird über die Quelle, nicht über die Vorlage: nach dem
    /// Hinzufügen kennt die Konfiguration nur noch die Quelle, und die
    /// Einstellungen brauchen die Beschriftung trotzdem. Gefunden wird über
    /// den <c>secretRef</c> — er steht in beiden.</para>
    ///
    /// <para>Für eine Quelle, die von Hand entstand, gibt es keine Vorlage.
    /// Dann wird eine Beschriftung gebaut, damit im Formular nicht „(kein
    /// Name)" steht — der Verweis selbst ist eine technische Kennung und
    /// gehört nicht als Feldname in eine Oberfläche.</para>
    /// </summary>
    public IReadOnlyList<ConnectorSecret> SecretsFor(DataSourceDefinition? source)
    {
        var refs = source?.Http?.Auth.SecretRefs ?? [];

        if (refs.Count == 0)
        {
            return [];
        }

        var bekannt = Templates
            .SelectMany(static t => t.Secrets)
            .GroupBy(static s => s.Ref, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);

        return
        [
            .. refs.Select(r => bekannt.TryGetValue(r, out var treffer)
                ? treffer
                : new ConnectorSecret(r, StandardLabel(source!, r), null)),
        ];
    }

    private string PathFor(string id) => Path.Combine(Directory, $"{id}.json");

    /// <summary>
    /// Die Beschriftung für ein Geheimnis ohne Vorlage — abgeleitet aus der
    /// Anmeldeart, weil die sagt, was für ein Wert gebraucht wird.
    /// </summary>
    private static string StandardLabel(DataSourceDefinition source, string reference)
    {
        var auth = source.Http?.Auth;

        if (auth is null)
        {
            return "Zugangsdaten";
        }

        return auth.Type switch
        {
            AuthKind.Bearer => "API-Token",
            AuthKind.ApiKey => "API-Schlüssel",
            AuthKind.Basic when reference == auth.UsernameSecretRef => "Benutzername",
            AuthKind.Basic => "Passwort",
            _ => "Zugangsdaten",
        };
    }

    /// <summary>
    /// Die eingebetteten Vorlagen.
    ///
    /// <b>Eingebettet und nicht auf der Platte.</b> Eine Datei neben der EXE
    /// müsste ein Installer mitbringen, ein unpackaged-Build vergisst sie, und
    /// ein Virenscanner räumt sie weg — dieselbe Art Ärger, die schon bei den
    /// Symbolen einen halben Tag gekostet hat.
    /// </summary>
    private IEnumerable<ConnectorTemplate> LoadBuiltIn()
    {
        var assembly = typeof(ConnectorLibrary).Assembly;

        foreach (var name in assembly.GetManifestResourceNames()
            .Where(static n => n.StartsWith(BuiltInPrefix, StringComparison.Ordinal))
            .OrderBy(static n => n, StringComparer.Ordinal))
        {
            if (ReadResource(assembly, name) is not { } text)
            {
                continue;
            }

            if (!ConnectorTemplateReader.TryRead(text, out var template, out var error))
            {
                // Eine mitgelieferte Vorlage, die nicht liest, ist ein Fehler
                // in nipp — aber kein Grund, den Katalog leer zu lassen.
                CatalogLog.BuiltInBroken(_logger, name, error ?? "unbekannt");
                continue;
            }

            yield return template! with { Origin = ConnectorOrigin.BuiltIn };
        }
    }

    private List<ConnectorTemplate> LoadImported()
    {
        var gefunden = new List<ConnectorTemplate>();

        if (!System.IO.Directory.Exists(Directory))
        {
            return gefunden;
        }

        string[] dateien;

        try
        {
            dateien = System.IO.Directory.GetFiles(Directory, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CatalogLog.DirectoryUnreadable(_logger, Directory, ex.Message);
            return gefunden;
        }

        foreach (var datei in dateien.OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
        {
            string text;

            try
            {
                text = File.ReadAllText(datei);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CatalogLog.TemplateUnreadable(_logger, Path.GetFileName(datei), ex.Message);
                continue;
            }

            if (!ConnectorTemplateReader.TryRead(text, out var template, out var error))
            {
                // Nur ins Protokoll: hier schaut niemand zu. Wer eine kaputte
                // Datei einliest, bekommt den Grund auf dem Importweg.
                CatalogLog.TemplateUnreadable(_logger, Path.GetFileName(datei), error ?? "unbekannt");
                continue;
            }

            gefunden.Add(template!);
        }

        return gefunden;
    }

    private static string? ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);

        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

internal static partial class CatalogLog
{
    [LoggerMessage(EventId = 2700, Level = LogLevel.Information,
        Message = "Anbietervorlagen geladen: {Count}")]
    public static partial void Loaded(ILogger logger, int count);

    [LoggerMessage(EventId = 2701, Level = LogLevel.Information,
        Message = "Anbietervorlage importiert: {Id}")]
    public static partial void Imported(ILogger logger, string id);

    [LoggerMessage(EventId = 2702, Level = LogLevel.Warning,
        Message = "Anbietervorlage {Id} liess sich nicht ablegen: {Reason}")]
    public static partial void ImportFailed(ILogger logger, string id, string reason);

    [LoggerMessage(EventId = 2703, Level = LogLevel.Warning,
        Message = "Anbietervorlage {Id} liess sich nicht entfernen: {Reason}")]
    public static partial void RemoveFailed(ILogger logger, string id, string reason);

    [LoggerMessage(EventId = 2704, Level = LogLevel.Warning,
        Message = "Vorlagendatei {File} uebersprungen: {Reason}")]
    public static partial void TemplateUnreadable(ILogger logger, string file, string reason);

    [LoggerMessage(EventId = 2705, Level = LogLevel.Warning,
        Message = "Vorlagenordner {Directory} nicht lesbar: {Reason}")]
    public static partial void DirectoryUnreadable(ILogger logger, string directory, string reason);

    [LoggerMessage(EventId = 2706, Level = LogLevel.Error,
        Message = "Mitgelieferte Vorlage {Resource} ist unbrauchbar: {Reason}")]
    public static partial void BuiltInBroken(ILogger logger, string resource, string reason);
}
