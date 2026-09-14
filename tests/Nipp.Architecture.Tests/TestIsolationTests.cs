using System.Reflection;
using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt, dass kein Test auf dem Benutzerprofil arbeitet.
///
/// <b>Warum es diesen Test gibt.</b> Am 05.09.2026 hat ein gewöhnliches
/// <c>dotnet test</c> das eingerichtete SIP-Konto des angemeldeten Benutzers
/// samt Passwort gelöscht. Die Ursache war unspektakulär und stand sogar als
/// bewusste Entscheidung im Kommentar: die Einstellungstests liefen „gegen die
/// echten Pfade unter %APPDATA% und %LOCALAPPDATA% — deshalb räumen sie vorher
/// und nachher auf". Das Aufräumen war das Problem. Und der Test „eine kaputte
/// Datei kostet nicht den Start" schreibt eine kaputte Datei genau dorthin, wo
/// die echte liegt.
///
/// Ein Test darf Dinge kaputtmachen. Er darf nur nicht die Dinge des Benutzers
/// kaputtmachen. Deshalb: keine <c>SpecialFolder</c> im Testcode; wer eine
/// Ablage braucht, nimmt <c>Path.GetTempPath()</c>.
/// </summary>
public sealed class TestIsolationTests
{
    /// <summary>
    /// Die Ordner, die dem angemeldeten Benutzer gehören. <c>ApplicationData</c>
    /// ist <c>%APPDATA%</c>, <c>LocalApplicationData</c> ist
    /// <c>%LOCALAPPDATA%</c> — dort liegen Konfiguration, Zugangsdaten,
    /// Anrufliste und Aufnahmen von nipp.
    /// </summary>
    private static readonly Regex UserProfileFolder = new(
        @"SpecialFolder\.(ApplicationData|LocalApplicationData|CommonApplicationData|UserProfile|MyDocuments|Desktop)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Umgebungsvariablen, die auf dasselbe zeigen — der zweite Weg dorthin.
    /// </summary>
    private static readonly Regex UserProfileVariable = new(
        @"%(APPDATA|LOCALAPPDATA|USERPROFILE|PROGRAMDATA)%",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Kein_Test_greift_auf_das_Benutzerprofil_zu()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateTestSources())
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Kommentare dürfen die Pfade benennen — diese Datei tut es
                // selbst, und die Erklärung ist der halbe Wert der Regel.
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal)
                    || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (UserProfileFolder.IsMatch(line) || UserProfileVariable.IsMatch(line))
                {
                    violations.Add($"{Relative(file)}:{i + 1}  {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Diese Teststellen greifen auf Ordner des angemeldeten Benutzers zu.
             Ein Testlauf hat auf diesem Weg schon einmal ein eingerichtetes SIP-Konto
             samt Passwort gelöscht.

             Gefunden ({violations.Count}):

             {string.Join(Environment.NewLine, violations)}

             Auflösung: Path.GetTempPath() mit einem eigenen Unterverzeichnis je Test
             verwenden und den Ablageort über den Konstruktor übergeben — SettingsService
             und SecretStore nehmen dafür einen optionalen Pfad entgegen.
             """);
    }

    [Fact]
    public void Der_Scan_findet_ueberhaupt_Testdateien()
    {
        var count = EnumerateTestSources().Count();

        Assert.True(count > 0, $"Keine Testquelldateien gefunden. Erwartet unter '{TestRoot}'.");
    }

    private static IEnumerable<string> EnumerateTestSources() =>
        Directory.EnumerateFiles(TestRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) => Path.GetRelativePath(TestRoot, path);

    /// <summary>
    /// Das <c>tests</c>-Verzeichnis — es liegt neben <c>src</c>, dessen Pfad
    /// die csproj bereits einbettet.
    /// </summary>
    private static string TestRoot { get; } = ResolveTestRoot();

    private static string ResolveTestRoot()
    {
        var sourceRoot = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositorySourceRoot")
            ?.Value;

        var repository = sourceRoot is { Length: > 0 }
            ? Path.GetFullPath(Path.Combine(sourceRoot, ".."))
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        return Path.Combine(repository, "tests");
    }
}
