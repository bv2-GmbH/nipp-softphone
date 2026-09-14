using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// §8.1 und §13: die Nummernnormalisierung ist die Stelle, an der Softphones
/// typischerweise falsch wählen. Deshalb eine reine Funktion, und deshalb
/// diese Tests — sie sind in §13 ausdrücklich verbindlich.
///
/// Die Fälle aus §13 sind wörtlich übernommen: 044 512 84 30, +41445128430,
/// 0041445128430, *8010, 40, 112, Ausland 0049…
/// </summary>
public sealed class NumberNormalizerTests
{
    private static readonly NumberNormalizer Swiss = new("+41");

    // ---------------------------------------------------------------- §13

    [Theory]
    [InlineData("044 512 84 30", "+41445128430")]
    [InlineData("+41445128430", "+41445128430")]
    [InlineData("0041445128430", "+41445128430")]
    public void Die_drei_Schreibweisen_derselben_Nummer_ergeben_dasselbe(string input, string expected)
    {
        // Der eigentliche Sinn der Normalisierung: derselbe Anschluss, egal
        // wie er notiert wurde. Sonst stimmen später Verlauf und
        // CLIP-Auflösung nicht überein.
        Assert.Equal(expected, Swiss.Normalize(input));
    }

    [Theory]
    [InlineData("*8010")]      // §8.1: Stern plus 3-4 Stellen
    [InlineData("*80")]
    [InlineData("*123")]
    [InlineData("40")]         // §8.1: bis vier Ziffern
    [InlineData("112")]        // Notruf — darf NIEMALS umgeschrieben werden
    [InlineData("117")]
    [InlineData("144")]
    [InlineData("1414")]
    public void Interne_Ziele_und_Kurznummern_bleiben_unveraendert(string input)
    {
        Assert.Equal(input, Swiss.Normalize(input));
    }

    [Fact]
    public void Auslandsnummer_mit_00_wird_zu_Plus()
    {
        Assert.Equal("+4989123456", Swiss.Normalize("0049 89 123456"));
    }

    // ------------------------------------------------- Formatierung, Zeichen

    [Theory]
    [InlineData("044-512-84-30", "+41445128430")]
    [InlineData("044/512 84 30", "+41445128430")]
    [InlineData("(044) 512 84 30", "+41445128430")]
    [InlineData("044.512.84.30", "+41445128430")]
    [InlineData("  044 512 84 30  ", "+41445128430")]
    public void Trennzeichen_und_Leerraum_werden_entfernt(string input, string expected)
    {
        Assert.Equal(expected, Swiss.Normalize(input));
    }

    [Fact]
    public void Ein_Plus_mitten_in_der_Nummer_zaehlt_nicht_als_Laenderpraefix()
    {
        // "+" gilt nur am Anfang. Sonst würde eine vertippte Nummer
        // stillschweigend zu etwas ganz anderem.
        Assert.Equal("+41445128430", Swiss.Normalize("044+512 84 30"));
    }

    // ---------------------------------------------------------- Sonderfälle

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Leere_Eingabe_bleibt_leer(string input)
    {
        Assert.Equal(string.Empty, Swiss.Normalize(input));
    }

    [Fact]
    public void Null_ergibt_eine_leere_Zeichenfolge()
    {
        Assert.Equal(string.Empty, Swiss.Normalize(null));
    }

    [Fact]
    public void Sip_Adressen_werden_nicht_angetastet()
    {
        // Wer eine SIP-URI eingibt, meint sie auch so. Eine Normalisierung
        // würde daraus Unsinn machen.
        Assert.Equal("sip:echo@pbx.example.ch", Swiss.Normalize("sip:echo@pbx.example.ch"));
        Assert.Equal("echo@pbx.example.ch", Swiss.Normalize("echo@pbx.example.ch"));
    }

    [Fact]
    public void Buchstaben_bleiben_erhalten_wenn_es_keine_Nummer_ist()
    {
        // Manche Anlagen kennen benannte Ziele wie "voicemail".
        Assert.Equal("voicemail", Swiss.Normalize("voicemail"));
    }

    // ------------------------------------------------- Anderes Länderpräfix

    [Fact]
    public void Das_Laenderpraefix_kommt_aus_den_Einstellungen()
    {
        // §9.1: das Präfix ist konfigurierbar. Mit +49 muss aus derselben
        // Eingabe eine deutsche Nummer werden.
        var german = new NumberNormalizer("+49");

        Assert.Equal("+4989123456", german.Normalize("089 123456"));
    }

    [Theory]
    [InlineData("41")]
    [InlineData("0041")]
    [InlineData("+41")]
    public void Das_Praefix_wird_unabhaengig_von_der_Schreibweise_verstanden(string prefix)
    {
        var normalizer = new NumberNormalizer(prefix);

        Assert.Equal("+41445128430", normalizer.Normalize("044 512 84 30"));
    }

    [Fact]
    public void Ohne_Praefix_bleibt_die_fuehrende_Null_stehen()
    {
        // Wenn kein Land konfiguriert ist, darf nicht geraten werden — dann
        // geht die Nummer so hinaus, wie sie eingegeben wurde.
        var none = new NumberNormalizer(countryPrefix: null);

        Assert.Equal("0445128430", none.Normalize("044 512 84 30"));
    }

    // ------------------------------------------------------------ Idempotenz

    [Theory]
    [InlineData("044 512 84 30")]
    [InlineData("+41445128430")]
    [InlineData("0041445128430")]
    [InlineData("*8010")]
    [InlineData("112")]
    [InlineData("sip:echo@pbx.example.ch")]
    public void Zweimal_normalisieren_aendert_nichts_mehr(string input)
    {
        // Wichtig, weil dieselbe Nummer mehrfach durch die Funktion läuft:
        // Eingabe, Verlauf, Rückruf aus dem Verlauf.
        var once = Swiss.Normalize(input);

        Assert.Equal(once, Swiss.Normalize(once));
    }

    // ------------------------------------------------------ Interne Erkennung

    [Theory]
    [InlineData("40", true)]
    [InlineData("1414", true)]
    [InlineData("*8010", true)]
    [InlineData("12345", false)]
    [InlineData("044 512 84 30", false)]
    [InlineData("+41445128430", false)]
    public void Interne_Ziele_werden_als_solche_erkannt(string input, bool expected)
    {
        // Die Unterscheidung braucht §8.4 (BLF nur für interne Nebenstellen)
        // und §8.3 (Anzeige im Verlauf).
        Assert.Equal(expected, NumberNormalizer.IsInternalTarget(input));
    }

    // -------------------------------------------------- Wählbar oder gesucht

    [Theory]
    [InlineData("044 512 84 30")]
    [InlineData("+41 79 123 45 67")]
    [InlineData("+41445128430")]
    [InlineData("0041445128430")]
    [InlineData("40")]
    [InlineData("112")]
    [InlineData("*8010")]
    [InlineData("#31#0445128430")]
    [InlineData("(044) 512-84-30")]
    [InlineData("sip:151@pbx.example.ch")]
    [InlineData("tel:+41445128430")]
    [InlineData("meier@example.ch")]
    public void Was_sich_waehlen_laesst_gilt_als_waehlbar(string input)
    {
        // Adressartiges zählt dazu: wer eine SIP-Adresse eingibt, meint sie
        // auch so — Normalize() lässt sie aus demselben Grund unangetastet.
        Assert.True(NumberNormalizer.IsDialable(input));
    }

    [Theory]
    [InlineData("Meier")]
    [InlineData("Anna Meier")]
    [InlineData("Meier AG")]
    [InlineData("A1")]          // eine Ziffer darin macht daraus keine Nummer
    [InlineData("044 Meier")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ein_Name_ist_nicht_waehlbar(string input)
    {
        // C4: seit ADR-046 ist das Nummernfeld auch das Suchfeld. Ohne diese
        // Unterscheidung löste die Eingabetaste auf einem Namen einen Anruf
        // an sip:Meier@… aus — Normalize() reicht benannte Ziele
        // absichtlich durch, damit aus «112» nie «+41112» wird.
        Assert.False(NumberNormalizer.IsDialable(input));
    }

    [Fact]
    public void Waehlbar_haengt_nicht_am_Laenderpraefix()
    {
        // Eine reine Funktion: ob etwas wählbar IST, hat mit dem Land nichts
        // zu tun — nur, wohin es normalisiert wird.
        Assert.True(NumberNormalizer.IsDialable("044 512 84 30"));
        Assert.False(NumberNormalizer.IsDialable("Meier"));
    }

    // ------------------------------------------------- Die Klammer-Null (C2)

    [Theory]
    [InlineData("+41 (0)79 123 45 67", "+41791234567")]
    [InlineData("0041 (0)79 123 45 67", "+41791234567")]
    [InlineData("+41(0)791234567", "+41791234567")]
    [InlineData("+41 (0) 79 123 45 67", "+41791234567")]
    [InlineData("+49 (0)30 123456", "+4930123456")]
    public void Die_Klammer_Null_nach_der_Landesvorwahl_faellt_weg(string input, string expected)
    {
        // C2: das ist die Schreibweise aus Outlook, aus vCards und aus jeder
        // zweiten E-Mail-Signatur. Bis zum 13.09.2026 blieb die 0 als Ziffer
        // stehen — «+41 (0)79 …» wurde zu «+410791234567» gewählt, und zwar
        // ohne Fehlermeldung: die Nummer sah plausibel aus und ging ins Leere.
        //
        // Die Klammer-Null IST die nationale Verkehrsausscheidungsziffer und
        // gehört weg, sobald eine Landesvorwahl davorsteht — genau dafür
        // schreibt man sie überhaupt in Klammern.
        Assert.Equal(expected, Swiss.Normalize(input));
    }

    [Theory]
    [InlineData("079 (0)123", "079(0)123")]
    [InlineData("044 (0) 512", "044(0)512")]
    public void Ohne_Landesvorwahl_bleibt_die_Klammer_Null_eine_Ziffer(string input, string roh)
    {
        // Die Regel greift NUR hinter einer Landesvorwahl. Ohne sie ist die
        // Null in Klammern keine Verkehrsausscheidungsziffer, sondern einfach
        // eine Ziffer, die jemand getippt hat — und die darf nicht
        // stillschweigend verschwinden.
        //
        // Was hier herauskommt, ist dasselbe wie vor der Änderung: die
        // Klammern fliegen raus, die Ziffern bleiben.
        var erwartet = Swiss.Normalize(roh);

        Assert.Equal(erwartet, Swiss.Normalize(input));
    }

    [Theory]
    [InlineData("+41 (1)79 123 45 67")]   // nur die Null ist gemeint
    [InlineData("+4179 (0)123")]          // Landesvorwahl ist höchstens dreistellig
    public void Nur_eine_Null_direkt_hinter_der_Vorwahl_faellt_weg(string input)
    {
        // Die Gegenprobe: was nicht genau dieses Muster ist, wird nicht
        // angefasst. Eine Regel, die «irgendeine Klammer irgendwo» entfernt,
        // würde aus einer vertippten Nummer stillschweigend eine andere.
        var ohneKlammern = input.Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);

        Assert.Equal(Swiss.Normalize(ohneKlammern), Swiss.Normalize(input));
    }

    [Fact]
    public void Die_Klammer_Null_macht_eine_Nummer_nicht_unwaehlbar()
    {
        // IsDialable kennt Klammern schon; die Zusage steht hier, damit
        // niemand sie beim Aufräumen verliert.
        Assert.True(NumberNormalizer.IsDialable("+41 (0)79 123 45 67"));
    }
}
