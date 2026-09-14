using Nipp.Core.Diagnostics;

namespace Nipp.Core.Tests.Diagnostics;

/// <summary>
/// §21.2 verbietet Rufnummern im Protokoll, und das Diagnosepaket nimmt die
/// Protokolle mit zum Support (§9.6). Die Regel galt bisher nur für die
/// Integrationen; der Telefonie-Kern schrieb in dieselbe Datei „Anruf an
/// +41791234567".
///
/// Diese Tests halten den Kompromiss fest: die letzten drei Ziffern bleiben
/// stehen, damit sich zwei Meldungen noch demselben Anruf zuordnen lassen —
/// wählen kann man damit niemanden mehr.
/// </summary>
public sealed class LogMaskingTests
{
    [Theory]
    [InlineData("+41791234567", "…567")]
    [InlineData("0041445128430", "…430")]
    [InlineData("044 512 84 30", "…430")]
    [InlineData("0791234567", "…567")]
    public void Eine_Rufnummer_verliert_alles_bis_auf_die_letzten_drei_Ziffern(string input, string expected)
    {
        Assert.Equal(expected, LogMasking.Number(input));
    }

    [Theory]
    [InlineData("151")]
    [InlineData("40")]
    [InlineData("1234")]
    public void Interne_Nebenstellen_bleiben_lesbar(string extension)
    {
        // Bis vier Ziffern gilt dieselbe Grenze wie in PhoneNumberKey: das ist
        // ein Apparat im eigenen Haus, keine Person. Ohne diese Ausnahme wäre
        // jedes Protokoll über die eigene Anlage unbrauchbar.
        Assert.Equal(extension, LogMasking.Number(extension));
    }

    [Fact]
    public void Eine_SIP_Adresse_ohne_Ziffern_behaelt_nur_die_Domaene()
    {
        // „sip:info@example.ch" ist keine Rufnummer, aber trotzdem eine
        // Person. Die Domäne allein hilft bei der Diagnose und verrät niemanden.
        Assert.Equal("…@example.ch", LogMasking.Number("sip:info@example.ch"));
    }

    [Fact]
    public void Leer_wird_als_leer_gemeldet_und_nicht_als_Nummer()
    {
        Assert.Equal("(leer)", LogMasking.Number(null));
        Assert.Equal("(leer)", LogMasking.Number("   "));
    }

    [Fact]
    public void Ein_Aufnahmepfad_behaelt_Ordner_und_Zeit_und_verliert_die_Nummer()
    {
        // Namensschema aus §8.2: yyyy-MM-dd_HHmmss_<Nummer>.wav. Die Datei muss
        // auffindbar bleiben, sonst nützt der Protokolleintrag nichts.
        var masked = LogMasking.Path(@"C:\Aufnahmen\2026-09-06_181500_+41791234567.wav");

        Assert.Contains("2026-09-06_181500_", masked, StringComparison.Ordinal);
        Assert.Contains("…567", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("791234", masked, StringComparison.Ordinal);
        Assert.EndsWith(".wav", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Protokollzeile_verliert_jede_lange_Ziffernfolge()
    {
        var line = "2026-09-06 18:38:46.020 [INF] Anruf e94ce3a2 an +41791234567 aufgebaut";
        var masked = LogMasking.Line(line);

        Assert.DoesNotContain("791234567", masked, StringComparison.Ordinal);
        Assert.Contains("…567", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Zeitstempel_und_Adressen_ueberstehen_die_Maskierung()
    {
        // Ohne diese Zusicherung wäre ein maskiertes Protokoll für die
        // Fehlersuche wertlos: die Uhrzeit ist die wichtigste Spalte darin.
        var line = "2026-09-06 18:38:46.020 [INF] Registrierung bei 192.168.1.20:5061 erneuert";
        var masked = LogMasking.Line(line);

        Assert.Contains("2026-09-06 18:38:46.020", masked, StringComparison.Ordinal);
        Assert.Contains("192.168.1.20:5061", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Zeile_ohne_Ziffernfolgen_bleibt_unveraendert()
    {
        const string line = "[INF] Symbol im Infobereich angelegt";

        Assert.Equal(line, LogMasking.Line(line));
    }
}
