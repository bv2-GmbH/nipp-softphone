namespace Nipp.Core.Services.Windows.Hid;

/// <summary>
/// Der gewünschte Lampenzustand als eine Zahl (§22.5, W2.1 Etappe B7).
///
/// <para><b>Wofür die Zahl da ist.</b> Der Schreib-Thread liest sie ohne
/// Sperre, und der meldende Thread schreibt sie mit <c>Interlocked</c> —
/// drei einzelne <c>bool</c>-Felder liessen sich nicht in einem Zug
/// austauschen, und zwischen zweien davon könnte ein halber Zustand
/// hinausgehen.</para>
///
/// <para><b>Und wofür das Sammelfenster.</b> Ein Anruf durchläuft mehrere
/// Zustände in Millisekunden. Bei einem Selbstanruf am 09.09.2026 gingen zwei
/// Reports 1 ms auseinander hinaus, und der erste war schon überholt, als er
/// ankam. Das wäre gleichgültig, wenn ein Report billig wäre — am Jabra
/// Engage 75 dauerte einer <b>2,94 Sekunden</b>, und solange läutet das
/// Headset weiter. Es zählt ohnehin nur der neueste Zustand.</para>
/// </summary>
public static class LampenSammler
{
    /// <summary>Wie lange gesammelt wird, bevor geschrieben wird.</summary>
    public const int SammelfensterMs = 60;

    private const int BitInCall = 1;
    private const int BitRinging = 2;
    private const int BitMuted = 4;

    /// <summary>Kein Zustand — und <b>nicht</b> dasselbe wie «alles aus».</summary>
    public const int Unbekannt = -1;

    /// <summary>Die drei Zustände als eine Zahl.</summary>
    public static int Bits(bool imGespraech, bool klingelt, bool stumm) =>
        (imGespraech ? BitInCall : 0)
        | (klingelt ? BitRinging : 0)
        | (stumm ? BitMuted : 0);

    /// <summary>Die Zahl zurück in drei Zustände.</summary>
    public static (bool ImGespraech, bool Klingelt, bool Stumm) Aus(int bits) =>
        ((bits & BitInCall) != 0, (bits & BitRinging) != 0, (bits & BitMuted) != 0);

    /// <summary>
    /// Ob sich gegenüber dem zuletzt gewünschten Zustand etwas geändert hat.
    ///
    /// <para><b>Der Anfangswert ist <see cref="Unbekannt"/> und nicht
    /// null</b>: «alles aus» ist ein gültiger Zustand, den nipp auch wirklich
    /// meldet, wenn ein Gespräch endet. Wäre der Anfangswert 0, bliebe genau
    /// dieser Bericht beim ersten Mal liegen — und die Lampe am Gerät an.</para>
    /// </summary>
    public static bool Aenderung(int bisher, int gewuenscht) => bisher != gewuenscht;
}
