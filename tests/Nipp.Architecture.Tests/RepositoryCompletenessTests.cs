using System.Diagnostics;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Jede Quelldatei, die gebaut wird, liegt auch im Repo.
///
/// <para><b>Warum das ein Test sein muss.</b> Am 07.09.2026 fehlte
/// <c>Services/Integrations/Secrets/IntegrationSecrets.cs</c> seit ihrem
/// ersten Tag in der Versionsverwaltung: die <c>.gitignore</c> trug
/// <c>secrets/</c> für Zugangsdaten, und das Muster traf den <b>Quellordner</b>
/// gleichen Namens mit. Lokal fiel es nie auf — die Datei lag ja da, und alles
/// baute. Der erste frische Klon brach mit zehn Compilerfehlern ab, und keiner
/// davon zeigte auf die fehlende Datei: gemeldet wurden die <i>Verwender</i>
/// eines Namensraums, den es plötzlich nicht mehr gab.</para>
///
/// <para>Das ist die Sorte Fehler, die genau einmal auffällt — beim nächsten
/// Menschen, der klont. Sechs Monate später wäre die Frage „was fehlt denn
/// hier?" nicht mehr in Minuten zu beantworten.</para>
///
/// <para>Der Test ruft <c>git ls-files</c> auf. Ohne Git — etwa in einem aus
/// einem Archiv entpackten Baum — bleibt er still, statt falsch anzuschlagen.</para>
/// </summary>
public sealed class RepositoryCompletenessTests
{
    [Fact]
    public void Jede_Quelldatei_unter_src_ist_eingecheckt()
    {
        if (RunGit("ls-files --cached --others --exclude-standard -- src") is not { } tracked)
        {
            // Kein Git greifbar. Ein Test, der dann rot wird, sagt etwas ueber
            // die Umgebung und nichts ueber das Repo.
            return;
        }

        var known = new HashSet<string>(
            tracked
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim().Replace('/', Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);

        var sources = RepositoryFiles
            .EnumerateSources(RepositoryFiles.SourceRoot)
            .Select(RepositoryFiles.Relative)
            .Select(relative => Path.Combine("src", relative))
            .ToList();

        // Ohne diese Zeile waere ein leerer Scan gruen — und damit ein Waechter,
        // der nie anschlaegt, ohne dass es jemandem auffiele. Dieselbe
        // Absicherung tragen die Grenztests.
        Assert.True(
            sources.Count > 100,
            $"Der Scan hat nur {sources.Count} Quelldateien gefunden. Stimmt der Pfad noch?");

        var missing = sources.Where(path => !known.Contains(path)).ToList();

        Assert.True(
            missing.Count == 0,
            $"""
             Diese Dateien werden gebaut, liegen aber nicht im Repo ({missing.Count}):

             {string.Join(Environment.NewLine, missing)}

             Fast immer ist es die .gitignore: ein Muster fuer Zugangsdaten oder
             Ausgabeordner trifft einen Quellordner mit. Das Muster gehoert enger
             gefasst — eine Ausnahme mit '!' hilft nicht, wenn schon das
             VERZEICHNIS ausgeschlossen ist.
             """);
    }

    /// <summary>
    /// Führt Git im Repo aus. <c>null</c>, wenn es nicht läuft — dann ist der
    /// Test nicht anwendbar, nicht fehlgeschlagen.
    /// </summary>
    private static string? RunGit(string arguments)
    {
        var repositoryRoot = Path.GetDirectoryName(RepositoryFiles.SourceRoot);

        if (repositoryRoot is null)
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(milliseconds: 30_000);

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
