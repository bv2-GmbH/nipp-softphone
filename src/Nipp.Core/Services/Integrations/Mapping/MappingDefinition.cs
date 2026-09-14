using System.Text.Json.Serialization;

namespace Nipp.Core.Services.Integrations.Mapping;

/// <summary>
/// Wie ein einzelnes Feld aus einer fremden Antwort entsteht (§21.3).
///
/// Entweder <see cref="Path"/> <b>oder</b> <see cref="Expr"/> — nie beides.
/// Der Pfad zieht einen Wert aus der Antwort, der Ausdruck rechnet aus dem,
/// was schon gemappt ist.
/// </summary>
/// <param name="Path">Ein JSONPath, etwa <c>$.contact.fullName</c>.</param>
/// <param name="Expr">
/// Ein Ausdruck, etwa <c>concat(firstName, ' ', lastName)</c>. Er sieht die
/// Felder derselben Quelle unter ihrem Namen und die rohe Antwort unter
/// <c>$.pfad</c>.
/// </param>
/// <param name="As">In welche Art der Wert gebracht wird.</param>
public sealed record FieldMapping(
    string? Path = null,
    string? Expr = null,
    [property: JsonConverter(typeof(CamelCaseEnumConverter))] ValueKind As = ValueKind.Auto)
{
    /// <summary>Ob genau eine der beiden Quellen angegeben ist.</summary>
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(Path) ^ !string.IsNullOrWhiteSpace(Expr);
}

/// <summary>
/// Das Mapping einer Quelle: Feldname zu Herkunft.
///
/// <b>Die Namen der Felder bestimmt der Administrator</b>, nicht das fremde
/// System. Genau darin besteht die Entkopplung aus §21.1: die Karte kennt
/// <c>crm.customerName</c>, nicht <c>$.contact.fullName</c>. Ändert die
/// Gegenstelle ihre Struktur, ändert sich das Mapping — die Karten bleiben.
/// </summary>
public sealed record MappingDefinition
{
    /// <summary>
    /// Feldname zu Herkunft. Die Reihenfolge zählt für Ausdrücke: sie sehen,
    /// was vor ihnen steht.
    /// </summary>
    public Dictionary<string, FieldMapping> Fields { get; init; } = [];

    /// <summary>
    /// Wann eine Antwort als „nichts gefunden" gilt, obwohl sie technisch in
    /// Ordnung ist — etwa <c>isEmpty($.contact)</c> oder <c>status == 404</c>.
    ///
    /// Ohne diese Regel wäre eine leere Antwort ein Erfolg mit lauter leeren
    /// Feldern, und die Karte zeigte eine Kundenkarte ohne Kunden.
    /// </summary>
    public string? EmptyWhen { get; init; }

    /// <summary>
    /// Bei einer Suche: der Pfad auf die Liste der Treffer, etwa
    /// <c>$.items[*]</c>. Jeder Treffer wird einzeln nach
    /// <see cref="Fields"/> gemappt.
    ///
    /// Leer heisst: die Antwort ist ein einzelner Datensatz.
    /// </summary>
    public string? ItemsPath { get; init; }
}
