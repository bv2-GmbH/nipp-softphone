using System.Text;
using System.Text.RegularExpressions;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Bringt eingegebene Nummern in die Form, in der sie gewählt werden (§8.1).
///
/// <b>Reine Funktion, absichtlich ohne jede Abhängigkeit</b> — kein SDK, keine
/// Einstellungen, kein Protokoll. §8.1 verlangt das ausdrücklich, weil hier
/// Softphones typischerweise falsch wählen und weil nur so vollständig
/// getestet werden kann (§13).
///
/// Die Regeln:
/// <list type="bullet">
///   <item>Interne Ziele bleiben unverändert: <c>*</c> plus 3–4 Stellen, oder bis zu vier Ziffern.</item>
///   <item><c>00</c> am Anfang wird zu <c>+</c>.</item>
///   <item>Eine führende <c>0</c> wird durch das Länderpräfix ersetzt.</item>
///   <item>Ein führendes <c>+</c> bleibt.</item>
///   <item>SIP-Adressen und benannte Ziele werden nicht angetastet.</item>
/// </list>
/// </summary>
public sealed partial class NumberNormalizer
{
    /// <summary>
    /// Länderpräfix in der Form <c>+41</c>. Angaben wie <c>41</c> oder
    /// <c>0041</c> werden ebenfalls verstanden. <c>null</c> heisst: kein Land
    /// bekannt, dann wird nicht geraten.
    /// </summary>
    private readonly string? _countryPrefix;

    public NumberNormalizer(string? countryPrefix)
    {
        _countryPrefix = NormalizeCountryPrefix(countryPrefix);
    }

    /// <summary>Das wirksame Länderpräfix, etwa <c>+41</c>, oder <c>null</c>.</summary>
    public string? CountryPrefix => _countryPrefix;

    public string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();

        // SIP-Adressen und benannte Ziele bleiben, wie sie sind: wer sie
        // eingibt, meint sie auch so.
        if (IsAddressLike(trimmed))
        {
            return trimmed;
        }

        var digits = KeepDialCharacters(DropTrunkZero(trimmed));

        if (digits.Length == 0)
        {
            // Nichts Wählbares übrig — dann lieber die Eingabe zurückgeben als
            // eine leere Zeichenfolge, damit der Benutzer sieht, was er tippte.
            return trimmed;
        }

        // Interne Ziele werden nicht angefasst. Das schliesst Notrufe ein:
        // 112 oder 144 dürfen unter keinen Umständen zu +41112 werden.
        if (IsInternalTarget(digits))
        {
            return digits;
        }

        if (digits.StartsWith('+'))
        {
            return digits;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            return string.Concat("+", digits.AsSpan(2));
        }

        if (digits.StartsWith('0') && _countryPrefix is not null)
        {
            return string.Concat(_countryPrefix, digits.AsSpan(1));
        }

        return digits;
    }

    /// <summary>
    /// Ob das Ziel innerhalb der Anlage liegt (§8.1). Gebraucht auch von §8.4
    /// — BLF wird nur für interne Nebenstellen abonniert, nicht für
    /// Outlook-Kontakte, weil Subscribes Last auf der Anlage erzeugen (§14.8).
    /// </summary>
    public static bool IsInternalTarget(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var value = KeepDialCharacters(candidate.Trim());

        if (value.Length == 0)
        {
            return false;
        }

        // Dienstcodes: Stern plus drei oder vier Stellen.
        if (value.StartsWith('*'))
        {
            var rest = value[1..];
            return rest.Length is >= 2 and <= 4 && rest.All(char.IsAsciiDigit);
        }

        // Kurze Ziele: bis zu vier Ziffern. Darunter fallen Nebenstellen und
        // die Notrufnummern.
        return value.Length <= 4 && value.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Ob sich die Eingabe überhaupt wählen lässt (C4).
    ///
    /// <para><b>Die Gegenfrage zu <see cref="Normalize"/>, an derselben
    /// Stelle.</b> Seit ADR-046 ist das Nummernfeld auch das Suchfeld: dieselbe
    /// Eingabe geht an die Quellen und an die Wähltaste. <c>Normalize</c> reicht
    /// benannte Ziele absichtlich unverändert durch — «112» darf nie zu
    /// «+41112» werden —, und genau das machte aus einem gesuchten «Meier» ein
    /// <c>INVITE sip:Meier@…</c>, sobald jemand die Eingabetaste drückte. Der
    /// Benutzer hatte gesucht und bekam einen fehlgeschlagenen Anruf.</para>
    ///
    /// <para><b>Wahr für:</b> Ziffernfolgen mit den üblichen Trennzeichen,
    /// Dienstcodes mit <c>*</c> und <c>#</c>, und alles Adressartige
    /// (<c>sip:</c>, <c>tel:</c>, ein <c>@</c>) — wer eine SIP-Adresse eingibt,
    /// meint sie auch so. <b>Falsch für</b> alles, was Buchstaben enthält, ohne
    /// eine Adresse zu sein.</para>
    ///
    /// <para>Reine Funktion, ohne Länderpräfix: ob etwas <em>wählbar</em> ist,
    /// hängt nicht davon ab, in welchem Land nipp steht.</para>
    /// </summary>
    public static bool IsDialable(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var value = candidate.Trim();

        if (IsAddressLike(value))
        {
            return true;
        }

        // Es muss etwas Wählbares übrig bleiben — und nichts dürfen, was
        // dabei stillschweigend verschwindet. «Meier» ergäbe sonst die leere
        // Zeichenfolge und liefe als «nichts Wählbares» durch; «A1» ergäbe
        // eine «1», die niemand gemeint hat.
        return value.All(static c =>
            char.IsAsciiDigit(c) || c is '+' or '*' or '#'
            || c is ' ' or '-' or '/' or '(' or ')' or '.')
            && KeepDialCharacters(value).Length > 0;
    }

    /// <summary>
    /// Ob die Eingabe eine Adresse und keine Nummer ist. Ein <c>@</c> oder ein
    /// Schema wie <c>sip:</c> genügt als Merkmal.
    /// </summary>
    private static bool IsAddressLike(string value) =>
        value.Contains('@', StringComparison.Ordinal)
        || value.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Entfernt die eingeklammerte Verkehrsausscheidungsziffer hinter einer
    /// Landesvorwahl: aus <c>+41 (0)79 …</c> wird <c>+41 79 …</c> (C2).
    ///
    /// <para><b>Warum das nötig ist.</b> Diese Schreibweise steht in Outlook,
    /// in vCards und in jeder zweiten E-Mail-Signatur, und sie meint zwei
    /// Nummern in einer: <c>079 …</c> aus dem Inland, <c>+41 79 …</c> aus dem
    /// Ausland. Wer die internationale Form wählt, lässt die Null weg — genau
    /// dafür steht sie in Klammern. <c>KeepDialCharacters</c> sah nur eine
    /// Ziffer und behielt sie: gewählt wurde <c>+410791234567</c>, eine Nummer,
    /// die es nicht gibt, <b>ohne jede Fehlermeldung</b>.</para>
    ///
    /// <para><b>Warum die Regel so eng ist.</b> Sie greift nur am Anfang und
    /// nur direkt hinter <c>+</c> oder <c>00</c> plus einer bis drei Ziffern.
    /// Eine Null in Klammern <em>ohne</em> Landesvorwahl ist keine
    /// Verkehrsausscheidungsziffer, sondern eine Ziffer, die jemand getippt
    /// hat; sie bleibt. Wer hier grosszügiger filtert, macht aus einer
    /// vertippten Nummer stillschweigend eine andere — dieselbe Überlegung wie
    /// beim <c>+</c>, das nur an erster Stelle zählt.</para>
    ///
    /// <para>Das Ergebnis geht anschliessend wie bisher durch
    /// <see cref="KeepDialCharacters"/>; diese Funktion entfernt nichts als
    /// diese eine Klammergruppe.</para>
    /// </summary>
    private static string DropTrunkZero(string value) =>
        TrunkZeroAfterCountryCode().Replace(value, "$1", 1);

    /// <summary>
    /// <c>+41 (0)</c> oder <c>0041 (0)</c> am Anfang. Die Zeitgrenze ist die
    /// übliche Vorsichtsmassnahme; das Muster ist nicht rückverfolgend, aber
    /// die Eingabe kommt vom Benutzer.
    /// </summary>
    [GeneratedRegex(
        @"^(\+\d{1,3}|00\d{1,3})\s*\(\s*0\s*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex TrunkZeroAfterCountryCode();

    /// <summary>
    /// Behält nur Ziffern, ein führendes <c>+</c> und <c>*</c> oder <c>#</c>.
    /// Alles andere — Leerzeichen, Bindestriche, Schrägstriche, Klammern,
    /// Punkte — fliegt raus.
    ///
    /// Das <c>+</c> zählt nur an erster Stelle: sonst würde aus einer
    /// vertippten Nummer stillschweigend eine andere.
    /// </summary>
    private static string KeepDialCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsAsciiDigit(c) || c is '*' or '#')
            {
                builder.Append(c);
            }
            else if (c == '+' && builder.Length == 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Versteht <c>+41</c>, <c>41</c> und <c>0041</c> als dasselbe und liefert
    /// immer die Form <c>+41</c>. <c>null</c> bleibt <c>null</c> — ohne
    /// konfiguriertes Land wird nicht geraten.
    /// </summary>
    private static string? NormalizeCountryPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        var value = KeepDialCharacters(prefix.Trim());

        if (value.StartsWith('+'))
        {
            value = value[1..];
        }
        else if (value.StartsWith("00", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.Length > 0 && value.All(char.IsAsciiDigit)
            ? "+" + value
            : null;
    }
}
