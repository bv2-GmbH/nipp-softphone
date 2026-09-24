namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Wie aus einer Eingabe eine wählbare Adresse wird und wie eine Aufnahme
/// heisst (§8.2, W2.1 Etappe B5) — beides ohne SDK und ohne Dateisystem.
///
/// <para><b>Warum das eine eigene Klasse ist, obwohl es zwei Funktionen
/// sind.</b> Beide standen in <c>SipService</c> zwischen SDK-Aufrufen und
/// waren damit nur an einer Anlage zu messen — dabei fasst keine von ihnen
/// das SDK an. Es ist die billigste Etappe des Beweisplans und deckt den
/// Weg, auf dem die Klammer-Null gefunden wurde.</para>
/// </summary>
public static class CallAddressing
{
    /// <summary>
    /// Die Adresse, die das SDK wählen kann.
    ///
    /// <para><b>Was schon eine Adresse ist, bleibt eine</b> — erkennbar am
    /// <c>@</c> oder am Schema. Eine Nebenstelle dagegen bekommt die Domäne
    /// des Kontos angehängt, über das gewählt wird: ohne sie wählt das SDK
    /// gegen die zuletzt benutzte Anlage, und bei zwei Konten ist das die
    /// falsche.</para>
    ///
    /// <para><b>Ohne Domäne bleibt die Eingabe stehen.</b> Das SDK lehnt sie
    /// dann ab, und die Meldung sagt, was fehlt — besser als eine erfundene
    /// Domäne, die irgendwohin wählt.</para>
    /// </summary>
    public static string ToDialable(string destination, string? domain)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Contains('@', StringComparison.Ordinal)
            || destination.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        return domain is { Length: > 0 }
            ? $"sip:{destination}@{domain}"
            : destination;
    }

    /// <summary>
    /// Der Dateiname einer Aufnahme nach dem Schema aus §8.2:
    /// <c>JJJJ-MM-TT_HHMMSS_&lt;Nummer&gt;.wav</c>.
    ///
    /// <para><b>Der Zeitstempel ist der des Anrufaufbaus</b>, weil der Pfad zu
    /// diesem Zeitpunkt feststehen muss — deshalb kommt die Uhr als Parameter
    /// herein und nicht aus <c>DateTimeOffset.Now</c>.</para>
    ///
    /// <para><b>Was Windows im Dateinamen nicht annimmt, fliegt raus</b> —
    /// dazu Stern und Raute, die in einer Rufnummer vorkommen und die
    /// Windows zwar erlaubt, die aber als Platzhalter gelesen werden. Bleibt
    /// nichts übrig, heisst die Datei «unbekannt»: eine Aufnahme ohne Namen
    /// wäre schlimmer als eine mit einem unscharfen.</para>
    /// </summary>
    public static string RecordingFileName(string remoteNumber, DateTimeOffset when)
    {
        var invalid = Path.GetInvalidFileNameChars();

        var safe = new string([
            .. (remoteNumber ?? string.Empty).Where(c => !invalid.Contains(c) && c is not ('*' or '#')),
        ]);

        if (safe.Length == 0)
        {
            safe = "unbekannt";
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{when:yyyy-MM-dd_HHmmss}_{safe}.wav");
    }
}
