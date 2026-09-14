namespace Nipp.Core.Diagnostics;

/// <summary>
/// Wie eine Fehlermeldung für den Benutzer aussieht (W1.3, Befund C7).
///
/// <para><b>Die Regel aus CLAUDE.md:</b> kein Paragrafenverweis, kein
/// Methodenname, kein .NET-Typname und kein nackter HTTP-Code als ganze
/// Aussage; jede Meldung sagt, was zu tun ist. Ein Rohtext darf bleiben —
/// aber hinter einem eigenen Satz.</para>
///
/// <para><b>Was sie verhindert.</b> An mehreren Stellen war die Meldung des
/// Systems die ganze Aussage: «Ausgabe fehlgeschlagen» als Titel und
/// <c>ex.Message</c> als Text. Was dort steht, ist auf Englisch, nennt den
/// Typ und sagt nie, was der Benutzer tun soll — «Access to the path is
/// denied» ist wahr und nutzlos.</para>
///
/// <para>Reine Funktion, damit die Form prüfbar ist.</para>
/// </summary>
public static class UserMessage
{
    /// <summary>
    /// Ein eigener Satz, dann die technische Ursache.
    /// </summary>
    /// <param name="satz">
    /// Was nicht ging <b>und was zu tun ist</b> — in der Sprache der
    /// Oberfläche, mit Umlauten, ohne Bezeichner.
    /// </param>
    /// <param name="exception">Was das System gemeldet hat.</param>
    public static string WithCause(string satz, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var ursache = exception.Message;

        if (string.IsNullOrWhiteSpace(ursache))
        {
            return satz;
        }

        // Maskiert: eine Ausnahmemeldung nennt oft einen Pfad, und ein
        // Aufnahmepfad traegt die Rufnummer (§21.2). Was der Benutzer auf dem
        // Bildschirm sieht, kopiert er in ein Support-Ticket.
        return satz
            + Environment.NewLine
            + Environment.NewLine
            + "Technische Ursache: "
            + LogMasking.Line(ursache.Trim());
    }
}
