using System.Globalization;

namespace Nipp.Core.Services.Telephony;

/// <summary>Welche Art von Störung eine SDK-Zeile meldet.</summary>
public enum StoerungsArt
{
    /// <summary>
    /// Der Ausgabepuffer ist vollgelaufen, und die Flusskontrolle wirft Ton
    /// weg (<c>MSAudioFlowControl</c>).
    ///
    /// <para><b>Das heisst nicht, dass es jemand gehört hat</b>, und bis zum
    /// 12.10.2026 stand hier «Hörbar als Lücke oder Knacken». Das war eine
    /// Annahme, und sie war falsch: das SDK läuft mit
    /// <c>flow_control_strategy=soft</c> (gemessen, <c>strategy=[1]</c> im
    /// Protokoll), und die sucht sich zum Wegwerfen eine leise Stelle. An
    /// einem Tag mit 45 Verwürfen über vier Gespräche hat sie <b>45-mal</b>
    /// eine gefunden. Wo sie keine findet, sagt sie es selbst — und das ist
    /// <see cref="VerwurfHoerbar"/>.</para>
    /// </summary>
    Verwurf,

    /// <summary>
    /// Dasselbe, aber die Flusskontrolle fand <b>keine stille Stelle</b> und
    /// hat einen ganzen Frame weggeworfen (<c>too many samples to drop,
    /// dropping entire frame</c>). <b>Das ist der Fall, den man hört.</b>
    ///
    /// <para>Gezählt wird er getrennt, weil er sonst in den Verwürfen
    /// untergeht: eine Zahl, die beides zusammenfasst, kann richtig sein und
    /// beantwortet trotzdem die falsche Frage (CLAUDE.md, 16.09.2026). Am
    /// 12.10.2026 wäre die Antwort «45 Störungen» gewesen, die hörbare Zahl
    /// war <b>null</b>.</para>
    /// </summary>
    VerwurfHoerbar,

    /// <summary>
    /// Der Audio-Ticker ist zu spät gekommen. Er läuft auf 10 ms; alles
    /// darüber ist Stillstand in der Kette, nicht Rechenlast.
    /// </summary>
    TickerZuSpaet,

    /// <summary>
    /// Die Wiedergabe hat vom Gerät keinen Puffer bekommen
    /// (<c>Could not get buffer</c>). Ein einzelner Aussetzer.
    /// </summary>
    Pufferfehler,

    /// <summary>
    /// Der Echo-Canceller hat sich abgeschaltet, weil er die Abtastrate des
    /// Gesprächs nicht beherrscht. Der Wert ist die Rate in Hertz.
    /// </summary>
    EchoAbgeschaltet,

    /// <summary>
    /// Der Ausgabepuffer ist beim Schreiben voll (<c>cannot write output
    /// buffer of N samples, not enough space</c>), gemeldet von
    /// <c>mswasapi</c> als <c>[ERR]</c> — kommt also auch ohne Debug-Stufe
    /// durch. Gemessen vom 17. bis 30.10.2026: 149 Vorkommen in vierzehn
    /// Tagen, davon 80 allein am 30.10. innerhalb einer Minute.
    ///
    /// <para><b>Die Deutung ist offen, und das ist Absicht</b>
    /// (`LOGBEFUNDE-PLAN.md`, L4). Das sieht nach dem Gegenstück zu
    /// <see cref="Pufferfehler"/> aus der anderen Richtung aus — ein voller
    /// Ausgabepuffer statt eines leeren —, und das ist <b>vermutlich</b> so,
    /// aber <b>ungemessen</b>. Ob es hörbar ist, sagt die Zeile nicht. Genau
    /// deshalb wird sie gezählt, bevor jemand daraus eine Ursache macht, und
    /// genau deshalb ist es eine eigene Art: wer sie mit
    /// <see cref="Pufferfehler"/> oder <see cref="Verwurf"/> zusammenzählt,
    /// kann sie nie wieder trennen.</para>
    ///
    /// <para>Der Wert ist die Zahl der <b>Abtastwerte</b>, die nicht mehr
    /// hineinpassten (z. B. <c>480</c>) — <b>keine Millisekunden</b>. Wer sie
    /// mit den Verwurf-Millisekunden vergleicht, vergleicht zwei verschiedene
    /// Einheiten.</para>
    /// </summary>
    AusgabepufferVoll,

    /// <summary>
    /// Der Audio-Ticker ist beim Aufwachen zu spät dran
    /// (<c>MSTicker: late wakeup by N ms</c>), als <c>[ERR]</c> gemeldet.
    /// Gemessen vom 17. bis 30.10.2026: 8 Vorkommen in vierzehn Tagen.
    ///
    /// <para>Das ist <b>nicht dieselbe Zeile</b> wie
    /// <see cref="TickerZuSpaet"/> (<c>We are late of N miliseconds</c>) —
    /// unterschiedlicher Wortlaut, und beide werden getrennt gezählt, statt
    /// die zweite in die erste einzusortieren.</para>
    /// </summary>
    TickerAufwachenSpaet,
}

/// <summary>Eine erkannte Störung: die Art und die Zahl, die dabei stand.</summary>
/// <param name="Art">Worum es geht.</param>
/// <param name="Wert">
/// Millisekunden bei <see cref="StoerungsArt.Verwurf"/>,
/// <see cref="StoerungsArt.TickerZuSpaet"/> und
/// <see cref="StoerungsArt.TickerAufwachenSpaet"/>, Abtastwerte bei
/// <see cref="StoerungsArt.AusgabepufferVoll"/>, Hertz bei
/// <see cref="StoerungsArt.EchoAbgeschaltet"/>, sonst <c>0</c>.
/// </param>
public readonly record struct Stoerung(StoerungsArt Art, int Wert);

/// <summary>
/// Liest aus einer Protokollzeile des SDK, ob sie eine Audiostörung meldet
/// (W2.1, ADR-077).
///
/// <para><b>Warum nipp seine eigenen Protokollzeilen liest.</b> Das sieht nach
/// einem Umweg aus, ist aber der kurze Weg: diese sieben Ereignisse meldet das
/// SDK <b>ausschliesslich</b> über das Protokoll — es gibt keine Zählung, kein
/// Ereignis und keinen Statistikwert dafür. Die Filterstatistik mit den
/// Spitzenlasten erscheint erst beim Streamende und nur auf Debug-Stufe, und
/// Debug kostet rund 20 MB am Tag. Was hier gezählt wird, kommt dagegen schon
/// als <c>Warning</c> und <c>Error</c> durch — also auch im Alltagsbetrieb.</para>
///
/// <para><b>Woher die Muster stammen.</b> Aus den Protokollen vom 05. bis zum
/// 08.10.2026, 25 verbundene Gespräche. Die Zeilen stehen dort wörtlich so:
/// <c>mswasapi: output buffer was filled with at least 46 ms in the last 5000
/// ms, asking to drop.</c> und <c>MSAudio MSTicker: We are late of 144
/// miliseconds.</c> — die Schreibweise «miliseconds» mit einem l ist die des
/// SDK und <b>kein Tippfehler hier</b>. Zwei weitere kamen am 30.10.2026 aus
/// den Protokollen vom 17. bis 30.10.2026 dazu (`LOGBEFUNDE-PLAN.md`, L4):
/// <c>mswasapi: cannot write output buffer of 480 samples, not enough space
/// [480=9600-9120]</c> und <c>MSTicker: late wakeup by 20 ms</c>.</para>
///
/// <para><b>Was passiert, wenn das SDK seine Texte ändert:</b> die Zählung
/// geht auf null, und der Gesprächsbericht meldet «keine Störungen». Das ist
/// die unangenehme Sorte Fehler — er sieht aus wie ein gutes Ergebnis.
/// Deshalb nennt der Bericht bei null Störungen ausdrücklich die Protokollstufe
/// mit, und <c>SdkStoerungTests</c> hält die Zeilen im Wortlaut fest.</para>
/// </summary>
public static class SdkStoerung
{
    /// <summary>
    /// Prüft eine SDK-Zeile. <c>null</c> heisst: keine Störung, und das ist
    /// der weitaus häufigste Fall — die Prüfung läuft über <b>jede</b> Zeile
    /// des SDK und ist deshalb auf billiges Scheitern gebaut.
    /// </summary>
    public static Stoerung? Lies(string zeile)
    {
        if (string.IsNullOrEmpty(zeile))
        {
            return null;
        }

        // Reihenfolge nach Häufigkeit: Verwürfe sind das, was im Alltag
        // vorkommt (52 an einem Tag), Pufferfehler die Ausnahme (2).
        if (zeile.Contains("asking to drop", StringComparison.Ordinal))
        {
            return new Stoerung(StoerungsArt.Verwurf, ZahlVor(zeile, " ms in the last"));
        }

        // Steht vor keiner der anderen Prüfungen zufällig: beide Texte
        // enthalten «to drop», und dieser hier trägt keine Millisekundenzahl,
        // die sich greifen liesse — der Frame ist weg, nicht ein Stück davon.
        if (zeile.Contains("too many samples to drop", StringComparison.Ordinal))
        {
            return new Stoerung(StoerungsArt.VerwurfHoerbar, 0);
        }

        // Das Gegenstück zu "asking to drop" aus der anderen Richtung: dort
        // ist der Puffer leer gelaufen, hier läuft er beim Schreiben voll.
        // Kollidiert mit keinem der Texte oben oder unten — «cannot write
        // output buffer» kommt in keiner der anderen Zeilen vor, und
        // «Could not get buffer» unten ist ein eigener, kürzerer Text ohne
        // «write» und ohne «samples».
        if (zeile.Contains("cannot write output buffer of", StringComparison.Ordinal))
        {
            return new Stoerung(
                StoerungsArt.AusgabepufferVoll,
                ZahlNach(zeile, "cannot write output buffer of "));
        }

        if (zeile.Contains("We are late of", StringComparison.Ordinal))
        {
            return new Stoerung(StoerungsArt.TickerZuSpaet, ZahlNach(zeile, "We are late of "));
        }

        // Eine andere Meldung desselben Tickers, mit anderem Wortlaut — «late
        // wakeup by» kommt nicht in «We are late of … miliseconds» vor und
        // umgekehrt, die beiden Prüfungen können also in beliebiger
        // Reihenfolge stehen. Getrennt gezählt, weil die Zeilen getrennt sind.
        if (zeile.Contains("late wakeup by", StringComparison.Ordinal))
        {
            return new Stoerung(
                StoerungsArt.TickerAufwachenSpaet,
                ZahlNach(zeile, "late wakeup by "));
        }

        if (zeile.Contains("Could not get buffer", StringComparison.Ordinal))
        {
            return new Stoerung(StoerungsArt.Pufferfehler, 0);
        }

        if (zeile.Contains("Echo canceller does not support sampling rate", StringComparison.Ordinal))
        {
            return new Stoerung(
                StoerungsArt.EchoAbgeschaltet,
                ZahlNach(zeile, "sampling rate "));
        }

        return null;
    }

    /// <summary>Die Zahl unmittelbar vor einem Anker («… 46 ms in the last …»).</summary>
    private static int ZahlVor(string zeile, string anker)
    {
        var ende = zeile.IndexOf(anker, StringComparison.Ordinal);

        if (ende <= 0)
        {
            return 0;
        }

        var start = ende;

        while (start > 0 && char.IsAsciiDigit(zeile[start - 1]))
        {
            start--;
        }

        return Teil(zeile, start, ende);
    }

    /// <summary>Die Zahl unmittelbar nach einem Anker («… late of 144 …»).</summary>
    private static int ZahlNach(string zeile, string anker)
    {
        var start = zeile.IndexOf(anker, StringComparison.Ordinal);

        if (start < 0)
        {
            return 0;
        }

        start += anker.Length;
        var ende = start;

        while (ende < zeile.Length && char.IsAsciiDigit(zeile[ende]))
        {
            ende++;
        }

        return Teil(zeile, start, ende);
    }

    private static int Teil(string zeile, int start, int ende) =>
        ende > start
        && int.TryParse(
            zeile.AsSpan(start, ende - start),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var wert)
            ? wert
            : 0;
}
