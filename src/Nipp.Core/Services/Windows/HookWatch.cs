namespace Nipp.Core.Services.Windows;

/// <summary>Was ein gemeldeter Gabelzustand bedeutet.</summary>
public enum HookVerdict
{
    /// <summary>
    /// Nichts — derselbe Zustand wie zuvor, oder das Loslassen der Taste.
    /// </summary>
    Verworfen,

    /// <summary>
    /// Der Benutzer hat die Gabeltaste betätigt. <b>In welche Richtung, ist
    /// bedeutungslos</b> — siehe <see cref="HookWatch"/>.
    /// </summary>
    Betaetigung,

    /// <summary>
    /// Die Antwort des Geräts auf einen Report, den nipp gerade geschrieben
    /// hat. Zustand nachziehen, aber nichts tun.
    /// </summary>
    Bestaetigung,
}

/// <summary>
/// Entscheidet, ob ein gemeldeter Gabelzustand ein Tastendruck war.
///
/// <para><b>Der Befund vom 09.09.2026, und er kostete jedes Gespräch.</b>
/// Vorher schrieb <c>HeadsetCallControl.PushState</c> den Gabelzustand des
/// Geräts mit <c>SyncHook(imGespraech)</c> selbst — also in genau das Feld,
/// aus dem der Lese-Thread seine Flanken ableitet. Damit stand dort
/// „abgenommen", sobald ein Gespräch verbunden war, <b>ohne dass das Gerät je
/// eine Taste gemeldet hatte</b>. Der nächste Eingangsreport trug die
/// Hook-Usage nicht — und aus der Abwesenheit einer Usage wurde eine Flanke
/// nach unten, die es physisch nie gab. Gedeutet wurde sie als „auflegen“:
/// 10 ms nach dem Annehmen war das Gespräch weg, bei ausgehenden Anrufen
/// 331 ms nach dem Verbinden. <b>Ein Spiegel dessen, was das Gerät gemeldet
/// hat, darf nur aus Gerätereports gefüllt werden.</b></para>
///
/// <para><b>Warum die Richtung nicht mehr zählt.</b> nipp läuft an drei
/// Headsets im Alltag (Jabra Engage 75, Link 400, PRO 9470), und ob eines
/// seine Gabeltaste als Momentan-Taster meldet (Drücken und Loslassen, zwei
/// Meldungen) oder als Ein/Aus-Schalter (eine Meldung, Zustand gehalten),
/// steht in seinem Report-Deskriptor und nicht in unserem Quelltext. Wer die
/// Bedeutung an die Richtung hängt, wettet auf ein Gerät. Deshalb heisst jede
/// echte Meldung nur „betätigt“; <b>was sie bedeutet, entscheidet der
/// Anrufzustand</b> (<see cref="HeadsetPolicy.Interpret"/>) — dieselbe Regel,
/// die der globale Hotkey seit immer benutzt und die deshalb nie ein Gespräch
/// verloren hat.</para>
///
/// <para>Bleiben zwei Meldungen, die kein Tastendruck sind, und beide werden
/// hier abgefangen: das <b>Loslassen</b> kurz nach dem Drücken, und die
/// <b>Antwort</b> des Geräts auf einen Report, den nipp gerade geschrieben
/// hat.</para>
///
/// <para>Ohne Sperre — wie <c>RingbackWatch</c>. Der Aufrufer hält sie, weil
/// Lese-Thread und UI-Thread hier zusammenkommen.</para>
/// </summary>
public sealed class HookWatch
{
    /// <summary>
    /// Wie lange nach einer Betätigung eine zweite Meldung als Loslassen
    /// gilt.
    ///
    /// <para>Gemessen am 09.09.2026: zwischen Meldung und Gegenmeldung lagen
    /// 4, 10 und 331 Millisekunden. Ein Mensch, der annimmt und es sich
    /// anders überlegt, braucht länger als 700 ms — und wer es nicht tut,
    /// drückt eben noch einmal.</para>
    /// </summary>
    public static readonly TimeSpan Entprellung = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Wie lange nach dem Ende eines eigenen Ausgangsreports die Antwort des
    /// Geräts noch als Echo gilt.
    ///
    /// <para><b>Gemessen am 09.09.2026 am Jabra Engage 75.</b> Das Gerät
    /// antwortete auf den Report 56 ms vor dessen Bestätigung und danach
    /// erneut nach 52, 653 und 253 Millisekunden. 800 ms deckt das mit
    /// Abstand.</para>
    /// </summary>
    public static readonly TimeSpan Echofenster = TimeSpan.FromMilliseconds(800);

    private bool _gemeldet;
    private DateTimeOffset? _schreibenSeit;
    private DateTimeOffset? _schreibenFertig;
    private DateTimeOffset? _letzteBetaetigung;

    /// <summary>Der letzte Stand, den das <b>Gerät</b> gemeldet hat.</summary>
    public bool OffHook => _gemeldet;

    /// <summary>
    /// Ein Ausgangsreport geht an das Gerät.
    ///
    /// <para><b>Ab hier ist jede Gabelmeldung eine Antwort und keine
    /// Absicht</b> — siehe <see cref="Melde"/>.</para>
    /// </summary>
    public void SchreibenBeginnt(DateTimeOffset now)
    {
        _schreibenSeit = now;
        _schreibenFertig = null;
    }

    /// <summary>Das Gerät hat den Ausgangsreport bestätigt (oder abgelehnt).</summary>
    public void SchreibenFertig(DateTimeOffset now)
    {
        _schreibenSeit = null;
        _schreibenFertig = now;
    }

    /// <summary>
    /// Ob ein anderes Programm dieses Gerät im Gespräch hält.
    ///
    /// <para>Drei Bedingungen zusammen: das <b>Gerät</b> meldet abgenommen,
    /// nipp hat keinen eigenen Anruf, und es ist keine eigene Meldung
    /// unterwegs — sonst wäre es deren Echo, und genau diese Verwechslung hat
    /// am 09.09.2026 jedes Gespräch gekostet.</para>
    ///
    /// <para><b>Warum das hier steht.</b> Der Spiegel wird ausschliesslich aus
    /// Gerätereports gefüllt, und das Wissen, ob gerade geschrieben wird, gibt
    /// es nur an dieser Stelle. Eine zweite Fassung davon wäre die
    /// Doppelwahrheit, die den Fehler hervorgebracht hat.</para>
    /// </summary>
    public bool Fremdbelegung(bool eigeneAnrufe, DateTimeOffset now) =>
        _gemeldet
        && !eigeneAnrufe
        && _schreibenSeit is null
        && !(_schreibenFertig is { } fertig && now - fertig <= Echofenster);

    /// <summary>Das Gerät hat einen Gabelzustand gemeldet.</summary>
    public HookVerdict Melde(bool offHook, DateTimeOffset now)
    {
        // Kein Wechsel. Das Gerät schickt seinen Zustand mehrfach — beim
        // Engage 75 kommt er mit jeder anderen Zustandsänderung mit, die im
        // selben Report steht (0x2A, 0x97).
        if (offHook == _gemeldet)
        {
            return HookVerdict.Verworfen;
        }

        _gemeldet = offHook;

        // <b>Während ein Ausgangsreport unterwegs ist, ist jede Gabelmeldung
        // eine Antwort des Geräts.</b>
        //
        // Das ist der Befund vom 09.09.2026, und er hat eine Annahme
        // widerlegt: das Gerät spiegelt den gemeldeten Zustand nicht, es
        // ändert seinen eigenen. Auf „ein Gespräch läuft" hin gab das Engage
        // 75 seinen Off-Hook-Zustand <b>auf</b> — und weil die Richtung nicht
        // zur Meldung passte, galt das als Tastendruck und beendete das
        // Gespräch drei Sekunden nach dem Annehmen.
        //
        // Deshalb zählt hier nicht die Richtung, sondern der Zeitpunkt: was
        // während eines Schreibvorgangs oder unmittelbar danach kommt, ist
        // Antwort. Ein Schreibvorgang dauert normalerweise eine Millisekunde;
        // an diesem Gerät waren es <b>2,94 Sekunden</b>, und genau dort
        // braucht es den Schutz.
        if (_schreibenSeit is not null
            || (_schreibenFertig is { } fertig && now - fertig <= Echofenster))
        {
            return HookVerdict.Bestaetigung;
        }

        // Das Loslassen der Taste, oder ein prellender Kontakt.
        if (_letzteBetaetigung is { } zuletzt && now - zuletzt < Entprellung)
        {
            return HookVerdict.Verworfen;
        }

        _letzteBetaetigung = now;

        return HookVerdict.Betaetigung;
    }
}
