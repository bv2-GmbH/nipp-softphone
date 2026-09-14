using System.Globalization;
using System.Text;

namespace Nipp.Core.Services.Integrations.Expressions;

/// <summary>Art eines Zeichens im Ausdruck.</summary>
internal enum TokenKind
{
    Text,
    Number,
    True,
    False,
    Null,
    Identifier,
    JsonPath,
    OpenParen,
    CloseParen,
    Comma,
    Dot,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    And,
    Or,
    Not,
    Plus,
    Minus,
    End,
}

/// <param name="Kind">Was es ist.</param>
/// <param name="Text">Der Rohtext; bei <see cref="TokenKind.Text"/> der bereits ausgepackte Inhalt.</param>
/// <param name="Number">Der Zahlenwert, nur bei <see cref="TokenKind.Number"/>.</param>
/// <param name="Position">Stelle im Ausdruck, einsbasiert — sie steht in der Fehlermeldung.</param>
internal sealed record Token(TokenKind Kind, string Text, decimal Number, int Position);

/// <summary>
/// Zerlegt einen Ausdruck in Zeichen (ADR-016).
///
/// <b>Warum von Hand und nicht mit einer Bibliothek.</b> §21.2 verbietet
/// ausführbare Skripte. Eine allgemeine Ausdrucksbibliothek kann fast immer
/// mehr, als hier erlaubt sein darf — Typen auflösen, Methoden rufen,
/// Reflection —, und eine Bibliothek auf eine sichere Teilmenge
/// zurückzuschneiden ist schwerer zu verantworten als eine kleine Grammatik,
/// deren Umfang vollständig bekannt ist. Diese drei Klassen zusammen sind
/// überschaubar und lückenlos prüfbar.
///
/// <b>Grenzen sind Teil der Sicherheit</b>, nicht Sparsamkeit: die Ausdrücke
/// kommen aus einer Konfigurationsdatei, die über das Netz verteilt werden
/// kann (§11). Ein Ausdruck aus zehntausend Zeichen ist kein Mapping, sondern
/// ein Angriff auf den Anrufpfad.
/// </summary>
internal static class ExpressionTokenizer
{
    /// <summary>
    /// Höchstzahl Zeichen. Ein echtes Mapping bleibt weit darunter — der
    /// längste Ausdruck in der Beispielkonfiguration hat elf.
    /// </summary>
    public const int MaxTokens = 512;

    /// <summary>Höchstlänge des Quelltexts eines einzelnen Ausdrucks.</summary>
    public const int MaxLength = 4096;

    public static IReadOnlyList<Token> Tokenize(string? expression)
    {
        var input = expression ?? string.Empty;

        if (input.Length > MaxLength)
        {
            throw new ExpressionParseException(
                $"Der Ausdruck ist mit {input.Length} Zeichen zu lang (höchstens {MaxLength}).",
                position: MaxLength);
        }

        var tokens = new List<Token>();
        var i = 0;

        while (i < input.Length)
        {
            var c = input[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;

            if (c == '\'')
            {
                tokens.Add(ReadText(input, ref i));
            }
            else if (char.IsAsciiDigit(c))
            {
                tokens.Add(ReadNumber(input, ref i));
            }
            else if (c == '$')
            {
                tokens.Add(ReadJsonPath(input, ref i));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                tokens.Add(ReadIdentifier(input, ref i));
            }
            else
            {
                tokens.Add(ReadOperator(input, ref i));
            }

            if (tokens.Count > MaxTokens)
            {
                throw new ExpressionParseException(
                    $"Der Ausdruck hat mehr als {MaxTokens} Bestandteile.",
                    start + 1);
            }
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, 0, input.Length + 1));
        return tokens;
    }

    /// <summary>
    /// Eine Zeichenfolge in einfachen Anführungszeichen. Verdoppeln maskiert:
    /// <c>'Hans''s'</c>.
    ///
    /// Einfache statt doppelte Anführungszeichen, weil der ganze Ausdruck in
    /// JSON steht — dort müsste jedes doppelte Anführungszeichen maskiert
    /// werden, und ein Mapping voller <c>\"</c> liest niemand mehr.
    /// </summary>
    private static Token ReadText(string input, ref int i)
    {
        var start = i;
        i++;

        var builder = new StringBuilder();

        while (i < input.Length)
        {
            if (input[i] == '\'')
            {
                if (i + 1 < input.Length && input[i + 1] == '\'')
                {
                    builder.Append('\'');
                    i += 2;
                    continue;
                }

                i++;
                return new Token(TokenKind.Text, builder.ToString(), 0, start + 1);
            }

            builder.Append(input[i]);
            i++;
        }

        throw new ExpressionParseException(
            "Die Zeichenfolge wurde nicht geschlossen — es fehlt ein '.",
            start + 1);
    }

    private static Token ReadNumber(string input, ref int i)
    {
        var start = i;

        while (i < input.Length && (char.IsAsciiDigit(input[i]) || input[i] == '.'))
        {
            i++;
        }

        var text = input[start..i];

        // Invariant und nur mit Punkt: der Ausdruck steht in einer Datei, die
        // auf jedem Gerät gleich gelesen werden muss.
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            throw new ExpressionParseException($"'{text}' ist keine gültige Zahl.", start + 1);
        }

        return new Token(TokenKind.Number, text, number, start + 1);
    }

    /// <summary>
    /// Ein JSONPath auf den Rohkörper der Antwort, etwa <c>$.contact.name</c>.
    ///
    /// <b>Nur einfache Pfade.</b> Filter wie <c>[?(@.type == 'mobile')]</c>
    /// enthalten Klammern, Anführungszeichen und Operatoren; sie hier zu
    /// zerlegen hiesse, die JSONPath-Grammatik in dieser Datei ein zweites Mal
    /// zu bauen. Wer filtern will, nimmt <c>path</c> statt <c>expr</c> — dort
    /// geht der ganze Pfad ungeteilt an die Bibliothek.
    /// </summary>
    private static Token ReadJsonPath(string input, ref int i)
    {
        var start = i;
        i++;

        while (i < input.Length
            && (char.IsLetterOrDigit(input[i]) || input[i] is '.' or '_' or '-' or '[' or ']' or '*'))
        {
            i++;
        }

        var text = input[start..i];

        if (text.Length < 2)
        {
            throw new ExpressionParseException(
                "Nach dem $ fehlt der Pfad, etwa $.kunde.name.",
                start + 1);
        }

        return new Token(TokenKind.JsonPath, text, 0, start + 1);
    }

    private static Token ReadIdentifier(string input, ref int i)
    {
        var start = i;

        while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
        {
            i++;
        }

        var text = input[start..i];

        var kind = text switch
        {
            "true" => TokenKind.True,
            "false" => TokenKind.False,
            "null" => TokenKind.Null,
            _ => TokenKind.Identifier,
        };

        return new Token(kind, text, 0, start + 1);
    }

    private static Token ReadOperator(string input, ref int i)
    {
        var start = i;
        var c = input[i];
        var next = i + 1 < input.Length ? input[i + 1] : '\0';

        (TokenKind Kind, int Length) op = (c, next) switch
        {
            ('=', '=') => (TokenKind.Equal, 2),
            ('!', '=') => (TokenKind.NotEqual, 2),
            ('<', '=') => (TokenKind.LessOrEqual, 2),
            ('>', '=') => (TokenKind.GreaterOrEqual, 2),
            ('&', '&') => (TokenKind.And, 2),
            ('|', '|') => (TokenKind.Or, 2),
            ('(', _) => (TokenKind.OpenParen, 1),
            (')', _) => (TokenKind.CloseParen, 1),
            (',', _) => (TokenKind.Comma, 1),
            ('.', _) => (TokenKind.Dot, 1),
            ('<', _) => (TokenKind.Less, 1),
            ('>', _) => (TokenKind.Greater, 1),
            ('!', _) => (TokenKind.Not, 1),
            ('+', _) => (TokenKind.Plus, 1),
            ('-', _) => (TokenKind.Minus, 1),

            // Ein einzelnes = ist der häufigste Tippfehler und verdient eine
            // eigene Meldung: in dieser Sprache gibt es keine Zuweisung.
            ('=', _) => throw new ExpressionParseException(
                "Zum Vergleichen wird == geschrieben, nicht =. Eine Zuweisung gibt es hier nicht.",
                start + 1),

            _ => throw new ExpressionParseException(
                $"Das Zeichen '{c}' gehört nicht in einen Ausdruck.",
                start + 1),
        };

        i += op.Length;
        return new Token(op.Kind, input[start..i], 0, start + 1);
    }
}

/// <summary>
/// Ein Ausdruck lässt sich nicht lesen.
///
/// Fliegt <b>nur beim Übersetzen</b>, nie beim Auswerten: ein kaputter
/// Ausdruck ist ein Konfigurationsfehler und wird beim Prüfen gemeldet
/// (§21.3), mit Stelle. Zur Laufzeit wird nicht mehr geworfen — dort liefert
/// ein Fehler <c>null</c> und eine Diagnosezeile, damit ein Mapping niemals
/// einen Anruf mitnimmt.
/// </summary>
public sealed class ExpressionParseException : Exception
{
    public ExpressionParseException(string message, int position)
        : base($"{message} (Stelle {position})")
    {
        Position = position;
    }

    public ExpressionParseException()
    {
    }

    public ExpressionParseException(string message)
        : base(message)
    {
    }

    public ExpressionParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Einsbasierte Stelle im Ausdruck.</summary>
    public int Position { get; }
}
