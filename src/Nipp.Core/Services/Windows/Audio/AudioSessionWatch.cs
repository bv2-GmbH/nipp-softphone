namespace Nipp.Core.Services.Windows.Audio;

/// <summary>
/// Woher die Liste der aktiven Audio-Sitzungen kommt.
///
/// <para><b>Eine Schnittstelle, damit die Entscheidung prüfbar ist.</b> Die
/// echte Quelle spricht COM und braucht ein Gerät; die Frage «gilt das als
/// Fremdbelegung» ist davon unabhängig — und sie kostet im Fehlerfall fremde
/// Gespräche.</para>
/// </summary>
public interface IAudioSessionSource
{
    /// <summary>
    /// Die Prozesskennungen, die auf den Standardgeräten gerade eine
    /// <b>aktive</b> Sitzung halten — Wiedergabe und Aufnahme zusammen.
    ///
    /// <para>Eine leere Liste heisst «niemand», <b>auch dann, wenn die Abfrage
    /// gescheitert ist</b>. Der Unterschied gehört ins Protokoll, nicht in die
    /// Entscheidung: wer bei jedem Fehler schweigt, hat ein Headset ohne
    /// Lampen, sobald irgendetwas an der Audio-Schnittstelle klemmt.</para>
    /// </summary>
    IReadOnlyList<uint> AktiveSitzungen();
}

/// <summary>
/// Ob ein <b>anderes</b> Programm die Audiogeräte gerade benutzt (ADR-068).
///
/// <para><b>Warum das gebraucht wird.</b> nipp teilt das
/// HID-Call-Control-Interface mit anderen Programmen, und jeder Bericht an das
/// Gerät ist eine Mitteilung, die auch die anderen bekommen (ADR-028
/// Nachtrag 4). Bis zum 14.09.2026 hing die Erkennung am Gabelzustand des
/// Geräts — und der sagt nichts: <b>in einem Teams-Meeting meldete das Jabra
/// durchgehend «aufgelegt»</b>, und nipp beendete das Meeting mit seinem
/// Abschlussbericht.</para>
///
/// <para><b>Die Audio-Sitzung sagt es dagegen deutlich</b> (gemessen am
/// 14.09.2026): in Ruhe ist die Liste leer, im Meeting steht das andere
/// Programm in beiden Richtungen darin.</para>
///
/// <para><b>Was sie nicht sagt:</b> ob es dasselbe Gerät ist. Gefragt werden
/// die Standardgeräte; solange das Headset das Standardgerät ist, ist das
/// dasselbe, sonst nicht. <b>Das ist eine Annahme und keine Messung.</b></para>
/// </summary>
public sealed class AudioSessionWatch
{
    private readonly IAudioSessionSource _quelle;
    private readonly uint _eigene;

    /// <param name="quelle">Woher die Sitzungen kommen.</param>
    /// <param name="eigeneProzesskennung">
    /// Die eigene Kennung. <b>Sie muss ausgenommen werden</b>: im eigenen
    /// Gespräch hält nipp dieselben Geräte, und wer das mitzählt, schweigt
    /// genau dann, wenn die Lampen gebraucht werden.
    /// </param>
    public AudioSessionWatch(IAudioSessionSource quelle, uint eigeneProzesskennung)
    {
        ArgumentNullException.ThrowIfNull(quelle);

        _quelle = quelle;
        _eigene = eigeneProzesskennung;
    }

    /// <summary>
    /// Ob gerade ein fremder Prozess eine aktive Sitzung hält.
    ///
    /// <para><b>Im Zweifel nein.</b> Scheitert die Abfrage, gilt das Gerät als
    /// frei — das ist das Verhalten von vor ADR-068 und damit kein
    /// Rückschritt. Andersherum wäre ein Fehler an der Audio-Schnittstelle ein
    /// Headset, das nie wieder etwas anzeigt.</para>
    /// </summary>
    public bool Fremdbelegt()
    {
        IReadOnlyList<uint> sitzungen;

        try
        {
            sitzungen = _quelle.AktiveSitzungen();
        }
        catch (Exception)
        {
            // Die echte Quelle faengt selbst; das hier ist die Zusicherung
            // gegenueber jeder anderen.
            return false;
        }

        foreach (var pid in sitzungen)
        {
            // Die Null steht fuer «kein Prozess» — Systemklaenge zum Beispiel.
            // Sie ist niemand, den man schonen muesste.
            if (pid != 0 && pid != _eigene)
            {
                return true;
            }
        }

        return false;
    }
}
