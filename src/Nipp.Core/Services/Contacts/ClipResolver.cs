namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Nummer → Name (CLIP, AP6.4).
///
/// Eine Stelle für alle: Toast, Gesprächsansicht und Anrufliste zeigen
/// denselben Namen, weil sie denselben Auflöser fragen. Drei getrennte
/// Auflösungen würden früher oder später drei verschiedene Namen zeigen.
///
/// <b>Der Kern ist eine reine Funktion</b> (<see cref="IsSameNumber"/>), damit
/// er ohne Kontakte, ohne Outlook und ohne Anlage testbar ist. Der Rest ist
/// ein Index darüber.
///
/// Die eigentliche Schwierigkeit ist, dass dieselbe Nummer in vier
/// Schreibweisen ankommt: <c>+41445128430</c> vom Amt, <c>0445128430</c> aus
/// Outlook, <c>044 512 84 30</c> von Hand getippt und <c>430</c> als interne
/// Nebenstelle. Verglichen wird deshalb über die Ziffern und von hinten.
/// </summary>
public sealed class ClipResolver
{
    /// <summary>
    /// Ab wie vielen Ziffern zwei Nummern über ihr Ende verglichen werden
    /// dürfen.
    ///
    /// Sieben ist kein runder Wert, sondern der kürzeste, bei dem ein
    /// Zufallstreffer unwahrscheinlich wird: eine Schweizer Rufnummer ohne
    /// Vorwahl hat sieben Stellen. Bei weniger — also bei internen
    /// Nebenstellen — wird auf Gleichheit verglichen, sonst würde die
    /// Nebenstelle <c>430</c> auf jede Nummer passen, die auf 430 endet.
    /// </summary>
    private const int MinimumSuffixLength = 7;

    /// <summary>
    /// Wie viele Ziffern die längere Nummer höchstens voraushaben darf, damit
    /// der Suffixvergleich noch greift — die Länge eines Länderpräfix.
    /// </summary>
    private const int MaxCountryPrefixDigits = 3;

    private readonly ContactStore _contacts;

    public ClipResolver(ContactStore contacts) => _contacts = contacts;

    /// <summary>
    /// Der Name zu einer Nummer, oder <c>null</c>, wenn keiner bekannt ist.
    ///
    /// Team-Nebenstellen gehen vor: wer intern anruft, soll als Kollege
    /// erscheinen, auch wenn dieselbe Nummer noch irgendwo in den
    /// Outlook-Kontakten steht.
    /// </summary>
    public string? ResolveName(string? number)
    {
        if (Resolve(number) is { } contact)
        {
            return contact.DisplayName;
        }

        return null;
    }

    /// <summary>Der ganze Kontakt zu einer Nummer.</summary>
    public Contact? Resolve(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var candidates = _contacts.Contacts;

        // Die Reihenfolge ist die Rangfolge: Kollege vor persönlichem Kontakt
        // vor Fremdsystem. Externe Kontakte stehen heute nicht im
        // Zwischenspeicher — sie entstehen erst bei einer Suche (§21) —, und
        // die Zeile ist Vorsorge für den Tag, an dem sich das ändert.
        return FindIn(candidates, ContactSourceKind.Team, number)
            ?? FindIn(candidates, ContactSourceKind.Outlook, number)
            ?? FindIn(candidates, ContactSourceKind.External, number);
    }

    // Hier stand `DescribeCaller` — „der Name, wenn bekannt, sonst die
    // Nummer". Gedacht als die eine Stelle für alle, verdrahtet wurde sie nie:
    // im ganzen src/ gab es keinen Aufrufer, während dieselbe Regel dreimal
    // ausgeschrieben danebenstand. Sie lebt jetzt als
    // `CallPartyResolver.Describe` und wird benutzt (ADR-043); der Unterschied
    // ist, dass sie dort auch die externen Quellen kennt und die Nummer für
    // die Anzeige gruppiert.

    private static Contact? FindIn(
        IReadOnlyList<Contact> contacts,
        ContactSourceKind kind,
        string number)
    {
        foreach (var contact in contacts)
        {
            if (contact.Source != kind)
            {
                continue;
            }

            foreach (var candidate in contact.Numbers)
            {
                if (IsSameNumber(candidate.Number, number))
                {
                    return contact;
                }
            }

            if (contact.SipAddress is { Length: > 0 } sip && IsSameNumber(sip, number))
            {
                return contact;
            }
        }

        return null;
    }

    /// <summary>
    /// Ob zwei Nummern dieselbe bezeichnen — die reine Funktion, um die es
    /// geht.
    ///
    /// Regel: nur Ziffern vergleichen, und zwar von hinten, sofern beide
    /// mindestens <see cref="MinimumSuffixLength"/> Ziffern haben. Kürzere
    /// Nummern (interne Nebenstellen) müssen exakt übereinstimmen.
    /// </summary>
    public static bool IsSameNumber(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = Significant(DigitsOnly(left));
        var b = Significant(DigitsOnly(right));

        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;

        if (shorter.Length < MinimumSuffixLength)
        {
            return false;
        }

        // <b>Wie viel darf die längere Nummer voraushaben?</b> Nur so viel, wie
        // ein Länderpräfix ausmacht.
        //
        // Der Suffixvergleich soll dieselbe Nummer in verschiedenen
        // Schreibweisen erkennen — «0445128430» und «+41445128430»
        // unterscheiden sich um genau die zwei Ziffern des Landes. Ohne
        // Obergrenze traf er aber auch Nummern aus anderen Ländern: der
        // Outlook-Kontakt «044 512 84 30» wurde zu «445128430», und ein Anruf
        // aus Berlin «+49 30 445128430» endet darauf. Der Anrufer bekam damit
        // den Namen eines Zürcher Kontakts — falsch, und im Journal steht es
        // hinterher genauso falsch.
        //
        // Drei Ziffern decken jedes Länderpräfix ab (1 für Nordamerika, 41 für
        // die Schweiz, 998 als längster Fall). Was das kostet: eine im
        // Adressbuch ohne Vorwahl notierte Nummer wird nicht mehr erkannt. Das
        // ist richtig so — sie ist ohne Vorwahl nicht eindeutig, und ein
        // falscher Name ist schlechter als keiner.
        if (longer.Length - shorter.Length > MaxCountryPrefixDigits)
        {
            return false;
        }

        return longer.EndsWith(shorter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Schneidet fuehrende Nullen ab.
    ///
    /// Der Grund ist die nationale Null: <c>+41445128430</c> wird zu
    /// <c>41445128430</c>, <c>0445128430</c> bleibt mit ihrer Null stehen — und
    /// dann endet die lange Nummer eben <b>nicht</b> auf die kurze, obwohl es
    /// dieselbe ist. Der erste Testlauf hat genau das aufgedeckt.
    ///
    /// Dass dabei auch die Verkehrsausscheidungsziffer <c>00</c> faellt, ist
    /// erwuenscht: <c>0041…</c> und <c>+41…</c> sind dieselbe Nummer.
    /// </summary>
    private static string Significant(string digits) => digits.TrimStart('0');

    /// <summary>
    /// Die Ziffern einer Nummer, ohne alles andere.
    ///
    /// Ein führendes <c>+</c> fällt hier weg — das ist gewollt: <c>+41 44 …</c>
    /// und <c>0041 44 …</c> sollen sich treffen, und der Ländervorwahl-Teil
    /// wird durch den Vergleich von hinten ohnehin überlesen. Aus einer
    /// SIP-Adresse wird nur der Teil vor dem <c>@</c> betrachtet, sonst
    /// verglichen sich Domänen mit Ziffern mit.
    /// </summary>
    public static string DigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var span = value.AsSpan();

        if (span.StartsWith("sip:", StringComparison.OrdinalIgnoreCase))
        {
            span = span[4..];
        }
        else if (span.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
        {
            span = span[5..];
        }

        var at = span.IndexOf('@');

        if (at >= 0)
        {
            span = span[..at];
        }

        // Ein Stapelpuffer waere hier schneller, aber die Laenge kommt aus
        // der Signalisierung und damit von aussen — ein absurd langer
        // From-Header duerfte keinen Stapelueberlauf ausloesen.
        var buffer = new char[span.Length];
        var length = 0;

        foreach (var c in span)
        {
            if (char.IsAsciiDigit(c))
            {
                buffer[length++] = c;
            }
        }

        return new string(buffer, 0, length);
    }
}
