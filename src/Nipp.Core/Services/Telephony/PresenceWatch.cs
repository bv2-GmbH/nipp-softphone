namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Was über ein Präsenz-Abonnement zu melden ist.
/// </summary>
/// <param name="Address">Die beobachtete Nebenstelle.</param>
/// <param name="State">Der Zustandsname, wie das SDK ihn nennt.</param>
/// <param name="Hint">
/// Was der Zustand bedeutet (§15) — nicht nur, wie er heisst. Leer, wenn es
/// nichts zu erklären gibt.
/// </param>
/// <param name="IsError">
/// Ob es ein Fehlschlag ist. Danach entscheidet sich die Protokollstufe,
/// nicht nach dem Namen — sonst steht ein abgelehntes Abonnement neben einem
/// gewöhnlichen Zustandswechsel.
/// </param>
public sealed record PresenceNotice(string Address, string State, string Hint, bool IsError);

/// <summary>
/// Die Zustände der Präsenz-Abonnements (§14.8, W2.1 Etappe B4) — ohne SDK
/// und ohne Nebenwirkung.
///
/// <para><b>Was diese Klasse tut und was nicht.</b> Sie merkt sich, welcher
/// Zustand zuletzt galt, und meldet <b>nur Wechsel</b>. Sie erneuert nichts
/// und schickt nichts — das Abonnieren bleibt im Dienst, weil es ins SDK
/// greift. <b>Ein Vorrat an Funktionen, die niemand bestellt hat</b>, wäre
/// hier besonders teuer: das Besetztlampenfeld ist der Teil von nipp, der am
/// meisten Last auf der Anlage erzeugt.</para>
///
/// <para><b>Der Zustand kommt als Name herein</b>, nicht als SDK-Enum —
/// dasselbe Muster wie <c>TransferOutcomes.From</c>. Ein unbekannter Name ist
/// damit kein Compilerfehler, sondern ein Zustand ohne Erklärung, und das ist
/// die ehrlichere Antwort auf ein SDK, das Zustände hinzufügen darf.</para>
/// </summary>
public sealed class PresenceWatch
{
    private readonly Dictionary<string, string> _letzte = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Einen gemeldeten Zustand verbuchen. Gibt <c>null</c> zurück, wenn sich
    /// nichts geändert hat — <b>und das ist der Normalfall</b>: die Prüfung
    /// läuft alle fünf Sekunden, ein Abonnement steht danach stundenlang
    /// unverändert.
    /// </summary>
    public PresenceNotice? Observe(string address, string state)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        if (_letzte.TryGetValue(address, out var vorher)
            && string.Equals(vorher, state, StringComparison.Ordinal))
        {
            return null;
        }

        _letzte[address] = state;

        return new PresenceNotice(address, state, Erklaere(state), IsError: state is "Error");
    }

    /// <summary>
    /// Eine Nebenstelle, die nicht mehr beobachtet wird, vergessen.
    ///
    /// <para><b>Sonst gilt ihr alter Zustand weiter</b>, und wenn sie später
    /// zurückkommt — eine Nebenstelle, die aus der Gruppe und wieder hinein
    /// gezogen wird —, bleibt der erste Wechsel stumm.</para>
    /// </summary>
    public void Forget(string address) => _letzte.Remove(address);

    /// <summary>Alles vergessen — beim Abmelden oder beim Neuaufbau der Liste.</summary>
    public void Clear() => _letzte.Clear();

    /// <summary>Wie viele Nebenstellen gerade einen bekannten Zustand haben.</summary>
    public int Count => _letzte.Count;

    /// <summary>
    /// §15: nicht nur der Zustand, sondern was er bedeutet — und beim Fehler,
    /// wo die Antwort der Anlage zu finden ist.
    /// </summary>
    private static string Erklaere(string state) => state switch
    {
        "Active" =>
            "Die Anlage hat das Abonnement angenommen. Kommt jetzt keine "
            + "Praesenz, veroeffentlicht die Nebenstelle keine.",
        "Error" =>
            "Die Anlage hat abgelehnt. Die SIP-Antwort (z. B. 489 Bad Event) steht "
            + "im Protokoll, wenn die Protokollierung auf Debug steht.",
        "Terminated" =>
            "Die Anlage hat das Abonnement beendet.",
        "OutgoingProgress" or "Pending" =>
            "SUBSCRIBE gesendet, Antwort steht aus.",
        _ => string.Empty,
    };
}
