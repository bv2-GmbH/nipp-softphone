namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Welcher Echo-Canceller des SDK benutzt wird und bei welcher Abtastrate er
/// überhaupt arbeitet (§9.4, ADR-006 Punkt 2).
///
/// <para><b>Warum es diese Stelle gibt.</b> Der Filtername und die Raten, bei
/// denen er greift, gehören zusammen. Standen sie getrennt — der Name beim
/// Aufbau des Core, die Rate in der Anzeige —, dann meldete die Oberfläche
/// nach einem Filterwechsel weiter die Grenze des alten Filters. Genau das
/// ist hier passiert: <c>IsEchoCancellationEffective</c> trug die Zahl 8000
/// als Literal, und sie war die Grenze von <c>MSWebRTCAEC</c>.</para>
///
/// <para><b>Was gemessen ist</b> (08.10.2026, vier Protokolltage vom 05. bis
/// zum 08.10.2026, 128 Filterstatistiken): Der Standardfilter des SDK,
/// <c>MSWebRTCAEC</c>, steht in <b>jeder</b> Statistik mit <c>Count 0</c> —
/// er hat in keinem einzigen Gespräch einen Tick gearbeitet. Daneben steht
/// 27 Mal <c>Echo canceller does not support sampling rate 8000Hz, so it has
/// been disabled</c>. Alle 29 Codec-Verhandlungen dieser vier Tage endeten
/// bei PCMU oder PCMA, also bei 8 kHz. Die Echounterdrückung aus §9.4 war
/// damit im ganzen Alltag wirkungslos, obwohl die Einstellung „ein" sagte.</para>
///
/// <para><b>Was nicht gemessen ist</b>, und das ist die Hälfte, die ein
/// Gespräch an der Anlage braucht: dass <c>MSSpeexEC</c> bei 8 kHz
/// <i>tatsächlich</i> läuft. Belegt ist nur, dass der Filter im SDK vorhanden
/// ist (Name und <c>speex_echo_*</c> in <c>mediastreamer2.dll</c>) und dass
/// Speex als Telefonie-Canceller für Schmalband gebaut wurde. <b>Der Beweis
/// ist eine Zeile in der Filterstatistik:</b> steht dort nach einem Gespräch
/// <c>MSSpeexEC</c> mit einem Count über null, greift er. Bleibt er bei null
/// oder erscheint die Abschaltmeldung erneut, ist diese Wahl falsch und
/// gehört zurückgenommen — siehe T332 in <c>docs/test-matrix.md</c>.</para>
/// </summary>
public static class EchoCancellerChoice
{
    /// <summary>
    /// Der Filter, den nipp am Core einstellt.
    ///
    /// <para>Leer gelassen nähme das SDK seinen eigenen Standard, und das ist
    /// auf Windows <c>MSWebRTCAEC</c> — der Filter, der oben in jeder
    /// Statistik auf null steht.</para>
    /// </summary>
    public const string FilterName = "MSSpeexEC";

    /// <summary>
    /// Die Abtastraten, bei denen <see cref="FilterName"/> arbeitet.
    ///
    /// <para><b>Diese Liste ist eine Annahme, bis T332 sie bestätigt</b> —
    /// sie folgt dem Einsatzzweck von Speex (Schmalband-Telefonie), nicht
    /// einer Messung. Sie steht hier und nicht als Zahl im Dienst, damit ein
    /// Messergebnis <b>eine</b> Zeile ändert.</para>
    /// </summary>
    public static readonly IReadOnlyList<int> UnterstuetzteRaten = [8000, 16000, 32000];

    /// <summary>
    /// Ob die Echounterdrückung bei dieser Abtastrate etwas tut.
    /// </summary>
    /// <param name="abtastrate">
    /// Die Rate des verhandelten Codecs in Hertz. <c>0</c> heisst „noch nicht
    /// bekannt" und beantwortet die Frage mit <c>false</c>: solange niemand
    /// weiss, womit gesprochen wird, ist eine Zusage nicht zu halten.
    /// </param>
    public static bool IstWirksamBei(int abtastrate) =>
        UnterstuetzteRaten.Contains(abtastrate);
}
