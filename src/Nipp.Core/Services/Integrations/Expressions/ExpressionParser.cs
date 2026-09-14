using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.Services.Integrations.Expressions;

/// <summary>
/// Übersetzt einen Ausdruck in einen Baum (ADR-016).
///
/// Rekursiv absteigend, mit der üblichen Rangfolge — <c>||</c> bindet am
/// schwächsten, ein Aufruf am stärksten:
///
/// <code>
/// ausdruck    := oder
/// oder        := und ( '||' und )*
/// und         := gleichheit ( '&amp;&amp;' gleichheit )*
/// gleichheit  := vergleich ( ('==' | '!=') vergleich )*
/// vergleich   := summe ( ('&lt;' | '&lt;=' | '&gt;' | '&gt;=') summe )*
/// summe       := vorzeichen ( ('+' | '-') vorzeichen )*
/// vorzeichen  := ('!' | '-') vorzeichen | einfach
/// einfach     := zahl | text | true | false | null
///              | bezeichner ( '.' bezeichner )*
///              | bezeichner '(' ( ausdruck ( ',' ausdruck )* )? ')'
///              | jsonpfad
///              | '(' ausdruck ')'
/// </code>
///
/// <b>Was die Sprache nicht hat, und zwar mit Absicht:</b> keine Zuweisung,
/// keine Schleife, keine eigene Funktion, keinen Zugriff auf Typen, Dateien
/// oder Umgebung, keine Rekursion. Der einzige Weg zu Verhalten sind die
/// Funktionen aus <see cref="ExpressionFunctions"/>, und die Liste steht im
/// Quelltext (§21.2).
/// </summary>
public static class ExpressionParser
{
    /// <summary>
    /// Grösste erlaubte Verschachtelungstiefe.
    ///
    /// Der Auswerter läuft rekursiv über den Baum. Ohne Grenze liesse sich mit
    /// genügend Klammern ein <c>StackOverflowException</c> erzeugen — und die
    /// lässt sich in .NET nicht abfangen, sie beendet den Prozess. Bei einem
    /// Softphone hiesse das: die Konfiguration legt das Telefon still.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Übersetzt einen Ausdruck. Wirft <see cref="ExpressionParseException"/>
    /// mit Stelle — der Aufrufer ist die Prüfung der Konfiguration, nicht der
    /// Anrufpfad.
    /// </summary>
    public static ExpressionNode Parse(string? expression)
    {
        var tokens = ExpressionTokenizer.Tokenize(expression);
        var state = new ParserState(tokens);

        var node = ParseOr(state);

        if (state.Current.Kind != TokenKind.End)
        {
            throw new ExpressionParseException(
                $"Nach dem Ausdruck steht noch '{state.Current.Text}'. Fehlt ein Operator oder eine Klammer?",
                state.Current.Position);
        }

        if (node.Depth > MaxDepth)
        {
            throw new ExpressionParseException(
                $"Der Ausdruck ist mit {node.Depth} Ebenen zu tief verschachtelt (höchstens {MaxDepth}).",
                position: 1);
        }

        return node;
    }

    /// <summary>
    /// Übersetzt und meldet den Fehler als Text statt als Ausnahme — für die
    /// Prüfung einer Konfigurationsdatei, die alle Fehler auf einmal
    /// aufzählen soll, statt beim ersten abzubrechen.
    /// </summary>
    public static bool TryParse(string? expression, out ExpressionNode? node, out string? error)
    {
        try
        {
            node = Parse(expression);
            error = null;
            return true;
        }
        catch (ExpressionParseException ex)
        {
            node = null;
            error = ex.Message;
            return false;
        }
    }

    private sealed class ParserState(IReadOnlyList<Token> tokens)
    {
        private int _index;

        public Token Current => tokens[_index];

        public Token Take() => tokens[_index++];

        public bool TakeIf(TokenKind kind)
        {
            if (tokens[_index].Kind != kind)
            {
                return false;
            }

            _index++;
            return true;
        }

        public void Expect(TokenKind kind, string what)
        {
            if (!TakeIf(kind))
            {
                throw new ExpressionParseException(
                    $"Hier fehlt {what}, gefunden wurde '{Describe(Current)}'.",
                    Current.Position);
            }
        }

        private static string Describe(Token token) =>
            token.Kind == TokenKind.End ? "das Ende des Ausdrucks" : token.Text;
    }

    private static ExpressionNode ParseOr(ParserState state)
    {
        var left = ParseAnd(state);

        while (state.TakeIf(TokenKind.Or))
        {
            left = new BinaryNode(BinaryOperator.Or, left, ParseAnd(state));
        }

        return left;
    }

    private static ExpressionNode ParseAnd(ParserState state)
    {
        var left = ParseEquality(state);

        while (state.TakeIf(TokenKind.And))
        {
            left = new BinaryNode(BinaryOperator.And, left, ParseEquality(state));
        }

        return left;
    }

    private static ExpressionNode ParseEquality(ParserState state)
    {
        var left = ParseComparison(state);

        while (true)
        {
            var op = state.Current.Kind switch
            {
                TokenKind.Equal => BinaryOperator.Equal,
                TokenKind.NotEqual => BinaryOperator.NotEqual,
                _ => (BinaryOperator?)null,
            };

            if (op is null)
            {
                return left;
            }

            state.Take();
            left = new BinaryNode(op.Value, left, ParseComparison(state));
        }
    }

    private static ExpressionNode ParseComparison(ParserState state)
    {
        var left = ParseAdditive(state);

        while (true)
        {
            var op = state.Current.Kind switch
            {
                TokenKind.Less => BinaryOperator.Less,
                TokenKind.LessOrEqual => BinaryOperator.LessOrEqual,
                TokenKind.Greater => BinaryOperator.Greater,
                TokenKind.GreaterOrEqual => BinaryOperator.GreaterOrEqual,
                _ => (BinaryOperator?)null,
            };

            if (op is null)
            {
                return left;
            }

            state.Take();
            left = new BinaryNode(op.Value, left, ParseAdditive(state));
        }
    }

    private static ExpressionNode ParseAdditive(ParserState state)
    {
        var left = ParseUnary(state);

        while (true)
        {
            var op = state.Current.Kind switch
            {
                TokenKind.Plus => BinaryOperator.Add,
                TokenKind.Minus => BinaryOperator.Subtract,
                _ => (BinaryOperator?)null,
            };

            if (op is null)
            {
                return left;
            }

            state.Take();
            left = new BinaryNode(op.Value, left, ParseUnary(state));
        }
    }

    private static ExpressionNode ParseUnary(ParserState state)
    {
        if (state.TakeIf(TokenKind.Not))
        {
            return new UnaryNode(UnaryOperator.Not, ParseUnary(state));
        }

        if (state.TakeIf(TokenKind.Minus))
        {
            return new UnaryNode(UnaryOperator.Negate, ParseUnary(state));
        }

        return ParsePrimary(state);
    }

    private static ExpressionNode ParsePrimary(ParserState state)
    {
        var token = state.Current;

        switch (token.Kind)
        {
            case TokenKind.Number:
                state.Take();
                return new LiteralNode(ContextValue.FromNumber(token.Number));

            case TokenKind.Text:
                state.Take();

                // Absichtlich nicht über FromText: ein leeres Literal '' soll
                // ein leerer Text bleiben und nicht zu null werden — sonst
                // liesse sich mit == '' nicht auf Leere prüfen.
                return new LiteralNode(new TextValue(token.Text));

            case TokenKind.True:
                state.Take();
                return new LiteralNode(ContextValue.FromBoolean(true));

            case TokenKind.False:
                state.Take();
                return new LiteralNode(ContextValue.FromBoolean(false));

            case TokenKind.Null:
                state.Take();
                return new LiteralNode(ContextValue.Null);

            case TokenKind.JsonPath:
                state.Take();
                return new JsonPathNode(token.Text);

            case TokenKind.OpenParen:
                state.Take();
                var inner = ParseOr(state);
                state.Expect(TokenKind.CloseParen, "eine schliessende Klammer");
                return inner;

            case TokenKind.Identifier:
                return ParseIdentifier(state);

            default:
                throw new ExpressionParseException(
                    token.Kind == TokenKind.End
                        ? "Der Ausdruck bricht ab — hier fehlt ein Wert."
                        : $"'{token.Text}' kann hier nicht stehen.",
                    token.Position);
        }
    }

    /// <summary>
    /// Ein Bezeichner ist entweder ein Aufruf oder ein Feldverweis. Die
    /// Klammer entscheidet.
    /// </summary>
    private static ExpressionNode ParseIdentifier(ParserState state)
    {
        var first = state.Take();

        if (state.Current.Kind == TokenKind.OpenParen)
        {
            if (!ExpressionFunctions.Exists(first.Text))
            {
                throw new ExpressionParseException(
                    $"Die Funktion '{first.Text}' gibt es nicht. Erlaubt sind: "
                        + $"{ExpressionFunctions.NamesForMessage}.",
                    first.Position);
            }

            state.Take();
            var arguments = new List<ExpressionNode>();

            if (state.Current.Kind != TokenKind.CloseParen)
            {
                do
                {
                    arguments.Add(ParseOr(state));
                }
                while (state.TakeIf(TokenKind.Comma));
            }

            state.Expect(TokenKind.CloseParen, "eine schliessende Klammer");

            if (ExpressionFunctions.CheckArity(first.Text, arguments.Count) is { } problem)
            {
                throw new ExpressionParseException(problem, first.Position);
            }

            return new CallNode(first.Text, arguments);
        }

        // Feldverweis: quelle.feld, oder tiefer. Punkte gehören zum Pfad.
        var path = first.Text;

        while (state.TakeIf(TokenKind.Dot))
        {
            if (state.Current.Kind != TokenKind.Identifier)
            {
                throw new ExpressionParseException(
                    "Nach dem Punkt fehlt der Feldname.",
                    state.Current.Position);
            }

            path = string.Concat(path, ".", state.Take().Text);
        }

        // Ein Pfad mit Klammer dahinter ist der Versuch, etwas aufzurufen, das
        // wie ein Typ aussieht: System.IO.File.Delete('x'). Die Sprache lehnt
        // das ohnehin ab — ohne diesen Zweig aber mit „nach dem Ausdruck steht
        // noch (", was den Grund verschweigt. Wer das schreibt, soll lesen,
        // dass es hier keine Typen gibt und was stattdessen erlaubt ist.
        if (state.Current.Kind == TokenKind.OpenParen)
        {
            throw new ExpressionParseException(
                $"Die Funktion '{path}' gibt es nicht — Funktionen haben keinen Punkt im Namen, "
                    + $"und Typen oder Methoden sind hier nicht erreichbar. Erlaubt sind: "
                    + $"{ExpressionFunctions.NamesForMessage}.",
                first.Position);
        }

        return new ReferenceNode(path);
    }
}
