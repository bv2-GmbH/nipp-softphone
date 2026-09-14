using System.Text;

namespace Nipp.Core.Services.Integrations.Expressions;

/// <summary>
/// Wie ein eingesetzter Wert behandelt wird.
/// </summary>
public enum TemplateEscaping
{
    /// <summary>Unverändert — für Beschriftungen und Anfragekörper.</summary>
    None,

    /// <summary>
    /// Für einen Teil einer URL oder einen Anfrageparameter. <b>Pflicht</b>,
    /// sobald der Wert in eine Adresse eingesetzt wird: eine Rufnummer mit
    /// einem <c>+</c> wird sonst als Leerzeichen gelesen, und ein Wert mit
    /// <c>&amp;</c> hängt einen zusätzlichen Parameter an die Anfrage.
    /// </summary>
    UrlComponent,
}

/// <summary>
/// Setzt Werte in eine Vorlage ein: <c>/kunden/{{number.e164}}</c> (§21.3).
///
/// <b>Dieselbe Sprache wie in berechneten Feldern</b> — zwischen den doppelten
/// Klammern steht ein vollständiger Ausdruck, nicht bloss ein Feldname. Damit
/// gibt es genau eine Grammatik zu lernen und genau eine zu prüfen.
///
/// Die Vorlage wird beim Laden der Konfiguration <b>einmal</b> übersetzt
/// (<see cref="Compile"/>); zur Laufzeit wird nur noch eingesetzt. Ein Fehler
/// in der Vorlage ist damit ein Konfigurationsfehler mit Stelle, kein
/// Überraschungsfehler beim Anruf.
/// </summary>
public sealed class TemplateRenderer
{
    /// <summary>Ein fester Textteil oder ein Ausdruck. Genau eines von beiden.</summary>
    private sealed record Segment(string? Literal, ExpressionNode? Expression);

    private readonly IReadOnlyList<Segment> _segments;

    private TemplateRenderer(IReadOnlyList<Segment> segments) => _segments = segments;

    /// <summary>Die Vorlage im Rohtext — für Meldungen und die Prüfung.</summary>
    public string Source { get; private init; } = string.Empty;

    /// <summary>Ob die Vorlage überhaupt einen Ausdruck enthält.</summary>
    public bool HasExpressions => _segments.Any(static s => s.Expression is not null);

    /// <summary>
    /// Übersetzt eine Vorlage. Wirft <see cref="ExpressionParseException"/>
    /// mit Stelle, wenn ein Ausdruck darin nicht lesbar ist oder eine Klammer
    /// fehlt.
    /// </summary>
    public static TemplateRenderer Compile(string? template)
    {
        var input = template ?? string.Empty;
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var i = 0;

        while (i < input.Length)
        {
            if (input[i] == '{' && i + 1 < input.Length && input[i + 1] == '{')
            {
                var end = input.IndexOf("}}", i + 2, StringComparison.Ordinal);

                if (end < 0)
                {
                    throw new ExpressionParseException(
                        "Die Vorlage öffnet {{ und schliesst nie mit }}.",
                        i + 1);
                }

                if (literal.Length > 0)
                {
                    segments.Add(new Segment(literal.ToString(), null));
                    literal.Clear();
                }

                var expression = input[(i + 2)..end];

                if (string.IsNullOrWhiteSpace(expression))
                {
                    throw new ExpressionParseException(
                        "Zwischen {{ und }} steht kein Ausdruck.",
                        i + 1);
                }

                segments.Add(new Segment(null, ExpressionParser.Parse(expression)));
                i = end + 2;
                continue;
            }

            literal.Append(input[i]);
            i++;
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(literal.ToString(), null));
        }

        return new TemplateRenderer(segments) { Source = input };
    }

    /// <summary>
    /// Setzt ein. Wirft nicht — ein Ausdruck, der nichts ergibt, hinterlässt
    /// eine leere Stelle.
    /// </summary>
    /// <param name="scope">Woher die Werte kommen.</param>
    /// <param name="escaping">
    /// Wie der eingesetzte Wert behandelt wird. Für alles, was in eine URL
    /// geht, <see cref="TemplateEscaping.UrlComponent"/> — die Begründung
    /// steht dort.
    /// </param>
    /// <param name="diagnostics">Sammelt Ursachen, ohne Werte zu nennen.</param>
    public string Render(
        IExpressionScope scope,
        TemplateEscaping escaping = TemplateEscaping.None,
        IList<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var builder = new StringBuilder();

        foreach (var segment in _segments)
        {
            if (segment.Literal is { } text)
            {
                builder.Append(text);
                continue;
            }

            var value = ExpressionEvaluator.Evaluate(segment.Expression!, scope, diagnostics);
            var rendered = value.AsText();

            builder.Append(escaping == TemplateEscaping.UrlComponent
                ? Uri.EscapeDataString(rendered)
                : rendered);
        }

        return builder.ToString();
    }
}
