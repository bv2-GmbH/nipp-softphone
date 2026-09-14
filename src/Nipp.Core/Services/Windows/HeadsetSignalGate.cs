namespace Nipp.Core.Services.Windows;

/// <summary>Weshalb nipp dem Gerät etwas zu melden hätte.</summary>
public enum SignalAnlass
{
    /// <summary>Ein eigener Anruf — er klingelt, läuft oder ist stumm.</summary>
    Anruf,

    /// <summary>Das Gerät wurde gerade gefunden und angebunden.</summary>
    Anbinden,

    /// <summary>Der eigene Anruf ist vorbei, das Gerät soll aufräumen.</summary>
    Ende,
}

/// <summary>Ob ein Ausgangsbericht hinausgeht, und warum nicht.</summary>
/// <param name="Schreiben">Ob gemeldet wird.</param>
/// <param name="Grund">
/// Der Grund, in der Sprache des Protokolls. <b>Je Zweig ein eigener</b> — ein
/// Gate, dessen Gründe im Protokoll gleich aussehen, macht „ich fliege
/// trotzdem raus" wieder ununterscheidbar von „die Erkennung hat nicht
/// angeschlagen".
/// </param>
public readonly record struct SignalUrteil(bool Schreiben, string Grund);

/// <summary>
/// Ob dem Headset ein Zustand gemeldet werden darf (§22.5).
///
/// <para><b>Der Befund vom 10.09.2026.</b> „Bin ich in einem Teams-Meeting und
/// es klingelt auf nipp, fliege ich aus dem Meeting" — schon beim Läuten, ohne
/// dass jemand eine Taste berührt. Die Kette: nipp schreibt den Ring-Bericht,
/// das Gerät <b>verhandelt</b> seinen Zustand (ADR-028 Nachtrag 2) und meldet
/// den Wechsel auf der Eingangspipe zurück, und weil das Handle geteilt
/// geöffnet ist, bekommt Teams dieselbe Meldung. Teams hat kein
/// <see cref="HookWatch"/>: für Teams ist das ein Tastendruck des Benutzers,
/// und der bedeutet im Meeting auflegen.</para>
///
/// <para><b>Das ist derselbe Fehler wie in ADR-028 Nachtrag 1, eine
/// Prozessgrenze weiter.</b> Dort erfand nipp sich selbst Ereignisse, weil es
/// die eigene Absicht in den Gerätespiegel schrieb. Hier erfindet nipp
/// Ereignisse <b>für ein anderes Programm</b>. Ein geteiltes Handle heisst
/// geteilte Wirkung: <b>ein Ausgangsbericht ist keine Lampe, sondern eine
/// Mitteilung an ein Gerät, das nipp mit anderen teilt.</b> Er ist nie
/// folgenlos — also nur, wenn nipp einen eigenen Anlass hat.</para>
///
/// <para><b>Reine Funktion, wie <see cref="HeadsetPolicy"/>.</b> Sie
/// entscheidet über etwas, das man nur am Gerät sieht, und dort merkt man einen
/// Fehler erst, wenn er ein Meeting beendet hat.</para>
/// </summary>
public static class HeadsetSignalGate
{
    /// <summary>
    /// Ob der gewünschte Zustand an das Gerät gemeldet wird.
    /// </summary>
    /// <param name="gewuenscht">Was <see cref="HeadsetPolicy.StateFor"/> ergab.</param>
    /// <param name="jeGemeldet">
    /// Ob nipp diesem Gerät schon einmal einen nicht leeren Zustand gemeldet
    /// hat.
    /// </param>
    /// <param name="fremdbelegt">
    /// Ob ein anderes Programm das Gerät gerade benutzt — aus der
    /// Audio-Sitzung (<c>AudioSessionWatch</c>) oder aus dem Gabelzustand
    /// (<see cref="HookWatch.Fremdbelegung"/>). <b>Beide Quellen zusammen:</b>
    /// die Audio-Sitzung erkennt ein Meeting, der Gabelzustand einen Anruf des
    /// anderen Programms, und keine von beiden erkennt alles.
    /// </param>
    /// <param name="eigenesGespraech">
    /// Ob nipp selbst ein Gespräch führt — verbunden, gehalten oder
    /// ausgehend, <b>aber nicht bloss klingelnd</b>. Ein klingelnder Anruf ist
    /// noch keine Entscheidung des Benutzers; ein angenommener ist eine.
    /// </param>
    public static SignalUrteil Erlaubt(
        HeadsetState gewuenscht,
        bool jeGemeldet,
        bool fremdbelegt,
        bool eigenesGespraech)
    {
        var leer = !gewuenscht.ImGespraech && !gewuenscht.Klingelt && !gewuenscht.Stumm;

        // <b>„Alles aus" ist nur dann eine Mitteilung, wenn nipp vorher etwas
        // anderes gesagt hat.</b> Sonst ist es ein Bericht ohne Anlass — und
        // genau die gingen bisher beim Start und bei jedem Audiogerätewechsel
        // hinaus, dreizehnmal am 10.09.2026, ohne dass ein Anruf existierte.
        //
        // <b>Und es ist die Stelle, die den Befund vom 14.09.2026 auflöst.</b>
        // Wurde der Ring wegen einer Fremdbelegung verschwiegen, steht
        // <paramref name="jeGemeldet"/> auf false — dann bleibt auch der
        // Abschluss hier liegen, und das fremde Gespräch bleibt unberührt.
        // Hat nipp dagegen gemeldet, <b>muss</b> der Abschluss hinaus: sonst
        // klingelt das Gerät weiter. Am 14.09.2026 gemessen (M1): es hört
        // nicht von selbst auf, auch nicht, wenn der Anrufer auflegt.
        if (leer)
        {
            return jeGemeldet
                ? new SignalUrteil(true, "Ende")
                : new SignalUrteil(false, "ohne Anlass");
        }

        // <b>Ein fremdes Gespräch wird nicht angefasst — egal, was nipp zu
        // melden hätte</b> (ADR-068).
        //
        // <para>Bis zum 14.09.2026 stand hier nur der Ring, und die Begründung
        // war: «nimmt er den Anruf an, hat er entschieden». Der Satz stimmt,
        // aber er deckte den Fall nicht ab, der gemeldet wurde — <b>das
        // Ablehnen</b>. Dabei ging der Abschlussbericht hinaus und beendete
        // ein Teams-Meeting.</para>
        //
        // <para><b>Das eigene Gespräch schlägt die Fremdbelegung</b>, und das
        // ist kein Widerspruch: wer annimmt oder selbst wählt, hat sich für
        // nipp entschieden. Dass das andere Programm dabei das Gerät verliert,
        // ist die Folge seiner Wahl. <b>Ein klingelnder Anruf ist noch keine
        // Wahl</b> — deshalb zählt hier nur das verbundene Gespräch und nicht
        // die blosse Anwesenheit eines Anrufs.</para>
        if (fremdbelegt && !eigenesGespraech)
        {
            return new SignalUrteil(false, "Fremdbelegung");
        }

        return new SignalUrteil(true, "Anruf");
    }
}
