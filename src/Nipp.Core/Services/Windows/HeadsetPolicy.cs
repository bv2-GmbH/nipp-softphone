using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Windows;

/// <summary>Was ein Druck auf die Gabeltaste bedeuten soll.</summary>
public enum HeadsetAction
{
    /// <summary>Nichts — es gibt keinen Anruf, auf den sich der Druck bezieht.</summary>
    Nichts,

    Annehmen,

    Auflegen,
}

/// <summary>
/// Was dem Headset gemeldet wird: läuft ein Gespräch, klingelt es, ist es
/// stumm.
/// </summary>
public readonly record struct HeadsetState(bool ImGespraech, bool Klingelt, bool Stumm);

/// <summary>
/// Die zwei Regeln der Headset-Anbindung, für sich und ohne Hardware prüfbar.
///
/// <para><b>Warum getrennt.</b> „Welche Lampe bei welchem Zustand" und „was
/// bedeutet dieser Tastendruck" sind genau die Art Regel, die in nipp schon
/// dreimal falsch war — die Liste erlaubter Zustandsübergänge hat drei Anläufe
/// gebraucht, weil sie je eine Anrufrichtung vergass (CLAUDE.md). Eine Regel,
/// die in einer Klasse mit Threads, Handles und P/Invoke steckt, kann man nur
/// am Gerät prüfen, und dort merkt man erst, dass sie falsch ist, wenn ein
/// Kundengespräch daran hängt. Hier ist sie eine Funktion von Eingabe auf
/// Ausgabe.</para>
/// </summary>
public static class HeadsetPolicy
{
    /// <summary>
    /// Leitet aus den laufenden Anrufen ab, was dem Gerät zu melden ist.
    ///
    /// <para><b>Ein ausgehender Anruf gilt als abgenommen</b>, sobald gewählt
    /// wird. Sonst wäre die Taste während des Klingelns beim Gegenüber ohne
    /// Wirkung — und genau dann bricht man einen Anruf am häufigsten ab.</para>
    ///
    /// <para><b>Klingeln schlägt Gespräch.</b> Klingelt etwas, blinkt die
    /// Ring-Lampe, auch wenn daneben ein zweites Gespräch läuft (§8.2). Beide
    /// Lampen zugleich wären für das Gerät ein Widerspruch: es müsste
    /// entscheiden, ob die Taste annimmt oder auflegt.</para>
    /// </summary>
    public static HeadsetState StateFor(IReadOnlyList<CallInfo> calls)
    {
        var imGespraech = false;
        var klingelt = false;
        var stumm = false;

        foreach (var call in calls)
        {
            switch (call.Status)
            {
                case CallStatus.Incoming:
                    klingelt = true;
                    break;

                case CallStatus.Connected:
                    imGespraech = true;
                    stumm |= call.IsMuted;
                    break;

                case CallStatus.Dialing:
                case CallStatus.Ringing:
                case CallStatus.OnHold:
                    imGespraech = true;
                    break;
            }
        }

        return new HeadsetState(klingelt ? false : imGespraech, klingelt, stumm);
    }

    /// <summary>
    /// Deutet eine Betätigung der Gabeltaste — <b>gleich, in welche Richtung
    /// das Gerät den Wechsel gemeldet hat.</b>
    ///
    /// <para><b>Warum ohne Richtung.</b> Bis zum 09.09.2026 entschied sie:
    /// „abgenommen" nahm an, „aufgelegt" legte auf. Das setzt voraus, dass der
    /// Gabelzustand des Geräts und der von nipp übereinstimmen — und diese
    /// Voraussetzung war nicht zu halten. Ein Gespräch, das in der Oberfläche
    /// endete, liess das Gerät auf „abgenommen" stehen; ab da war jeder zweite
    /// Druck falsch (ADR-028, T82). Der Versuch, den Gerätezustand
    /// nachzuziehen, hat es schlimmer gemacht: er erfand Flanken und legte
    /// 10 ms nach jedem Annehmen auf. Und ob ein Gerät die Taste überhaupt als
    /// Schalter oder als Momentan-Taster meldet, steht in seinem
    /// Report-Deskriptor, nicht bei uns — nipp läuft an dreien.</para>
    ///
    /// <para>Was die Taste bedeutet, ist deshalb dieselbe Frage wie beim
    /// globalen Hotkey, und wird an derselben Stelle beantwortet: <b>klingelt
    /// etwas, wird angenommen; sonst wird das Gespräch im Vordergrund
    /// aufgelegt.</b> Diese Regel braucht keinen Gerätezustand und kann deshalb
    /// nicht mit ihm auseinanderlaufen. Ob die Meldung ein Tastendruck war und
    /// nicht ein Loslassen oder ein Echo, entscheidet
    /// <see cref="HookWatch"/>.</para>
    /// </summary>
    /// <returns>
    /// Was zu tun ist, und mit welchem Anruf. Bei
    /// <see cref="HeadsetAction.Nichts"/> ist der Anruf <c>null</c>.
    /// </returns>
    public static (HeadsetAction Action, CallInfo? Call) Interpret(IReadOnlyList<CallInfo> calls)
    {
        // <b>Klingeln schlägt Gespräch</b> — wie bei den Lampen. Klingelt
        // etwas, nimmt die Taste an, auch wenn daneben ein Gespräch läuft:
        // das geht dann auf Halten (§8.6). Andernfalls hätte die Taste beim
        // Klingeln zwei Bedeutungen, und das Gerät müsste raten.
        if (Ersten(calls, CallStatus.Incoming) is { } klingelt)
        {
            return (HeadsetAction.Annehmen, klingelt);
        }

        // <b>Das gewählte Gespräch, nicht irgendeines.</b> Bei zwei Gesprächen
        // (§8.2) legt die Taste das auf, das im Vordergrund steht — dasselbe,
        // das der Knopf in der Oberfläche auflegt. Zwei verschiedene
        // Bedeutungen für „auflegen" wären schlimmer als eine unvollständige.
        //
        // Die Reihenfolge ist die Rangfolge: ein verbundenes Gespräch vor
        // einem gehaltenen, ein gehaltenes vor einem, das gerade gewählt wird.
        var laufend = Ersten(calls, CallStatus.Connected)
            ?? Ersten(calls, CallStatus.OnHold)
            ?? Ersten(calls, CallStatus.Dialing)
            ?? Ersten(calls, CallStatus.Ringing);

        return laufend is null
            ? (HeadsetAction.Nichts, null)
            : (HeadsetAction.Auflegen, laufend);
    }

    private static CallInfo? Ersten(IReadOnlyList<CallInfo> calls, CallStatus status)
    {
        foreach (var call in calls)
        {
            if (call.Status == status)
            {
                return call;
            }
        }

        return null;
    }
}
