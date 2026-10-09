namespace Nipp.Core.Services.Telephony.Model;

/// <summary>
/// Wie eine Echo-Kalibrierung ausgegangen ist (§9.4, AP5.7, ADR-078).
///
/// <para><b>Warum es diesen Typ gibt und nicht das SDK-Enum:</b> ViewModels
/// kennen keine SDK-Typen (CLAUDE.md), und dieser Zustand wandert bis in die
/// Oberfläche. Übersetzt wird er in <c>SipEventBridge</c>, wo ohnehin
/// <c>using Linphone</c> steht.</para>
/// </summary>
public enum EchoKalibrierungsErgebnis
{
    /// <summary>
    /// Ein Echo wurde gemessen; <see cref="EchoKalibrierung.VerzoegerungMs"/>
    /// trägt die Laufzeit, auf die sich der Canceller einstellen muss.
    /// </summary>
    EchoGemessen,

    /// <summary>
    /// Die Messung lief sauber durch und fand <b>kein</b> Echo.
    ///
    /// <para><b>Das ist ein Ergebnis, kein Fehlschlag</b> — und bis zum
    /// 09.10.2026 hat nipp es als einen behandelt. Es heisst: Lautsprecher und
    /// Mikrofon sind so entkoppelt, dass nichts zurückkommt, oder der
    /// Audiotreiber unterdrückt es bereits selbst. Daraus folgt etwas
    /// Praktisches — eine Echounterdrückung in nipp kostet dann Rechenzeit und
    /// Pufferlatenz für nichts.</para>
    /// </summary>
    KeinEcho,

    /// <summary>Die Messung ist gescheitert; es gibt keine Aussage.</summary>
    Fehlgeschlagen,
}

/// <summary>
/// Das Ergebnis einer Echo-Kalibrierung.
/// </summary>
/// <param name="Ergebnis">Wie sie ausging.</param>
/// <param name="VerzoegerungMs">
/// Die gemessene Laufzeit in Millisekunden — nur bei
/// <see cref="EchoKalibrierungsErgebnis.EchoGemessen"/> von Bedeutung, sonst
/// <c>0</c>.
/// </param>
public readonly record struct EchoKalibrierung(
    EchoKalibrierungsErgebnis Ergebnis,
    int VerzoegerungMs)
{
    /// <summary>
    /// Der Satz für die Oberfläche. <b>Jeder Fall sagt, was er bedeutet</b> —
    /// «kein Ergebnis» für drei verschiedene Ausgänge war genau das Problem.
    /// </summary>
    public string Beschreibung => Ergebnis switch
    {
        EchoKalibrierungsErgebnis.EchoGemessen =>
            $"Echo gemessen: {VerzoegerungMs} ms Laufzeit. Die Echounterdrückung stellt sich darauf ein.",
        EchoKalibrierungsErgebnis.KeinEcho =>
            "Kein Echo feststellbar. Die Echounterdrückung wird hier nicht gebraucht "
                + "und kann ausgeschaltet bleiben.",
        _ =>
            "Die Messung ist gescheitert. Mikrofon und Lautsprecher prüfen und noch einmal versuchen.",
    };
}
