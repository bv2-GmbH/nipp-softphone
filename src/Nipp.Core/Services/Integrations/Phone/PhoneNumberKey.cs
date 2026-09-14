using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Integrations.Phone;

/// <summary>
/// Eine Rufnummer in den Formen, die eine Integration braucht (§21).
///
/// <b>Warum es diesen Typ gibt.</b> Was aus der Signalisierung kommt, ist der
/// <c>Username</c> des From-Headers: mal <c>+41791234567</c>, mal
/// <c>0791234567</c>, mal <c>151</c>. Fremde Systeme erwarten wiederum sehr
/// Verschiedenes — das eine will E.164, das nächste die nationale Schreibweise,
/// das dritte nur Ziffern. Diese Umrechnung an jeder Abfragestelle zu
/// wiederholen hiesse, sie an jeder Stelle anders falsch zu haben.
///
/// <b>Reine Funktion, ohne Zustand und ohne Abhängigkeit ausser den beiden
/// bestehenden Nummernklassen.</b> Gerechnet wird über
/// <see cref="NumberNormalizer"/> — dieselbe Regel, nach der auch gewählt wird
/// (§8.1). Zwei Wege zur Wählform wären zwei Gelegenheiten, sie
/// auseinanderlaufen zu lassen.
///
/// <b>Zwei Fälle führen absichtlich zu keiner Abfrage.</b>
/// <list type="bullet">
///   <item>
///     <see cref="IsInternal"/> — Nebenstellen, Dienstcodes und
///     <b>Notrufnummern</b> (112, 144 sind höchstens vierstellig und damit
///     nach <see cref="NumberNormalizer.IsInternalTarget"/> intern). Für einen
///     Kollegen hat kein CRM eine Antwort, und die Nebenstelle eines
///     Mitarbeiters hat auf einem fremden Server nichts zu suchen (§21.4).
///   </item>
///   <item>
///     <see cref="IsAddress"/> — eine SIP-Adresse ist keine Rufnummer.
///   </item>
/// </list>
/// </summary>
/// <param name="E164">
/// Die internationale Form, etwa <c>+41791234567</c> — der Schlüssel, unter dem
/// zwischengespeichert wird. <b>Leer</b>, wenn sie sich nicht bilden lässt:
/// bei internen Zielen, bei Adressen, und wenn ohne Länderpräfix nicht zu
/// entscheiden ist, welches Land gemeint war. Leer heisst „nicht bekannt", nie
/// „geraten".
/// </param>
/// <param name="Digits">
/// Nur die Ziffern, ohne <c>+</c> und ohne führende Nullen — der
/// Vergleichs- und Zwischenspeicherschlüssel.
///
/// <b>Die führenden Nullen müssen weg</b>, und zwar aus demselben Grund, aus
/// dem <see cref="ClipResolver"/> sie intern abschneidet: <c>0445128430</c>
/// und <c>+41445128430</c> sind dieselbe Nummer, und ein Schlüssel, unter dem
/// sie zweimal im Zwischenspeicher landen, ist keiner.
/// </param>
/// <param name="National">
/// Die Form, wie man die Nummer <b>von hier aus</b> wählt: <c>0791234567</c>
/// im eigenen Land, <c>0049211123456</c> ins Ausland. Für Systeme, die kein
/// <c>+</c> verstehen — und davon gibt es mehr, als man hofft.
/// </param>
/// <param name="IsInternal">Nebenstelle, Dienstcode oder Notruf. Keine externe Abfrage.</param>
/// <param name="IsAddress">SIP-Adresse oder benanntes Ziel. Keine externe Abfrage.</param>
public sealed record PhoneNumberKey(
    string E164,
    string Digits,
    string National,
    bool IsInternal,
    bool IsAddress)
{
    /// <summary>
    /// Kürzeste und längste Teilnehmerrufnummer nach E.164: mindestens zwei
    /// Stellen (Landesvorwahl) plus Anschluss, höchstens fünfzehn insgesamt.
    /// Was ausserhalb liegt, ist keine E.164-Nummer — dann bleibt
    /// <see cref="E164"/> leer, statt eine erfundene Form weiterzureichen.
    /// </summary>
    private const int MinimumE164Digits = 8;
    private const int MaximumE164Digits = 15;

    /// <summary>Nichts Wählbares — etwa ein leerer oder anonymer From-Header.</summary>
    public static PhoneNumberKey None { get; } =
        new(string.Empty, string.Empty, string.Empty, IsInternal: false, IsAddress: false);

    /// <summary>
    /// Ob diese Nummer überhaupt an ein externes System gehen darf.
    ///
    /// <paramref name="allowInternal"/> ist der Schalter
    /// <c>lookupInternalNumbers</c> aus der Konfiguration; er steht ab Werk auf
    /// <c>false</c> (§21.4).
    /// </summary>
    public bool IsLookupCandidate(bool allowInternal) =>
        !IsAddress
        && Digits.Length > 0
        && (!IsInternal || allowInternal)
        && (E164.Length > 0 || allowInternal);

    /// <summary>
    /// Baut die Formen aus dem, was die Signalisierung geliefert hat.
    ///
    /// <paramref name="normalizer"/> trägt das Länderpräfix aus den
    /// Einstellungen (<c>Advanced.CountryPrefix</c>, Standard <c>+41</c>).
    /// Ohne Präfix wird nicht geraten: eine Nummer mit führender Null bleibt
    /// dann ohne <see cref="E164"/>.
    /// </summary>
    public static PhoneNumberKey From(string? raw, NumberNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(normalizer);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return None;
        }

        var trimmed = raw.Trim();

        // Eine Adresse wird nicht umgerechnet. DigitsOnly betrachtet bereits
        // nur den Teil vor dem @ — die Ziffern daraus können für einen lokalen
        // Abgleich taugen, für eine Abfrage nach aussen nicht.
        if (IsAddressLike(trimmed))
        {
            return new PhoneNumberKey(
                E164: string.Empty,
                Digits: Significant(trimmed),
                National: string.Empty,
                IsInternal: false,
                IsAddress: true);
        }

        var normalized = normalizer.Normalize(trimmed);

        if (NumberNormalizer.IsInternalTarget(normalized))
        {
            // Die Nebenstelle bleibt in National, wie sie ist: sie ist
            // innerhalb der Anlage eindeutig und ausserhalb bedeutungslos.
            return new PhoneNumberKey(
                E164: string.Empty,
                Digits: Significant(normalized),
                National: normalized,
                IsInternal: true,
                IsAddress: false);
        }

        var e164 = ToE164(normalized);

        return new PhoneNumberKey(
            E164: e164,

            // Aus der E.164-Form, wenn es eine gibt: sie ist die einzige
            // Schreibweise, in der die Landesvorwahl sicher enthalten ist.
            Digits: e164.Length > 0 ? e164[1..] : Significant(normalized),
            National: ToNational(e164, normalizer.CountryPrefix, normalized),
            IsInternal: false,
            IsAddress: false);
    }

    /// <summary>
    /// Die Ziffern ohne führende Nullen — die Aufbereitung, die
    /// <see cref="ClipResolver"/> für seinen Vergleich intern vornimmt.
    /// </summary>
    private static string Significant(string value) =>
        ClipResolver.DigitsOnly(value).TrimStart('0');

    /// <summary>
    /// Nimmt die Wählform an, wenn sie eine gültige E.164-Nummer ist.
    ///
    /// <see cref="NumberNormalizer.Normalize"/> liefert bei unbekanntem Land
    /// oder bei Eingaben mit <c>*</c> und <c>#</c> etwas, das kein <c>+</c>
    /// trägt oder keine reine Ziffernfolge ist. Beides ist keine E.164-Nummer,
    /// und ein System, das eine erwartet, bekommt dann lieber nichts als eine
    /// erfundene.
    /// </summary>
    private static string ToE164(string normalized)
    {
        if (!normalized.StartsWith('+'))
        {
            return string.Empty;
        }

        var rest = normalized[1..];

        if (rest.Length is < MinimumE164Digits or > MaximumE164Digits
            || !rest.All(char.IsAsciiDigit))
        {
            return string.Empty;
        }

        return normalized;
    }

    /// <summary>
    /// Die Form, wie von hier aus gewählt wird.
    ///
    /// Im eigenen Land die Verkehrsausscheidungsziffer <c>0</c> statt der
    /// Landesvorwahl, ins Ausland <c>00</c> davor. Ohne bekanntes Land oder
    /// ohne E.164-Form bleibt es bei dem, was die Normalisierung ergab — das
    /// ist immer noch näher an der Wahrheit als eine Umrechnung auf gut Glück.
    /// </summary>
    private static string ToNational(string e164, string? countryPrefix, string fallback)
    {
        if (e164.Length == 0)
        {
            return fallback;
        }

        if (countryPrefix is { Length: > 1 }
            && e164.StartsWith(countryPrefix, StringComparison.Ordinal))
        {
            return string.Concat("0", e164.AsSpan(countryPrefix.Length));
        }

        return string.Concat("00", e164.AsSpan(1));
    }

    /// <summary>
    /// Ob die Eingabe eine Adresse und keine Nummer ist. Dieselbe Regel wie in
    /// <see cref="NumberNormalizer"/>, die dort privat ist — sie hier zu
    /// wiederholen ist der kleinere Preis, als sie dort öffentlich zu machen
    /// und damit zu einer Zusicherung zu erheben.
    /// </summary>
    private static bool IsAddressLike(string value) =>
        value.Contains('@', StringComparison.Ordinal)
        || value.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
}
