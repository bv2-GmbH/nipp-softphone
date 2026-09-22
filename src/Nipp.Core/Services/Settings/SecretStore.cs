using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Ablage für Passwörter (AP5.3, §10, §11).
///
/// §10 ist eindeutig: „DPAPI (<c>ProtectedData</c>, CurrentUser-Scope), Datei
/// unter <c>%LOCALAPPDATA%</c>; <b>niemals Klartext in linphonerc</b> — wenn
/// möglich HA1 statt Passwort ablegen."
///
/// <b>Was DPAPI leistet und was nicht.</b> Die Daten sind an das
/// Windows-Benutzerkonto gebunden: ein anderer Benutzer auf demselben Rechner
/// kann sie nicht entschlüsseln, und auf einen anderen Rechner kopiert sind
/// sie wertlos. Wer aber im Kontext dieses Benutzers Code ausführt, kommt
/// heran — DPAPI schützt vor Diebstahl der Datei, nicht vor Schadsoftware im
/// eigenen Benutzerkontext. Das ist für ein Softphone-Passwort angemessen und
/// sollte nicht als mehr verkauft werden.
/// </summary>
public sealed class SecretStore(ILogger<SecretStore> logger, string? path = null)
{
    /// <summary>
    /// Zusätzliche Entropie. Bindet die Daten an nipp: eine mit einem anderen
    /// Programm im selben Benutzerkontext erzeugte DPAPI-Datei lässt sich
    /// damit nicht unterschieben.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("nipp.secrets.v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>§10: Zugangsdaten unter %LOCALAPPDATA%, nicht bei der Konfiguration.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "secrets.dat");

    /// <summary>
    /// Die Datei, mit der diese Instanz arbeitet.
    ///
    /// Über den Konstruktor überschreibbar, damit Tests nicht auf den
    /// Zugangsdaten des angemeldeten Benutzers arbeiten. Das ist keine
    /// Bequemlichkeit: solange der Pfad fest verdrahtet war, hat ein
    /// gewöhnliches <c>dotnet test</c> ein eingerichtetes SIP-Konto samt
    /// Passwort gelöscht.
    /// </summary>
    public string StorePath { get; } = path ?? DefaultPath;

    /// <summary>
    /// Legt ein Geheimnis ab. <paramref name="key"/> ist üblicherweise die
    /// SIP-Identität, damit mehrere Konten nebeneinander bestehen (§9.1).
    /// </summary>
    public void Set(string key, string secret)
    {
        var all = ReadAll();

        // <b>Dasselbe wie in Remove, das es seit jeher richtig macht</b>
        // (Befund A1-22): ein Ablegen, das nichts aendert, ist keines. Am
        // 22.09.2026 stand «Zugangsdaten abgelegt» 787-mal an einem Tag im
        // Protokoll, fast immer mit demselben Inhalt — jedes Mal wurde die
        // verschluesselte Datei neu geschrieben. Ein Absturz mitten darin
        // traefe die Passwoerter, und der Anlass waere ein Kontoereignis
        // gewesen, das mit ihnen nichts zu tun hat.
        if (all.TryGetValue(key, out var bisher)
            && string.Equals(bisher, secret, StringComparison.Ordinal))
        {
            return;
        }

        all[key] = secret;
        WriteAll(all);
    }

    /// <summary>Liest ein Geheimnis, oder <c>null</c>, wenn keines abgelegt ist.</summary>
    public string? Get(string key) =>
        ReadAll().TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Entfernt ein Geheimnis. §9.1: beim Löschen eines Kontos geht die
    /// <c>AuthInfo</c> mit — „sonst bleiben Zugangsdaten verwaist liegen".
    /// </summary>
    public void Remove(string key)
    {
        var all = ReadAll();
        if (all.Remove(key))
        {
            WriteAll(all);
            SecretLog.Removed(logger, key);
        }
    }

    /// <summary>Löscht alles. Für das Zurücksetzen und für Tests.</summary>
    public void Clear()
    {
        var path = StorePath;
        if (File.Exists(path))
        {
            File.Delete(path);
            SecretLog.Cleared(logger);
        }
    }

    /// <summary>
    /// Berechnet den HA1-Hash. §10: „wenn möglich HA1 statt Passwort ablegen."
    ///
    /// Das SDK bietet dafür <c>Factory.ComputeHa1ForAlgorithm</c> an — diese
    /// Methode hier ist die SDK-freie Entsprechung, damit
    /// <c>Nipp.Core.Services.Settings</c> nicht in die Telefonieschicht
    /// greifen muss (§6).
    ///
    /// <b>Warum HA1 trotzdem nicht überall geht:</b> er ist an Benutzer, Realm
    /// und Passwort gebunden. Kennt man den Realm der Anlage nicht im Voraus —
    /// und beim ersten Anmelden kennt man ihn nicht —, führt kein Weg am
    /// Passwort vorbei. Deshalb bleibt beides möglich.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification =
            "MD5 ist bei HTTP-Digest und damit bei SIP fuer den HA1-Hash vorgeschrieben "
            + "(RFC 2617, RFC 3261). Das ist keine Wahl zwischen Algorithmen, sondern das "
            + "Protokoll: ein SHA-Hash waere hier schlicht falsch und die Anlage wuerde die "
            + "Anmeldung ablehnen. Der Wert schuetzt auch nichts, was MD5 schuetzen muesste — "
            + "er ersetzt lediglich das Klartextpasswort in der Ablage (Paragraph 10).")]
    public static string ComputeHa1(string username, string realm, string password)
    {
        var input = Encoding.UTF8.GetBytes($"{username}:{realm}:{password}");
        var hash = MD5.HashData(input);

        // ToHexStringLower gibt es erst ab .NET 9; Paragraph 4 legt .NET 8 LTS fest.
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private Dictionary<string, string> ReadAll()
    {
        var path = StorePath;

        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain, JsonOptions) ?? [];
        }
        catch (CryptographicException ex)
        {
            // Häufigster Fall: die Datei stammt von einem anderen
            // Benutzerkonto oder Rechner. Das ist kein Defekt, sondern genau
            // die Wirkung von DPAPI — die Zugangsdaten müssen dann neu
            // eingegeben werden.
            SecretLog.NotDecryptable(logger, path, ex.Message);
            return [];
        }
        catch (JsonException ex)
        {
            SecretLog.Corrupt(logger, path, ex.Message);
            return [];
        }
    }

    private void WriteAll(Dictionary<string, string> all)
    {
        var path = StorePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var plain = JsonSerializer.SerializeToUtf8Bytes(all, JsonOptions);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

        // Erst in eine Nebendatei, dann umbenennen: ein Absturz mitten im
        // Schreiben darf nicht alle Zugangsdaten vernichten.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, path, overwrite: true);

        SecretLog.Stored(logger, all.Count);
    }
}

internal static partial class SecretLog
{
    // Die Schlüssel sind SIP-Identitäten, keine Geheimnisse — sie dürfen ins
    // Log. Die Werte niemals.
    [LoggerMessage(EventId = 2400, Level = LogLevel.Debug,
        Message = "Zugangsdaten abgelegt ({Count} Eintraege)")]
    public static partial void Stored(ILogger logger, int count);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Information,
        Message = "Zugangsdaten fuer {Key} entfernt")]
    public static partial void Removed(ILogger logger, string key);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Information,
        Message = "Alle Zugangsdaten geloescht")]
    public static partial void Cleared(ILogger logger);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Warning,
        Message = "Zugangsdaten in {Path} lassen sich nicht entschluesseln: {Reason}. "
            + "Das passiert, wenn die Datei von einem anderen Benutzerkonto oder Rechner stammt — "
            + "die Zugangsdaten muessen neu eingegeben werden.")]
    public static partial void NotDecryptable(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Error,
        Message = "Zugangsdaten in {Path} sind beschaedigt: {Reason}")]
    public static partial void Corrupt(ILogger logger, string path, string reason);
}
