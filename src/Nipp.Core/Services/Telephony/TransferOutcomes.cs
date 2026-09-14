using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Was ein Zustand des SDK über eine laufende Weiterleitung sagt (W1.2,
/// Befund B5).
///
/// <para><b>Über den Namen, nicht über den Enum-Wert</b> — dieselbe Bauart wie
/// <c>SipEventBridge.MapKnownRegistrationStatus</c>. Zwei Gründe: die Regel
/// bleibt ohne SDK prüfbar, und ein Zustand, den diese Fassung nicht kennt,
/// fällt in den Zwischenfall statt in «gescheitert». Ein neuer SDK-Wert soll
/// keine Fehlermeldung erfinden.</para>
///
/// <para><b>Was das SDK hier meldet</b>, ist der Zustand des <b>neuen</b>
/// Anrufs, den die Übergabe erzeugt — nicht der des eigenen Gesprächs.</para>
/// </summary>
public static class TransferOutcomes
{
    /// <summary>
    /// Der Ausgang zu einem Zustandsnamen des SDK.
    /// </summary>
    /// <param name="stateName">
    /// Etwa <c>OutgoingProgress</c>, <c>Connected</c> oder <c>Error</c>.
    /// </param>
    public static TransferOutcome From(string? stateName) => stateName switch
    {
        // Das Ziel hat abgenommen. StreamsRunning steht daneben, weil manche
        // Anlagen Connected überspringen.
        "Connected" or "StreamsRunning" => TransferOutcome.Succeeded,

        // Die Anlage oder das Ziel hat abgelehnt — ein 403, 404 oder 603 auf
        // den REFER. «End» und «Released» zählen dazu: ein Übergabeziel, das
        // auflegt, bevor es verbunden war, hat die Übergabe nicht angenommen.
        "Error" or "End" or "Released" => TransferOutcome.Failed,

        // Alles andere — OutgoingInit, OutgoingProgress, OutgoingRinging, und
        // jeder Wert, den diese Fassung nicht kennt.
        _ => TransferOutcome.InProgress,
    };
}
