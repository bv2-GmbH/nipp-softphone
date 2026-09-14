using System.Reflection;
using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt die Schichtgrenze aus NIPP-BUILD.md §6: das Linphone SDK ist
/// ausschliesslich in <c>Services/Telephony/</c> bekannt. Das ist die Stelle,
/// an der sich später ein SDK-Wechsel oder ein Mock aufhängen lässt — sie ist
/// nur so viel wert, wie sie überwacht wird.
///
/// Der Test ist ein Quelltext-Scan, in §13 ausdrücklich zugelassen. Vorteil
/// gegenüber Reflection: er greift auch, solange das SDK noch nicht eingebunden
/// ist, und er nennt Datei und Zeile statt nur einen Typnamen.
/// </summary>
public sealed partial class SdkBoundaryTests
{
    /// <summary>Der einzige Ort, an dem das SDK bekannt sein darf (§6).</summary>
    private const string AllowedDirectory = @"Services\Telephony";

    /// <summary>Generierter Wrapper des SDK — fremder Code, kein Verstoss.</summary>
    private static readonly string[] ExemptFileNames = ["LinphoneWrapper.cs"];

    [Fact]
    public void Das_Sdk_wird_nur_unter_Services_Telephony_verwendet()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            if (IsInsideAllowedDirectory(file) || IsExempt(file))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (ReferencesSdk(lines[i]))
                {
                    violations.Add($"{Relative(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Das Linphone SDK darf laut NIPP-BUILD.md §6 nur unter '{AllowedDirectory}' verwendet werden.
             Gefundene Verstösse ({violations.Count}):

             {string.Join(Environment.NewLine, violations)}

             Auflösung: den SDK-Zugriff hinter ISipService legen und einen eigenen
             Modelltyp aus Services/Telephony/Model/ zurückgeben. Kein Linphone.Call
             verlässt den Service.
             """);
    }

    [Fact]
    public void ViewModels_kennen_keine_Sdk_Typen()
    {
        // §6: "Kein ViewModel importiert Linphone direkt." Der erste Test deckt das
        // mit ab, aber ein eigener Test benennt den Bruch klarer, wenn er auftritt.
        var violations = EnumerateSourceFiles()
            .Where(f => f.Contains(@"\ViewModels\", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllLines(f).Any(ReferencesSdk))
            .Select(Relative)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "ViewModels dürfen keine SDK-Typen kennen, nur eigene Modelle (§6). Betroffen: "
                + string.Join(", ", violations));
    }

    [Fact]
    public void Der_Quelltextscan_findet_ueberhaupt_Dateien()
    {
        // Ohne diesen Test wäre ein leerer Scan grün — und die Schichtgrenze
        // damit unbewacht, ohne dass es auffällt. Der Test schlägt fehl, wenn
        // der Pfad zum src-Verzeichnis nicht mehr stimmt.
        var count = EnumerateSourceFiles().Count();

        Assert.True(
            count > 0,
            $"Der Architekturtest hat keine Quelldateien gefunden. Erwartet unter '{SourceRoot}'. "
                + "Vermutlich stimmt die Property RepositorySourceRoot in der csproj nicht mehr.");
    }

    private static bool ReferencesSdk(string line)
    {
        var trimmed = line.TrimStart();

        // Kommentare zählen nicht: ein Verweis auf das SDK in einer Erklärung
        // ist kein Verstoss, und die Dokumentation soll das SDK benennen dürfen.
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith('*')
            || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return false;
        }

        return trimmed.StartsWith("using Linphone;", StringComparison.Ordinal)
            || trimmed.StartsWith("using Linphone.", StringComparison.Ordinal)
            || trimmed.StartsWith("using static Linphone.", StringComparison.Ordinal)
            || trimmed.Contains("global::Linphone.", StringComparison.Ordinal)

            // Ein voll qualifizierter Verweis ohne using — „Linphone.Core x" —
            // kam vorher durch. Das ist genau die Umgehung, die der Test
            // verhindern soll.
            || QualifiedSdkType().IsMatch(trimmed);
    }

    /// <summary>
    /// <c>Linphone.</c> gefolgt von einem Grossbuchstaben, aber nicht als Teil
    /// eines laengeren Bezeichners (etwa <c>LinphoneWrapper.</c>).
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_])Linphone\.[A-Z]")]
    private static partial Regex QualifiedSdkType();

    private static bool IsInsideAllowedDirectory(string path) =>
        path.Contains(AllowedDirectory, StringComparison.OrdinalIgnoreCase);

    private static bool IsExempt(string path) =>
        ExemptFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateSourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) =>
        Path.GetRelativePath(SourceRoot, path);

    private static string SourceRoot { get; } = ResolveSourceRoot();

    private static string ResolveSourceRoot()
    {
        var configured = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositorySourceRoot")
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
