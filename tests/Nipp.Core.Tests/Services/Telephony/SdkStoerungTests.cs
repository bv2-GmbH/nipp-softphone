using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Ob eine SDK-Protokollzeile eine Audiostörung meldet (ADR-077).
///
/// <para><b>Die Zeilen stehen hier wörtlich</b>, und zwar so, wie sie am
/// 08.10.2026 aus <c>nipp-20261008.log</c> kopiert wurden — mitsamt der
/// Schreibweise «miliseconds» mit einem l, die das SDK benutzt. Das ist der
/// Zweck dieser Tests: <b>wenn eine SDK-Fassung ihre Texte ändert, hört die
/// Zählung still auf zu zählen</b>, und ein Bericht ohne Störungen sieht
/// aus wie ein gutes Gespräch. Diese Datei ist die Stelle, an der das auffällt
/// — nicht am Gerät, und nicht erst, wenn jemand sich wundert.</para>
/// </summary>
public class SdkStoerungTests
{
    /// <summary>
    /// Die häufigste Störung im Alltag: 52 davon an einem Tag. Die Zahl steht
    /// <b>vor</b> dem Anker, und sie ist die aufgelaufene Latenz.
    /// </summary>
    [Fact]
    public void Verwurf_mit_Millisekunden()
    {
        var s = SdkStoerung.Lies(
            "mswasapi: output buffer was filled with at least 46 ms in the last 5000 ms, asking to drop.");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.Verwurf, s!.Value.Art);
        Assert.Equal(46, s.Value.Wert);
    }

    /// <summary>
    /// <b>Die 5000 im selben Satz darf nicht gewinnen.</b> Beide Zahlen tragen
    /// «ms», und die zweite ist die grössere — eine Suche nach «der ersten
    /// Zahl» oder «irgendeiner Zahl» hätte hier 5000 ms Latenz gemeldet und
    /// jeden Bericht unbrauchbar gemacht.
    /// </summary>
    [Fact]
    public void Verwurf_verwechselt_das_Zeitfenster_nicht()
    {
        var s = SdkStoerung.Lies(
            "mswasapi: output buffer was filled with at least 20 ms in the last 5000 ms, asking to drop.");

        Assert.Equal(20, s!.Value.Wert);
    }

    /// <summary>
    /// <b>Der Fall, den man hört</b> — und der bis zum 12.10.2026 als Verwurf
    /// mitgezählt wurde, obwohl er etwas anderes ist.
    ///
    /// <para>Der Wortlaut stammt aus <c>mediastreamer2.dll</c> der Fassung
    /// 5.5.18; am 12.10.2026 stand er in keinem Protokoll, weil die sanfte
    /// Strategie an diesem Tag jedes Mal eine stille Stelle fand. <b>Dass ein
    /// Muster nie auslöst, ist kein Grund, es nicht zu prüfen</b>: tut es das
    /// eines Tages doch, ist es die einzige Zahl im Bericht, die zählt.</para>
    /// </summary>
    [Fact]
    public void Verwurf_ohne_stille_Stelle_ist_der_hoerbare()
    {
        var s = SdkStoerung.Lies(
            "MSAudioFlowControl: too many samples to drop, dropping entire frame.");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.VerwurfHoerbar, s!.Value.Art);
    }

    /// <summary>
    /// Beide Texte enthalten «to drop», und die Reihenfolge der Prüfungen
    /// entscheidet. Verwechselt sie sich, meldet der Bericht jeden harmlosen
    /// Verwurf als hörbar — oder, schlimmer, umgekehrt.
    /// </summary>
    [Fact]
    public void Die_beiden_Verwurfarten_werden_nicht_verwechselt()
    {
        var harmlos = SdkStoerung.Lies(
            "mswasapi: output buffer was filled with at least 23 ms in the last 5000 ms, asking to drop.");
        var hoerbar = SdkStoerung.Lies(
            "MSAudioFlowControl: too many samples to drop, dropping entire frame.");

        Assert.Equal(StoerungsArt.Verwurf, harmlos!.Value.Art);
        Assert.Equal(StoerungsArt.VerwurfHoerbar, hoerbar!.Value.Art);
    }

    /// <summary>
    /// Die Anfrage der Flusskontrolle auf Debug-Stufe ist <b>dieselbe
    /// Störung</b> wie die Warnung von mswasapi, nur von der anderen Seite
    /// gemeldet — gezählt wird sie nicht, sonst stünde jeder Verwurf auf
    /// Debug doppelt im Bericht und die Protokollstufe veränderte die Zahl.
    /// </summary>
    [Fact]
    public void Die_Debug_Anfrage_wird_nicht_mitgezaehlt()
    {
        Assert.Null(SdkStoerung.Lies("MSAudioFlowControl: requested to drop 23 ms "));
    }

    /// <summary>
    /// Der volle Ausgabepuffer beim Schreiben (ADR-077 Nachtrag, L4 in
    /// <c>LOGBEFUNDE-PLAN.md</c>) — 149 Vorkommen in vierzehn Tagen, davon 80
    /// allein am 30.10.2026 innerhalb einer Minute. Der Wert sind
    /// <b>Abtastwerte</b>, keine Millisekunden — die 9600 und die 9120 in der
    /// Klammer sind Teil der Zeile und dürfen nicht gegriffen werden.
    /// </summary>
    [Fact]
    public void Ausgabepuffer_voll_mit_Abtastwerten()
    {
        var s = SdkStoerung.Lies(
            "mswasapi: cannot write output buffer of 480 samples, not enough space [480=9600-9120]");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.AusgabepufferVoll, s!.Value.Art);
        Assert.Equal(480, s.Value.Wert);
    }

    /// <summary>
    /// <b>Nicht dasselbe wie <see cref="StoerungsArt.Pufferfehler"/>.</b> Ein
    /// Gespräch kann beide Zeilen gleichzeitig enthalten, und wer sie
    /// zusammenzählt, kann sie nie wieder trennen (L4).
    /// </summary>
    [Fact]
    public void Ausgabepuffer_voll_und_Pufferfehler_werden_nicht_verwechselt()
    {
        var voll = SdkStoerung.Lies(
            "mswasapi: cannot write output buffer of 480 samples, not enough space [480=9600-9120]");
        var fehlt = SdkStoerung.Lies(
            "mswasapi: Could not get buffer from the MSWASAPI audio output interface 960 [0x88890006]");

        Assert.Equal(StoerungsArt.AusgabepufferVoll, voll!.Value.Art);
        Assert.Equal(StoerungsArt.Pufferfehler, fehlt!.Value.Art);
    }

    /// <summary>
    /// Der höchste Wert, der am 08.10.2026 gemessen wurde — auf einem Ticker,
    /// der alle 10 ms läuft.
    /// </summary>
    [Fact]
    public void Ticker_zu_spaet_mit_Millisekunden()
    {
        var s = SdkStoerung.Lies("MSAudio MSTicker: We are late of 144 miliseconds.");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.TickerZuSpaet, s!.Value.Art);
        Assert.Equal(144, s.Value.Wert);
    }

    /// <summary>
    /// Die zweite, später gefundene Ticker-Meldung (L4) — anderer Wortlaut als
    /// <see cref="StoerungsArt.TickerZuSpaet"/>, deshalb eine eigene Art und
    /// keine Verwechslung der beiden Prüfungen.
    /// </summary>
    [Fact]
    public void Ticker_Aufwachen_zu_spaet_mit_Millisekunden()
    {
        var s = SdkStoerung.Lies("MSTicker: late wakeup by 20 ms");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.TickerAufwachenSpaet, s!.Value.Art);
        Assert.Equal(20, s.Value.Wert);
    }

    /// <summary>
    /// Beide Ticker-Zeilen in einem Gespräch: keine erschlägt die andere.
    /// </summary>
    [Fact]
    public void Die_beiden_Tickerarten_werden_nicht_verwechselt()
    {
        var spaet = SdkStoerung.Lies("MSAudio MSTicker: We are late of 144 miliseconds.");
        var aufwachen = SdkStoerung.Lies("MSTicker: late wakeup by 20 ms");

        Assert.Equal(StoerungsArt.TickerZuSpaet, spaet!.Value.Art);
        Assert.Equal(StoerungsArt.TickerAufwachenSpaet, aufwachen!.Value.Art);
    }

    /// <summary>
    /// Der Pufferfehler trägt keine Dauer, nur die Tatsache — die Zahl in der
    /// Zeile ist die Puffergrösse, nicht die Störung.
    /// </summary>
    [Fact]
    public void Pufferfehler_ohne_Wert()
    {
        var s = SdkStoerung.Lies(
            "mswasapi: Could not get buffer from the MSWASAPI audio output interface 960 [0x88890006]");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.Pufferfehler, s!.Value.Art);
        Assert.Equal(0, s.Value.Wert);
    }

    /// <summary>
    /// Die Zeile, die ADR-006 Punkt 2 und ADR-076 trägt. Der Wert ist eine
    /// <b>Abtastrate</b>, keine Dauer — darum steht im Bericht «Hz» daneben.
    /// </summary>
    [Fact]
    public void Echo_abgeschaltet_mit_Rate()
    {
        var s = SdkStoerung.Lies(
            "Echo canceller does not support sampling rate 8000Hz, so it has been disabled");

        Assert.NotNull(s);
        Assert.Equal(StoerungsArt.EchoAbgeschaltet, s!.Value.Art);
        Assert.Equal(8000, s.Value.Wert);
    }

    /// <summary>
    /// <b>Der häufigste Fall ist «nichts».</b> Die Prüfung läuft über jede
    /// Zeile des SDK, und bei Debug-Stufe sind das an einem Tag mehr als
    /// 200 000.
    /// </summary>
    [Theory]
    [InlineData("belle-sip: channel [0000]: keep alive sent to [UDP://example:5060]")]
    [InlineData("mswasapi: output initialized for [Default Playback] at 48000 Hz")]
    [InlineData("MSSimpleQosAnalyzer: lost_percentage=0.000000, int_jitter=1.375000 ms")]
    [InlineData("")]
    public void Gewoehnliche_Zeilen_sind_keine_Stoerung(string zeile)
    {
        Assert.Null(SdkStoerung.Lies(zeile));
    }

    /// <summary>
    /// Eine abgeschnittene Zeile darf nicht werfen — das SDK protokolliert aus
    /// einem fremden Rahmen, und eine Ausnahme käme dort nie zurück (ADR-053).
    /// </summary>
    [Fact]
    public void Abgeschnittene_Zeile_ergibt_null_statt_Ausnahme()
    {
        var s = SdkStoerung.Lies("MSAudio MSTicker: We are late of ");

        Assert.NotNull(s);
        Assert.Equal(0, s!.Value.Wert);
    }
}
