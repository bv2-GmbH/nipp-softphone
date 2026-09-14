using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.Services.Integrations.Expressions;

/// <summary>
/// Ein übersetzter Ausdruck (ADR-016).
///
/// Der Baum entsteht einmal beim Laden der Konfiguration und wird danach nur
/// noch ausgewertet — das ist der Grund, warum ein Ausdruck auf einer Karte
/// keine messbare Zeit kostet, obwohl er bei jedem Zustandswechsel eines
/// Anrufs neu ausgewertet wird (§14.1).
///
/// Die Hierarchie ist geschlossen; alle Knoten stehen in dieser Datei.
/// </summary>
public abstract record ExpressionNode
{
    /// <summary>
    /// Wie tief der Baum unter diesem Knoten reicht. Der Parser begrenzt sie,
    /// damit eine mit Klammern erzeugte Tiefe nicht den Stapel des
    /// Auswerters aufbraucht.
    /// </summary>
    internal abstract int Depth { get; }
}

/// <summary>Ein fester Wert: <c>'Text'</c>, <c>42</c>, <c>true</c>, <c>null</c>.</summary>
internal sealed record LiteralNode(ContextValue Value) : ExpressionNode
{
    internal override int Depth => 1;
}

/// <summary>
/// Ein Feld im Kontext: <c>crm.customerName</c>, <c>number.e164</c>,
/// <c>status</c>.
/// </summary>
/// <param name="Path">Der ganze Pfad mit Punkten, wie geschrieben.</param>
internal sealed record ReferenceNode(string Path) : ExpressionNode
{
    internal override int Depth => 1;
}

/// <summary>
/// Ein einfacher JSONPath auf den Rohkörper: <c>$.contact.name</c>. Nur in
/// einem Mapping sinnvoll; ausserhalb liefert er <c>null</c>.
/// </summary>
internal sealed record JsonPathNode(string Path) : ExpressionNode
{
    internal override int Depth => 1;
}

/// <summary>Ein Aufruf aus der erlaubten Liste — siehe <see cref="ExpressionFunctions"/>.</summary>
internal sealed record CallNode(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode
{
    internal override int Depth =>
        1 + (Arguments.Count == 0 ? 0 : Arguments.Max(static a => a.Depth));
}

/// <summary>Vergleich, Verknüpfung oder Rechnung mit zwei Seiten.</summary>
internal sealed record BinaryNode(BinaryOperator Operator, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode
{
    internal override int Depth => 1 + Math.Max(Left.Depth, Right.Depth);
}

/// <summary>Verneinung oder Vorzeichen.</summary>
internal sealed record UnaryNode(UnaryOperator Operator, ExpressionNode Operand) : ExpressionNode
{
    internal override int Depth => 1 + Operand.Depth;
}

internal enum BinaryOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    And,
    Or,
    Add,
    Subtract,
}

internal enum UnaryOperator
{
    Not,
    Negate,
}
