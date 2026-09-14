namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Bringt SIP-Adressen auf eine vergleichbare Form.
///
/// <b>Warum es das braucht.</b> Am 05.09.2026 stand das Besetztlampenfeld auf
/// zehnmal „unbekannt", obwohl das SDK für alle zehn Nebenstellen einen Zustand
/// geliefert hatte. Die Einstellungen kannten <c>sip:152@pbx.example.ch</c>, das
/// SDK meldete <c>"152" &lt;sip:152@pbx.example.ch&gt;</c> — mit Anzeigename und
/// Winkelklammern. Zwei Schreibweisen derselben Adresse, und der Vergleich fand
/// nie zusammen. Der Zustand kam an, landete unter dem falschen Schlüssel und
/// wurde bei der nächsten Synchronisation als „nicht mehr beobachtet" gelöscht.
///
/// Reine Funktion, damit sie testbar ist — dieselbe Klasse Fehler wie beim
/// <see cref="ClipResolver"/>, nur für Adressen statt Nummern.
/// </summary>
public static class SipUri
{
    /// <summary>
    /// Die nackte URI: ohne Anzeigename, ohne Winkelklammern, ohne Parameter,
    /// mit Schema. <c>"152" &lt;sip:152@pbx;transport=udp&gt;</c> wird zu
    /// <c>sip:152@pbx</c>. Leere Eingabe bleibt leer.
    ///
    /// Vergleiche gehören mit <see cref="StringComparison.OrdinalIgnoreCase"/>
    /// gemacht: der Host ist nach RFC 3261 nicht schreibungsempfindlich, und
    /// für den Benutzerteil einer Nebenstelle spielt es keine Rolle.
    /// </summary>
    public static string Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        var span = address.AsSpan().Trim();

        // Anzeigename und Winkelklammern: "Name" <sip:...> → sip:...
        var open = span.IndexOf('<');
        var close = span.LastIndexOf('>');

        if (open >= 0 && close > open)
        {
            span = span[(open + 1)..close].Trim();
        }

        // Parameter (;transport=udp) und Kopfzeilen (?subject=) gehören nicht
        // zur Identität der Nebenstelle.
        var cut = span.IndexOfAny(';', '?');

        if (cut >= 0)
        {
            span = span[..cut];
        }

        if (span.Length == 0)
        {
            return string.Empty;
        }

        var text = span.ToString();

        // Ohne Schema ergänzen: „152@pbx" meint sip:152@pbx. Wer sips: schreibt,
        // meint es so.
        if (!text.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
        {
            text = "sip:" + text;
        }

        return text;
    }

    /// <summary>Ob zwei Adressen dieselbe Nebenstelle meinen.</summary>
    public static bool Same(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
