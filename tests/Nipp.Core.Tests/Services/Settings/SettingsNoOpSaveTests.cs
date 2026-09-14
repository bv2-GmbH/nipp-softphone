using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// Ein Speichern, das nichts ändert, löst keine Kette aus (13.09.2026).
///
/// <para><b>Der Befund.</b> An <c>Changed</c> hängen vier Empfänger: die
/// Einstellungen gehen auf den laufenden Core, Autostart und
/// Protokoll-Handler werden in die Registrierung geschrieben, das systemweite
/// Kürzel wird neu angemeldet, die Präsenz-Abonnements werden erneuert und das
/// Erscheinungsbild wird gesetzt. <b>Einer davon speicherte wieder</b> — und
/// damit lief eine Kette ohne Ende: schreiben, melden, anwenden, schreiben,
/// alle neun Millisekunden mit demselben Inhalt.</para>
///
/// <para>Am laufenden Programm gemessen: <b>248 MB Protokoll in sechs
/// Minuten</b>, und weil jede Runde die Einstellungen auf den Core übertrug,
/// registrierte das SDK im Sekundentakt neu. Die Präsenz aller zehn
/// Nebenstellen stand auf «offline», und die Anmeldung flackerte zwischen
/// «Angemeldet» und «fehlgeschlagen».</para>
///
/// <para><b>Wer die Kette auslöst, ist die falsche Frage.</b> Sie konnte
/// entstehen, weil niemand geprüft hat, ob es überhaupt etwas zu schreiben
/// gibt. Diese Prüfung gehört an die eine Stelle, durch die jeder Schreibweg
/// läuft.</para>
/// </summary>
public sealed class SettingsNoOpSaveTests : IDisposable
{
    private readonly string _verzeichnis = Path.Combine(
        Path.GetTempPath(),
        $"nipp-noop-{Guid.NewGuid():N}");

    private readonly SettingsService _dienst;

    public SettingsNoOpSaveTests()
    {
        Directory.CreateDirectory(_verzeichnis);

        _dienst = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_verzeichnis, "s.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_verzeichnis, "settings.json"));

        _dienst.Load();
    }

    public void Dispose()
    {
        if (Directory.Exists(_verzeichnis))
        {
            Directory.Delete(_verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Zweimal_derselbe_Stand_meldet_nur_einmal()
    {
        var meldungen = 0;
        _dienst.Changed += (_, _) => meldungen++;

        var neu = _dienst.Current with
        {
            Network = _dienst.Current.Network with { KeepAliveSeconds = 42 },
        };

        _dienst.Save(neu);
        _dienst.Save(neu);
        _dienst.Save(neu);

        Assert.Equal(1, meldungen);
    }

    [Fact]
    public void Ein_Empfaenger_der_zurueckspeichert_haelt_an()
    {
        // Die Nachbildung des Befunds: ein Empfänger, der auf jede Meldung
        // hin denselben Stand zurückschreibt. Ohne die Prüfung in Write ist
        // das eine Endlosschleife — der Test liefe nicht durch, er würde
        // den Stapel überlaufen lassen.
        var runden = 0;

        _dienst.Changed += (_, stand) =>
        {
            runden++;

            if (runden > 50)
            {
                return; // Notausstieg, damit ein Fehlschlag lesbar bleibt
            }

            _dienst.Save(stand);
        };

        _dienst.Save(_dienst.Current with
        {
            Network = _dienst.Current.Network with { KeepAliveSeconds = 42 },
        });

        Assert.Equal(1, runden);
    }

    [Fact]
    public void Eine_echte_Aenderung_meldet_weiterhin()
    {
        // Die Gegenprobe, und sie ist die wichtigere Hälfte: eine Bremse, die
        // auch echte Änderungen verschluckt, wäre schlimmer als die Schleife.
        var meldungen = 0;
        _dienst.Changed += (_, _) => meldungen++;

        for (var sekunden = 30; sekunden <= 33; sekunden++)
        {
            _dienst.Save(_dienst.Current with
            {
                Network = _dienst.Current.Network with { KeepAliveSeconds = sekunden },
            });
        }

        Assert.Equal(4, meldungen);
        Assert.Equal(33, _dienst.Current.Network.KeepAliveSeconds);
    }
}
