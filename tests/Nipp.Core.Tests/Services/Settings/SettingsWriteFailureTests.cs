using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// W2.2 und Befund B21: ein Schreibfehler ist kein Absturz.
///
/// <para><b>Der Befund.</b> Seit ADR-045 wird bei <b>jeder</b> Feldänderung
/// geschrieben. <c>File.WriteAllText</c> und <c>File.Move</c> standen dabei
/// ungefangen da, und nicht jeder Aufrufer fing: «Konto entfernen» lief aus
/// einem <c>async void</c> und nahm den Prozess mit, wenn ein Virenscanner
/// oder OneDrive die Datei kurz hielt.</para>
/// </summary>
public sealed class SettingsWriteFailureTests : IDisposable
{
    private readonly string _verzeichnis = Path.Combine(
        Path.GetTempPath(),
        $"nipp-write-{Guid.NewGuid():N}");

    public SettingsWriteFailureTests() => Directory.CreateDirectory(_verzeichnis);

    public void Dispose()
    {
        if (Directory.Exists(_verzeichnis))
        {
            Directory.Delete(_verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Eine_gehaltene_Datei_ergibt_eine_lesbare_Meldung()
    {
        var pfad = Path.Combine(_verzeichnis, "settings.json");

        var dienst = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_verzeichnis, "s.dat")),
            NullLogger<SettingsService>.Instance,
            pfad);

        dienst.Load();

        // Die Zieldatei offen halten — das ist, was ein Sicherungsdienst tut.
        // File.Move mit overwrite scheitert dann.
        using var sperre = new FileStream(
            pfad,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None);

        var fehler = Assert.Throws<InvalidOperationException>(
            () => dienst.Save(dienst.Current with
            {
                Network = dienst.Current.Network with { KeepAliveSeconds = 42 },
            }));

        // Kein .NET-Typname, kein Pfad, und ein Satz, der sagt was zu tun ist
        // — die Meldung landet in der Oberfläche.
        Assert.Contains("noch einmal versuchen", fehler.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IOException", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nach_einem_Fehlschlag_gilt_der_bisherige_Stand()
    {
        // Die Zusage aus der Meldung: «Die bisherigen Einstellungen gelten
        // weiter.» Ein halb übernommener Stand wäre schlimmer als ein
        // Fehlschlag.
        var pfad = Path.Combine(_verzeichnis, "settings.json");

        var dienst = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_verzeichnis, "s2.dat")),
            NullLogger<SettingsService>.Instance,
            pfad);

        dienst.Load();
        var vorher = dienst.Current.Network.KeepAliveSeconds;

        using var sperre = new FileStream(pfad, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<InvalidOperationException>(
            () => dienst.Save(dienst.Current with
            {
                Network = dienst.Current.Network with { KeepAliveSeconds = 42 },
            }));

        Assert.Equal(vorher, dienst.Current.Network.KeepAliveSeconds);
    }
}
