using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Expressions;

namespace Nipp.Core.Services.Integrations.Http;

/// <summary>
/// Eine übersetzte Anfragebeschreibung (§21.3).
///
/// Pfad, Parameter und Körper enthalten Vorlagen; sie werden beim Laden der
/// Konfiguration einmal übersetzt und zur Laufzeit nur noch eingesetzt. Ein
/// Fehler in einer Vorlage ist damit ein Konfigurationsfehler mit Stelle, kein
/// Überraschungsfehler beim Anruf.
///
/// <b>Was hier über Sicherheit entschieden wird:</b> jeder eingesetzte Wert in
/// Pfad und Parametern wird URL-kodiert. Ohne das würde aus einer Rufnummer
/// <c>+41791234567</c> im Parameter die Zahl <c>41791234567</c> — das
/// <c>+</c> steht dort für ein Leerzeichen —, und ein Wert mit <c>&amp;</c>
/// hängte einen zusätzlichen Parameter an die Anfrage.
/// </summary>
public sealed class HttpRequestTemplate
{
    private readonly TemplateRenderer _path;
    private readonly IReadOnlyList<(string Name, TemplateRenderer Value)> _query;
    private readonly JsonNode? _body;

    private HttpRequestTemplate(
        HttpMethod method,
        TemplateRenderer path,
        IReadOnlyList<(string, TemplateRenderer)> query,
        JsonNode? body)
    {
        Method = method;
        _path = path;
        _query = query;
        _body = body;
    }

    public HttpMethod Method { get; }

    /// <summary>
    /// Übersetzt eine Beschreibung. Wirft <see cref="ExpressionParseException"/>
    /// oder <see cref="ArgumentException"/> — der Aufrufer ist die Prüfung der
    /// Konfiguration.
    /// </summary>
    public static HttpRequestTemplate Compile(RequestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var method = definition.Method?.ToUpperInvariant() switch
        {
            "GET" or null or "" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,

            // Kein DELETE und kein HEAD: eine Integration liest, sie verändert
            // nichts (§21.5). Was nicht vorgesehen ist, wird auch nicht
            // versehentlich möglich.
            var other => throw new ArgumentException(
                $"'{other}' ist keine erlaubte HTTP-Methode. Erlaubt sind GET, POST, PUT und PATCH.",
                nameof(definition)),
        };

        var query = definition.Query
            .Select(pair => (pair.Key, TemplateRenderer.Compile(pair.Value)))
            .ToList();

        // Der Körper wird nur geprüft, nicht übersetzt: die Vorlagen darin
        // stehen in den Blattwerten und werden beim Bauen eingesetzt.
        if (definition.Body is not null)
        {
            ValidateBodyTemplates(definition.Body);
        }

        return new HttpRequestTemplate(
            method,
            TemplateRenderer.Compile(definition.Path),
            query,
            definition.Body);
    }

    /// <summary>
    /// Baut die fertige Anfrage.
    /// </summary>
    /// <param name="baseUrl">Basisadresse der Quelle.</param>
    /// <param name="scope">Woher die Werte kommen — Rufnummer, Suchtext, Status.</param>
    /// <param name="diagnostics">Ursachen ohne Werte (§21.2).</param>
    public HttpRequestMessage Build(
        Uri baseUrl,
        IExpressionScope scope,
        IList<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(scope);

        var path = _path.Render(scope, TemplateEscaping.UrlComponent, diagnostics);
        var builder = new StringBuilder(path);

        for (var i = 0; i < _query.Count; i++)
        {
            var value = _query[i].Value.Render(scope, TemplateEscaping.UrlComponent, diagnostics);

            builder.Append(i == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(_query[i].Name))
                .Append('=')
                .Append(value);
        }

        // Relativ zur Basis: ein Pfad, der mit / beginnt, würde den Pfadteil
        // der Basisadresse verwerfen — aus https://host/api/v2 plus /contacts
        // würde https://host/contacts. Deshalb wird der führende Schrägstrich
        // entfernt und die Basis mit einem beendet.
        var relative = builder.ToString().TrimStart('/');
        var uri = new Uri(EnsureTrailingSlash(baseUrl), relative);

        var request = new HttpRequestMessage(Method, uri);

        if (_body is not null)
        {
            var body = RenderBody(_body, scope, diagnostics);

            request.Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    /// <summary>
    /// Der Pfadteil der Anfrage <b>ohne Parameter</b> — für das Protokoll.
    ///
    /// Nur so viel darf protokolliert werden: in den Parametern steht die
    /// Rufnummer oder der Suchtext (§21.2).
    /// </summary>
    public string DescribeForLog() => _path.Source;

    /// <summary>
    /// Setzt die Vorlagen im Körper ein — rekursiv, denn ein Körper darf
    /// verschachtelt sein.
    ///
    /// <b>Ohne URL-Kodierung:</b> hier ist der Wert Inhalt, kein Teil einer
    /// Adresse. Die Einbettung in JSON übernimmt <c>JsonValue</c>, und die
    /// maskiert selbst — eine Zeichenfolge mit Anführungszeichen kann den
    /// Körper also nicht aufbrechen.
    /// </summary>
    private static JsonNode RenderBody(JsonNode node, IExpressionScope scope, IList<string>? diagnostics)
    {
        switch (node)
        {
            case JsonObject obj:
                {
                    var result = new JsonObject();

                    foreach (var (key, value) in obj)
                    {
                        result[key] = value is null ? null : RenderBody(value, scope, diagnostics);
                    }

                    return result;
                }

            case JsonArray array:
                {
                    var result = new JsonArray();

                    foreach (var item in array)
                    {
                        result.Add(item is null ? null : RenderBody(item, scope, diagnostics));
                    }

                    return result;
                }

            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(TemplateRenderer.Compile(text).Render(scope, TemplateEscaping.None, diagnostics))!;

            default:
                return node.DeepClone();
        }
    }

    /// <summary>
    /// Prüft beim Übersetzen, dass jede Vorlage im Körper lesbar ist — sonst
    /// fiele der Fehler erst beim Anruf auf.
    /// </summary>
    private static void ValidateBodyTemplates(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                {
                    if (value is not null)
                    {
                        ValidateBodyTemplates(value);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        ValidateBodyTemplates(item);
                    }
                }

                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                _ = TemplateRenderer.Compile(text);
                break;

            default:
                break;
        }
    }

    private static Uri EnsureTrailingSlash(Uri baseUrl) =>
        baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
}
