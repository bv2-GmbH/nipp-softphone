using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Integrations.Config;

/// <summary>
/// Lädt und speichert <c>integrations.json</c> (ADR-017).
///
/// <b>Eine eigene Datei und ein eigener Speicher</b>, nicht Teil von
/// <c>NippSettings</c>. Der Grund steht in ADR-017 und ist kurz dieser: an
/// <c>SettingsService.Changed</c> hängen fünf Empfänger, die bis in den
/// SDK-Core, in die Registrierung und ins Besetztlampenfeld reichen. Eine
/// korrigierte JSONPath-Zeile ist kein Grund, Präsenz-Abonnements zu erneuern.
///
/// Die Muster sind bewusst dieselben wie in <c>SettingsService</c>, damit hier
/// niemand neu nachdenken muss:
/// <list type="bullet">
///   <item>atomar schreiben über eine Nebendatei,</item>
///   <item>eine kaputte Datei beiseitelegen statt überschreiben,</item>
///   <item>Schemaversion mit Migrationshaken von Anfang an,</item>
///   <item>Pfad über den Konstruktor — Tests fassen nie das Benutzerprofil an.</item>
/// </list>
///
/// <b>Ein Fehler hier darf nichts kosten ausser den Integrationen.</b> Ist die
/// Datei unlesbar, gibt es eben keine externen Quellen; telefoniert wird
/// weiter (§21.2).
/// </summary>
public sealed class IntegrationConfigStore
{
    /// <summary>
    /// Wie gelesen und geschrieben wird — aus <see cref="IntegrationJson"/>,
    /// damit Ausgeben und Speichern dieselbe Datei erzeugen.
    ///
    /// <b>Vorher stand hier eine eigene Kopie</b>, und im
    /// <c>IntegrationSettingsViewModel</c> eine zweite mit anderem Inhalt: der
    /// fehlte der Enum-Konverter. Die Ausgabe funktionierte nur, weil jedes
    /// Enum sein eigenes Attribut trug — und schrieb dabei <c>"Bearer"</c>,
    /// wo jede Vorlage <c>"bearer"</c> zeigt.
    /// </summary>
    private static JsonSerializerOptions JsonOptions => IntegrationJson.Options;

    private readonly IntegrationConfigValidator _validator;
    private readonly ILogger<IntegrationConfigStore> _logger;

    private IntegrationConfig _current = new();
    private IReadOnlyList<ValidationIssue> _issues = [];

    /// <summary>
    /// Der Thread, auf dem dieser Store gebaut wurde — bei nipp der UI-Thread.
    ///
    /// <para>Gebraucht für <see cref="Changed"/>: das Profil wird beim Start in
    /// einem <c>Task.Run</c> geholt, also lief <c>Save</c> und mit ihm die
    /// Benachrichtigung auf einem Threadpool-Thread. Die Empfänger sind aber
    /// ViewModels mit <c>ObservableCollection</c>; eine Änderung daran vom
    /// falschen Thread quittiert WinUI mit einer <c>COMException</c> <b>ohne
    /// Meldung</b> — die Art Fehler, an der im Protokoll nur „Kontakte liessen
    /// sich nicht laden:" und dahinter nichts steht. Dass es bisher gutging,
    /// lag am Zeitpunkt: die Einstellungsseite war beim Start noch nicht offen.
    /// Das ist Glück, keine Zusage.</para>
    ///
    /// <para>Dasselbe Muster wie in <c>ContactSearchService</c> und
    /// <c>CallerContextService</c>. <c>Nipp.Core</c> bleibt dabei ohne
    /// WinUI-Abhängigkeit (§21.2) — ein <c>SynchronizationContext</c> ist
    /// Teil der Basisbibliothek.</para>
    /// </summary>
    private readonly SynchronizationContext? _origin = SynchronizationContext.Current;

    /// <param name="path">
    /// Wo die Datei liegt. <c>null</c> heisst <see cref="DefaultPath"/>.
    ///
    /// <b>Der Parameter existiert für die Tests</b>, und das ist keine
    /// Bequemlichkeit: ein <c>dotnet test</c> hat auf diesem Weg schon einmal
    /// das SIP-Konto des angemeldeten Benutzers gelöscht
    /// (<c>TestIsolationTests</c>).
    /// </param>
    public IntegrationConfigStore(
        IntegrationConfigValidator validator,
        ILogger<IntegrationConfigStore> logger,
        string? path = null)
    {
        _validator = validator;
        _logger = logger;
        ConfigPath = path ?? DefaultPath;
    }

    /// <summary>§10: Konfiguration unter %APPDATA%\nipp, neben settings.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "nipp",
        "integrations.json");

    /// <summary>Die Datei, mit der diese Instanz arbeitet.</summary>
    public string ConfigPath { get; }

    /// <summary>Was gerade gilt. Nach <see cref="Load"/> gefüllt, nie <c>null</c>.</summary>
    public IntegrationConfig Current => _current;

    /// <summary>Die Befunde der letzten Prüfung — für die Anzeige in den Einstellungen.</summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>
    /// Wird nach dem Laden und nach jedem Speichern ausgelöst. <b>Ein
    /// Empfänger</b>: die Registry baut ihre Provider neu.
    /// </summary>
    public event EventHandler<IntegrationConfig>? Changed;

    /// <summary>
    /// Die Quellen, die tatsächlich benutzt werden: eingeschaltet und ohne
    /// Fehler. Eine Quelle mit einer blossen Warnung läuft — etwa, wenn ein
    /// Geheimnis auf diesem Gerät noch fehlt; das meldet dann der HTTP-Client
    /// mit einem Hinweis, was zu tun ist.
    /// </summary>
    public IReadOnlyList<DataSourceDefinition> UsableSources =>
    [
        .. _current.DataSources
            .Where(s => s.Enabled)
            .Where(s => !_issues.Any(i =>
                i.Severity == IssueSeverity.Error
                && i.Path.StartsWith($"dataSources[{s.Id}]", StringComparison.Ordinal)))
            .OrderBy(static s => s.Priority)
            .ThenBy(static s => s.Id, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Lädt die Datei. Fehlt sie, gibt es keine Integrationen — das ist der
    /// Normalfall und kein Fehler.
    /// </summary>
    public IntegrationConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            ConnectorLog.NoConfig(_logger, ConfigPath);

            _current = new IntegrationConfig();
            _issues = [];

            return _current;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<IntegrationConfig>(
                File.ReadAllText(ConfigPath),
                JsonOptions);

            Apply(loaded ?? new IntegrationConfig());

            ConnectorLog.ConfigLoaded(_logger, _current.DataSources.Count, UsableSources.Count);
            ReportIssues();

            return _current;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Wie bei den Einstellungen: beiseitelegen statt überschreiben.
            // In einer Integrationsdatei steckt unter Umständen die Arbeit
            // eines halben Tages.
            ConnectorLog.ConfigUnreadable(_logger, ConfigPath, ex.Message);
            TryPreserveBroken();

            _current = new IntegrationConfig();
            _issues = [];

            return _current;
        }
    }

    /// <summary>
    /// Speichert. Prüft vorher — eine Datei mit Fehlern lässt sich schreiben,
    /// aber die betroffenen Quellen bleiben aus, und die Oberfläche zeigt,
    /// warum.
    /// </summary>
    public void Save(IntegrationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(ConfigPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temporary, ConfigPath, overwrite: true);

        Apply(config);

        ConnectorLog.ConfigSaved(_logger, ConfigPath);
        ReportIssues();
    }

    /// <summary>
    /// Fügt <b>eine</b> Quelle ein oder ersetzt sie und lässt alles andere
    /// unberührt (K2).
    ///
    /// <para><b>Der Grund, warum es diese Methode gibt.</b> Bis zum 07.09.2026
    /// entstand eine neue Quelle nur über das Einlesen einer Datei — und das
    /// ging über <see cref="Save"/> mit der <b>ganzen</b> Konfiguration. Nach
    /// <c>crm.json</c> war das Gesprächsjournal weg; eine zweite Quelle ging nur
    /// über Handarbeit im JSON. Genau das verhinderte, was §21 verlangt:
    /// weitere APIs anbinden.</para>
    ///
    /// <para><b>Was ausdrücklich <em>nicht</em> übernommen wird:</b> die
    /// globalen Einstellungen der Vorlage. Eine Vorlage trägt
    /// <c>callerLookup</c> und <c>contactSearch</c> mit — das sind
    /// Vorschläge für eine leere Konfiguration, keine Anweisung. Wer eine
    /// zweite Quelle hinzufügt, hat seine Wartezeiten und seine
    /// Zusammenführungsregeln längst eingestellt, und sie beim Hinzufügen
    /// zurückzusetzen wäre eine Nebenwirkung, die niemand erwartet.</para>
    ///
    /// <para><b>Eingeschaltet wird hier nichts.</b> Die Quelle kommt so
    /// herein, wie sie übergeben wird, und die Vorlagen sind abgeschaltet —
    /// eingeschaltet wird nach einem erfolgreichen Testabruf. Sonst fragt nipp
    /// beim ersten Anruf eine Adresse, die noch niemand gesehen hat.</para>
    /// </summary>
    /// <param name="source">Die Quelle. Ihre Kennung entscheidet über Einfügen oder Ersetzen.</param>
    /// <param name="cards">
    /// Karten, die dazugehören. Eine Karte ersetzt die vorhandene <b>ihrer
    /// Art</b> — es gilt eine je Art, und zwei wären ein Befund.
    /// </param>
    public void AddOrReplaceSource(
        DataSourceDefinition source,
        IEnumerable<Cards.CardDefinition>? cards = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var config = _current;

        var sources = config.DataSources
            .Where(s => !string.Equals(s.Id, source.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Angehängt und nicht vorn eingefügt: die Reihenfolge in der Datei ist
        // die Lesereihenfolge für Menschen. Wer gewinnt, entscheidet
        // `priority`, nicht die Position.
        sources.Add(source);

        var neue = cards?.ToList() ?? [];

        var karten = config.Cards
            .Where(k => !neue.Any(n => n.Kind == k.Kind))
            .Concat(neue)
            .ToList();

        Save(config with { DataSources = sources, Cards = karten });
    }

    /// <summary>
    /// Entfernt eine Quelle. Ihre Geheimnisse bleiben liegen — sie zu löschen
    /// wäre beim versehentlichen Entfernen der teurere Fehler, weil ein Token
    /// nur einmal ausgegeben wird.
    /// </summary>
    /// <returns>Ob es die Quelle gab.</returns>
    public bool RemoveSource(string id)
    {
        var config = _current;

        var sources = config.DataSources
            .Where(s => !string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sources.Count == config.DataSources.Count)
        {
            return false;
        }

        Save(config with { DataSources = sources });

        return true;
    }

    /// <summary>
    /// Eine Kennung, die noch frei ist — <c>crm</c>, sonst
    /// <c>crm-2</c>.
    ///
    /// Für den Fall, dass jemand dieselbe Vorlage zweimal hinzufügt: das
    /// passiert bei zwei Mandanten desselben Systems, und dann sollen beide
    /// stehen bleiben statt sich zu überschreiben.
    /// </summary>
    public string FreeSourceId(string wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted))
        {
            wanted = "quelle";
        }

        if (!_current.DataSources.Any(s => string.Equals(s.Id, wanted, StringComparison.OrdinalIgnoreCase)))
        {
            return wanted;
        }

        for (var n = 2; n < 100; n++)
        {
            var kandidat = $"{wanted}-{n}";

            if (!_current.DataSources.Any(s => string.Equals(s.Id, kandidat, StringComparison.OrdinalIgnoreCase)))
            {
                return kandidat;
            }
        }

        return $"{wanted}-{Guid.NewGuid().ToString("N")[..4]}";
    }

    /// <summary>
    /// Gibt eine Konfiguration als Text aus — <b>ohne Geheimnisse</b>, weil
    /// dort keine stehen: die Datei trägt nur Verweise darauf.
    ///
    /// <b>Hier und nicht im ViewModel</b>, damit eine ausgegebene Datei
    /// zeichengleich mit einer gespeicherten ist. Wer eine ausgegebene Datei
    /// wieder einliest, soll denselben Stand bekommen und nicht einen, der
    /// sich in Schreibweisen unterscheidet.
    /// </summary>
    public static string Serialize(IntegrationConfig config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    /// <summary>
    /// Gibt <b>eine</b> Quelle als Text aus — für die Bearbeitung im Detail.
    ///
    /// <para><b>Warum je Quelle und nicht die ganze Datei.</b> Wer einen
    /// JSONPath korrigieren wollte, musste bisher die gesamte Konfiguration
    /// ausgeben, im Editor die richtige Stelle suchen und alles wieder
    /// einlesen — und das Einlesen ersetzte alles. Bei zwei Quellen war das
    /// ein Weg, auf dem sich die andere verlieren liess.</para>
    /// </summary>
    public static string SerializeSource(DataSourceDefinition source) =>
        JsonSerializer.Serialize(source, JsonOptions);

    /// <summary>
    /// Gibt eine Karte als Text aus — für den Rückgängig-Stapel des Designers
    /// und für Tests, die zwei Karten vergleichen.
    ///
    /// <b>Warum über Text und nicht über <c>==</c>:</b> ein <c>record</c>
    /// vergleicht seine Listen nach Referenz, nicht nach Inhalt. Zwei Karten
    /// mit identischem Inhalt sind damit ungleich, und ein Vergleich, der
    /// immer „geändert" sagt, ist keiner.
    /// </summary>
    public static string SerializeCard(Cards.CardDefinition card) =>
        JsonSerializer.Serialize(card, JsonOptions);

    /// <summary>
    /// Liest eine einzelne Quelle aus Text. <b>Wirft nicht</b> — sie kommt aus
    /// einem Textfeld.
    /// </summary>
    /// <param name="error">
    /// Was nicht stimmt, nach §15 mit Ursache und Abhilfe. Leer bei Erfolg.
    /// </param>
    public static bool TryReadSource(
        string? text,
        out DataSourceDefinition? source,
        out string error)
    {
        source = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Es steht nichts da.";
            return false;
        }

        try
        {
            source = JsonSerializer.Deserialize<DataSourceDefinition>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Das ist kein gültiges JSON: {ex.Message}";
            return false;
        }

        if (source is null)
        {
            error = "Es steht nichts da.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(source.Id))
        {
            error = "Der Quelle fehlt die Kennung ('id'). Sie ist zugleich der "
                + "Namensraum auf der Karte.";

            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Liest eine Datei, ohne sie zu übernehmen — für den Import und für die
    /// Provisionierung. Meldet, was daran nicht stimmt.
    /// </summary>
    public bool TryRead(
        string text,
        out IntegrationConfig? config,
        out IReadOnlyList<ValidationIssue> issues)
    {
        try
        {
            config = JsonSerializer.Deserialize<IntegrationConfig>(text, JsonOptions);

            if (config is null)
            {
                issues = [new ValidationIssue("(Datei)", IssueSeverity.Error, "Die Datei ist leer.")];
                return false;
            }

            config = Migrate(config);
            issues = _validator.Validate(config);

            return !issues.Any(static i => i.Severity == IssueSeverity.Error);
        }
        catch (JsonException ex)
        {
            config = null;

            issues =
            [
                new ValidationIssue(
                    "(Datei)",
                    IssueSeverity.Error,
                    $"Die Datei ist kein gültiges JSON: {ex.Message}"),
            ];

            return false;
        }
    }

    private void Apply(IntegrationConfig config)
    {
        _current = Migrate(config);
        _issues = _validator.Validate(_current);

        Notify(_current);
    }

    /// <summary>
    /// Meldet die Änderung — immer auf dem Thread, auf dem dieser Store gebaut
    /// wurde. Die Begründung steht an <see cref="_origin"/>.
    /// </summary>
    private void Notify(IntegrationConfig config)
    {
        if (_origin is null || _origin == SynchronizationContext.Current)
        {
            Raise(config);
            return;
        }

        _origin.Post(_ => Raise(config), null);
    }

    private void Raise(IntegrationConfig config)
    {
        try
        {
            Changed?.Invoke(this, config);
        }
        catch (Exception ex)
        {
            // Aus einem geposteten Rückruf heraus fängt diese Ausnahme
            // niemand mehr. §21.2: an den Integrationen darf nichts hängen,
            // was das Telefonieren mitnimmt.
            ConnectorLog.ConfigNotifyFailed(_logger, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Migration älterer Fassungen. Noch nichts zu tun — die Stelle existiert,
    /// damit die erste echte Änderung nicht auch noch die Ablagestruktur
    /// anfassen muss. Dieselbe Vorsorge wie in <c>SettingsService</c>.
    /// </summary>
    private static IntegrationConfig Migrate(IntegrationConfig loaded) =>
        loaded.SchemaVersion == new IntegrationConfig().SchemaVersion
            ? loaded
            : loaded with { SchemaVersion = new IntegrationConfig().SchemaVersion };

    private void ReportIssues()
    {
        foreach (var issue in _issues.Where(static i => i.Severity == IssueSeverity.Error))
        {
            ConnectorLog.SourceInvalid(_logger, issue.Path, issue.Message);
        }
    }

    private void TryPreserveBroken()
    {
        try
        {
            var target = $"{ConfigPath}.kaputt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";

            File.Move(ConfigPath, target, overwrite: false);
            ConnectorLog.ConfigPreserved(_logger, target);
        }
        catch (IOException)
        {
            // Dann bleibt es eben bei „keine Integrationen".
        }
    }
}
