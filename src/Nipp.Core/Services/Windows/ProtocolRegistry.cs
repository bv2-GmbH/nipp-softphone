using Microsoft.Win32;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Die Naht zur Registrierung (HKCU) — damit prüfbar ist, <b>was</b> nipp
/// einträgt, ohne dass ein Test die Registrierung des Entwicklers anfasst.
/// Ein Wert ohne Namen (<c>null</c>) ist der Standardwert des Schlüssels.
/// </summary>
public interface IRegistryStore
{
    void SetValue(string keyPath, string? name, string value);

    string? GetValue(string keyPath, string? name);

    void DeleteValue(string keyPath, string name);

    void DeleteKeyTree(string keyPath);
}

/// <summary>Die echte Fassung: <c>HKEY_CURRENT_USER</c>, keine Administratorrechte.</summary>
public sealed class CurrentUserRegistry : IRegistryStore
{
    public void SetValue(string keyPath, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(name ?? string.Empty, value);
    }

    public string? GetValue(string keyPath, string? name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(name ?? string.Empty) as string;
    }

    public void DeleteValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKeyTree(string keyPath)
        => Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}

/// <summary>
/// Wie sich nipp bei Windows für <c>tel</c>, <c>sip</c>, <c>sips</c> und
/// <c>callto</c> anmeldet (ADR-082).
///
/// <para><b>Zwei Schichten, und keine ersetzt die andere.</b> Die nackten
/// Schlüssel unter <c>Software\Classes\&lt;protokoll&gt;</c> sind der Rückfallweg
/// und waren bis 0.9.16 alles. Gemessen am 26.11.2026 reichen sie nicht: ein
/// Programm, das die Protokolle per Paketmanifest beansprucht (Teams), gewinnt
/// ohne <c>UserChoice</c>, und nipp taucht in «Standard-Apps» und in «App
/// auswählen» gar nicht auf. Dafür braucht es das Standardmuster einer
/// <b>Anwendung mit Fähigkeiten</b>: <c>RegisteredApplications</c> verweist auf
/// <c>Capabilities</c>, deren <c>URLAssociations</c> auf eine ProgID zeigen.</para>
///
/// <para><b>Was hier nie geschrieben wird: <c>UserChoice</c>.</b> Windows
/// schützt den Schlüssel mit einem Hash, und die Wahl der Standard-App gehört
/// dem Benutzer oder der IT, nicht dem Programm.</para>
/// </summary>
public sealed class ProtocolRegistry(IRegistryStore store, string executablePath)
{
    /// <summary>Die Protokolle aus §10.</summary>
    public static readonly IReadOnlyList<string> Protocols = ["tel", "sip", "sips", "callto"];

    /// <summary>Die gemeinsame ProgID für alle vier Protokolle.</summary>
    public const string ProgId = "nipp.Url";

    /// <summary>Der Wert unter <c>RegisteredApplications</c>.</summary>
    public const string RegisteredName = "nipp";

    /// <summary>
    /// Der Herstellerordner <c>bv2</c> wie bei den Daten (ADR-079) — nicht
    /// <c>Software\nipp</c>, das mit dem Velopack-Namen verwechselbar wäre.
    /// </summary>
    public const string CapabilitiesPath = @"Software\bv2\nipp\Capabilities";

    public const string RegisteredApplicationsPath = @"Software\RegisteredApplications";

    private static string ClassesPath(string name) => $@"Software\Classes\{name}";

    /// <summary>Schreibt beide Schichten. Wirft bei verweigertem Zugriff.</summary>
    public void Register()
    {
        foreach (var protocol in Protocols)
        {
            RegisterBare(protocol);
        }

        RegisterAsApplication();
    }

    /// <summary>Entfernt nur, was nipp gehört — Fremdes bleibt stehen.</summary>
    public void Remove()
    {
        foreach (var protocol in Protocols)
        {
            RemoveBare(protocol);
        }

        RemoveApplication();
    }

    private string Command => $"\"{executablePath}\" \"%1\"";

    private void RegisterBare(string protocol)
    {
        var path = ClassesPath(protocol);
        store.SetValue(path, null, $"URL:{protocol}");

        // Dieser Wert macht den Schlüssel überhaupt erst zu einem
        // Protokoll-Handler. Ohne ihn ignoriert Windows den Eintrag.
        store.SetValue(path, "URL Protocol", string.Empty);
        store.SetValue($@"{path}\DefaultIcon", null, $"\"{executablePath}\",0");

        // §10: «Aktivierung mit URI → direkt wählen, keine Rückfrage.»
        store.SetValue($@"{path}\shell\open\command", null, Command);
    }

    private void RegisterAsApplication()
    {
        // Die ProgID, auf die UserChoice später zeigt, wenn der Benutzer nipp wählt.
        var progId = ClassesPath(ProgId);
        store.SetValue(progId, null, "URL:nipp");
        store.SetValue(progId, "URL Protocol", string.Empty);
        store.SetValue($@"{progId}\Application", "ApplicationName", "nipp");
        store.SetValue($@"{progId}\DefaultIcon", null, $"\"{executablePath}\",0");
        store.SetValue($@"{progId}\shell\open\command", null, Command);

        // Die Fähigkeiten: das ist, was «Standard-Apps» und «App auswählen» lesen.
        store.SetValue(CapabilitiesPath, "ApplicationName", "nipp");
        store.SetValue(
            CapabilitiesPath,
            "ApplicationDescription",
            "Softphone: wählt tel:-, sip:-, sips:- und callto:-Links.");

        foreach (var protocol in Protocols)
        {
            store.SetValue($@"{CapabilitiesPath}\URLAssociations", protocol, ProgId);
        }

        store.SetValue(RegisteredApplicationsPath, RegisteredName, CapabilitiesPath);
    }

    private void RemoveBare(string protocol)
    {
        // Vorher stand hier ein DeleteSubKeyTree auf Software\Classes\tel ohne
        // Prüfung: wer nipp abwählt, nähme sonst auch die Registrierung eines
        // anderen Programms mit. Eigen ist, was nach nipp aussieht.
        if (store.GetValue($@"{ClassesPath(protocol)}\shell\open\command", null) is not { } command
            || !command.Contains("Nipp.App", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        store.DeleteKeyTree(ClassesPath(protocol));
    }

    private void RemoveApplication()
    {
        store.DeleteKeyTree(ClassesPath(ProgId));
        store.DeleteKeyTree(CapabilitiesPath);

        // Nur der Wert «nipp» — die Liste gehört allen Programmen. Und nur, wenn
        // er auf unsere Fähigkeiten zeigt.
        if (string.Equals(
                store.GetValue(RegisteredApplicationsPath, RegisteredName),
                CapabilitiesPath,
                StringComparison.OrdinalIgnoreCase))
        {
            store.DeleteValue(RegisteredApplicationsPath, RegisteredName);
        }
    }
}
