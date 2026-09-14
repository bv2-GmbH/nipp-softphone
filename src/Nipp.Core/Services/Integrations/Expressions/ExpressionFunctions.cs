using System.Globalization;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Integrations.Expressions;

/// <summary>
/// Die erlaubten Funktionen — und nur diese (§21.2, ADR-016).
///
/// <b>Diese Liste ist die Sicherheitsgrenze der Ausdruckssprache.</b> Es gibt
/// keinen anderen Weg, in einem Ausdruck Verhalten auszulösen: keine
/// Methodenaufrufe auf Werten, keine Typnamen, keine Reflection. Was hier
/// nicht steht, wird schon beim Übersetzen abgelehnt — nicht erst beim
/// Auswerten, denn dann stünde es in einer Konfiguration, die jemand längst
/// verteilt hat.
///
/// Jede Funktion ist rein: gleiche Eingabe, gleiche Ausgabe, keine
/// Nebenwirkung, kein Zugriff nach aussen. <c>formatDate</c> ist der
/// Grenzfall, weil Formatierung Kultur braucht — deshalb steht dort, welche
/// verwendet wird und warum.
/// </summary>
internal static class ExpressionFunctions
{
    /// <summary>Wie viele Parameter eine Funktion nimmt. <c>null</c> heisst „beliebig viele".</summary>
    private sealed record Signature(int? Minimum, int? Maximum);

    private static readonly Dictionary<string, Signature> Known = new(StringComparer.Ordinal)
    {
        ["concat"] = new(Minimum: 1, Maximum: null),
        ["coalesce"] = new(Minimum: 1, Maximum: null),
        ["if"] = new(Minimum: 3, Maximum: 3),
        ["isEmpty"] = new(Minimum: 1, Maximum: 1),
        ["upper"] = new(Minimum: 1, Maximum: 1),
        ["lower"] = new(Minimum: 1, Maximum: 1),
        ["trim"] = new(Minimum: 1, Maximum: 1),
        ["substring"] = new(Minimum: 2, Maximum: 3),
        ["contains"] = new(Minimum: 2, Maximum: 2),
        ["startsWith"] = new(Minimum: 2, Maximum: 2),
        ["join"] = new(Minimum: 2, Maximum: 2),
        ["count"] = new(Minimum: 1, Maximum: 1),
        ["formatDate"] = new(Minimum: 2, Maximum: 2),
        ["formatPhone"] = new(Minimum: 1, Maximum: 1),
        ["formatNumber"] = new(Minimum: 2, Maximum: 2),

        // Die beiden quellenunabhängigen. Sie brauchen den Bereich selbst und
        // werden deshalb im Evaluator behandelt, nicht in Invoke — sie stehen
        // hier nur, damit ein Tippfehler beim Übersetzen auffällt und nicht
        // beim Anruf.
        ["role"] = new(Minimum: 1, Maximum: 1),
        ["anyOf"] = new(Minimum: 1, Maximum: null),
    };

    /// <summary>Für Fehlermeldungen: alle Namen, alphabetisch.</summary>
    public static string NamesForMessage { get; } =
        string.Join(", ", Known.Keys.OrderBy(static k => k, StringComparer.Ordinal));

    public static bool Exists(string name) => Known.ContainsKey(name);

    /// <summary>
    /// Ob die Anzahl Parameter passt. Liefert die Meldung, wenn nicht — schon
    /// beim Übersetzen, damit <c>substring(x)</c> nicht erst beim Anruf
    /// auffällt.
    /// </summary>
    public static string? CheckArity(string name, int count)
    {
        if (!Known.TryGetValue(name, out var signature))
        {
            return $"Die Funktion '{name}' gibt es nicht.";
        }

        if (signature.Minimum is { } min && count < min)
        {
            return $"'{name}' braucht mindestens {min} {(min == 1 ? "Angabe" : "Angaben")}, hat aber {count}.";
        }

        if (signature.Maximum is { } max && count > max)
        {
            return $"'{name}' nimmt höchstens {max} {(max == 1 ? "Angabe" : "Angaben")}, hat aber {count}.";
        }

        return null;
    }

    /// <summary>
    /// Ruft eine Funktion auf; die Werte in <paramref name="arguments"/> sind
    /// bereits ausgewertet.
    ///
    /// <b>Zwei Funktionen kommen hier nie an:</b> <c>if</c> und
    /// <c>coalesce</c> wählen aus, welcher Teilausdruck überhaupt gerechnet
    /// wird, und werden deshalb im Auswerter behandelt.
    /// </summary>
    /// <remarks>
    /// Wirft nicht. Was nicht geht, ergibt <see cref="ContextValue.Null"/>:
    /// ein Mapping darf einen Anruf nicht mitnehmen (§21.2).
    /// </remarks>
    public static ContextValue Invoke(string name, IReadOnlyList<ContextValue> arguments) => name switch
    {
        "concat" => Concat(arguments),
        "coalesce" => Coalesce(arguments),
        "isEmpty" => ContextValue.FromBoolean(arguments[0].IsEmpty),
        "upper" => Text(arguments[0].AsText().ToUpperInvariant()),
        "lower" => Text(arguments[0].AsText().ToLowerInvariant()),
        "trim" => Text(arguments[0].AsText().Trim()),
        "substring" => Substring(arguments),
        "contains" => ContextValue.FromBoolean(
            arguments[0].AsText().Contains(arguments[1].AsText(), StringComparison.OrdinalIgnoreCase)),
        "startsWith" => ContextValue.FromBoolean(
            arguments[0].AsText().StartsWith(arguments[1].AsText(), StringComparison.OrdinalIgnoreCase)),
        "join" => Join(arguments),
        "count" => Count(arguments[0]),
        "formatDate" => FormatDate(arguments[0], arguments[1].AsText()),
        "formatPhone" => Text(PhoneNumberFormat.ForDisplay(arguments[0].AsText())),
        "formatNumber" => FormatNumber(arguments[0], arguments[1].AsText()),
        _ => ContextValue.Null,
    };

    /// <summary>
    /// Höchstlänge eines Ergebnistexts (§21.2).
    ///
    /// <c>concat</c> und <c>join</c> können aus einer grossen Liste einen
    /// beliebig langen Text bauen. Auf einer Karte in einem 400 Pixel breiten
    /// Fenster ist alles jenseits davon ohnehin unsichtbar — abgeschnitten
    /// wird, damit eine unerwartete Antwort nicht die Oberfläche belastet.
    /// </summary>
    public const int MaxTextLength = 4096;

    private static ContextValue Text(string value) =>
        ContextValue.FromText(
            value.Length > MaxTextLength ? value[..MaxTextLength] : value);

    /// <summary>
    /// Verkettet. <c>null</c>-Glieder tragen nichts bei, statt die ganze
    /// Verkettung leer zu machen — <c>concat(vorname, ' ', nachname)</c> soll
    /// bei fehlendem Vornamen den Nachnamen zeigen.
    /// </summary>
    private static ContextValue Concat(IReadOnlyList<ContextValue> arguments)
    {
        var parts = arguments.Select(static a => a.AsText());
        return Text(string.Concat(parts));
    }

    /// <summary>Der erste Wert, der etwas enthält.</summary>
    private static ContextValue Coalesce(IReadOnlyList<ContextValue> arguments)
    {
        foreach (var argument in arguments)
        {
            if (!argument.IsEmpty)
            {
                return argument;
            }
        }

        return ContextValue.Null;
    }

    /// <summary>
    /// Teilzeichenfolge, <b>ohne</b> Bereichsfehler: eine Länge über das Ende
    /// hinaus schneidet am Ende ab, ein negativer Anfang beginnt bei null. Ein
    /// Mapping, das an einer zu kurzen Kundennummer scheitert, wäre der
    /// falsche Umgang mit fremden Daten.
    /// </summary>
    private static ContextValue Substring(IReadOnlyList<ContextValue> arguments)
    {
        var text = arguments[0].AsText();

        if (!arguments[1].TryAsNumber(out var startNumber))
        {
            return ContextValue.Null;
        }

        var start = Math.Clamp((int)startNumber, 0, text.Length);
        var length = text.Length - start;

        if (arguments.Count == 3)
        {
            if (!arguments[2].TryAsNumber(out var lengthNumber))
            {
                return ContextValue.Null;
            }

            length = Math.Clamp((int)lengthNumber, 0, text.Length - start);
        }

        return Text(text.Substring(start, length));
    }

    private static ContextValue Join(IReadOnlyList<ContextValue> arguments)
    {
        var separator = arguments[1].AsText();

        return arguments[0] switch
        {
            ListValue list => Text(string.Join(separator, list.Items.Select(static i => i.AsText()))),
            NullValue => ContextValue.Null,
            var single => Text(single.AsText()),
        };
    }

    /// <summary>
    /// Anzahl Einträge. Ein einzelner Wert zählt als eins, nichts als null —
    /// so lässt sich <c>count(x) > 0</c> schreiben, ohne zu wissen, ob die
    /// Gegenstelle eine Liste oder einen Einzelwert liefert. Genau das
    /// unterscheiden fremde APIs gern von Fall zu Fall.
    /// </summary>
    private static ContextValue Count(ContextValue value) => value switch
    {
        ListValue list => ContextValue.FromNumber(list.Items.Count),
        NullValue => ContextValue.FromNumber(0),
        _ => ContextValue.FromNumber(1),
    };

    /// <summary>
    /// Die Formen, in denen ein Datum aus einer fremden Antwort angenommen
    /// wird — <b>nur ISO 8601</b>, und ausdrücklich aufgezählt.
    ///
    /// <c>TryParse</c> mit der invarianten Kultur wäre grosszügiger und genau
    /// deshalb falsch: es nimmt auch <c>06.09.2026</c> an und deutet es als
    /// den 6. September. Dieselbe Angabe heisst in einem anderen Land der
    /// 9. Juni. Ein Datum, das um drei Monate danebenliegt, fällt auf einer
    /// Karte niemandem auf.
    /// </summary>
    private static readonly string[] IsoFormats =
    [
        "O",
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd HH:mm:ss",
    ];

    /// <summary>
    /// Datum formatieren.
    ///
    /// <b>Mit <see cref="CultureInfo.CurrentCulture"/></b>, anders als
    /// <see cref="ContextValue.AsText"/>: dieser Wert ist zum Lesen bestimmt,
    /// und <c>formatDate(x, 'd')</c> soll auf einem deutschsprachigen Windows
    /// die gewohnte Form zeigen. Wer eine feste Form braucht — etwa für einen
    /// Anfrageparameter —, schreibt sie aus: <c>'yyyy-MM-dd'</c>.
    ///
    /// Angenommen wird nur ISO 8601, siehe <see cref="IsoFormats"/>.
    /// </summary>
    private static ContextValue FormatDate(ContextValue value, string format)
    {
        var date = value switch
        {
            DateValue typed => typed.Value,
            TextValue text when DateTimeOffset.TryParseExact(
                text.Value,
                IsoFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                out var parsed) => parsed,
            _ => (DateTimeOffset?)null,
        };

        if (date is null)
        {
            return ContextValue.Null;
        }

        try
        {
            return Text(date.Value.ToString(format, CultureInfo.CurrentCulture));
        }
        catch (FormatException)
        {
            // Eine unbrauchbare Formatangabe steht in der Konfiguration, nicht
            // in den Daten — sie wird beim Prüfen gemeldet. Hier zählt nur,
            // dass sie keinen Anruf kostet.
            return ContextValue.Null;
        }
    }

    /// <summary>
    /// Zahl formatieren, mit <see cref="CultureInfo.CurrentCulture"/> — aus
    /// demselben Grund wie beim Datum. <c>formatNumber(erp.revenue, 'N0')</c>
    /// ergibt auf einem deutschsprachigen Windows <c>125'000</c>
    /// beziehungsweise <c>125.000</c>.
    /// </summary>
    private static ContextValue FormatNumber(ContextValue value, string format)
    {
        if (!value.TryAsNumber(out var number))
        {
            return ContextValue.Null;
        }

        try
        {
            return Text(number.ToString(format, CultureInfo.CurrentCulture));
        }
        catch (FormatException)
        {
            return ContextValue.Null;
        }
    }
}
