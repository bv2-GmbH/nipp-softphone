using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Ob eine gemeldete Gabelstellung ein Tastendruck war.
///
/// <para><b>Der Fehler, den diese Tests festhalten, kostete am 09.09.2026
/// jedes Gespräch.</b> Der Gabelzustand des Geräts wurde von nipp selbst
/// mitgeschrieben („ein Gespräch läuft, also ist abgenommen"), und der nächste
/// Eingangsreport ohne Hook-Usage war dadurch eine Flanke nach unten, die es
/// physisch nie gab — gedeutet als „auflegen". Belegt dreimal im Protokoll,
/// darunter ein Fall mit Klick im Toast und ohne jeden Tastendruck:</para>
/// <code>
/// 16:01:57.797  Toast-Aktion: accept
/// 16:01:57.892  Anruf 5e83db16: Incoming -> Connected
/// 16:01:59.458  Auflegen am Headset gedrueckt
/// 16:01:59.547  Anruf 5e83db16: Connected -> Ended
/// </code>
/// </summary>
public class HookWatchTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 9, 10, 17, 0, TimeSpan.Zero);

    private static DateTimeOffset Nach(int ms) => T0 + TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Ein_Wechsel_auf_abgenommen_ist_eine_Betaetigung()
    {
        var watch = new HookWatch();

        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: true, T0));
        Assert.True(watch.OffHook);
    }

    /// <summary>
    /// Solange die Taste gehalten wird, schickt das Gerät denselben Zustand
    /// mehrfach. Wer jeden Report deutet, legt beim Halten mehrmals auf — und
    /// der zweite Aufruf träfe schon das nächste Gespräch.
    /// </summary>
    [Fact]
    public void Derselbe_Zustand_zaehlt_nicht_noch_einmal()
    {
        var watch = new HookWatch();

        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: true, T0));
        Assert.Equal(HookVerdict.Verworfen, watch.Melde(offHook: true, Nach(20)));
        Assert.Equal(HookVerdict.Verworfen, watch.Melde(offHook: true, Nach(40)));
    }

    /// <summary>
    /// <b>Der Momentan-Taster.</b> Drücken und Loslassen sind zwei Meldungen,
    /// aber ein Tastendruck. Ohne die Entprellung wäre die zweite ein zweiter
    /// Druck — und weil beim Loslassen das Gespräch gerade verbunden ist,
    /// hiesse der zweite „auflegen".
    /// </summary>
    [Fact]
    public void Das_Loslassen_kurz_nach_dem_Druecken_ist_kein_zweiter_Druck()
    {
        var watch = new HookWatch();

        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: true, T0));
        Assert.Equal(HookVerdict.Verworfen, watch.Melde(offHook: false, Nach(300)));
    }

    /// <summary>
    /// Die gemessenen Abstände des Befundes: 4, 10 und 331 Millisekunden.
    /// Keiner davon ist ein Mensch, der es sich anders überlegt.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(331)]
    [InlineData(699)]
    public void Die_gemessenen_Abstaende_gelten_alle_als_Loslassen(int ms)
    {
        var watch = new HookWatch();

        watch.Melde(offHook: true, T0);

        Assert.Equal(HookVerdict.Verworfen, watch.Melde(offHook: false, Nach(ms)));
    }

    /// <summary>
    /// <b>Der Ein/Aus-Schalter.</b> Ein Gerät, das seinen Gabelzustand hält,
    /// meldet den nächsten Druck als Wechsel nach unten — und der ist ein
    /// Tastendruck, kein Loslassen. Die Entprellung darf ihn nicht
    /// verschlucken.
    /// </summary>
    [Fact]
    public void Ein_Wechsel_lange_danach_ist_wieder_eine_Betaetigung()
    {
        var watch = new HookWatch();

        watch.Melde(offHook: true, T0);

        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: false, Nach(5000)));
        Assert.False(watch.OffHook);
    }

    /// <summary>
    /// <b>Der Befund vom Gerät, und er widerlegt eine Annahme.</b> Das Jabra
    /// Engage 75 spiegelt den gemeldeten Zustand nicht — es gibt seinen
    /// eigenen Off-Hook-Zustand <b>auf</b>, wenn nipp „ein Gespräch läuft"
    /// meldet. Die echte Aufzeichnung:
    /// <code>
    /// 26.655  Usages 0x2A 0x97 0x20   Gabel=true    echter Druck
    /// 26.762  Incoming -&gt; Connected                nipp schreibt „imGespraech"
    /// 29.645  Usages 0x2A 0x97        Gabel=false   Antwort des Geraets
    /// 29.647  Auflegen am Headset gedrueckt         &lt;- war der Fehler
    /// 29.701  Zustand gemeldet, angenommen=true     Schreibvorgang zurueck
    /// </code>
    /// Deshalb zählt nicht die Richtung, sondern der Zeitpunkt: was während
    /// eines Schreibvorgangs kommt, ist Antwort.
    /// </summary>
    [Fact]
    public void Waehrend_ein_Report_unterwegs_ist_legt_keine_Meldung_auf()
    {
        var watch = new HookWatch();

        // Der Benutzer nimmt am Geraet ab.
        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: true, Nach(26655)));

        // nipp meldet „ein Gespraech laeuft" — der Report ist 2,94 s unterwegs.
        watch.SchreibenBeginnt(Nach(26762));

        // Das Geraet antwortet mitten darin, und zwar in die andere Richtung.
        Assert.Equal(HookVerdict.Bestaetigung, watch.Melde(offHook: false, Nach(29645)));

        watch.SchreibenFertig(Nach(29701));
    }

    /// <summary>
    /// Und die Nachbeben: dasselbe Gerät meldete nach dem Schreibvorgang
    /// erneut, 52 ms und 653 ms danach. Auch das ist Antwort.
    /// </summary>
    [Theory]
    [InlineData(52)]
    [InlineData(253)]
    [InlineData(653)]
    [InlineData(800)]
    public void Kurz_nach_einem_Report_ist_eine_Meldung_noch_Antwort(int ms)
    {
        var watch = new HookWatch();

        watch.SchreibenBeginnt(T0);
        watch.SchreibenFertig(Nach(100));

        Assert.Equal(HookVerdict.Bestaetigung, watch.Melde(offHook: true, Nach(100 + ms)));
    }

    /// <summary>
    /// <b>Aber nicht für immer.</b> Sonst wäre nach dem ersten Gespräch keine
    /// Taste mehr zu gebrauchen — und der eigentliche Zweck des Geräts weg.
    /// </summary>
    [Fact]
    public void Lange_nach_einem_Report_ist_eine_Meldung_wieder_eine_Betaetigung()
    {
        var watch = new HookWatch();

        watch.SchreibenBeginnt(T0);
        watch.SchreibenFertig(Nach(100));

        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: true, Nach(2000)));
    }

    /// <summary>
    /// Ohne jeden Schreibvorgang bleibt die Meldung eine Betätigung — das ist
    /// der Normalfall im stehenden Gespräch, in dem aufgelegt werden soll.
    /// </summary>
    [Fact]
    public void Ohne_Schreibvorgang_ist_jeder_Wechsel_eine_Betaetigung()
    {
        var watch = new HookWatch();

        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: true, T0));
        Assert.Equal(HookVerdict.Betaetigung, watch.Melde(offHook: false, Nach(4000)));
    }

    /// <summary>
    /// <b>Die vollständige Aufzeichnung vom 09.09.2026, 12:20:22 bis
    /// 12:20:31.</b> Zwölf Gerätemeldungen, ein Tastendruck. Vorher wurden
    /// daraus zwei — und der zweite beendete das Gespräch.
    /// </summary>
    [Fact]
    public void Die_Aufzeichnung_vom_Geraet_ergibt_genau_eine_Betaetigung()
    {
        var watch = new HookWatch();
        var betaetigungen = new List<int>();

        void Melde(bool offHook, int ms)
        {
            if (watch.Melde(offHook, Nach(ms)) == HookVerdict.Betaetigung)
            {
                betaetigungen.Add(ms);
            }
        }

        // Das Geraet meldet sich, waehrend es klingelt (0x97, dann 0x2A 0x97).
        Melde(false, 22190);
        Melde(false, 22232);

        // Der Druck: 0x20 kommt hinzu.
        Melde(true, 26655);

        // nipp meldet „ein Gespraech laeuft" — 2,94 s unterwegs.
        watch.SchreibenBeginnt(Nach(26762));
        Melde(false, 29645);          // Antwort des Geraets, mitten im Schreiben
        watch.SchreibenFertig(Nach(29701));

        Melde(false, 29703);          // kein Wechsel
        Melde(true, 29753);           // Nachbeben
        Melde(true, 30354);           // Nachbeben

        // Das Gespraech endet, nipp meldet den Ruhezustand.
        watch.SchreibenBeginnt(Nach(29840));
        watch.SchreibenFertig(Nach(30368));
        Melde(false, 30621);
        Melde(false, 31295);

        Assert.Equal([26655], betaetigungen);
    }

    /// <summary>
    /// <b>Die Rundreise am Momentan-Taster.</b> Klingeln, drücken, loslassen,
    /// verbunden — später drücken, loslassen, beendet. Genau zwei
    /// Betätigungen, und nicht vier.
    /// </summary>
    [Fact]
    public void Ein_Gespraech_am_Momentan_Taster_ergibt_genau_zwei_Betaetigungen()
    {
        var watch = new HookWatch();
        var betaetigungen = 0;

        void Melde(bool offHook, int ms)
        {
            if (watch.Melde(offHook, Nach(ms)) == HookVerdict.Betaetigung)
            {
                betaetigungen++;
            }
        }

        // Annehmen: druecken, loslassen.
        Melde(true, 0);
        Melde(false, 280);

        // nipp meldet „ein Gespraech laeuft".
        watch.SchreibenBeginnt(Nach(300));
        watch.SchreibenFertig(Nach(340));

        // Auflegen: druecken, loslassen.
        Melde(true, 12000);
        Melde(false, 12300);

        // Und das Ende des Gespraechs wird gemeldet.
        watch.SchreibenBeginnt(Nach(12400));
        watch.SchreibenFertig(Nach(12440));

        Assert.Equal(2, betaetigungen);
    }

    /// <summary>
    /// Ein anderes Programm hält das Gerät im Gespräch: es meldet abgenommen,
    /// nipp hat keinen Anruf, und keine eigene Meldung war unterwegs.
    ///
    /// <para>Am 10.09.2026 im Protokoll belegt — beim Beitritt zu einem
    /// Teams-Meeting um 13:29:25, eine Minute vor dem Termin im Kalender.</para>
    /// </summary>
    [Fact]
    public void Abgenommen_ohne_eigenen_Anruf_ist_eine_Fremdbelegung()
    {
        var watch = new HookWatch();

        watch.Melde(offHook: true, T0);

        Assert.True(watch.Fremdbelegung(eigeneAnrufe: false, Nach(5000)));
    }

    [Fact]
    public void Mit_eigenem_Anruf_ist_es_keine_Fremdbelegung()
    {
        var watch = new HookWatch();

        watch.Melde(offHook: true, T0);

        Assert.False(watch.Fremdbelegung(eigeneAnrufe: true, Nach(5000)));
    }

    /// <summary>
    /// Das Echo einer eigenen Meldung ist keine Fremdbelegung — dieselbe
    /// Verwechslung hat am 09.09.2026 jedes Gespräch gekostet, nur in der
    /// anderen Richtung.
    /// </summary>
    [Fact]
    public void Das_Echo_einer_eigenen_Meldung_ist_keine_Fremdbelegung()
    {
        var watch = new HookWatch();

        watch.SchreibenBeginnt(Nach(100));
        watch.Melde(offHook: true, Nach(150));

        Assert.False(watch.Fremdbelegung(eigeneAnrufe: false, Nach(160)));

        watch.SchreibenFertig(Nach(200));

        // Im Echofenster weiterhin nicht.
        Assert.False(watch.Fremdbelegung(eigeneAnrufe: false, Nach(400)));

        // Danach schon: das Geraet haelt einen Zustand, den nipp nicht mehr
        // erklaeren kann.
        Assert.True(
            watch.Fremdbelegung(eigeneAnrufe: false, Nach(200) + HookWatch.Echofenster
                + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void Aufgelegt_ist_keine_Fremdbelegung()
    {
        var watch = new HookWatch();

        watch.Melde(offHook: true, T0);
        watch.Melde(offHook: false, Nach(2000));

        Assert.False(watch.Fremdbelegung(eigeneAnrufe: false, Nach(3000)));
    }

    /// <summary>
    /// <b>Der blinde Fleck, festgeschrieben.</b> Ohne einen einzigen
    /// Gerätereport behauptet nipp keine Fremdbelegung — ein Meeting, das
    /// schon lief, als nipp startete, wird also nicht erkannt. Wer das ändern
    /// will, braucht <c>HidD_GetInputReport</c>, und das ist wieder die
    /// Control-Pipe, die 83 Sekunden gekostet hat (ADR-028 Nachtrag 3).
    /// </summary>
    [Fact]
    public void Ohne_jeden_Geraetereport_wird_keine_Fremdbelegung_behauptet()
    {
        var watch = new HookWatch();

        Assert.False(watch.Fremdbelegung(eigeneAnrufe: false, T0));
    }
}
