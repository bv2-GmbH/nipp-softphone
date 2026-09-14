using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Eine übersetzte Karte. Entsteht einmal beim Laden der Konfiguration.
/// </summary>
public sealed class CompiledCard
{
    internal sealed record Element(
        string Key,
        CardElement Source,
        ExpressionNode? Visible,
        IReadOnlyDictionary<string, ExpressionNode> Values,
        IReadOnlyDictionary<string, TemplateRenderer> Templates);

    internal sealed record Column(int Span, IReadOnlyList<Element> Elements);

    internal sealed record Row(IReadOnlyList<Column> Columns);

    internal sealed record Section(
        string Id,
        string? Title,
        ExpressionNode? Visible,
        IReadOnlyList<Row> Rows);

    internal IReadOnlyList<Section> Sections { get; init; } = [];

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public CardKind Kind { get; init; }
}

/// <summary>
/// Macht aus einer Kartenbeschreibung und einem Kontext eine fertige Karte
/// (§21).
///
/// <b>Zwei Schritte, und die Trennung ist der Punkt.</b> Übersetzt wird
/// einmal beim Laden der Konfiguration: dort werden Ausdrücke geparst und
/// Fehler mit Stelle gemeldet. Aufgelöst wird bei jeder Antwort einer Quelle
/// — und das läuft auf dem Thread, der alle 20 ms <c>Core.Iterate()</c>
/// bedient (§6). Ein Parser in diesem Pfad wäre die falsche Arbeit am
/// falschen Ort.
///
/// <b>Diese Klasse wirft beim Auflösen nicht.</b> Was nicht geht, wird zu
/// einem leeren Wert oder einem unsichtbaren Baustein.
/// </summary>
public static class CardLayoutEngine
{
    /// <summary>
    /// Übersetzt eine Kartenbeschreibung und sammelt alle Fehler.
    /// </summary>
    public static bool TryCompile(
        CardDefinition? definition,
        out CompiledCard card,
        out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();

        if (definition is null)
        {
            card = new CompiledCard();
            errors = problems;
            return true;
        }

        var sections = new List<CompiledCard.Section>();

        foreach (var (section, sectionIndex) in definition.Sections.Take(CardLayout.MaxSections).Select(static (s, i) => (s, i)))
        {
            var rows = new List<CompiledCard.Row>();

            foreach (var (row, rowIndex) in section.Rows.Take(CardLayout.MaxRowsPerSection).Select(static (r, i) => (r, i)))
            {
                var spans = row.Columns.Sum(static c => Math.Max(1, c.Span));

                if (spans > CardLayout.Columns)
                {
                    problems.Add(
                        $"Abschnitt '{section.Id}', Zeile {rowIndex + 1}: die Spalten belegen "
                            + $"{spans} von {CardLayout.Columns} Einheiten. Das passt nicht "
                            + "nebeneinander.");
                }

                var columns = new List<CompiledCard.Column>();

                foreach (var (column, columnIndex) in row.Columns.Select(static (c, i) => (c, i)))
                {
                    var elements = new List<CompiledCard.Element>();

                    foreach (var (element, elementIndex) in
                        column.Elements.Take(CardLayout.MaxElementsPerColumn).Select(static (e, i) => (e, i)))
                    {
                        var key = $"{sectionIndex}.{rowIndex}.{columnIndex}.{elementIndex}";
                        var where = $"Abschnitt '{section.Id}', Zeile {rowIndex + 1}";

                        if (TryCompileElement(element, key, where, problems) is { } compiled)
                        {
                            elements.Add(compiled);
                        }
                    }

                    columns.Add(new CompiledCard.Column(Math.Max(1, column.Span), elements));
                }

                rows.Add(new CompiledCard.Row(columns));
            }

            sections.Add(new CompiledCard.Section(
                section.Id,
                section.Title,
                Compile(section.VisibleWhen, $"Abschnitt '{section.Id}'", "visibleWhen", problems),
                rows));
        }

        card = new CompiledCard
        {
            Id = definition.Id,
            Name = definition.Name,
            Kind = definition.Kind,
            Sections = sections,
        };

        errors = problems;
        return problems.Count == 0;
    }

    /// <summary>
    /// Löst eine übersetzte Karte gegen einen Kontext auf. Wirft nicht.
    /// </summary>
    public static CardModel Build(CompiledCard card, ContextSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (snapshot is null)
        {
            return CardModel.Empty;
        }

        var scope = new SnapshotScope(snapshot);
        var sections = new List<CardSectionModel>();

        foreach (var section in card.Sections)
        {
            var rows = section.Rows
                .Select(row => new CardRowModel(
                    [.. row.Columns.Select(column => new CardColumnModel(
                        column.Span,
                        [.. column.Elements.Select(element => Resolve(element, scope, snapshot))]))]))
                .ToList();

            var visible = IsVisible(section.Visible, scope)
                && rows.Any(static r => r.Columns.Any(static c => c.Elements.Any(static e => e.Visible)));

            sections.Add(new CardSectionModel(section.Id, section.Title, visible, rows));
        }

        return new CardModel(card.Id, sections);
    }

    private static CompiledCard.Element? TryCompileElement(
        CardElement element,
        string key,
        string where,
        List<string> problems)
    {
        var values = new Dictionary<string, ExpressionNode>(StringComparer.Ordinal);
        var templates = new Dictionary<string, TemplateRenderer>(StringComparer.Ordinal);

        var visible = Compile(element.VisibleWhen, where, "visibleWhen", problems);

        switch (element)
        {
            case CardText text:
                Add(values, "value", text.Value, where, problems);
                break;

            case CardField field:
                Add(values, "value", field.Value, where, problems);
                break;

            case CardBadge badge:
                Add(values, "text", badge.Text, where, problems);
                Add(values, "tone", badge.Tone, where, problems);
                break;

            case CardButton button:
                if (button.EnabledWhen is { Length: > 0 })
                {
                    var enabled = Compile(button.EnabledWhen, where, "enabledWhen", problems);

                    if (enabled is not null)
                    {
                        values["enabled"] = enabled;
                    }
                }

                AddTemplate(templates, "action", ActionTemplateOf(button.Action), where, problems);
                break;

            case CardLink link:
                AddTemplate(templates, "url", link.Url, where, problems);
                break;

            case CardDivider:
            case CardSpacer:
            case CardSourceStatus:
                break;

            default:
                // Ein Typ aus einer neueren Kartenversion. Er wird übergangen,
                // nicht geraten — und gemeldet, damit klar ist, warum die
                // Karte anders aussieht als gedacht.
                problems.Add($"{where}: unbekannter Bausteintyp '{element.GetType().Name}'.");
                return null;
        }

        return new CompiledCard.Element(key, element, visible, values, templates);
    }

    private static string ActionTemplateOf(CardAction action) => action switch
    {
        OpenUrlAction open => open.Url,
        DialAction dial => dial.Number,
        CopyAction copy => copy.Value,
        _ => string.Empty,
    };

    private static void Add(
        Dictionary<string, ExpressionNode> values,
        string name,
        string? expression,
        string where,
        List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        if (ExpressionParser.TryParse(expression, out var node, out var error))
        {
            values[name] = node!;
        }
        else
        {
            problems.Add($"{where}, '{name}': {error}");
        }
    }

    private static void AddTemplate(
        Dictionary<string, TemplateRenderer> templates,
        string name,
        string? template,
        string where,
        List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        try
        {
            templates[name] = TemplateRenderer.Compile(template);
        }
        catch (ExpressionParseException ex)
        {
            problems.Add($"{where}, '{name}': {ex.Message}");
        }
    }

    private static ExpressionNode? Compile(
        string? expression,
        string where,
        string name,
        List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        if (ExpressionParser.TryParse(expression, out var node, out var error))
        {
            return node;
        }

        problems.Add($"{where}, '{name}': {error}");
        return null;
    }

    /// <summary>
    /// Ohne Bedingung sichtbar. <b>Mit einer Bedingung, die sich nicht
    /// entscheiden lässt, nicht</b> — die Begründung steht an
    /// <c>ExpressionEvaluator</c>: eine Karte soll bei fehlenden Daten nichts
    /// behaupten.
    /// </summary>
    private static bool IsVisible(ExpressionNode? condition, IExpressionScope scope) =>
        condition is null || ExpressionEvaluator.IsTrue(ExpressionEvaluator.Evaluate(condition, scope));

    private static CardElementModel Resolve(
        CompiledCard.Element element,
        IExpressionScope scope,
        ContextSnapshot snapshot)
    {
        var visible = IsVisible(element.Visible, scope);

        switch (element.Source)
        {
            case CardText text:
                {
                    var value = Text(element, "value", scope);

                    // Ein leerer Text ist eine leere Zeile — die wird nicht
                    // gezeigt, sonst klaffen Lücken in der Karte.
                    return new CardTextModel(
                        element.Key,
                        visible && value.Length > 0,
                        value,
                        text.Style,
                        Math.Max(1, text.MaxLines));
                }

            case CardField field:
                {
                    var value = Text(element, "value", scope);

                    if (value.Length == 0)
                    {
                        // Ohne Wert entscheidet der Administrator: Platzhalter
                        // oder gar keine Zeile.
                        return new CardFieldModel(
                            element.Key,
                            visible && field.EmptyText is { Length: > 0 },
                            field.Label,
                            field.EmptyText ?? string.Empty,
                            field.ShowLabel);
                    }

                    return new CardFieldModel(
                        element.Key, visible, field.Label, value, field.ShowLabel);
                }

            case CardBadge:
                {
                    var text = Text(element, "text", scope);

                    return new CardBadgeModel(
                        element.Key,
                        visible && text.Length > 0,
                        text,
                        ToneOf(Text(element, "tone", scope)));
                }

            case CardDivider:
                return new CardDividerModel(element.Key, visible);

            case CardSpacer spacer:
                return new CardSpacerModel(element.Key, visible, spacer.Size);

            case CardButton button:
                {
                    var enabled = !element.Values.TryGetValue("enabled", out var condition)
                        || ExpressionEvaluator.IsTrue(ExpressionEvaluator.Evaluate(condition, scope));

                    var action = ResolveAction(button.Action, element, scope);

                    return new CardButtonModel(
                        element.Key,
                        visible,
                        button.Label,

                        // Eine Schaltfläche ohne brauchbare Aktion ist nicht
                        // bedienbar: sie verspräche sonst etwas, das nicht
                        // geschieht.
                        enabled && action is not null,
                        action);
                }

            case CardLink link:
                {
                    var target = SafeUri(Render(element, "url", scope));

                    return new CardLinkModel(element.Key, visible && target is not null, link.Label, target);
                }

            case CardSourceStatus status:
                {
                    var fragment = snapshot.Sources.GetValueOrDefault(status.Source);

                    if (fragment is null)
                    {
                        return new CardSourceStatusModel(
                            element.Key, false, status.Source, status.Source, SourceState.Skipped, string.Empty);
                    }

                    return new CardSourceStatusModel(
                        element.Key,

                        // Eine erfolgreiche Quelle sagt nichts: ihre Felder stehen
                        // auf der Karte, und „CRM: gefunden" wäre Rauschen.
                        visible && fragment.State != SourceState.Success,
                        status.Source,
                        fragment.DisplayName,
                        fragment.State,
                        DescribeState(fragment));
                }

            default:
                return new CardDividerModel(element.Key, Visible: false);
        }
    }

    /// <summary>
    /// Der Satz zu einem Quellenzustand. <b>Hier und nicht im Renderer</b>:
    /// §15 verlangt, dass eine Meldung Ursache und Abhilfe nennt, und beides
    /// weiss nur, wer den Zustand kennt.
    /// </summary>
    private static string DescribeState(ContextFragment fragment) => fragment.State switch
    {
        SourceState.Loading => $"{fragment.DisplayName} wird gefragt …",
        SourceState.Empty => $"{fragment.DisplayName}: nichts gefunden",
        SourceState.Timeout => fragment.Message ?? $"{fragment.DisplayName} antwortet nicht",
        SourceState.Error => fragment.Message ?? $"{fragment.DisplayName}: Fehler",
        SourceState.Skipped => fragment.Message ?? $"{fragment.DisplayName} übersprungen",
        _ => fragment.DisplayName,
    };

    private static CardResolvedAction? ResolveAction(
        CardAction action,
        CompiledCard.Element element,
        IExpressionScope scope)
    {
        var value = Render(element, "action", scope);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return action switch
        {
            OpenUrlAction => SafeUri(value) is { } uri ? new CardOpenUrl(uri) : null,
            DialAction => new CardDial(value),
            CopyAction => new CardCopy(value),
            _ => null,
        };
    }

    /// <summary>
    /// Eine Adresse, die geöffnet werden darf — <b>nur http und https</b>.
    ///
    /// Alles andere landete in <c>ShellExecute</c>, und das startet, was auch
    /// immer Windows hinter einem Schema vermutet. Die Prüfung steht hier und
    /// nicht im Renderer, damit sie nicht vergessen werden kann (§21.2).
    /// </summary>
    private static Uri? SafeUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ? uri : null;
    }

    private static CardTone ToneOf(string tone) => tone.ToUpperInvariant() switch
    {
        "INFO" => CardTone.Info,
        "SUCCESS" => CardTone.Success,
        "WARNING" => CardTone.Warning,
        "DANGER" => CardTone.Danger,
        _ => CardTone.Neutral,
    };

    /// <summary>
    /// Der Wert eines Kartenelements — <b>in der Form für Menschen</b>.
    ///
    /// <para>AsDisplayText und nicht AsText: ein Datum stand hier sonst als
    /// „2027-01-01T00:00:00.0000000+01:00" auf der Karte. In Vorlagen und
    /// Ausdrücken (siehe <see cref="Render"/>) bleibt die ISO-Form richtig,
    /// weil dort verglichen und zusammengesetzt wird.</para>
    /// </summary>
    private static string Text(CompiledCard.Element element, string name, IExpressionScope scope) =>
        element.Values.TryGetValue(name, out var node)
            ? ExpressionEvaluator.Evaluate(node, scope).AsDisplayText()
            : string.Empty;

    private static string Render(CompiledCard.Element element, string name, IExpressionScope scope) =>
        element.Templates.TryGetValue(name, out var template)
            ? template.Render(scope)
            : string.Empty;

    /// <summary>
    /// Was eine Karte sieht: den Kontext des Anrufs.
    ///
    /// Ein JSONPath ergibt hier nichts — die rohe Antwort einer Quelle gibt es
    /// auf dieser Ebene nicht mehr, und das ist die Zusage aus §21.1: die
    /// Oberfläche arbeitet nicht mit den ursprünglichen Strukturen.
    /// </summary>
    private sealed class SnapshotScope(ContextSnapshot snapshot) : IExpressionScope
    {
        public ContextValue Resolve(string path) => snapshot.Resolve(path);

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;

        /// <summary>
        /// <b>Der einzige Bereich, der das kann</b> — und deshalb der Grund,
        /// warum es überhaupt eine Vorgabe an der Schnittstelle gibt. Nur hier
        /// liegen mehrere Quellen nebeneinander; ein Mapping sieht genau eine.
        /// </summary>
        public ContextValue ResolveAcrossSources(string field) =>
            snapshot.FieldAcrossSources(field);
    }
}
