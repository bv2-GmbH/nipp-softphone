using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Welcher Pfad in Autostart und Protokoll-Handler geschrieben wird (ADR-038).
///
/// <para><b>Warum das ein eigener Test ist.</b> Seit der Auslieferung mit
/// Velopack liegt die laufende EXE in einem Ordner <c>current</c>, dessen
/// Inhalt bei jedem Update ersetzt wird, und daneben steht ein Stub, der über
/// alle Updates gleich bleibt. Ein Registrierungseintrag auf die falsche der
/// beiden Dateien fällt <b>erst nach dem ersten Update</b> auf — und dann
/// damit, dass ein <c>tel:</c>-Link nichts mehr tut (T122).</para>
/// </summary>
public sealed class ExecutablePathTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "nipp-pfadtest-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void In_einer_Velopack_Installation_gewinnt_der_Stub()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(_root, "Nipp.App.exe"), "stub");
        File.WriteAllText(Path.Combine(current, "Nipp.App.exe"), "app");

        var resolved = WindowsIntegration.ResolveExecutablePath(current);

        Assert.Equal(Path.Combine(_root, "Nipp.App.exe"), resolved);
    }

    [Fact]
    public void Ein_abschliessender_Trenner_aendert_nichts()
    {
        // AppContext.BaseDirectory endet mit einem Backslash. Ohne das
        // Abschneiden hiesse das Verzeichnis "" statt "current", und die
        // Erkennung liefe ins Leere — lautlos, mit dem falschen Pfad.
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(_root, "Nipp.App.exe"), "stub");

        var resolved = WindowsIntegration.ResolveExecutablePath(current + Path.DirectorySeparatorChar);

        Assert.Equal(Path.Combine(_root, "Nipp.App.exe"), resolved);
    }

    [Fact]
    public void Ohne_Stub_bleibt_es_bei_der_eigenen_Exe()
    {
        // Der Entwicklungsalltag: gebaut, aus bin\ gestartet, kein Installer
        // weit und breit.
        var directory = Path.Combine(_root, "win-x64");
        Directory.CreateDirectory(directory);

        var resolved = WindowsIntegration.ResolveExecutablePath(directory);

        Assert.Equal(Path.Combine(directory, "Nipp.App.exe"), resolved);
    }

    [Fact]
    public void Ein_Ordner_namens_current_ohne_Stub_daneben_zaehlt_nicht()
    {
        // Nur der Name genuegt nicht: es muss auch wirklich eine EXE eine
        // Ebene hoeher liegen. Sonst zeigte der Eintrag auf eine Datei, die
        // es nicht gibt.
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);

        var resolved = WindowsIntegration.ResolveExecutablePath(current);

        Assert.Equal(Path.Combine(current, "Nipp.App.exe"), resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
