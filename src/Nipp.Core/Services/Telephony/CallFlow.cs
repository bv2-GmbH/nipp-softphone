using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>Was mit einem gemeldeten Anruf geschehen soll.</summary>
public enum CallAction
{
    /// <summary>Nichts — das Ereignis gehört zu einem Anruf, den nipp nicht führt.</summary>
    Ignorieren,

    /// <summary>Ablehnen: der dritte gleichzeitige Anruf (§8.2).</summary>
    Ablehnen,

    /// <summary>Anlegen: ein eingehender Anruf, den nipp noch nicht kennt.</summary>
    Anlegen,

    /// <summary>Aktualisieren: ein bekannter Anruf hat einen neuen Zustand.</summary>
    Aktualisieren,
}

/// <summary>
/// Was aus einer Momentaufnahme folgt. Reine Daten — <c>SipService</c> führt
/// aus, entscheidet aber nichts mehr.
/// </summary>
/// <param name="Action">Was zu tun ist.</param>
/// <param name="Previous">
/// Der Zustand vor diesem Ereignis, oder <c>null</c> bei einem neuen Anruf.
/// <b>Daran hängt mehr, als es aussieht</b> — siehe <see cref="CallFlow"/>.
/// </param>
/// <param name="AutoAnswer">
/// Ob dieser Anruf automatisch angenommen werden soll — <b>vorgemerkt</b>,
/// nicht ausgeführt.
/// </param>
/// <param name="StopRingback">Ob ein laufender eigener Rufton enden soll.</param>
/// <param name="Remove">Ob der Anruf aus der Verwaltung genommen wird.</param>
/// <param name="ResumeLast">
/// Ob ein allein zurückbleibendes, gehaltenes Gespräch zurückgeholt wird
/// (ADR-073).
/// </param>
public sealed record CallDecision(
    CallAction Action,
    CallStatus? Previous = null,
    bool AutoAnswer = false,
    bool StopRingback = false,
    bool Remove = false,
    bool ResumeLast = false);

/// <summary>
/// Die Zustandsmaschine der Anrufe, ohne SDK und ohne Nebenwirkung (W2.1,
/// Etappe B1).
///
/// <para><b>Warum es diese Klasse gibt.</b> Die Regeln unten haben dieses
/// Projekt je einen Tag Fehlersuche gekostet, und bis zum 24.09.2026 standen
/// sie als Kommentare in einer 165 Zeilen langen Callback-Methode, die nur an
/// einer echten Anlage lief. Ein Kommentar hält keine Regel fest — er
/// beschreibt sie, bis jemand daneben etwas ändert.</para>
///
/// <para><b>Was hier nicht steht, steht nirgends:</b> die sechs Regeln sind
/// vollständig in <see cref="Decide"/> und <see cref="Apply"/>, und jede hat
/// einen Test in <c>CallFlowTests</c>.</para>
///
/// <para><b>Und was hier bewusst fehlt:</b> das Ausführen. Ablehnen, annehmen,
/// den Rufton stoppen und den Anruf aus der Verwaltung nehmen tut
/// <c>SipService</c> — aus einem SDK-Callback heraus darf das SDK nicht
/// angefasst werden (ADR-053), und diese Klasse weiss nicht einmal, dass es
/// eines gibt.</para>
/// </summary>
public static class CallFlow
{
    /// <summary>
    /// Was mit einer Momentaufnahme geschehen soll.
    /// </summary>
    /// <param name="snapshot">Was das SDK gemeldet hat.</param>
    /// <param name="tracked">
    /// Der bekannte Anruf, oder <c>null</c>, wenn nipp ihn nicht führt.
    /// </param>
    /// <param name="activeCalls">Wie viele Anrufe nipp gerade führt.</param>
    /// <param name="autoAnswer">Ob automatisch angenommen wird (§9.6).</param>
    /// <param name="maxConcurrentCalls">Wie viele gleichzeitig erlaubt sind (§8.2).</param>
    /// <param name="ringbackPlaying">Ob nipp gerade selbst einen Rufton spielt.</param>
    public static CallDecision Decide(
        CallSnapshot snapshot,
        CallInfo? tracked,
        int activeCalls,
        bool autoAnswer,
        int maxConcurrentCalls,
        bool ringbackPlaying)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (tracked is null)
        {
            // <b>Ein unbekannter Anruf wird nur als eingehender angelegt.</b>
            // Jedes andere Ereignis zu einem unbekannten Anruf gehört einem,
            // den nipp nie geführt hat — etwa dem Rückfrageanruf einer
            // Weiterleitung, nachdem sie abgeschlossen ist.
            if (!snapshot.IsIncomingNew)
            {
                return new CallDecision(CallAction.Ignorieren);
            }

            // <b>§8.2: mehr als zwei wird abgelehnt</b> — und zwar hier,
            // nicht in der Oberfläche.
            //
            // <b>Vormerken statt sofort ablehnen:</b> Decline ist
            // zustandsändernd, und aus einem Callback heraus meldet das SDK
            // die nächsten Zustände mitten im laufenden Aufruf. Dieselbe
            // Reentranz hat bei der automatischen Annahme schon einmal einen
            // echten Fehler ergeben.
            if (activeCalls >= maxConcurrentCalls)
            {
                return new CallDecision(CallAction.Ablehnen);
            }

            // <b>Die automatische Annahme wird vorgemerkt, nicht ausgeführt</b>
            // — aus demselben Grund wie das Ablehnen.
            return new CallDecision(CallAction.Anlegen, Previous: null, AutoAnswer: autoAnswer);
        }

        // <b>Ein Zwischenzustand ohne eigene Aussage lässt den bisherigen
        // stehen.</b> Das SDK meldet mehr Schritte, als es Zustände gibt.
        var status = snapshot.Status ?? tracked.Status;

        return new CallDecision(
            CallAction.Aktualisieren,
            Previous: tracked.Status,

            // <b>Ein eigener Rufton endet, sobald der Anruf nicht mehr
            // läutet</b> — vorgemerkt, denn auch das fasst das SDK an.
            StopRingback: ringbackPlaying && status is not (CallStatus.Dialing or CallStatus.Ringing),

            // <b>Beendet und gescheitert nehmen denselben Weg.</b>
            Remove: status is CallStatus.Ended or CallStatus.Failed,

            // <b>Bleibt genau ein Gespräch übrig und liegt es auf Halten,
            // gehört es zurückgeholt</b> (ADR-073, T322): beim begleiteten
            // Vermitteln nimmt das Ziel nicht ab, der Rückfrageanruf endet —
            // und das erste Gespräch lag noch auf Halten, weil das Wählen es
            // dorthin gelegt hatte.
            ResumeLast: status is CallStatus.Ended or CallStatus.Failed);
    }

    /// <summary>
    /// Der neue Stand eines Anrufs aus seiner Momentaufnahme.
    ///
    /// <para><b>Was hier nicht überschrieben wird, bleibt stehen</b> — Codec
    /// und Anzeigename kommen im Gespräch nicht bei jedem Ereignis mit, und
    /// ein <c>null</c> aus dem SDK ist dann keine Aussage, sondern eine
    /// Lücke.</para>
    /// </summary>
    /// <param name="tracked">Der bisherige Stand.</param>
    /// <param name="snapshot">Was das SDK gemeldet hat.</param>
    /// <param name="now">
    /// Die Uhr — als Parameter, damit «wann wurde verbunden» prüfbar ist.
    /// </param>
    public static CallInfo Apply(CallInfo tracked, CallSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        ArgumentNullException.ThrowIfNull(snapshot);

        var status = snapshot.Status ?? tracked.Status;

        return tracked with
        {
            Status = status,
            StatusMessage = snapshot.Message,
            RemoteDisplayName = snapshot.DisplayName ?? tracked.RemoteDisplayName,

            // <b>Der Zeitpunkt des Verbindens wird einmal gesetzt</b> und
            // danach nicht mehr: ein Halten und Zurückholen ist kein zweiter
            // Gesprächsbeginn, und an dieser Zeit hängt die Dauer in der
            // Anrufliste.
            ConnectedAt = status == CallStatus.Connected && tracked.ConnectedAt is null
                ? now
                : tracked.ConnectedAt,

            Codec = snapshot.Codec ?? tracked.Codec,
            Encryption = snapshot.Encryption,

            // <b>§20.3: der Endgrund wird beim letzten Ereignis
            // festgehalten</b>, sonst steht er nirgends — danach ist der
            // Anruf aus der Verwaltung.
            EndReason = status is CallStatus.Ended or CallStatus.Failed
                ? snapshot.EndReason
                : null,
        };
    }
}
