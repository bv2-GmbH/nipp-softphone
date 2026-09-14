namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Macht aus einer Rufnummer etwas Lesbares.
///
/// Das Gegenstück zu <see cref="NumberNormalizer"/>: der bringt eine Nummer in
/// die Form, die gewählt wird, dieser in die Form, die jemand liest.
/// <c>+41786672728</c> ist richtig und trotzdem eine Zumutung — auf einen Blick
/// erkennt man daran nichts, und wer sie mit einer notierten Nummer vergleicht,
/// zählt Ziffern.
///
/// Reine Funktion, testbar, ohne Zustand. Sie kennt nur den Schweizer
/// Nummernplan; alles andere bleibt unverändert, statt falsch gruppiert zu
/// werden.
/// </summary>
public static class PhoneNumberFormat
{
    /// <summary>
    /// Formatiert eine Nummer für die Anzeige. Was nicht erkannt wird, kommt
    /// unverändert zurück — eine falsch gruppierte Nummer wäre schlechter als
    /// eine ungruppierte.
    /// </summary>
    public static string ForDisplay(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        var trimmed = number.Trim();

        // Eine SIP-Adresse oder ein benanntes Ziel ist keine Nummer.
        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            return trimmed;
        }

        var digits = Digits(trimmed);

        if (digits.Length == 0)
        {
            return trimmed;
        }

        // Interne Nebenstelle: bleibt, wie sie ist. Eine dreistellige Nummer zu
        // gruppieren macht sie nicht lesbarer.
        if (digits.Length <= 5)
        {
            return trimmed;
        }

        // +41 79 123 45 67 — die übliche Schreibweise in der Schweiz.
        if (digits.StartsWith("41", StringComparison.Ordinal) && digits.Length == 11)
        {
            return $"+41 {digits[2..4]} {digits[4..7]} {digits[7..9]} {digits[9..]}";
        }

        // 0041… ist dieselbe Nummer in der alten Schreibweise.
        if (digits.StartsWith("0041", StringComparison.Ordinal) && digits.Length == 13)
        {
            return ForDisplay("+" + digits[2..]);
        }

        // 079 123 45 67 — national, mit der Verkehrsausscheidungsziffer.
        if (digits.StartsWith('0') && digits.Length == 10)
        {
            return $"{digits[..3]} {digits[3..6]} {digits[6..8]} {digits[8..]}";
        }

        // Ausland oder etwas Unbekanntes: nur das + erhalten, sonst nichts tun.
        return trimmed.StartsWith('+') ? "+" + digits : trimmed;
    }

    private static string Digits(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                buffer[length++] = c;
            }
        }

        return new string(buffer, 0, length);
    }
}
