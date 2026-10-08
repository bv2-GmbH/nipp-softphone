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
