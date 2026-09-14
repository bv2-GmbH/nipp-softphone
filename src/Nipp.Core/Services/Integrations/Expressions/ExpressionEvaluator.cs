using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.Services.Integrations.Expressions;

/// <summary>
/// Woher ein Ausdruck seine Werte nimmt.
///
/// Zwei Verwender, deshalb ein Interface: das <b>Mapping</b> löst gegen die
/// rohe Antwort einer Quelle auf (mit <c>$.pfad</c>), die <b>Karte</b> gegen
/// den fertigen Kontext-Schnappschuss (mit <c>quelle.feld</c>). Beide
/// benutzen dieselbe Sprache, und genau das ist der Sinn: ein berechnetes
/// Feld und eine Sichtbarkeitsregel sollen sich nicht unterschiedlich
/// verhalten.
/// </summary>
public interface IExpressionScope
{
    /// <summary>
    /// Ein Feld über seinen Pfad, etwa <c>crm.customerName</c> oder
    /// <c>number.e164</c>. Unbekannt ist kein Fehler, sondern
    /// <see cref="ContextValue.Null"/>.
    /// </summary>
    ContextValue Resolve(string path);

    /// <summary>
    /// Ein einfacher JSONPath auf die rohe Antwort. Ausserhalb eines Mappings
    /// gibt es keine — dann <see cref="ContextValue.Null"/>.
    /// </summary>
    ContextValue ResolveJsonPath(string path);

    /// <summary>
    /// Ein Feld <b>nach Bedeutung</b>, ohne Quelle — die Grundlage von
    /// <c>role(...)</c> und <c>anyOf(...)</c>.
    ///
    /// <para><b>Mit Vorgabe, und die Vorgabe ist die richtige Antwort.</b>
    /// Nur die Karte kennt mehrere Quellen gleichzeitig. Ein Mapping läuft
    /// gegen die Antwort <b>einer</b> Quelle, eine Anfragevorlage kennt nur
    /// die Rufnummer — dort gibt es nichts, worüber sich suchen liesse, und
    /// <see cref="ContextValue.Null"/> ist keine Notlösung, sondern die
    /// Wahrheit.</para>
    /// </summary>
    ContextValue ResolveAcrossSources(string field) => ContextValue.Null;
}

/// <summary>
/// Wertet einen übersetzten Ausdruck aus (ADR-016).
///
/// <b>Diese Klasse wirft nicht.</b> Sie läuft bei jedem Zustandswechsel eines
/// Anrufs, auf demselben Thread, der alle 20 ms <c>Core.Iterate()</c> bedient
/// (§6, §14.1). Eine Ausnahme von hier stiege durch die Kartenlogik in einen
/// SDK-Ereignishandler — und eine Ausnahme in einem Ereignishandler nimmt die
/// ganze Anwendung mit. Was nicht geht, ergibt <see cref="ContextValue.Null"/>
/// und, wenn ein Sammler mitgegeben wurde, eine Zeile für die Diagnose.
///
/// <b>Unbekanntes bleibt unbekannt.</b> Ein Vergleich, dessen Operanden nicht
/// zusammenpassen, ergibt weder wahr noch falsch, sondern <c>null</c> — und
/// <see cref="IsTrue"/> behandelt nur ausdrückliches <c>true</c> als wahr.
/// Ein Feld, dessen Bedingung sich nicht entscheiden lässt, wird damit
/// <b>nicht</b> angezeigt. Die Gegenrichtung wäre schlimmer: eine Karte, die
/// bei fehlenden Daten Dinge behauptet.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>
    /// Wertet aus. Wirft nicht.
    /// </summary>
    /// <param name="node">Der übersetzte Ausdruck.</param>
    /// <param name="scope">Woher die Werte kommen.</param>
    /// <param name="diagnostics">
    /// Sammelt, was nicht ging — für die Vorschau in den Einstellungen und für
    /// das Diagnosepaket. <b>Ohne Werte, nur mit Ursachen:</b> hier landet
    /// „Vergleich zwischen Text und Zahl", nicht der Inhalt des Feldes (§21.2).
    /// </param>
    public static ContextValue Evaluate(
        ExpressionNode node,
        IExpressionScope scope,
        IList<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(scope);

        try
        {
            return Visit(node, scope, diagnostics);
        }
        catch (Exception ex)
        {
            // Absichtlich alles: der Baum ist geprüft, aber die Werte kommen
            // von einem fremden Server. Ein Ausdruck darf kein Anrufereignis
            // kosten — die Begründung steht oben.
            diagnostics?.Add($"Der Ausdruck liess sich nicht auswerten: {ex.GetType().Name}");
            return ContextValue.Null;
        }
    }

    /// <summary>
    /// Ob eine Bedingung erfüllt ist. Nur ausdrückliches <c>true</c> zählt;
    /// <c>null</c> und alles andere gelten als nicht erfüllt.
    /// </summary>
    public static bool IsTrue(ContextValue value) =>
        value is BooleanValue { Value: true };

    private static ContextValue Visit(
        ExpressionNode node,
        IExpressionScope scope,
        IList<string>? diagnostics) => node switch
        {
            LiteralNode literal => literal.Value,
            ReferenceNode reference => scope.Resolve(reference.Path),
            JsonPathNode path => scope.ResolveJsonPath(path.Path),
            UnaryNode unary => VisitUnary(unary, scope, diagnostics),
            BinaryNode binary => VisitBinary(binary, scope, diagnostics),
            CallNode call => VisitCall(call, scope, diagnostics),
            _ => ContextValue.Null,
        };

    private static ContextValue VisitUnary(
        UnaryNode node,
        IExpressionScope scope,
        IList<string>? diagnostics)
    {
        var operand = Visit(node.Operand, scope, diagnostics);

        switch (node.Operator)
        {
            case UnaryOperator.Not:
                return operand.TryAsBoolean(out var flag)
                    ? ContextValue.FromBoolean(!flag)
                    : Unknown(diagnostics, "! braucht einen Wahrheitswert");

            case UnaryOperator.Negate:
                return operand.TryAsNumber(out var number)
                    ? ContextValue.FromNumber(-number)
                    : Unknown(diagnostics, "das Vorzeichen braucht eine Zahl");

            default:
                return ContextValue.Null;
        }
    }

    private static ContextValue VisitBinary(
        BinaryNode node,
        IExpressionScope scope,
        IList<string>? diagnostics)
    {
        // Kurzschluss: die rechte Seite wird nur ausgewertet, wenn sie das
        // Ergebnis noch ändern kann. Das ist nicht Sparsamkeit — rechts kann
        // ein JSONPath über eine grosse Antwort stehen.
        if (node.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            var shortCircuit = node.Operator == BinaryOperator.Or;
            var left = Visit(node.Left, scope, diagnostics);

            if (left.TryAsBoolean(out var leftFlag) && leftFlag == shortCircuit)
            {
                return ContextValue.FromBoolean(shortCircuit);
            }

            var right = Visit(node.Right, scope, diagnostics);

            if (right.TryAsBoolean(out var rightFlag) && rightFlag == shortCircuit)
            {
                return ContextValue.FromBoolean(shortCircuit);
            }

            // Hier ist keine Seite der abkürzende Wert. Beide bekannt heisst:
            // das Ergebnis ist der andere Wert. Sonst unbekannt.
            return left.TryAsBoolean(out _) && right.TryAsBoolean(out _)
                ? ContextValue.FromBoolean(!shortCircuit)
                : Unknown(diagnostics, "eine Verknüpfung braucht Wahrheitswerte");
        }

        var a = Visit(node.Left, scope, diagnostics);
        var b = Visit(node.Right, scope, diagnostics);

        return node.Operator switch
        {
            BinaryOperator.Equal => ContextValue.FromBoolean(AreEqual(a, b)),
            BinaryOperator.NotEqual => ContextValue.FromBoolean(!AreEqual(a, b)),

            BinaryOperator.Less or BinaryOperator.LessOrEqual
                or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual =>
                Compare(node.Operator, a, b, diagnostics),

            BinaryOperator.Add or BinaryOperator.Subtract =>
                Arithmetic(node.Operator, a, b, diagnostics),

            _ => ContextValue.Null,
        };
    }

    /// <summary>
    /// Gleichheit, <b>ohne stillschweigende Umwandlung</b>: <c>'3' == 3</c>
    /// ist falsch. Zwei Werte sind gleich, wenn sie dieselbe Art haben und
    /// denselben Inhalt.
    ///
    /// <b>Eine Ausnahme, mit Absicht:</b> Text wird ohne Rücksicht auf
    /// Gross- und Kleinschreibung verglichen. Fremde Systeme liefern
    /// <c>Active</c> und <c>active</c> für dieselbe Sache, und ein Mapping,
    /// das daran scheitert, wäre für den Administrator nicht zu erklären.
    /// <c>contains</c> und <c>startsWith</c> verhalten sich genauso.
    /// </summary>
    private static bool AreEqual(ContextValue a, ContextValue b) => (a, b) switch
    {
        (NullValue, NullValue) => true,
        (NullValue, _) or (_, NullValue) => false,
        (TextValue x, TextValue y) => string.Equals(x.Value, y.Value, StringComparison.OrdinalIgnoreCase),
        (NumberValue x, NumberValue y) => x.Value == y.Value,
        (BooleanValue x, BooleanValue y) => x.Value == y.Value,
        (DateValue x, DateValue y) => x.Value == y.Value,
        (ListValue x, ListValue y) =>
            x.Items.Count == y.Items.Count
            && x.Items.Zip(y.Items).All(static pair => AreEqual(pair.First, pair.Second)),
        _ => false,
    };

    /// <summary>
    /// Grösser und kleiner — nur für Zahlen und für Zeitpunkte. Ein
    /// Grössenvergleich zwischen Texten wäre kulturabhängig und damit auf
    /// verschiedenen Geräten verschieden; das gibt es hier nicht.
    /// </summary>
    private static ContextValue Compare(
        BinaryOperator op,
        ContextValue a,
        ContextValue b,
        IList<string>? diagnostics)
    {
        int order;

        if (a is DateValue left && b is DateValue right)
        {
            order = left.Value.CompareTo(right.Value);
        }
        else if (a.TryAsNumber(out var x) && b.TryAsNumber(out var y))
        {
            order = x.CompareTo(y);
        }
        else
        {
            return Unknown(diagnostics, "ein Grössenvergleich braucht Zahlen oder Zeitpunkte");
        }

        return ContextValue.FromBoolean(op switch
        {
            BinaryOperator.Less => order < 0,
            BinaryOperator.LessOrEqual => order <= 0,
            BinaryOperator.Greater => order > 0,
            _ => order >= 0,
        });
    }

    /// <summary>
    /// <c>+</c> und <c>-</c> rechnen, sie verketten nicht. Für Text gibt es
    /// <c>concat</c> — ein <c>+</c>, das je nach Datenlage rechnet oder
    /// verkettet, ist die Quelle für Karten, die beim Kunden anders aussehen
    /// als im Test.
    /// </summary>
    private static ContextValue Arithmetic(
        BinaryOperator op,
        ContextValue a,
        ContextValue b,
        IList<string>? diagnostics)
    {
        if (!a.TryAsNumber(out var x) || !b.TryAsNumber(out var y))
        {
            return Unknown(
                diagnostics,
                "+ und - rechnen nur mit Zahlen; für Text gibt es concat");
        }

        try
        {
            return ContextValue.FromNumber(op == BinaryOperator.Add ? x + y : x - y);
        }
        catch (OverflowException)
        {
            return Unknown(diagnostics, "das Ergebnis ist zu gross");
        }
    }

    private static ContextValue VisitCall(
        CallNode node,
        IExpressionScope scope,
        IList<string>? diagnostics)
    {
        // if und coalesce entscheiden selbst, was gerechnet wird; role und
        // anyOf brauchen den Bereich und nicht nur ausgerechnete Werte.
        switch (node.Name)
        {
            case "role":
                {
                    // Der Rollenname ist ein Literal, wird aber wie jedes
                    // andere Argument ausgewertet — dann darf auch
                    // `role(if(x, 'name', 'company'))` dastehen. Kein
                    // Sonderfall im Parser dafür.
                    var name = Visit(node.Arguments[0], scope, diagnostics).AsText();
                    var role = Cards.FieldCatalog.RoleFromExpression(name);

                    if (role is null)
                    {
                        return Unknown(
                            diagnostics,
                            $"die Rolle '{name}' gibt es nicht; bekannt sind "
                                + string.Join(", ", Cards.FieldCatalog.RoleNames));
                    }

                    foreach (var field in Cards.FieldCatalog.NamesFor(role.Value))
                    {
                        var value = scope.ResolveAcrossSources(field);

                        if (!value.IsEmpty)
                        {
                            return value;
                        }
                    }

                    return ContextValue.Null;
                }

            case "anyOf":
                {
                    foreach (var argument in node.Arguments)
                    {
                        var field = Visit(argument, scope, diagnostics).AsText();

                        if (field.Length == 0)
                        {
                            continue;
                        }

                        var value = scope.ResolveAcrossSources(field);

                        if (!value.IsEmpty)
                        {
                            return value;
                        }
                    }

                    return ContextValue.Null;
                }

            case "if":
                {
                    var condition = Visit(node.Arguments[0], scope, diagnostics);
                    var branch = IsTrue(condition) ? node.Arguments[1] : node.Arguments[2];

                    return Visit(branch, scope, diagnostics);
                }

            case "coalesce":
                {
                    foreach (var argument in node.Arguments)
                    {
                        var value = Visit(argument, scope, diagnostics);

                        if (!value.IsEmpty)
                        {
                            return value;
                        }
                    }

                    return ContextValue.Null;
                }

            default:
                {
                    var arguments = new ContextValue[node.Arguments.Count];

                    for (var i = 0; i < arguments.Length; i++)
                    {
                        arguments[i] = Visit(node.Arguments[i], scope, diagnostics);
                    }

                    return ExpressionFunctions.Invoke(node.Name, arguments);
                }
        }
    }

    private static ContextValue Unknown(IList<string>? diagnostics, string reason)
    {
        diagnostics?.Add(reason);
        return ContextValue.Null;
    }
}
