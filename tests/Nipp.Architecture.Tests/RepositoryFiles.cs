using System.Reflection;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Wo der Quelltext liegt, und welche Dateien ein Architekturtest ansehen darf.
///
/// <b>Warum das hier steht.</b> Jeder Quelltext-Scan braucht dieselben zwei
/// Dinge: den Pfad zu <c>src/</c> und eine Aufzählung, die generierte Dateien
/// und Ausgabeordner überspringt. Die bestehenden Tests lösen das je für sich;
/// diese Klasse ist der gemeinsame Ort für alles Neue. Die älteren Tests
/// darauf umzustellen wäre Aufräumarbeit ohne Verhaltensänderung und gehört
/// nicht in denselben Durchgang.
///
/// Der Pfad kommt aus den Assembly-Metadaten und damit von MSBuild — zur
/// Laufzeit vom Testverzeichnis aus nach oben zu raten bricht, sobald jemand
/// den Ausgabepfad ändert.
/// </summary>
internal static class RepositoryFiles
{
    /// <summary>Das <c>src</c>-Verzeichnis des Repos.</summary>
    public static string SourceRoot { get; } = ResolveSourceRoot();

    /// <summary>
    /// Alle selbst geschriebenen C#-Dateien unterhalb eines Ordners.
    ///
    /// Übersprungen werden <c>bin</c>, <c>obj</c> und die vom XAML-Compiler
    /// erzeugten Dateien — sie gehören niemandem und sind kein Verstoss.
    /// </summary>
    public static IEnumerable<string> EnumerateSources(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(static f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
                .Where(static f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
                .Where(static f => !f.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                .Where(static f => !f.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
            : [];

    /// <summary>Der Pfad relativ zu <see cref="SourceRoot"/>, für lesbare Fehlermeldungen.</summary>
    public static string Relative(string path) => Path.GetRelativePath(SourceRoot, path);

    /// <summary>
    /// Ob eine Zeile Programmtext ist und kein Kommentar. Ein Verweis in einer
    /// Erklärung ist kein Verstoss — die Dokumentation soll benennen dürfen,
    /// wovon sie handelt.
    /// </summary>
    public static bool IsCode(string line)
    {
        var trimmed = line.TrimStart();

        return !trimmed.StartsWith("//", StringComparison.Ordinal)
            && !trimmed.StartsWith('*')
            && !trimmed.StartsWith("/*", StringComparison.Ordinal);
    }

    private static string ResolveSourceRoot()
    {
        var configured = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static a => a.Key == "RepositorySourceRoot")
            ?.Value;

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "Die Assembly-Metadaten enthalten kein RepositorySourceRoot. "
                    + "Erwartet aus Nipp.Architecture.Tests.csproj.");
        }

        return Path.GetFullPath(configured);
    }
}
