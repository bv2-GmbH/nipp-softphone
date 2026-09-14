using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Diagnostics;

/// <summary>
/// Erstellt das Diagnosepaket (§9.6, AP8.6).
///
/// „ZIP mit Logs, Config <b>ohne Passwörter</b>, Systeminfo."
///
/// Der Punkt „ohne Passwörter" ist der einzige, bei dem ein Fehler teuer wird:
/// ein Diagnosepaket geht per Mail an den Support und liegt danach in
/// Postfächern. Deshalb wird die Konfiguration hier nicht kopiert, sondern
/// <b>neu aufgebaut</b> — was nicht ausdrücklich hineingeschrieben wird, kann
/// auch nicht versehentlich mitgehen. §12 (M7) verlangt, das explizit zu
/// prüfen; der Test dazu steht in <c>DiagnosticsBundleTests</c>.
/// </summary>
public sealed class DiagnosticsBundle(
    SettingsService settings,
    Services.Integrations.Config.IntegrationConfigStore integrations,
    Services.Integrations.Secrets.IntegrationSecrets secrets,
    ILogger<DiagnosticsBundle> logger,
    string? logDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Woher die Protokolle kommen.
    ///
    /// Ueber den Konstruktor steuerbar, damit ein Test weiss, was im Paket
    /// landet. Solange das fest verdrahtet war, las der Test die Logs der
    /// laufenden Anwendung mit — und pruefte bei jedem Lauf etwas anderes.
    /// Einmal ist er dabei rot geworden und beim naechsten Mal wieder gruen,
    /// was schlimmer ist als ein Test, den es nicht gibt.
    /// </summary>
    public string LogDirectory { get; } = logDirectory ?? DefaultLogDirectory;

    /// <summary>§10: Protokolle unter %LOCALAPPDATA%\nipp\logs.</summary>
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "logs");

    /// <summary>Wohin das Paket geschrieben wird.</summary>
    public static string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "diagnostics");

    /// <summary>
    /// Baut das Paket und gibt den Pfad zurück.
    /// </summary>
    public Task<string> CreateAsync(string? outputDirectory = null, CancellationToken cancellationToken = default)
    {
        var directory = outputDirectory ?? DefaultOutputDirectory;
        Directory.CreateDirectory(directory);

        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"nipp-diagnose-{DateTimeOffset.Now:yyyy-MM-dd_HHmmss}.zip");
        var path = Path.Combine(directory, name);

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddSystemInfo(archive);
            AddSanitizedSettings(archive);
            AddSanitizedIntegrations(archive);
            AddLogs(archive);
        }

        DiagnosticsLog.Created(logger, path);
        return Task.FromResult(path);
    }

    private static void AddSystemInfo(ZipArchive archive)
    {
        var report = new StringBuilder();

        report.AppendLine("nipp — Systeminformationen");
        report.AppendLine(CultureInfo.InvariantCulture, $"Erstellt: {DateTimeOffset.Now:O}");
        report.AppendLine();
        // W1.5 (Befund E12): die vollstaendige Fassung, nicht nur drei Zahlen.
        //
        // Release-Nipp.ps1 uebergibt die Vorabversion als
        // InformationalVersion; die Assembly-Version kann kein «-beta.1»
        // tragen. Stand hier nur sie, sah ein installiertes 0.9.3-beta.1 wie
        // ein 0.9.3 aus — und der Support wusste nicht, welchen Kanal er vor
        // sich hat.
        report.AppendLine(CultureInfo.InvariantCulture, $"nipp-Version:     {Fassung()}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Betriebssystem:   {RuntimeInformation.OSDescription}");
        report.AppendLine(CultureInfo.InvariantCulture, $"OS-Architektur:   {RuntimeInformation.OSArchitecture}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Prozess:          {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine(CultureInfo.InvariantCulture, $".NET:             {RuntimeInformation.FrameworkDescription}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Arbeitsspeicher:  {GC.GetTotalMemory(false) / 1024 / 1024} MB verwaltet");
        report.AppendLine(CultureInfo.InvariantCulture, $"Prozessoren:      {Environment.ProcessorCount}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Kultur:           {CultureInfo.CurrentCulture.Name}");
        report.AppendLine();

        // Diese Übersicht nennt bewusst weder Benutzernamen noch Rechnernamen
        // oder Domäne: ein Diagnosepaket wandert durch Postfächer und soll das
        // Problem beschreiben, nicht den Arbeitsplatz.
        //
        // Vollständig vermeiden lässt sich der Benutzername allerdings nicht:
        // die beigelegten Logs enthalten Dateipfade unter %LOCALAPPDATA%, und
        // darin steht er. Das hier ist also Sparsamkeit, keine Anonymisierung —
        // und wird als solche benannt, statt eine Zusage zu machen, die nicht
        // hält.
        report.AppendLine("Diese Übersicht nennt weder Benutzer- noch Rechnernamen.");
        report.AppendLine("Hinweis: die beigelegten Logs enthalten Dateipfade, in denen der");
        report.AppendLine("Windows-Benutzername vorkommt. Das Paket ist sparsam, aber nicht anonym.");

        WriteEntry(archive, "systeminfo.txt", report.ToString());
    }

    /// <summary>
    /// Schreibt die Konfiguration <b>neu</b>, Feld für Feld. Kopieren wäre
    /// gefährlich: käme später ein Feld dazu, das ein Geheimnis enthält, wäre
    /// es sofort im Paket. So muss jedes Feld bewusst aufgenommen werden.
    /// </summary>
    private void AddSanitizedSettings(ZipArchive archive)
    {
        var s = settings.Current;

        var safe = new
        {
            Hinweis = "Passwörter und TURN-Zugangsdaten sind bewusst nicht enthalten (Paragraph 9.6).",

            Konten = s.Accounts.Select(a => new
            {
                a.Username,
                a.Domain,
                a.AuthUserId,
                a.DisplayName,
                Transport = a.Transport.ToString(),
                a.ExpiresSeconds,
                a.OutboundProxy,
                HatPasswort = !string.IsNullOrEmpty(a.Password),
            }),

            Netzwerk = s.Network,
            NatMedien = new
            {
                s.NatMedia.StunServer,
                s.NatMedia.EnableIce,
                s.NatMedia.EnableTurn,
                s.NatMedia.TurnServer,
                TurnBenutzer = string.IsNullOrEmpty(s.NatMedia.TurnUsername) ? null : "(gesetzt)",
                Verschluesselung = s.NatMedia.Encryption.ToString(),
                s.NatMedia.EncryptionMandatory,
                s.NatMedia.AdaptiveBitrate,
            },
            Audio = s.Audio,
            Codecs = new
            {
                s.Codecs.Order,
                s.Codecs.Enabled,
                Dtmf = s.Codecs.Dtmf.ToString(),
            },
            Erweitert = new
            {
                s.Advanced.StartWithWindows,
                s.Advanced.StartMinimized,
                s.Advanced.RegisterProtocolHandlers,
                s.Advanced.GlobalHotkey,
                s.Advanced.AutoAnswer,
                Protokollierung = s.Advanced.Logging.ToString(),
                // Nur der Rechnername: in einer Provisionierungsadresse kann ein
                // Geraetetoken oder «benutzer:passwort@» stehen, und diese Datei
                // geht an den Support (Paragraph 9.6).
                Provisionierung = HostOf(s.Advanced.ProvisioningUri),
                s.Advanced.CountryPrefix,
                s.Advanced.RecordingDirectory,
                s.Advanced.HistoryRetentionDays,
                Erscheinungsbild = s.Advanced.Theme.ToString(),
                s.Advanced.AlwaysOnTop,
            },
        };

        WriteEntry(archive, "einstellungen.json", JsonSerializer.Serialize(safe, JsonOptions));
    }

    /// <summary>
    /// Die Integrationen (§21.3) — nach derselben Whitelist-Regel wie die
    /// Einstellungen: <b>neu aufgebaut, nicht kopiert</b>.
    ///
    /// Was hier bewusst <b>nicht</b> steht: Kopfzeilen, Anfrageparameter,
    /// Anfragekörper, Mappings, Kartendefinitionen und selbstverständlich
    /// keine Zugangsdaten. Die Parameter tragen die Vorlagen für Rufnummer und
    /// Suchtext, und ein Diagnosepaket geht per Mail an den Support und liegt
    /// danach in Postfächern (§21.2).
    ///
    /// Was hier steht, beantwortet die Fragen, die ein Support wirklich hat:
    /// welche Quellen gibt es, sind sie an, welcher Rechner, welche
    /// Zeitgrenzen, ist der Schlüssel hinterlegt, und was hat die Prüfung
    /// bemängelt.
    /// </summary>
    private void AddSanitizedIntegrations(ZipArchive archive)
    {
        var config = integrations.Current;

        var safe = new
        {
            Hinweis = "Endpunkte, Kopfzeilen, Mappings und Zugangsdaten sind bewusst nicht "
                + "enthalten (Paragraph 21.2). Vom Endpunkt steht nur der Rechnername hier.",

            config.SchemaVersion,
            // Nur der Dateiname: der volle Pfad traegt den Benutzernamen.
            Datei = Path.GetFileName(integrations.ConfigPath),

            AnruferKontext = new
            {
                config.CallerLookup.Enabled,
                config.CallerLookup.LookupIncoming,
                config.CallerLookup.LookupOutgoing,
                config.CallerLookup.LookupInternalNumbers,
                config.CallerLookup.CacheSeconds,
            },

            KontaktSuche = new
            {
                config.ContactSearch.DebounceMs,
                config.ContactSearch.MinQueryLength,
                config.ContactSearch.ResultLimitPerSource,
                ZusammenfuehrenAktiv = config.ContactSearch.Merge.Enabled,
            },

            Quellen = config.DataSources.Select(source => new
            {
                source.Id,
                source.DisplayName,
                source.Type,
                source.Enabled,
                source.Priority,

                // Nur der Rechnername, nie der ganze Endpunkt: im Pfad steht
                // gelegentlich eine Mandanten- oder Kundenkennung.
                Host = Uri.TryCreate(source.Http?.BaseUrl, UriKind.Absolute, out var url)
                    ? url.Host
                    : "(unbrauchbar)",
                Verschluesselt = Uri.TryCreate(source.Http?.BaseUrl, UriKind.Absolute, out var scheme)
                    && scheme.Scheme == Uri.UriSchemeHttps,
                ZeitgrenzeMs = source.Http?.TimeoutMs,
                MaxAntwortBytes = source.Http?.MaxResponseBytes,
                // Mit dem Schema, nicht nur der Art: bei Bearer steht in der
                // Kopfzeile nicht zwingend „Bearer" (das CRM verlangt
                // „Token"). Ohne diese Angabe meldet das Paket „Bearer" und
                // schickt die Fehlersuche bei einer 401 in die falsche
                // Richtung. Ein Schema ist kein Geheimnis.
                Anmeldung = source.Http?.Auth is { } auth
                    ? auth.Type == AuthKind.Bearer && !string.IsNullOrWhiteSpace(auth.Scheme)
                        ? $"{auth.Type} ({auth.Scheme.Trim()})"
                        : auth.Type.ToString()
                    : "None",

                // Ob hinterlegt, nicht was.
                ZugangsdatenHinterlegt = source.Http is null
                    || source.Http.Auth.SecretRefs.Count == 0
                    || secrets.FindMissing(source.Http.Auth.SecretRefs).Count == 0,

                Faehigkeiten = source.Capabilities.Select(static c => c.ToString()),
                AnzahlFelder = (source.LookupByPhone?.Mapping.Count ?? 0)
                    + (source.SearchContacts?.Mapping.Count ?? 0),
            }),

            Befunde = integrations.Issues.Select(issue => new
            {
                issue.Path,
                Schwere = issue.Severity.ToString(),
                issue.Message,
            }),
        };

        WriteEntry(archive, "integrationen.json", JsonSerializer.Serialize(safe, JsonOptions));
    }

    private void AddLogs(ZipArchive archive)
    {
        var logDirectory = LogDirectory;

        if (!Directory.Exists(logDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(logDirectory, "*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(7))
        {
            TryAddMaskedFile(archive, file, $"logs/{Path.GetFileName(file)}");
        }

        var crash = Path.Combine(logDirectory, "crash.txt");
        if (File.Exists(crash))
        {
            TryAddMaskedFile(archive, crash, "logs/crash.txt");
        }
    }

    /// <summary>
    /// Nimmt eine Protokolldatei auf — <b>Zeile für Zeile durch die
    /// Maskierung</b>.
    ///
    /// <para><b>Warum das sein muss.</b> Die Whitelist-Regel, nach der die
    /// JSON-Dateien dieses Pakets Feld für Feld neu aufgebaut werden, galt für
    /// die Protokolle nicht: sie wurden unverändert kopiert. Damit landeten
    /// dort alle Rufnummern, die der Telefonie-Kern schreibt — und auf
    /// Debug-Stufe (der empfohlene Weg für einen SIP-Trace) zusätzlich die
    /// SIP-Nachrichten des SDK mit Absender und Ziel. Ein Diagnosepaket geht
    /// per Mail an den Support und liegt danach in Postfächern (§21.2).</para>
    ///
    /// <para>Gestreamt und nicht eingelesen: die Protokolle werden mehrere
    /// Megabyte gross, und der Arbeitsspeicher ist bei einem Softphone knapp
    /// bemessen (§2).</para>
    /// </summary>
    private void TryAddMaskedFile(ZipArchive archive, string source, string entryName)
    {
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(input, Encoding.UTF8);
            using var entry = archive.CreateEntry(entryName).Open();
            using var writer = new StreamWriter(entry, Encoding.UTF8);

            while (reader.ReadLine() is { } line)
            {
                writer.WriteLine(LogMasking.Line(line));
            }
        }
        catch (IOException ex)
        {
            DiagnosticsLog.FileSkipped(logger, source, ex.Message);
        }
    }

    /// <summary>
    /// Der Rechnername aus einer Adresse, oder ein Hinweis, dass keine
    /// eingetragen ist. Nie die vollständige Adresse — dort können
    /// Zugangsdaten stehen.
    /// </summary>
    private static string HostOf(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return "(keine)";
        }

        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? $"{parsed.Scheme}://{parsed.Host}"
            : "(unbrauchbar)";
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    /// <summary>
    /// Die Fassung, wie sie ausgeliefert wurde (W1.5, E12).
    ///
    /// <para><c>InformationalVersion</c> traegt die Vorabversion samt Suffix,
    /// die Assembly-Version kann das nicht. Fehlt sie — etwa in einem
    /// Debug-Build —, gilt die Assembly-Version.</para>
    /// </summary>
    private static string Fassung()
    {
        var assembly = typeof(DiagnosticsBundle).Assembly;

        var voll = assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (voll is not { Length: > 0 })
        {
            return assembly.GetName().Version?.ToString() ?? "unbekannt";
        }

        // Der SDK haengt bei deterministischen Builds die Commit-Kennung an
        // ("0.9.3-beta.1+a1b2c3d"). Sie gehoert nicht in die erste Zeile.
        var plus = voll.IndexOf('+', StringComparison.Ordinal);

        return plus > 0 ? voll[..plus] : voll;
    }

}

internal static partial class DiagnosticsLog
{
    [LoggerMessage(EventId = 2900, Level = LogLevel.Information,
        Message = "Diagnosepaket erstellt: {Path}")]
    public static partial void Created(ILogger logger, string path);

    [LoggerMessage(EventId = 2901, Level = LogLevel.Warning,
        Message = "Datei {Path} konnte nicht ins Diagnosepaket: {Reason}")]
    public static partial void FileSkipped(ILogger logger, string path, string reason);
}
