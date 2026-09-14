using System.Reflection;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Wer nipp ist und welche Fassung läuft — für den Info-Bereich der
/// Einstellungen (§9, §16.2).
///
/// <para><b>Warum es das braucht.</b> Zur Version gab es bis zum 07.09.2026
/// keinen einzigen Laufzeitcode; die einzige Quelle war
/// <c>VersionPrefix</c> in <c>Directory.Build.props</c>. Im Info-Bereich stand
/// eine Zeile Text: „nipp · bv2 GmbH". Wer bei einem Support-Fall nach der
/// Version gefragt wurde, konnte sie nicht nennen.</para>
///
/// <para><b>Nicht über <c>Package.Current</c>.</b> Das wirft ohne
/// Paketidentität, und der Alltag läuft unpackaged (ADR-008) — eine Ausnahme
/// als Normalfall verdeckt echte Fehler. Die Version kommt deshalb aus den
/// Assembly-Attributen, die das SDK aus <c>VersionPrefix</c> ohnehin erzeugt;
/// das funktioniert in beiden Fassungen. Ob ein Paket dahintersteht, sagt
/// <see cref="WindowsIntegration.HasPackageIdentity"/> über die Win32-API.</para>
///
/// <para><b>Und warum die Buildart mit dasteht.</b> Sie hat zweimal Zeit
/// gekostet: packaged bekommt keine Toasts (<c>Register()</c> scheitert mit
/// <c>E_FAIL</c>, weil im Manifest die Toast-Erweiterung fehlt), und ein
/// Wechsel des Startwegs sah dadurch wie ein Rückschritt im Code aus. Wer den
/// Info-Bereich aufschlägt, sieht jetzt sofort, welche Fassung vor ihm steht.
/// </para>
/// </summary>
public static class AppInfo
{
    /// <summary>Der Name, wie er in der Oberfläche steht.</summary>
    public const string ProductName = "nipp";

    /// <summary>Der Hersteller.</summary>
    public const string Company = "bv2 GmbH";

    /// <summary>Wohin man sich wendet.</summary>
    public const string ContactMail = "kontakt@bv2.ch";

    /// <summary>Die Website.</summary>
    public const string Website = "https://www.bv2.ch";

    /// <summary>Die Fassung, etwa <c>0.1.0</c>.</summary>
    public static string Version { get; } = VersionOf(
        Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly);

    /// <summary>Ob nipp aus einem MSIX-Paket läuft.</summary>
    public static bool IsPackaged => WindowsIntegration.HasPackageIdentity;

    /// <summary>„nipp 0.1.0 (unpackaged)" — eine Zeile, die man vorlesen kann.</summary>
    public static string VersionLine =>
        $"{ProductName} {Version} ({(IsPackaged ? "packaged" : "unpackaged")})";

    /// <summary>Die Zeile mit dem Urheberrecht.</summary>
    public static string Copyright => $"© {DateTime.Now.Year} {Company}";

    /// <summary>
    /// Die Fassung einer Assembly.
    ///
    /// <para>Bevorzugt <c>AssemblyInformationalVersion</c> — das ist der Wert
    /// aus <c>VersionPrefix</c> und damit die Zahl, die im Projekt gepflegt
    /// wird. Ein Suffix aus dem Quellstand („0.1.0+3f2a1c") wird abgeschnitten:
    /// im Info-Bereich soll eine Versionsnummer stehen, keine Commit-Kennung.
    /// </para>
    ///
    /// <para>Als Parameter, damit die Regel prüfbar ist: bei einem Testlauf
    /// wäre <c>GetEntryAssembly</c> der Testrunner.</para>
    /// </summary>
    public static string VersionOf(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);

            return plus > 0 ? informational[..plus] : informational;
        }

        // Ohne das Attribut bleibt die dreiteilige Assembly-Version. Ganz ohne
        // Angabe stünde im Info-Bereich nichts, und das sähe nach einem Fehler
        // aus, wo nur eine Angabe fehlt.
        return assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "unbekannt";
    }
}
