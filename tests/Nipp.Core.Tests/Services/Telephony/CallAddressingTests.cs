using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Wählbare Adressen und Aufnahmenamen (§8.2, W2.1 Etappe B5).
///
/// <para><b>Beide Funktionen standen zwischen SDK-Aufrufen</b> und waren
/// damit nur an einer Anlage zu messen — dabei fasst keine von ihnen das SDK
/// an. Das ist die billigste Etappe des Beweisplans.</para>
/// </summary>
public class CallAddressingTests
{
    private static readonly DateTimeOffset Zeit =
        new(2026, 9, 24, 22, 57, 13, TimeSpan.FromHours(2));

    // --- Die wählbare Adresse ---------------------------------------------

    /// <summary>
    /// <b>Eine Nebenstelle bekommt die Domäne des Kontos</b>, über das
    /// gewählt wird. Ohne sie wählt das SDK gegen die zuletzt benutzte
    /// Anlage, und bei zwei Konten ist das die falsche.
    /// </summary>
    [Fact]
    public void Eine_Nebenstelle_bekommt_die_Domaene()
    {
        Assert.Equal("sip:201@anlage.test", CallAddressing.ToDialable("201", "anlage.test"));
    }

    /// <summary>
    /// <b>Ohne Domäne bleibt die Eingabe stehen.</b> Das SDK lehnt sie dann
    /// ab, und die Meldung sagt, was fehlt — besser als eine erfundene
    /// Domäne, die irgendwohin wählt.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Ohne_Domaene_bleibt_die_Eingabe_stehen(string? domain)
    {
        Assert.Equal("201", CallAddressing.ToDialable("201", domain));
    }

    /// <summary>Was schon eine Adresse ist, bleibt eine — in jeder Schreibweise.</summary>
    [Theory]
    [InlineData("sip:201@andere.test")]
    [InlineData("SIP:201@andere.test")]
    [InlineData("sips:201@andere.test")]
    [InlineData("201@andere.test")]
    public void Eine_fertige_Adresse_bleibt_unveraendert(string eingabe)
    {
        Assert.Equal(eingabe, CallAddressing.ToDialable(eingabe, "anlage.test"));
    }

    /// <summary>
    /// <b>Eine E.164-Nummer ist keine Adresse</b> und bekommt die Domäne —
    /// das Pluszeichen ändert daran nichts.
    /// </summary>
    [Fact]
    public void Eine_E164_Nummer_bekommt_die_Domaene()
    {
        Assert.Equal("sip:+41791234567@anlage.test", CallAddressing.ToDialable("+41791234567", "anlage.test"));
    }

    /// <summary>
    /// <b>Leerzeichen bleiben stehen</b>, und das ist Absicht: normalisiert
    /// wird an einer Stelle, und die ist <c>NumberNormalizer</c> (ADR-049).
    /// Hier zweimal zu putzen hiesse, zwei Wahrheiten darüber zu haben, was
    /// eine Nummer ist.
    /// </summary>
    [Fact]
    public void Leerzeichen_werden_hier_nicht_geputzt()
    {
        Assert.Equal("sip:079 123 45 67@anlage.test", CallAddressing.ToDialable("079 123 45 67", "anlage.test"));
    }

    // --- Der Name der Aufnahme --------------------------------------------

    /// <summary>Das Schema aus §8.2, mit dem Zeitpunkt des Anrufaufbaus.</summary>
    [Fact]
    public void Der_Name_folgt_dem_Schema()
    {
        Assert.Equal("2026-09-24_225713_0791234567.wav", CallAddressing.RecordingFileName("0791234567", Zeit));
    }

    /// <summary>
    /// <b>Was Windows im Dateinamen nicht annimmt, fliegt raus</b> — sonst
    /// scheitert die Aufnahme erst beim Schreiben, also nach dem Gespräch.
    /// </summary>
    [Theory]
    [InlineData("07/91:23*45#67", "2026-09-24_225713_0791234567.wav")]
    [InlineData("sip:201@anlage.test", "2026-09-24_225713_sip201@anlage.test.wav")]
    [InlineData("+41 79 123 45 67", "2026-09-24_225713_+41 79 123 45 67.wav")]
    public void Unerlaubte_Zeichen_fallen_weg(string nummer, string erwartet)
    {
        Assert.Equal(erwartet, CallAddressing.RecordingFileName(nummer, Zeit));
    }

    /// <summary>
    /// <b>Bleibt nichts übrig, heisst die Datei «unbekannt».</b> Eine
    /// Aufnahme ohne Namen wäre schlimmer als eine mit einem unscharfen — sie
    /// hiesse dann <c>2026-09-24_225713_.wav</c> und die nächste genauso.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("***")]
    [InlineData("##")]
    public void Ohne_brauchbare_Zeichen_heisst_sie_unbekannt(string nummer)
    {
        Assert.Equal("2026-09-24_225713_unbekannt.wav", CallAddressing.RecordingFileName(nummer, Zeit));
    }

    /// <summary>
    /// Zwei Aufnahmen in derselben Sekunde zur selben Nummer hiessen gleich —
    /// <b>und das ist hier kein Fehler</b>, sondern eine Aussage über die
    /// Auflösung des Schemas aus §8.2. Wer daran etwas ändert, ändert den
    /// Dateinamen, den Benutzer wiedererkennen.
    /// </summary>
    [Fact]
    public void Dieselbe_Sekunde_ergibt_denselben_Namen()
    {
        Assert.Equal(
            CallAddressing.RecordingFileName("201", Zeit),
            CallAddressing.RecordingFileName("201", Zeit));
    }
}
