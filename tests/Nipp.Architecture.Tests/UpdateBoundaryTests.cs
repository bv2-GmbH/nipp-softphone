namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt die Grenze der Update-Verteilung (ADR-038, ADR-039).
///
/// <para>Dieselbe Überlegung wie bei <see cref="SdkBoundaryTests"/> und
/// <see cref="IntegrationBoundaryTests"/>: <b>die Update-Quelle wandert.</b>
/// Heute ein privates GitHub-Repo mit Token, nach dem Öffentlichmachen
/// dasselbe ohne, für eine Kundenverteilung womöglich ein Webserver bei bv2
/// (docs/plans/RELEASE-PLAN.md R7 und R10). Eine Bibliothek, die überall im Kern steht,
/// lässt sich nicht tauschen — eine, die hinter <c>IUpdateGateway</c> liegt,
/// schon.</para>
///
/// <para>Die eine erlaubte Ausnahme ausserhalb ist <c>Program.cs</c> der App:
/// der Velopack-Hook muss als Erstes im Prozess laufen, vor jeder
/// WinUI-Initialisierung, und lässt sich deshalb nicht in den Kern
/// verschieben.</para>
/// </summary>
public sealed class UpdateBoundaryTests
{
    /// <summary>Der einzige Ort im Kern, an dem Velopack bekannt sein darf.</summary>
    private static string UpdatesDirectory { get; } =
        Path.Combine(RepositoryFiles.SourceRoot, "Nipp.Core", "Services", "Updates");

    /// <summary>Der Einstiegspunkt der App — die Ausnahme, mit Begründung oben.</summary>
    private static string EntryPoint { get; } =
        Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App", "Program.cs");

    [Fact]
    public void Velopack_steht_nur_im_Update_Dienst_und_im_Einstiegspunkt()
    {
        var violations = new List<string>();

        foreach (var file in RepositoryFiles.EnumerateSources(RepositoryFiles.SourceRoot))
        {
            if (IsAllowed(file))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (ReferencesVelopack(lines[i]))
                {
                    violations.Add($"{RepositoryFiles.Relative(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Velopack darf nur unter Services/Updates/ und in Nipp.App/Program.cs vorkommen
             (ADR-039). Gefundene Verstösse ({violations.Count}):

             {string.Join(Environment.NewLine, violations)}

             Auflösung: den Zugriff hinter IUpdateGateway legen. Der UpdateService selbst
             kennt keinen einzigen Velopack-Typ — deshalb läuft er in Tests ohne Netz.
             """);
    }

    [Fact]
    public void Der_Update_Dienst_selbst_kommt_ohne_Velopack_aus()
    {
        // Die Logik, die etwas entscheidet — Kanal, Zustand, "nicht waehrend
        // eines Gespraechs" — muss ohne Netz und ohne Installation pruefbar
        // sein. Waere sie mit der Bibliothek verwoben, waere sie es nicht.
        var service = Path.Combine(UpdatesDirectory, "UpdateService.cs");

        Assert.True(File.Exists(service), $"Erwartet: {service}");

        var offenders = File.ReadAllLines(service)
            .Select((line, index) => (line, index))
            .Where(x => ReferencesVelopack(x.line))
            .Select(x => $"Zeile {x.index + 1}: {x.line.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "UpdateService darf keine Velopack-Typen kennen — der Zugriff gehört ins Gateway. "
                + string.Join(" | ", offenders));
    }

    [Fact]
    public void Der_Quelltextscan_findet_ueberhaupt_Dateien()
    {
        // Ohne diesen Test waere ein leerer Scan gruen — und die Grenze
        // unbewacht, ohne dass es jemandem auffiele. Dieselbe Absicherung
        // haben die anderen Grenztests.
        Assert.NotEmpty(RepositoryFiles.EnumerateSources(UpdatesDirectory));
    }

    private static bool IsAllowed(string file) =>
        file.StartsWith(UpdatesDirectory, StringComparison.OrdinalIgnoreCase)
        || string.Equals(file, EntryPoint, StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesVelopack(string line) =>
        RepositoryFiles.IsCode(line)
        && (line.Contains("using Velopack", StringComparison.Ordinal)
            || line.Contains("Velopack.", StringComparison.Ordinal)
            || line.Contains("UpdateManager", StringComparison.Ordinal)
            || line.Contains("GithubSource", StringComparison.Ordinal));
}
