using Nipp.Core.Services.Windows.Hid;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Was ein Gerätereport sagt, und welche Lampen ein Bericht anschaltet
/// (§22.5, W2.1 Etappe B7).
///
/// <para><b>Beides stand zwischen P/Invoke-Aufrufen</b> und war nur mit
/// einem Headset in der Hand zu messen — dabei ist keines von beiden
/// gerätenah. <c>HookWatch</c> war geprüft, die Deutung darum herum nicht.</para>
/// </summary>
public class HidReportDeutungTests
{
    // --- Was das Gerät meldet ---------------------------------------------

    /// <summary>Der Gabelschalter allein.</summary>
    [Fact]
    public void Ein_Gabeldruck_wird_erkannt()
    {
        var druck = HidReportDeutung.Lies([HidReportDeutung.UsageHookSwitch], 1);

        Assert.True(druck.OffHook);
        Assert.False(druck.Mute);
    }

    /// <summary>Die Stummtaste allein.</summary>
    [Fact]
    public void Ein_Stummdruck_wird_erkannt()
    {
        var druck = HidReportDeutung.Lies([HidReportDeutung.UsagePhoneMute], 1);

        Assert.False(druck.OffHook);
        Assert.True(druck.Mute);
    }

    /// <summary>Beide zusammen — ein Report kann mehrere Usages tragen.</summary>
    [Fact]
    public void Beide_Tasten_in_einem_Report()
    {
        var druck = HidReportDeutung.Lies(
            [HidReportDeutung.UsageHookSwitch, HidReportDeutung.UsagePhoneMute],
            2);

        Assert.True(druck.OffHook);
        Assert.True(druck.Mute);
    }

    /// <summary>
    /// <b>Ein leerer Report heisst «aufgelegt»</b> — und das ist die Hälfte,
    /// auf die es ankommt: das Loslassen der Taste meldet das Gerät genau so.
    /// </summary>
    [Fact]
    public void Ein_leerer_Report_meldet_aufgelegt()
    {
        var druck = HidReportDeutung.Lies([], 0);

        Assert.False(druck.OffHook);
        Assert.False(druck.Mute);
    }

    /// <summary>
    /// <b>Nur die ersten <c>anzahl</c> Einträge gelten.</b> Windows liefert
    /// ein Feld in der Grösse der grössten möglichen Meldung; wer den Rest
    /// mitliest, bekommt die Usages der vorigen Meldung — ein Gerät, das nie
    /// auflegt.
    /// </summary>
    [Fact]
    public void Was_hinter_der_Anzahl_steht_zaehlt_nicht()
    {
        // Das Feld trägt noch den Gabelschalter von vorhin, gültig ist nur
        // der erste Eintrag — und der ist leer.
        var druck = HidReportDeutung.Lies([0, HidReportDeutung.UsageHookSwitch], anzahl: 1);

        Assert.False(druck.OffHook);
    }

    /// <summary>Eine Anzahl über der Feldlänge kostet keinen Absturz.</summary>
    [Fact]
    public void Eine_zu_grosse_Anzahl_wird_abgefangen()
    {
        var druck = HidReportDeutung.Lies([HidReportDeutung.UsageHookSwitch], anzahl: 99);

        Assert.True(druck.OffHook);
    }

    /// <summary>
    /// Fremde Usages sind keine Tastendrücke — ein Gerät meldet auf derselben
    /// Seite auch anderes.
    /// </summary>
    [Fact]
    public void Fremde_Usages_bedeuten_nichts()
    {
        var druck = HidReportDeutung.Lies([0x21, 0x2E, 0x99], 3);

        Assert.False(druck.OffHook);
        Assert.False(druck.Mute);
    }

    // --- Welche Lampen angehen --------------------------------------------

    /// <summary>
    /// <b>Die Reihenfolge ist nicht gleichgültig:</b> Gespräch, Klingeln,
    /// Stumm. Kennt ein Gerät eine Lampe nicht, geht der Aufrufer die Liste
    /// einzeln durch — und dabei gilt «im Gespräch» als wichtigstes.
    /// </summary>
    [Fact]
    public void Die_Lampen_stehen_in_fester_Reihenfolge()
    {
        var lampen = HidReportDeutung.Lampen(imGespraech: true, klingelt: true, stumm: true);

        Assert.Equal(
            [HidReportDeutung.UsageLedOffHook, HidReportDeutung.UsageLedRing, HidReportDeutung.UsageLedMute],
            lampen);
    }

    /// <summary>Kein Zustand, keine Lampe — und damit nichts zu setzen.</summary>
    [Fact]
    public void Ohne_Zustand_bleibt_die_Liste_leer()
    {
        Assert.Empty(HidReportDeutung.Lampen(false, false, false));
    }

    /// <summary>Jeder Zustand für sich.</summary>
    [Theory]
    [InlineData(true, false, false, HidReportDeutung.UsageLedOffHook)]
    [InlineData(false, true, false, HidReportDeutung.UsageLedRing)]
    [InlineData(false, false, true, HidReportDeutung.UsageLedMute)]
    public void Jeder_Zustand_schaltet_seine_Lampe(bool imGespraech, bool klingelt, bool stumm, ushort erwartet)
    {
        Assert.Equal([erwartet], HidReportDeutung.Lampen(imGespraech, klingelt, stumm));
    }

    // --- Die Zahl, die der Schreib-Thread liest ---------------------------

    /// <summary>Hin und zurück ergibt dasselbe — für alle acht Möglichkeiten.</summary>
    [Fact]
    public void Bits_und_zurueck_ergeben_denselben_Zustand()
    {
        foreach (var imGespraech in new[] { false, true })
        {
            foreach (var klingelt in new[] { false, true })
            {
                foreach (var stumm in new[] { false, true })
                {
                    var bits = LampenSammler.Bits(imGespraech, klingelt, stumm);
                    var zurueck = LampenSammler.Aus(bits);

                    Assert.Equal((imGespraech, klingelt, stumm), zurueck);
                }
            }
        }
    }

    /// <summary>Acht Zustände, acht verschiedene Zahlen.</summary>
    [Fact]
    public void Jeder_Zustand_hat_seine_eigene_Zahl()
    {
        var zahlen = new List<int>();

        foreach (var imGespraech in new[] { false, true })
        {
            foreach (var klingelt in new[] { false, true })
            {
                foreach (var stumm in new[] { false, true })
                {
                    zahlen.Add(LampenSammler.Bits(imGespraech, klingelt, stumm));
                }
            }
        }

        Assert.Equal(8, zahlen.Distinct().Count());
    }

    /// <summary>
    /// <b>«Alles aus» ist etwas anderes als «noch nichts gemeldet»</b>, und
    /// das ist keine Spitzfindigkeit: nipp meldet «alles aus» wirklich, wenn
    /// ein Gespräch endet. Wäre der Anfangswert 0, bliebe genau dieser
    /// Bericht beim ersten Mal liegen — und die Lampe am Gerät an.
    /// </summary>
    [Fact]
    public void Alles_aus_ist_nicht_unbekannt()
    {
        var allesAus = LampenSammler.Bits(false, false, false);

        Assert.NotEqual(LampenSammler.Unbekannt, allesAus);
        Assert.True(LampenSammler.Aenderung(LampenSammler.Unbekannt, allesAus));
    }

    /// <summary>Derselbe Zustand zweimal ist keine Änderung — und kein Report.</summary>
    [Fact]
    public void Derselbe_Zustand_ist_keine_Aenderung()
    {
        var bits = LampenSammler.Bits(true, false, false);

        Assert.False(LampenSammler.Aenderung(bits, bits));
        Assert.True(LampenSammler.Aenderung(bits, LampenSammler.Bits(true, false, true)));
    }
}
