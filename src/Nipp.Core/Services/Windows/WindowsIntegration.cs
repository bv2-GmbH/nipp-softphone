using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Autostart und Protokoll-Handler (§10, AP7.2, AP7.4).
///
/// <b>Warum über die Registrierung und nicht über das Manifest.</b> §10 nennt
/// für den packaged Fall <c>StartupTask</c> und Manifest-Einträge. Beide
/// verlangen, dass die App als MSIX <b>installiert</b> ist — beim Entwickeln
/// läuft sie aber meist unpackaged, und dann greift keiner von beiden. Der
/// Weg über HKCU funktioniert in beiden Fällen und braucht keine
/// Administratorrechte.
///
/// Für die Auslieferung (P9) bleibt der Manifest-Weg die bessere Wahl: er wird
/// bei der Deinstallation sauber zurückgebaut. Diese Klasse ist die
/// Rückfallebene, die §10 ausdrücklich vorsieht.
/// </summary>
public sealed class WindowsIntegration(ILogger<WindowsIntegration> logger)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "nipp";

    /// <summary>Die Protokolle aus §10.</summary>
    private static readonly string[] Protocols = ["tel", "sip", "sips", "callto"];

    /// <summary>
    /// Ob nipp mit Paketidentität läuft — also als installiertes MSIX.
    ///
    /// <b>Warum das hier zählt.</b> Im installierten Zustand erledigen
    /// <c>StartupTask</c> und die Protokoll-Extensions des Manifests genau
    /// diese Aufgabe (§10). Beide Wege gleichzeitig zu gehen ist nicht
    /// doppelt sicher, sondern falsch: der HKCU-Eintrag zeigt dann auf die EXE
    /// im WindowsApps-Ordner, Windows führt zwei Startobjekte namens „nipp",
    /// und im Dialog „Öffnen mit" steht nipp zweimal. Bei der Deinstallation
    /// bleibt der Registrierungseintrag ausserdem liegen.
    ///
    /// Ermittelt über die Win32-API und nicht über <c>Package.Current</c>:
    /// letzteres wirft ohne Paketidentität eine Ausnahme, und eine Ausnahme
    /// als Normalfall im Startpfad verdeckt echte Fehler.
    /// </summary>
    public static bool HasPackageIdentity { get; } = DetectPackageIdentity();

    private static bool DetectPackageIdentity()
    {
        try
        {
            var length = 0;
            _ = GetCurrentPackageFullName(ref length, null);

            // APPMODEL_ERROR_NO_PACKAGE = 15700. Alles andere — auch der
            // erwartete Puffer-zu-klein-Fehler — bedeutet: es gibt ein Paket.
            return length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    /// <summary>§9.6: mit Windows starten.</summary>
    public static bool IsAutostartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
    }

    public void SetAutostart(bool enabled, bool startMinimized)
    {
        if (HasPackageIdentity)
        {
            // Im Paket regelt das der StartupTask aus dem Manifest (AP7.4).
            // Der Benutzer kann ihn im Task-Manager abschalten, und die
            // Deinstallation räumt ihn auf — beides kann der HKCU-Weg nicht.
            WindowsIntegrationLog.AutostartFromPackage(logger);
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                // §9.6: „Minimiert im Infobereich starten" — als Argument,
                // damit die App weiss, dass sie sich nicht zeigen soll.
                var command = startMinimized
                    ? $"\"{ExecutablePath}\" --minimized"
                    : $"\"{ExecutablePath}\"";

                key.SetValue(AppName, command);
                WindowsIntegrationLog.AutostartEnabled(logger, startMinimized);
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                WindowsIntegrationLog.AutostartDisabled(logger);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            WindowsIntegrationLog.RegistryDenied(logger, "Autostart", ex.Message);
        }
    }

    /// <summary>
    /// §10: <c>tel:</c>, <c>sip:</c>, <c>callto:</c> registrieren. Damit
    /// erledigt sich Klick-to-Call aus Outlook und aus dem CRM ohne
    /// eigene Schnittstelle.
    /// </summary>
    public void RegisterProtocolHandlers(bool register)
    {
        if (HasPackageIdentity)
        {
            // Im Paket stehen die Handler als Extensions im Manifest. Ein
            // zweiter Eintrag in HKCU erzeugte nur einen zweiten Vorschlag
            // im Dialog „Öffnen mit" — auf denselben Ordner.
            WindowsIntegrationLog.ProtocolsFromPackage(logger);
            return;
        }

        foreach (var protocol in Protocols)
        {
            try
            {
                if (register)
                {
                    RegisterOne(protocol);
                }
                else
                {
                    RemoveOne(protocol);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                WindowsIntegrationLog.RegistryDenied(logger, protocol, ex.Message);
            }
        }

        if (register)
        {
            WindowsIntegrationLog.ProtocolsRegistered(logger, string.Join(", ", Protocols));
        }
        else
        {
            WindowsIntegrationLog.ProtocolsRemoved(logger);
        }
    }

    private static void RegisterOne(string protocol)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}");
        key.SetValue(string.Empty, $"URL:{protocol}");

        // Dieser Wert macht den Schlüssel überhaupt erst zu einem
        // Protokoll-Handler. Ohne ihn ignoriert Windows den Eintrag.
        key.SetValue("URL Protocol", string.Empty);

        using var icon = key.CreateSubKey("DefaultIcon");
        icon.SetValue(string.Empty, $"\"{ExecutablePath}\",0");

        using var command = key.CreateSubKey(@"shell\open\command");

        // §10: „Aktivierung mit URI → direkt wählen, keine Rückfrage."
        command.SetValue(string.Empty, $"\"{ExecutablePath}\" \"%1\"");
    }

    /// <summary>
    /// Entfernt die eigene Protokollregistrierung — und nur die eigene.
    ///
    /// Vorher stand hier ein <c>DeleteSubKeyTree</c> auf
    /// <c>Software\Classes\tel</c>. Das löscht den Schlüssel samt Inhalt,
    /// auch wenn ihn ein anderes Programm angelegt hatte: wer nipp als
    /// Standard abwählt, hätte damit auch die Registrierung von Teams oder
    /// Skype mitgenommen.
    /// </summary>
    private static void RemoveOne(string protocol)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Classes\{protocol}\shell\open\command",
            writable: false);

        if (key?.GetValue(string.Empty) as string is not { } command
            || !command.Contains("Nipp.App", StringComparison.OrdinalIgnoreCase))
        {
            // Fremd oder nicht vorhanden — nicht anfassen.
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{protocol}", throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Ob nipp der eingetragene Handler ist. Für die Anzeige in den
    /// Einstellungen — ein anderes Programm kann den Eintrag übernommen haben.
    /// </summary>
    public static bool AreProtocolHandlersRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\tel\shell\open\command");
        return key?.GetValue(string.Empty) as string is { } command
            && command.Contains("Nipp.App", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zieht aus einer Aktivierungs-URI die Nummer.
    ///
    /// <c>tel:+41445128430</c>, <c>sip:151@pbx.ch</c> und
    /// <c>callto:0791234567</c> kommen alle vor; manche Quellen kodieren
    /// zusätzlich (<c>tel:%2B4144...</c>) oder hängen Parameter an.
    /// </summary>
    public static string? ParseCallUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        var value = uri.Trim();

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var scheme = value[..separator].ToLowerInvariant();
        if (!Protocols.Contains(scheme))
        {
            return null;
        }

        var target = Uri.UnescapeDataString(value[(separator + 1)..]);

        // Fuehrende Schraegstriche weg: manche Quellen schreiben
        // «callto://0791234567» in der Form einer Netzwerkadresse. Ohne diese
        // Zeile blieb «//0791234567» stehen, und gewaehlt wurde nichts
        // Brauchbares. Bei tel: und sip: kommt die Form nicht vor, schadet aber
        // auch dort nicht.
        target = target.TrimStart('/');

        // Parameter abschneiden: tel:+41...;phone-context=... oder
        // sip:151@pbx.ch;transport=udp
        var parameterStart = target.IndexOf(';', StringComparison.Ordinal);
        if (parameterStart >= 0)
        {
            target = target[..parameterStart];
        }

        var queryStart = target.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            target = target[..queryStart];
        }

        return target.Length > 0 ? target : null;
    }

    /// <summary>
    /// Der Pfad, der in Autostart und Protokoll-Handler geschrieben wird.
    ///
    /// <para><b>Nicht einfach die eigene EXE</b> — seit ADR-038 wird nipp mit
    /// Velopack installiert, und dessen Verzeichnis sieht so aus:</para>
    ///
    /// <code>
    /// %LocalAppData%\nipp\
    ///     Nipp.App.exe      &lt;- Stub, bleibt ueber alle Updates gleich
    ///     Update.exe
    ///     current\          &lt;- der Inhalt wird beim Update ersetzt
    ///         Nipp.App.exe  &lt;- AppContext.BaseDirectory zeigt hierher
    /// </code>
    ///
    /// <para>Ein Registrierungseintrag auf <c>current\Nipp.App.exe</c> zeigt
    /// während des Austauschs auf eine Datei, die gerade ersetzt wird, und er
    /// umgeht den Stub, der genau dafür da ist. Deshalb: <b>liegt eine Ebene
    /// höher eine gleichnamige EXE neben einem <c>current</c>-Ordner, ist das
    /// der Stub</b>, und der gehört in die Registrierung.</para>
    ///
    /// <para>Ohne Velopack — also beim Entwickeln aus dem Ausgabeverzeichnis
    /// und in der packaged Fassung — greift die Bedingung nicht, und es bleibt
    /// bei der eigenen EXE. Die Erkennung geht über das Dateisystem und nicht
    /// über eine Velopack-API, weil diese Klasse in jeder Betriebsart läuft,
    /// auch in der, in der es keine Update-Ablage gibt.</para>
    /// </summary>
    public static string ExecutablePath => ResolveExecutablePath(AppContext.BaseDirectory);

    /// <summary>
    /// Die Regel aus <see cref="ExecutablePath"/>, als Funktion des
    /// Verzeichnisses — damit sie prüfbar ist, ohne nipp zu installieren.
    /// </summary>
    public static string ResolveExecutablePath(string baseDirectory)
    {
        const string exeName = "Nipp.App.exe";
        var own = Path.Combine(baseDirectory, exeName);

        var directory = new DirectoryInfo(baseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.Equals(directory.Name, "current", StringComparison.OrdinalIgnoreCase)
            || directory.Parent is not { } parent)
        {
            return own;
        }

        var stub = Path.Combine(parent.FullName, exeName);
        return File.Exists(stub) ? stub : own;
    }
}

internal static partial class WindowsIntegrationLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Autostart eingerichtet (minimiert: {Minimized})")]
    public static partial void AutostartEnabled(ILogger logger, bool minimized);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Autostart entfernt")]
    public static partial void AutostartDisabled(ILogger logger);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Protokolle registriert: {Protocols}")]
    public static partial void ProtocolsRegistered(ILogger logger, string protocols);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Protokoll-Registrierungen entfernt")]
    public static partial void ProtocolsRemoved(ILogger logger);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning,
        Message = "Registrierung fuer {What} nicht moeglich: {Reason}")]
    public static partial void RegistryDenied(ILogger logger, string what, string reason);

    // Jede dieser vier Zeilen nennt, WOFUER das Kuerzel steht.
    //
    // <b>Der Befund vom 13.09.2026, aus einem gewoehnlichen Start.</b> Seit C7
    // meldet nipp zwei Kuerzel an, und im Protokoll stand:
    //
    //   [WRN] Tastenkuerzel Ctrl+Shift+A ist bereits ... belegt
    //   [INF] Tastenkuerzel abgeschaltet
    //
    // Die zweite Zeile gehoerte zum ZWEITEN Kuerzel (dem leeren Stummkuerzel)
    // und las sich wie die Folge der ersten: «es war belegt, also hat nipp es
    // abgeschaltet». Genau der Fehlertyp, den dieses Projekt schon beim
    // Rufton teuer bezahlt hat — eine Protokollzeile, die eine Ursache
    // nahelegt, die sie nicht gemessen hat, ist schlechter als keine.
    [LoggerMessage(EventId = 3010, Level = LogLevel.Information,
        Message = "Tastenkuerzel {Hotkey} ist aktiv ({Rolle})")]
    public static partial void HotkeyRegistered(ILogger logger, string hotkey, string rolle);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information,
        Message = "Kein Tastenkuerzel fuer {Rolle}")]
    public static partial void HotkeyDisabled(ILogger logger, string rolle);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning,
        Message = "Tastenkuerzel {Hotkey} ({Rolle}) laesst sich nicht deuten. "
            + "Erwartet wird etwas wie Ctrl+Shift+A.")]
    public static partial void HotkeyUnreadable(ILogger logger, string hotkey, string rolle);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning,
        Message = "Tastenkuerzel {Hotkey} ({Rolle}) ist bereits von einer anderen Anwendung "
            + "belegt (Windows-Fehler {ErrorCode}). Ein anderes waehlen.")]
    public static partial void HotkeyTaken(ILogger logger, string hotkey, string rolle, int errorCode);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Warning,
        Message = "Der Thread des Tastenkuerzels ist nach zwei Sekunden noch nicht beendet. "
            + "Seine Fensterprozedur bleibt deshalb bis zum Prozessende bestehen.")]
    public static partial void HotkeyThreadStillRunning(ILogger logger);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Warning,
        Message = "Nachrichtenfenster fuer das Tastenkuerzel liess sich nicht anlegen: {Reason}")]
    public static partial void HotkeyWindowFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Debug,
        Message = "Autostart regelt der StartupTask des Pakets — kein Eintrag in der Registrierung")]
    public static partial void AutostartFromPackage(ILogger logger);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Debug,
        Message = "Protokoll-Handler kommen aus dem Paketmanifest — kein Eintrag in der Registrierung")]
    public static partial void ProtocolsFromPackage(ILogger logger);

    // W2.4: die Einzelinstanz ohne Paketidentitaet. Alle vier Zeilen auf
    // Information oder Warnung, weil «warum laeuft nipp zweimal?» und «warum
    // tut der tel:-Klick nichts?» beide hier beantwortet werden.
    [LoggerMessage(EventId = 3380, Level = LogLevel.Information,
        Message = "Eine Instanz laeuft bereits — Kommandozeile weitergereicht, dieser Start "
            + "beendet sich")]
    public static partial void SingleInstanceForwarded(ILogger logger);

    [LoggerMessage(EventId = 3381, Level = LogLevel.Warning,
        Message = "Die laufende Instanz nimmt nichts entgegen ({ExceptionType}). Dieser Start "
            + "beendet sich trotzdem; eine mitgegebene Nummer geht dabei verloren.")]
    public static partial void SingleInstanceNotForwarded(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 3382, Level = LogLevel.Warning,
        Message = "Die vorige Instanz hat sich nicht sauber beendet. nipp uebernimmt.")]
    public static partial void SingleInstanceTookOver(ILogger logger);

    [LoggerMessage(EventId = 3383, Level = LogLevel.Warning,
        Message = "Die Einzelinstanz-Sperre steht nicht ({ExceptionType}: {Reason}). nipp startet "
            + "trotzdem — es kann dann mehrfach laufen.")]
    public static partial void SingleInstanceFailed(ILogger logger, string exceptionType, string reason);

    [LoggerMessage(EventId = 3384, Level = LogLevel.Warning,
        Message = "Der Lauscher der Einzelinstanz hatte einen Fehler ({ExceptionType}) und "
            + "laeuft weiter")]
    public static partial void SingleInstanceListenerFailed(ILogger logger, string exceptionType);
}
