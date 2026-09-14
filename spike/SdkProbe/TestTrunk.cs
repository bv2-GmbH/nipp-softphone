using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdkProbe;

/// <summary>
/// Zugangsdaten des Test-Trunks für AP2.3.
///
/// Die Datei liegt unter <c>%LOCALAPPDATA%\nipp\test-trunk.json</c> — bewusst
/// ausserhalb des Repos, damit sie nicht versehentlich eingecheckt werden kann.
/// Vorlage: <c>spike/SdkProbe/test-trunk.template.json</c>.
///
/// §13: nur der Test-Trunk der Test-PBX, nie ein Kundentenant.
/// </summary>
internal sealed record TestTrunk
{
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "nipp Testgerät";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("authUserId")] public string AuthUserId { get; init; } = "";
    [JsonPropertyName("password")] public string Password { get; init; } = "";
    [JsonPropertyName("domain")] public string Domain { get; init; } = "";
    [JsonPropertyName("transport")] public string Transport { get; init; } = "Tls";
    [JsonPropertyName("outboundProxy")] public string OutboundProxy { get; init; } = "";
    [JsonPropertyName("expires")] public int Expires { get; init; } = 600;
    [JsonPropertyName("echoTarget")] public string EchoTarget { get; init; } = "";
    [JsonPropertyName("rootCaFile")] public string RootCaFile { get; init; } = "";

    /// <summary>Die Auth-ID fällt auf den Benutzernamen zurück (§9.1).</summary>
    public string EffectiveAuthUserId =>
        string.IsNullOrWhiteSpace(AuthUserId) ? Username : AuthUserId;

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "test-trunk.json");

    /// <summary>
    /// Lädt die Zugangsdaten. Gibt <c>null</c> zurück, wenn die Datei fehlt
    /// oder unvollständig ist — mit einer Meldung, die sagt, was zu tun ist
    /// (§15: Fehlermeldungen nennen die Abhilfe).
    /// </summary>
    public static TestTrunk? TryLoad(out string problem)
    {
        var path = ConfigPath;

        if (!File.Exists(path))
        {
            problem = $"""
                       Keine Zugangsdaten gefunden.

                       Erwartet:  {path}
                       Vorlage:   spike\SdkProbe\test-trunk.template.json

                       So einrichten:
                         1. Vorlage nach obigen Pfad kopieren
                         2. username, password, domain und echoTarget ausfuellen
                         3. Spike erneut starten

                       Nur der Test-Trunk der Test-PBX — nie ein Kundentenant (Paragraph 13).
                       """;
            return null;
        }

        TestTrunk? trunk;
        try
        {
            trunk = JsonSerializer.Deserialize<TestTrunk>(
                File.ReadAllText(path),
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            problem = $"'{path}' ist kein gueltiges JSON: {ex.Message}";
            return null;
        }

        if (trunk is null)
        {
            problem = $"'{path}' ist leer.";
            return null;
        }

        var fehlend = new List<string>();
        if (string.IsNullOrWhiteSpace(trunk.Username)) fehlend.Add("username");
        if (string.IsNullOrWhiteSpace(trunk.Password)) fehlend.Add("password");
        if (string.IsNullOrWhiteSpace(trunk.Domain)) fehlend.Add("domain");

        if (fehlend.Count > 0)
        {
            problem = $"In '{path}' fehlen noch: {string.Join(", ", fehlend)}.";
            return null;
        }

        problem = "";
        return trunk;
    }

    /// <summary>
    /// Fasst die Konfiguration zusammen — <b>ohne das Passwort</b>. Diese
    /// Methode ist die einzige erlaubte Art, den Trunk auszugeben; §9.6 verlangt
    /// dasselbe fuer das Diagnosepaket.
    /// </summary>
    public string ToSafeString() =>
        $"{Username}@{Domain} über {Transport}, Auth-ID '{EffectiveAuthUserId}', "
        + $"Expires {Expires} s, Echo-Ziel '{(string.IsNullOrWhiteSpace(EchoTarget) ? "keines" : EchoTarget)}'"
        + (string.IsNullOrWhiteSpace(OutboundProxy) ? "" : $", Proxy {OutboundProxy}");
}
