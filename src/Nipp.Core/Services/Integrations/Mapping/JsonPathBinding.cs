using System.Globalization;
using System.Text.Json.Nodes;
using Json.Path;
using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.Services.Integrations.Mapping;

/// <summary>
/// In welche Art ein gelesener Wert gebracht wird.
/// </summary>
public enum ValueKind
{
    /// <summary>Nach dem, was im JSON steht — Zeichenfolge bleibt Text, Zahl bleibt Zahl.</summary>
    Auto,

    Text,

    /// <summary>
    /// Zahl. <b>Hier ist die Umwandlung aus Text ausdrücklich erlaubt</b> — es
    /// gibt APIs, die <c>"openOrders": "3"</c> liefern. Der Unterschied zu
    /// <see cref="Auto"/> ist die Absicht: sie steht dann in der
    /// Konfiguration und nicht in einer Vermutung des Programms
    /// (<see cref="ContextValue.TryAsNumber"/>).
    /// </summary>
    Number,

    Boolean,

    /// <summary>Zeitpunkt. Angenommen wird nur ISO 8601.</summary>
    Date,

    /// <summary>Immer eine Liste, auch bei einem einzigen Treffer.</summary>
    List,
}

/// <summary>
/// Ein übersetzter JSONPath (ADR-016).
///
/// <b>Die einzige Datei mit <c>using Json.Path</c>.</b>
/// <c>IntegrationBoundaryTests</c> erzwingt das: die Bibliothek soll
/// austauschbar bleiben, und nach aussen verlässt diese Schicht nur
/// <see cref="ContextValue"/> — kein <c>JsonNode</c>, kein <c>Node</c>, keine
/// <c>NodeList</c>.
///
/// Der Pfad wird beim Laden der Konfiguration einmal übersetzt; zur Laufzeit
/// wird nur noch ausgewertet.
/// </summary>
public sealed class JsonPathBinding
{
    /// <summary>
    /// Wie viele Treffer aus einem Pfad höchstens übernommen werden.
    ///
    /// Ein Pfad wie <c>$..*</c> auf eine grosse Antwort liefert tausende
    /// Knoten. Eine Karte in einem 400 Pixel breiten Fenster zeigt davon
    /// nichts, und die Arbeit fiele auf dem Thread an, der alle 20 ms
    /// <c>Core.Iterate()</c> bedient (§6). Was darüber liegt, wird
    /// abgeschnitten und gemeldet — nicht gerechnet.
    /// </summary>
    public const int MaxMatches = 100;

    /// <summary>
    /// Absichtlich strikt nach RFC 9535: keine Rechenoperationen, keine
    /// JSON-Literale im Pfad, kein <c>in</c>-Operator, kein relativer
    /// Pfadbeginn.
    ///
    /// Diese Erweiterungen sind nützlich, wenn man einer Bibliothek vertraut,
    /// und ein Angriffsweg, wenn der Pfad aus einer Datei kommt, die über das
    /// Netz verteilt wird (§11). Der Standard reicht für jedes Mapping, das
    /// hier vorkommt.
    /// </summary>
    private static readonly PathParsingOptions StrictOptions = new()
    {
        AllowMathOperations = false,
        AllowJsonConstructs = false,
        AllowInOperator = false,
        AllowRelativePathStart = false,
        TolerateExtraWhitespace = true,
    };

    private readonly JsonPath _path;

    private JsonPathBinding(JsonPath path, string source)
    {
        _path = path;
        Source = source;
    }

    /// <summary>Der Pfad, wie er in der Konfiguration steht.</summary>
    public string Source { get; }

    /// <summary>
    /// Übersetzt einen Pfad. Wirft <see cref="ArgumentException"/> mit dem
    /// Grund — der Aufrufer ist die Prüfung der Konfiguration, nicht der
    /// Anrufpfad.
    /// </summary>
    public static JsonPathBinding Compile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Der Pfad ist leer.", nameof(path));
        }

        if (!JsonPath.TryParse(path, out var parsed, StrictOptions))
        {
            throw new ArgumentException(
                $"'{path}' ist kein gültiger JSONPath. Erwartet wird die Form $.feld.unterfeld "
                    + "oder $.liste[*].feld (RFC 9535).",
                nameof(path));
        }

        return new JsonPathBinding(parsed, path);
    }

    /// <summary>Übersetzt und meldet den Fehler als Text — für die Prüfung ganzer Dateien.</summary>
    public static bool TryCompile(string? path, out JsonPathBinding? binding, out string? error)
    {
        try
        {
            binding = Compile(path);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            binding = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Liest den Wert aus einer Antwort. Wirft nicht.
    /// </summary>
    /// <param name="root">Die geparste Antwort.</param>
    /// <param name="kind">In welche Art der Wert gebracht wird.</param>
    /// <param name="diagnostics">Ursachen ohne Werte (§21.2).</param>
    public ContextValue Evaluate(
        JsonNode? root,
        ValueKind kind = ValueKind.Auto,
        IList<string>? diagnostics = null)
    {
        if (root is null)
        {
            return ContextValue.Null;
        }

        try
        {
            var matches = _path.Evaluate(root).Matches;

            if (matches.Count == 0)
            {
                return ContextValue.Null;
            }

            if (kind == ValueKind.List)
            {
                if (matches.Count > MaxMatches)
                {
                    diagnostics?.Add(
                        $"Der Pfad '{Source}' trifft {matches.Count} Werte; "
                            + $"übernommen werden die ersten {MaxMatches}.");
                }

                var items = new List<ContextValue>();

                for (var i = 0; i < Math.Min(matches.Count, MaxMatches); i++)
                {
                    var item = Convert(matches[i].Value, ValueKind.Auto, diagnostics);

                    // Ein nicht darstellbarer Eintrag fällt heraus, statt die
                    // ganze Liste zu verwerfen: fremde Listen sind gemischt.
                    if (item is not NullValue)
                    {
                        items.Add(item);
                    }
                }

                return ContextValue.FromList(items);
            }

            // Skalar: der erste Treffer. Ein Pfad mit mehreren Treffern auf ein
            // Einzelfeld ist ein Konfigurationsfehler, aber kein Grund, nichts
            // zu zeigen — gemeldet wird er trotzdem.
            if (matches.Count > 1)
            {
                diagnostics?.Add(
                    $"Der Pfad '{Source}' trifft {matches.Count} Werte, erwartet wird einer. "
                        + "Verwendet wird der erste; für alle Treffer 'as: list' setzen.");
            }

            return Convert(matches[0].Value, kind, diagnostics);
        }
        catch (Exception ex)
        {
            // Der Pfad ist geprüft, die Antwort kommt von einem fremden Server.
            // Ein Mapping darf kein Anrufereignis kosten (§21.2).
            diagnostics?.Add($"Der Pfad '{Source}' liess sich nicht auswerten: {ex.GetType().Name}");
            return ContextValue.Null;
        }
    }

    /// <summary>
    /// Die Treffer als rohe Knoten — <b>nur für die Zerlegung einer
    /// Trefferliste</b> (<c>itemsPath</c> bei der Kontaktsuche).
    ///
    /// <b>Warum das eine Ausnahme ist.</b> Sonst verlässt diese Klasse nur
    /// <see cref="ContextValue"/>; das ist die Grenze aus ADR-016. Ein
    /// einzelner Suchtreffer ist aber ein <b>Objekt</b>, und ein Objekt ist
    /// kein Wert — es muss noch einmal durch dasselbe Mapping laufen. Der
    /// erste Entwurf ging über Text und erneutes Lesen; er lieferte nichts,
    /// weil die Objekte gar nicht erst durch die Wertumwandlung kamen.
    ///
    /// <c>internal</c>, und <c>JsonNode</c> gehört zu <c>System.Text.Json</c>
    /// — nicht zur JSONPath-Bibliothek. Die bleibt gekapselt.
    /// </summary>
    internal IReadOnlyList<JsonNode?> EvaluateNodes(JsonNode? root, int limit)
    {
        if (root is null)
        {
            return [];
        }

        try
        {
            var matches = _path.Evaluate(root).Matches;
            var nodes = new List<JsonNode?>();

            for (var i = 0; i < Math.Min(matches.Count, limit); i++)
            {
                nodes.Add(matches[i].Value);
            }

            return nodes;
        }
        catch (Exception)
        {
            // Wie in Evaluate: die Antwort kommt von einem fremden Server, und
            // eine Suche darf nicht mehr kosten als ein leeres Ergebnis.
            return [];
        }
    }

    /// <summary>
    /// Macht aus einem JSON-Knoten einen Kontextwert.
    ///
    /// <b>Ein Objekt wird nichts.</b> Es als JSON-Text auf eine Karte zu
    /// schreiben wäre kein Feld, sondern ein Auszug aus der Antwort — und
    /// genau das soll das Mapping verhindern (§21.1: die Oberfläche arbeitet
    /// nicht mit den ursprünglichen Strukturen). Wer ein Unterfeld braucht,
    /// verlängert den Pfad.
    /// </summary>
    private ContextValue Convert(JsonNode? node, ValueKind kind, IList<string>? diagnostics)
    {
        if (node is null)
        {
            return ContextValue.Null;
        }

        if (node is JsonArray array)
        {
            var items = array
                .Take(MaxMatches)
                .Select(item => Convert(item, ValueKind.Auto, diagnostics))
                .Where(static item => item is not NullValue)
                .ToList();

            return ContextValue.FromList(items);
        }

        // Ein Objekt ist kein Wert, aber es ist da — und dieser Unterschied
        // muss erhalten bleiben, sonst wäre isEmpty($.contact) immer wahr.
        // Die Begründung steht an StructureValue. Ob ein Objekt an dieser
        // Stelle ein Konfigurationsfehler ist, entscheidet die MappingEngine:
        // bei einem Feld ja, in einer Regel wie emptyWhen nein.
        if (node is JsonObject)
        {
            return new StructureValue();
        }

        if (node is not JsonValue value)
        {
            return ContextValue.Null;
        }

        return kind switch
        {
            ValueKind.Text => ContextValue.FromText(AsRawText(value)),
            ValueKind.Number => AsNumber(value, diagnostics),
            ValueKind.Boolean => AsBoolean(value, diagnostics),
            ValueKind.Date => AsDate(value, diagnostics),
            _ => Auto(value),
        };
    }

    /// <summary>
    /// Ohne Angabe gilt, was im JSON steht. Eine Zeichenfolge bleibt Text,
    /// auch wenn Ziffern darin stehen — die Rufnummer <c>+41791234567</c> ist
    /// keine Zahl.
    /// </summary>
    private static ContextValue Auto(JsonValue value)
    {
        if (value.TryGetValue<bool>(out var flag))
        {
            return ContextValue.FromBoolean(flag);
        }

        if (value.TryGetValue<string>(out var text))
        {
            return ContextValue.FromText(text);
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return ContextValue.FromNumber(number);
        }

        // Zahlen ausserhalb von decimal — etwa in wissenschaftlicher
        // Schreibweise. Sie bleiben Text: falsch gerundet wäre schlimmer als
        // unformatiert.
        return ContextValue.FromText(value.ToJsonString().Trim('"'));
    }

    private static string AsRawText(JsonValue value) =>
        value.TryGetValue<string>(out var text) ? text : value.ToJsonString().Trim('"');

    private ContextValue AsNumber(JsonValue value, IList<string>? diagnostics)
    {
        if (value.TryGetValue<decimal>(out var number))
        {
            return ContextValue.FromNumber(number);
        }

        // Der ausdrücklich gewollte Fall: "3" als Zahl. Invariant gelesen,
        // weil die Antwort von einem Server kommt und nicht von einem Gerät
        // mit Ländereinstellung.
        if (value.TryGetValue<string>(out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return ContextValue.FromNumber(parsed);
        }

        diagnostics?.Add($"Der Wert unter '{Source}' ist keine Zahl.");
        return ContextValue.Null;
    }

    private ContextValue AsBoolean(JsonValue value, IList<string>? diagnostics)
    {
        if (value.TryGetValue<bool>(out var flag))
        {
            return ContextValue.FromBoolean(flag);
        }

        if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
        {
            return ContextValue.FromBoolean(parsed);
        }

        diagnostics?.Add($"Der Wert unter '{Source}' ist kein Wahrheitswert.");
        return ContextValue.Null;
    }

    private ContextValue AsDate(JsonValue value, IList<string>? diagnostics)
    {
        if (value.TryGetValue<DateTimeOffset>(out var typed))
        {
            return ContextValue.FromDate(typed);
        }

        if (value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParseExact(
                text,
                IsoFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return ContextValue.FromDate(parsed);
        }

        diagnostics?.Add(
            $"Der Wert unter '{Source}' ist kein Datum in ISO-8601-Form (etwa 2026-09-06).");

        return ContextValue.Null;
    }

    /// <summary>
    /// Nur ISO 8601, ausdrücklich aufgezählt — aus demselben Grund wie in
    /// <c>ExpressionFunctions</c>: <c>06.09.2026</c> heisst in einem anderen
    /// Land der 9. Juni, und ein um drei Monate falsches Datum fällt auf einer
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
}
