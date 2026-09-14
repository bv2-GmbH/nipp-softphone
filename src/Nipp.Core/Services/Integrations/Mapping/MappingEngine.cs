using System.Text.Json;
using System.Text.Json.Nodes;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;

namespace Nipp.Core.Services.Integrations.Mapping;

/// <summary>
/// Ein übersetztes Mapping. Entsteht einmal beim Laden der Konfiguration.
/// </summary>
public sealed class CompiledMapping
{
    /// <summary>Ein Feld nach dem Übersetzen: entweder Pfad oder Ausdruck.</summary>
    internal sealed record Field(
        string Name,
        JsonPathBinding? Path,
        ExpressionNode? Expression,
        ValueKind Kind);

    internal IReadOnlyList<Field> PathFields { get; init; } = [];

    internal IReadOnlyList<Field> ExpressionFields { get; init; } = [];

    internal ExpressionNode? EmptyWhen { get; init; }

    internal JsonPathBinding? ItemsPath { get; init; }

    /// <summary>Ob überhaupt ein Feld gemappt wird.</summary>
    public bool IsEmpty => PathFields.Count == 0 && ExpressionFields.Count == 0;

    /// <summary>Die Namen aller gemappten Felder — für die Feldliste in der Verwaltung.</summary>
    public IReadOnlyList<string> FieldNames =>
        [.. PathFields.Select(static f => f.Name), .. ExpressionFields.Select(static f => f.Name)];
}

/// <summary>
/// Was aus einer Antwort geworden ist.
/// </summary>
/// <param name="Fields">Feldname zu Wert. Nie <c>null</c>, notfalls leer.</param>
/// <param name="IsEmpty">Ob die Regel <c>emptyWhen</c> zutraf.</param>
/// <param name="Diagnostics">
/// Was nicht ging — Ursachen ohne Werte (§21.2). Für die Vorschau in den
/// Einstellungen und für das Diagnosepaket.
/// </param>
public sealed record MappingResult(
    IReadOnlyDictionary<string, ContextValue> Fields,
    bool IsEmpty,
    IReadOnlyList<string> Diagnostics)
{
    public static MappingResult Empty { get; } =
        new(new Dictionary<string, ContextValue>(StringComparer.Ordinal), IsEmpty: true, []);
}

/// <summary>
/// Macht aus einer fremden Antwort einen Namensraum eigener Felder (§21.1).
///
/// <code>
/// { "contact": { "fullName": "Hans Muster" } }   →   crm.customerName = "Hans Muster"
/// </code>
///
/// <b>Zwei Durchgänge, und die Reihenfolge ist der Grund.</b> Zuerst werden
/// alle Pfad-Felder gelesen, danach die berechneten — sonst müsste ein
/// Ausdruck wissen, ob das Feld, auf das er sich bezieht, schon existiert.
/// Innerhalb der berechneten Felder gilt die Reihenfolge der Datei: ein
/// Ausdruck sieht, was vor ihm steht.
///
/// <b>Diese Klasse wirft nicht.</b> Sie läuft im Anrufpfad; die Begründung
/// steht an <see cref="ExpressionEvaluator"/>.
/// </summary>
public static class MappingEngine
{
    /// <summary>
    /// Höchstzahl Felder je Quelle. Eine Karte in einem 400 Pixel breiten
    /// Fenster zeigt ein Dutzend; alles darüber ist Last ohne Nutzen.
    /// </summary>
    public const int MaxFields = 64;

    /// <summary>
    /// Übersetzt ein Mapping. Sammelt <b>alle</b> Fehler, statt beim ersten
    /// abzubrechen: wer eine Konfiguration schreibt, will sie einmal
    /// korrigieren und nicht fünfmal.
    /// </summary>
    public static bool TryCompile(
        MappingDefinition? definition,
        out CompiledMapping mapping,
        out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();
        var pathFields = new List<CompiledMapping.Field>();
        var expressionFields = new List<CompiledMapping.Field>();
        ExpressionNode? emptyWhen = null;
        JsonPathBinding? itemsPath = null;

        if (definition is null)
        {
            mapping = new CompiledMapping();
            errors = problems;
            return true;
        }

        if (definition.Fields.Count > MaxFields)
        {
            problems.Add(
                $"Das Mapping hat {definition.Fields.Count} Felder, erlaubt sind {MaxFields}.");
        }

        foreach (var (name, field) in definition.Fields.Take(MaxFields))
        {
            if (!IsValidFieldName(name))
            {
                problems.Add(
                    $"'{name}' ist kein gültiger Feldname. Erlaubt sind Buchstaben, Ziffern und "
                        + "Unterstrich, beginnend mit einem Buchstaben.");
                continue;
            }

            if (!field.IsWellFormed)
            {
                problems.Add(
                    $"Feld '{name}': genau eines von 'path' und 'expr' muss angegeben sein.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(field.Path))
            {
                if (JsonPathBinding.TryCompile(field.Path, out var binding, out var pathError))
                {
                    pathFields.Add(new CompiledMapping.Field(name, binding, null, field.As));
                }
                else
                {
                    problems.Add($"Feld '{name}': {pathError}");
                }

                continue;
            }

            if (ExpressionParser.TryParse(field.Expr, out var node, out var expressionError))
            {
                expressionFields.Add(new CompiledMapping.Field(name, null, node, field.As));
            }
            else
            {
                problems.Add($"Feld '{name}': {expressionError}");
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.EmptyWhen)
            && !ExpressionParser.TryParse(definition.EmptyWhen, out emptyWhen, out var emptyError))
        {
            problems.Add($"'emptyWhen': {emptyError}");
        }

        if (!string.IsNullOrWhiteSpace(definition.ItemsPath)
            && !JsonPathBinding.TryCompile(definition.ItemsPath, out itemsPath, out var itemsError))
        {
            problems.Add($"'itemsPath': {itemsError}");
        }

        mapping = new CompiledMapping
        {
            PathFields = pathFields,
            ExpressionFields = expressionFields,
            EmptyWhen = emptyWhen,
            ItemsPath = itemsPath,
        };

        errors = problems;
        return problems.Count == 0;
    }

    /// <summary>
    /// Wendet ein Mapping auf eine Antwort an. Wirft nicht.
    /// </summary>
    /// <param name="mapping">Das übersetzte Mapping.</param>
    /// <param name="body">Die geparste Antwort, oder <c>null</c>.</param>
    /// <param name="ambient">
    /// Werte, die der Ausdruck ausserhalb der Antwort sieht — die Rufnummer
    /// unter <c>number.…</c>, der HTTP-Status unter <c>status</c>. Ohne sie
    /// liessen sich weder <c>emptyWhen</c> auf den Status noch Vorlagen auf
    /// die Nummer schreiben.
    /// </param>
    public static MappingResult Map(
        CompiledMapping mapping,
        JsonNode? body,
        IReadOnlyDictionary<string, ContextValue>? ambient = null)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var diagnostics = new List<string>();
        var fields = new Dictionary<string, ContextValue>(StringComparer.Ordinal);
        var scope = new MappingScope(fields, body, ambient, diagnostics);

        foreach (var field in mapping.PathFields)
        {
            var value = field.Path!.Evaluate(body, field.Kind, diagnostics);

            // Ein Feld, das auf ein Objekt zeigt, ist ein Konfigurationsfehler:
            // die Karte könnte es nur als JSON-Auszug zeigen, und das soll das
            // Mapping gerade verhindern (§21.1). In einer Regel wie emptyWhen
            // ist dasselbe Objekt dagegen sinnvoll — deshalb steht die
            // Bewertung hier und nicht in JsonPathBinding.
            if (value is StructureValue)
            {
                diagnostics.Add(
                    $"Feld '{field.Name}': der Pfad '{field.Path.Source}' zeigt auf ein Objekt, "
                        + "nicht auf einen Wert. Den Pfad um das gewünschte Feld verlängern.");

                value = ContextValue.Null;
            }

            fields[field.Name] = value;
        }

        foreach (var field in mapping.ExpressionFields)
        {
            var value = ExpressionEvaluator.Evaluate(field.Expression!, scope, diagnostics);

            // Auch ein berechnetes Feld darf eine Art fordern:
            // concat(...) mit "as: number" ergibt eine Zahl, wenn der Text
            // eine ist. Das ist derselbe ausdrückliche Weg wie bei einem Pfad.
            fields[field.Name] = Coerce(value, field.Kind);
        }

        var isEmpty = mapping.EmptyWhen is { } rule
            && ExpressionEvaluator.IsTrue(ExpressionEvaluator.Evaluate(rule, scope, diagnostics));

        return new MappingResult(fields, isEmpty, diagnostics);
    }

    /// <summary>
    /// Zerlegt eine Antwort in ihre Treffer — für die Kontaktsuche.
    ///
    /// Ohne <c>itemsPath</c> ist die ganze Antwort ein einziger Treffer; das
    /// ist der Normalfall beim Anruferkontext.
    /// </summary>
    public static IReadOnlyList<MappingResult> MapItems(
        CompiledMapping mapping,
        JsonNode? body,
        int limit,
        IReadOnlyDictionary<string, ContextValue>? ambient = null)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (mapping.ItemsPath is null)
        {
            return [Map(mapping, body, ambient)];
        }

        if (body is null)
        {
            return [];
        }

        var results = new List<MappingResult>();

        // Rohe Knoten, nicht Werte: ein Suchtreffer ist ein Objekt, und ein
        // Objekt ist kein Wert — es muss noch einmal durch dasselbe Mapping.
        foreach (var item in mapping.ItemsPath.EvaluateNodes(body, Math.Max(0, limit)))
        {
            results.Add(Map(mapping, item, ambient));
        }

        return results;
    }

    /// <summary>
    /// Bringt einen berechneten Wert in die geforderte Art. <c>Auto</c> lässt
    /// ihn, wie er ist.
    /// </summary>
    private static ContextValue Coerce(ContextValue value, ValueKind kind) => kind switch
    {
        ValueKind.Text => ContextValue.FromText(value.AsText()),
        ValueKind.Number => value.TryAsNumber(out var number)
            ? ContextValue.FromNumber(number)
            : ParseNumber(value.AsText()),
        ValueKind.Boolean => value.TryAsBoolean(out var flag)
            ? ContextValue.FromBoolean(flag)
            : ParseBoolean(value.AsText()),
        _ => value,
    };

    private static ContextValue ParseNumber(string text) =>
        decimal.TryParse(
            text,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? ContextValue.FromNumber(number)
            : ContextValue.Null;

    private static ContextValue ParseBoolean(string text) =>
        bool.TryParse(text, out var flag) ? ContextValue.FromBoolean(flag) : ContextValue.Null;

    /// <summary>
    /// Ein Feldname muss als Bezeichner in einem Ausdruck schreibbar sein —
    /// sonst liesse sich das Feld auf einer Karte nicht ansprechen.
    /// </summary>
    private static bool IsValidFieldName(string name) =>
        name.Length > 0
        && char.IsLetter(name[0])
        && name.All(static c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Was ein Ausdruck während des Mappings sieht: die bereits gemappten
    /// Felder unter ihrem Namen, die Umgebung unter ihrem Pfad, und die rohe
    /// Antwort unter <c>$.…</c>.
    /// </summary>
    private sealed class MappingScope(
        IReadOnlyDictionary<string, ContextValue> fields,
        JsonNode? body,
        IReadOnlyDictionary<string, ContextValue>? ambient,
        IList<string> diagnostics) : IExpressionScope
    {
        public ContextValue Resolve(string path)
        {
            if (fields.TryGetValue(path, out var field))
            {
                return field;
            }

            return ambient is not null && ambient.TryGetValue(path, out var value)
                ? value
                : ContextValue.Null;
        }

        public ContextValue ResolveJsonPath(string path) =>
            JsonPathBinding.TryCompile(path, out var binding, out var error)
                ? binding!.Evaluate(body, ValueKind.Auto, diagnostics)
                : Fail(error);

        private ContextValue Fail(string? error)
        {
            if (error is { Length: > 0 })
            {
                diagnostics.Add(error);
            }

            return ContextValue.Null;
        }
    }
}
