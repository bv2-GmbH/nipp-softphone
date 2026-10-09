using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Was am Ende eines Gesprächs im Protokoll steht (ADR-077).
///
/// <para><b>Warum das geprüft wird.</b> Der Bericht ist die Stelle, an der ein
/// Problem später erklärt werden soll — er wird gelesen, wenn niemand mehr
/// weiss, was war. Eine Zahl, die dort falsch steht, ist schlimmer als keine:
/// sie leitet die nächste Suche in die Irre, und das kostet dann einen
/// Nachmittag statt einer Minute.</para>
/// </summary>
public class GespraechsBerichtTests
{
    private static readonly DateTimeOffset Null = new(2026, 10, 8, 13, 44, 17, TimeSpan.Zero);

    private static CallQuality Messung(
        float verlust = 0,
        float umlaufSekunden = 0.015f,
        float puffer = 40,
        float mos = 4.2f) =>
        new(umlaufSekunden, puffer, verlust, DownloadKbitPerSecond: 64, Mos: mos);

    private static GespraechsBericht Bericht() => new(Null);

    /// <summary>
    /// Der Normalfall: ein Gespräch wie das vom 08.10.2026 um 13:44 — neun
    /// Minuten, 13 Verwürfe bis 46 ms, drei Ticker-Verspätungen bis 87 ms.
    /// </summary>
    [Fact]
    public void Der_Bericht_traegt_Dauer_Stoerungen_und_Qualitaet()
    {
        var b = Bericht();
        b.Codec = "PCMU";
        b.Abtastrate = 8000;
        b.Ausgabegeraet = "Default Playback";
        b.Eingabegeraet = "Default Capture";

        b.Erfasse(Messung());
        b.Erfasse(Messung(verlust: 0.4f, umlaufSekunden: 0.025f));
        b.Erfasse(new Stoerung(StoerungsArt.Verwurf, 46));
        b.Erfasse(new Stoerung(StoerungsArt.Verwurf, 21));
        b.Erfasse(new Stoerung(StoerungsArt.TickerZuSpaet, 87));

        var zeile = b.AlsZeile(Null.AddMinutes(9).AddSeconds(20), stufeIstDebug: false);

        Assert.Contains("Dauer 9:20", zeile, StringComparison.Ordinal);
        Assert.Contains("Codec PCMU/8000", zeile, StringComparison.Ordinal);
        Assert.Contains("Verwuerfe 2 (max 46 ms)", zeile, StringComparison.Ordinal);
        Assert.Contains("Ticker spaet 1 (max 87 ms)", zeile, StringComparison.Ordinal);
        Assert.Contains("Pufferfehler 0", zeile, StringComparison.Ordinal);
        Assert.Contains("Verlust max 0.4%", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Mittlerer Wert und Maximum stehen beide da</b>, und zwar weil sie nie
    /// dasselbe beantworten (CLAUDE.md, 16.09.2026 — der Rauschfilter stand
    /// bei 0,7 ms im Mittel und 77 ms im Maximum, und nur die zweite Zahl war
    /// das Problem).
    /// </summary>
    [Fact]
    public void Umlaufzeit_traegt_mittleren_Wert_und_Maximum()
    {
        var b = Bericht();
        b.Erfasse(Messung(umlaufSekunden: 0.010f));
        b.Erfasse(Messung(umlaufSekunden: 0.010f));
        b.Erfasse(Messung(umlaufSekunden: 0.250f));

        var zeile = b.AlsZeile(Null.AddMinutes(1), stufeIstDebug: false);

        Assert.Contains("Umlauf 10 ms (max 250)", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Ein einzelner Müllwert des SDK darf den Bericht nicht umwerfen</b> —
    /// am 09.10.2026 passiert und der Grund für den Median.
    ///
    /// <para>Das SDK meldete für den Jitterpuffer 222 Werte, davon 220 zwischen
    /// 34 und 79 ms und <b>einen bei 2 334 266 ms</b>. Der Durchschnitt stand
    /// dadurch bei 12 031 ms, und niemand konnte den Eintrag mehr deuten. Der
    /// Median nimmt davon keine Notiz; <b>das Maximum zeigt den Ausreisser
    /// weiterhin</b>, und genau diese Gegenüberstellung macht ihn erkennbar.</para>
    /// </summary>
    [Fact]
    public void Ein_Muellwert_verdirbt_den_mittleren_Wert_nicht()
    {
        var b = Bericht();

        for (var i = 0; i < 20; i++)
        {
            b.Erfasse(Messung(puffer: 40));
        }

        b.Erfasse(Messung(puffer: 2334266));

        var zeile = b.AlsZeile(Null.AddMinutes(20), stufeIstDebug: false);

        Assert.Contains("Puffer 40 ms (max 2334266)", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Die Abschaltmeldung schlägt, was das SDK sonst behauptet.</b> Meldet
    /// das SDK «Echounterdrückung an» und hat sie sich im selben Gespräch
    /// abgeschaltet, dann meldet es den Wunsch und nicht den Zustand — der
    /// Bericht nennt dann die gezählte Wahrheit, mitsamt der Rate, die sie
    /// gekippt hat.
    /// </summary>
    [Fact]
    public void Gezaehlte_Abschaltung_schlaegt_die_Auskunft_des_SDK()
    {
        var b = Bericht();
        b.EchoLautSdk = true;
        b.Erfasse(new Stoerung(StoerungsArt.EchoAbgeschaltet, 8000));

        var zeile = b.AlsZeile(Null.AddMinutes(1), stufeIstDebug: false);

        Assert.Contains("Echo AUS (SDK meldete Abschaltung bei 8000 Hz)", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ohne Abschaltmeldung gilt, was das SDK sagt — das ist der Fall, den
    /// T332 erzeugen soll.
    /// </summary>
    [Fact]
    public void Ohne_Abschaltung_gilt_die_Auskunft_des_SDK()
    {
        var b = Bericht();
        b.EchoLautSdk = true;

        Assert.Contains("Echo an", b.AlsZeile(Null.AddMinutes(1), stufeIstDebug: false), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Die Protokollstufe steht im Bericht</b>, und das ist kein Beiwerk:
    /// «null Störungen» heisst bei abgeschaltetem SDK-Protokoll etwas anderes
    /// als bei eingeschaltetem. Ohne diese Angabe liest jemand ein stilles
    /// Gespräch als ein gutes.
    /// </summary>
    [Fact]
    public void Die_Protokollstufe_steht_dabei()
    {
        var b = Bericht();

        Assert.Contains("SDK-Stufe normal", b.AlsZeile(Null.AddMinutes(1), false), StringComparison.Ordinal);
        Assert.Contains("SDK-Stufe Debug", b.AlsZeile(Null.AddMinutes(1), true), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein Gespräch, das endet, bevor eine Sekundenmessung durchkam, darf
    /// keinen Mittelwert aus null Werten bilden — das wäre eine Division durch
    /// null mitten im SDK-Callback.
    /// </summary>
    [Fact]
    public void Ohne_Messung_steht_das_da_statt_zu_rechnen()
    {
        var zeile = Bericht().AlsZeile(Null.AddSeconds(2), stufeIstDebug: false);

        Assert.Contains("keine Qualitaetsmessung", zeile, StringComparison.Ordinal);
        Assert.Contains("Dauer 0:02", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lange Gerätenamen werden gekürzt — «Speakers (Qualcomm(R) Aqstic(TM)
    /// Audio Adapter Device)» ist 53 Zeichen lang, und davon unterscheiden die
    /// ersten vierzig genug.
    /// </summary>
    [Fact]
    public void Lange_Geraetenamen_werden_gekuerzt()
    {
        var b = Bericht();
        b.Ausgabegeraet = "Speakers (Qualcomm(R) Aqstic(TM) Audio Adapter Device)";

        var zeile = b.AlsZeile(Null.AddMinutes(1), stufeIstDebug: false);

        Assert.Contains("Aus Speakers (Qualcomm(R) Aqstic(TM) Audio …", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fehlt ein Wert, steht ein Fragezeichen — nicht eine leere Stelle, an
    /// der man rätselt, ob die Zahl null war oder nie kam.
    /// </summary>
    [Fact]
    public void Unbekanntes_steht_als_Fragezeichen()
    {
        var zeile = Bericht().AlsZeile(Null.AddMinutes(1), stufeIstDebug: false);

        Assert.Contains("Codec ?/0", zeile, StringComparison.Ordinal);
        Assert.Contains("Aus ?", zeile, StringComparison.Ordinal);
        Assert.Contains("Echo unbekannt", zeile, StringComparison.Ordinal);
    }
}
