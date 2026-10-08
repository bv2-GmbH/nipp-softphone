using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Sammelt über ein Gespräch hinweg, was danach zur Beurteilung gebraucht
/// wird, und fasst es in einen Satz Zahlen (§8.2, ADR-077).
///
/// <para><b>Warum es das gibt.</b> Am 08.10.2026 ist eine gemeldete
/// Sprachqualität («ein paar Mal Probleme, vor allem beim letzten Call») aus
/// 57 MB Rohprotokoll von Hand rekonstruiert worden — Gespräche aus
/// Zustandswechseln zusammengesucht, Störungen über Zeitfenster zugeordnet,
/// Verlustwerte aus verstreuten Analysatorzeilen gelesen. Das hat eine Stunde
/// gekostet und ist jedes Mal neu zu tun. <b>Die Zahlen waren alle da, nur
/// nicht an einer Stelle.</b></para>
///
/// <para><b>Was hier bewusst nicht entschieden wird:</b> ob ein Gespräch gut
/// war. Der Bericht urteilt nicht und stuft nicht ein — dafür gibt es
/// <see cref="CallQuality.Rating"/> in der Oberfläche, und das beantwortet
/// eine andere Frage (was der Benutzer <i>jetzt</i> sieht). Hier stehen die
/// Messwerte, damit jemand sie später vergleichen kann. <b>Ein Mittelwert und
/// ein Maximum beantworten nie dieselbe Frage</b> (CLAUDE.md, 16.09.2026),
/// deshalb trägt der Bericht beide.</para>
/// </summary>
public sealed class GespraechsBericht
{
    private readonly List<CallQuality> _messungen = [];
    private readonly Dictionary<StoerungsArt, (int Anzahl, int Hoechstwert)> _stoerungen = [];

    /// <summary>Wann das Gespräch verbunden wurde.</summary>
    public DateTimeOffset Beginn { get; }

    /// <summary>Der verhandelte Codec, sobald er feststeht.</summary>
    public string? Codec { get; set; }

    /// <summary>Die Abtastrate des Codecs in Hertz; <c>0</c>, solange unbekannt.</summary>
    public int Abtastrate { get; set; }

    /// <summary>Das Aufnahmegerät, wie das SDK es nennt.</summary>
    public string? Eingabegeraet { get; set; }

    /// <summary>Das Wiedergabegerät, wie das SDK es nennt.</summary>
    public string? Ausgabegeraet { get; set; }

    /// <summary>
    /// Was das SDK über die Echounterdrückung <b>im Stream</b> sagt, nicht was
    /// eingestellt ist. <c>null</c>, solange nicht gelesen.
    ///
    /// <para>Steht hier <c>true</c> und zählt gleichzeitig
    /// <see cref="StoerungsArt.EchoAbgeschaltet"/>, dann meldet das SDK den
    /// Wunsch und nicht den Zustand — <b>und dann ist die Zählung die
    /// verlässlichere Quelle.</b> Genau dafür stehen beide im Bericht.</para>
    /// </summary>
    public bool? EchoLautSdk { get; set; }

    public GespraechsBericht(DateTimeOffset beginn) => Beginn = beginn;

    /// <summary>Nimmt eine Sekundenmessung auf.</summary>
    public void Erfasse(CallQuality quality) => _messungen.Add(quality);

    /// <summary>Nimmt eine erkannte Störung auf.</summary>
    public void Erfasse(Stoerung stoerung)
    {
        var bisher = _stoerungen.GetValueOrDefault(stoerung.Art);

        _stoerungen[stoerung.Art] = (
            bisher.Anzahl + 1,
            Math.Max(bisher.Hoechstwert, stoerung.Wert));
    }

    /// <summary>Wie oft eine Störungsart vorkam.</summary>
    public int Anzahl(StoerungsArt art) => _stoerungen.GetValueOrDefault(art).Anzahl;

    /// <summary>Der grösste Wert, der zu einer Störungsart gemeldet wurde.</summary>
    public int Hoechstwert(StoerungsArt art) => _stoerungen.GetValueOrDefault(art).Hoechstwert;

    /// <summary>Wie viele Sekundenmessungen eingegangen sind.</summary>
    public int Messungen => _messungen.Count;

    /// <summary>
    /// Der fertige Bericht als eine Zeile, zum Protokollieren.
    ///
    /// <para>Eine Zeile und nicht mehrere, <b>weil sie gegriffen werden
    /// soll</b>: ein <c>Select-String</c> über eine Woche Protokoll liefert
    /// damit eine Tabelle aller Gespräche. Mehrzeilig wäre sie schöner zu
    /// lesen und für genau diesen Zweck unbrauchbar.</para>
    /// </summary>
    /// <param name="ende">Wann das Gespräch endete.</param>
    /// <param name="stufeIstDebug">
    /// Ob das SDK-Protokoll auf Debug stand. <b>Steht hier <c>false</c> und
    /// sind null Störungen gezählt, heisst das nicht «es war alles gut»</b> —
    /// es heisst, dass die Zeilen durchgekommen sein müssten und keine kam.
    /// Der Unterschied gehört in den Bericht, sonst liest ihn jemand falsch.
    /// </param>
    public string AlsZeile(DateTimeOffset ende, bool stufeIstDebug)
    {
        var dauer = ende - Beginn;
        var t = new List<string>
        {
            $"Dauer {(int)dauer.TotalMinutes}:{dauer.Seconds:D2}",
            $"Codec {Codec ?? "?"}/{Abtastrate}",
            $"Echo {EchoText()}",
            $"Aus {Kurz(Ausgabegeraet)}",
            $"Ein {Kurz(Eingabegeraet)}",
        };

        if (_messungen.Count > 0)
        {
            t.Add($"Verlust max {Max(q => q.ReceiverLossPercent):0.0}%");
            t.Add($"Umlauf {Mittel(q => q.RoundTripSeconds * 1000):0} ms (max {Max(q => q.RoundTripSeconds * 1000):0})");
            t.Add($"Puffer {Mittel(q => q.JitterBufferMilliseconds):0} ms (max {Max(q => q.JitterBufferMilliseconds):0})");

            var mos = _messungen.Where(q => q.Mos > 0).Select(q => q.Mos).ToList();
            t.Add(mos.Count > 0
                ? $"MOS {mos.Average():0.0} (min {mos.Min():0.0})"
                : "MOS keiner");
        }
        else
        {
            t.Add("keine Qualitaetsmessung");
        }

        t.Add($"Verwuerfe {Anzahl(StoerungsArt.Verwurf)} (max {Hoechstwert(StoerungsArt.Verwurf)} ms)");
        t.Add($"Ticker spaet {Anzahl(StoerungsArt.TickerZuSpaet)} (max {Hoechstwert(StoerungsArt.TickerZuSpaet)} ms)");
        t.Add($"Pufferfehler {Anzahl(StoerungsArt.Pufferfehler)}");
        t.Add($"Messungen {_messungen.Count}");
        t.Add($"SDK-Stufe {(stufeIstDebug ? "Debug" : "normal")}");

        return string.Join(", ", t);
    }

    private string EchoText()
    {
        var abgeschaltet = Anzahl(StoerungsArt.EchoAbgeschaltet);

        if (abgeschaltet > 0)
        {
            return $"AUS (SDK meldete Abschaltung bei {Hoechstwert(StoerungsArt.EchoAbgeschaltet)} Hz)";
        }

        return EchoLautSdk switch
        {
            true => "an",
            false => "aus",
            _ => "unbekannt",
        };
    }

    private double Mittel(Func<CallQuality, double> wahl) => _messungen.Average(wahl);

    private double Max(Func<CallQuality, double> wahl) => _messungen.Max(wahl);

    /// <summary>
    /// Gerätenamen sind lang («Speakers (Qualcomm(R) Aqstic(TM) Audio Adapter
    /// Device)»). Für den Bericht reicht, was sie unterscheidet.
    /// </summary>
    private static string Kurz(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var kurz = name.Trim();

        return kurz.Length <= 40
            ? kurz
            : string.Concat(kurz.AsSpan(0, 39), "…");
    }
}
