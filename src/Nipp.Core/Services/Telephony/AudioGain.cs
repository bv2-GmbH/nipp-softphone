namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Rechnet zwischen der Skala aus §9.4 und der des SDK um.
///
/// <b>Warum es diese Klasse gibt.</b> §9.4 gibt Wiedergabelautstärke und
/// Mikrofonpegel als „0–100" vor, mit den Standardwerten 72 und 50. Das SDK
/// arbeitet dagegen mit <b>Dezibel</b> (<c>Core.PlaybackGainDb</c>,
/// <c>Core.MicGainDb</c>, je <c>float</c>, 0 = neutral). Den Rohwert
/// durchzureichen wäre falsch: 72 dB Verstärkung gibt es nicht.
///
/// <b>Die gewählte Abbildung.</b> Linear, mit der Skalenmitte als neutralem
/// Punkt:
/// <code>
///   0   →  -15 dB   (sehr leise)
///   50  →    0 dB   (neutral, unveränderter Pegel)
///   100 →  +15 dB   (deutlich verstärkt)
/// </code>
///
/// Damit liegt der Mikrofon-Standard aus §9.4 (50) genau auf neutral, und der
/// Wiedergabe-Standard (72) etwas darüber — was zur Absicht passt, dass die
/// Wiedergabe ab Werk gut hörbar ist.
///
/// Die Wahl ist eine Festlegung, keine Ableitung: §9.4 sagt nichts über den
/// Dezibelbereich. ±15 dB ist bewusst konservativ — genug, um ein leises
/// Headset brauchbar zu machen, zu wenig, um mit einem Regler Übersteuerung
/// und Verzerrung zu erzeugen.
/// </summary>
public static class AudioGain
{
    /// <summary>Neutraler Punkt auf der Skala 0–100.</summary>
    public const int NeutralScaleValue = 50;

    /// <summary>Dezibel je Skalenschritt.</summary>
    private const float DecibelPerStep = 0.3f;

    /// <summary>Grenzen der Skala aus §9.4.</summary>
    public const int MinScaleValue = 0;

    public const int MaxScaleValue = 100;

    /// <summary>Skalenwert 0–100 in Dezibel.</summary>
    public static float ToDecibel(int scaleValue) =>
        (Math.Clamp(scaleValue, MinScaleValue, MaxScaleValue) - NeutralScaleValue) * DecibelPerStep;

    /// <summary>
    /// Dezibel zurück auf die Skala. Wird gebraucht, wenn ein Wert aus dem SDK
    /// gelesen wird — etwa nach einem Provisioning-Profil, das direkt in die
    /// <c>linphonerc</c> geschrieben hat.
    /// </summary>
    public static int ToScaleValue(float decibel) =>
        Math.Clamp(
            (int)Math.Round((decibel / DecibelPerStep) + NeutralScaleValue),
            MinScaleValue,
            MaxScaleValue);
}
