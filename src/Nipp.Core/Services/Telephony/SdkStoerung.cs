using System.Globalization;

namespace Nipp.Core.Services.Telephony;

/// <summary>Welche Art von Störung eine SDK-Zeile meldet.</summary>
public enum StoerungsArt
{
    /// <summary>
    /// Der Ausgabepuffer ist vollgelaufen, und die Flusskontrolle wirft Ton
    /// weg (<c>MSAudioFlowControl</c>). Hörbar als Lücke oder Knacken.
    /// </summary>
    Verwurf,

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
}

/// <summary>Eine erkannte Störung: die Art und die Zahl, die dabei stand.</summary>
/// <param name="Art">Worum es geht.</param>
/// <param name="Wert">
/// Millisekunden bei <see cref="StoerungsArt.Verwurf"/> und
/// <see cref="StoerungsArt.TickerZuSpaet"/>, Hertz bei
/// <see cref="StoerungsArt.EchoAbgeschaltet"/>, sonst <c>0</c>.
/// </param>
public readonly record struct Stoerung(StoerungsArt Art, int Wert);

/// <summary>
/// Liest aus einer Protokollzeile des SDK, ob sie eine Audiostörung meldet
/// (W2.1, ADR-077).
///
/// <para><b>Warum nipp seine eigenen Protokollzeilen liest.</b> Das sieht nach
/// einem Umweg aus, ist aber der kurze Weg: diese vier Ereignisse meldet das
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
/// SDK und <b>kein Tippfehler hier</b>.</para>
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

        if (zeile.Contains("We are late of", StringComparison.Ordinal))
        {
            return new Stoerung(StoerungsArt.TickerZuSpaet, ZahlNach(zeile, "We are late of "));
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
