using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Mapping;

namespace Nipp.Core.Services.Integrations.Search;

/// <summary>
/// Sucht Kontakte in einer HTTP-Quelle (§21.1).
///
/// <b>Die Übersetzung fremder Felder in einen Kontakt folgt einer
/// Namenskonvention</b>, nicht einem zweiten Schema: das Mapping liefert
/// Felder, und die heissen <c>displayName</c>, <c>company</c>, <c>email</c>,
/// <c>externalId</c> — und für die Nummern alles, was mit <c>phone</c>
/// beginnt: <c>phoneBusiness</c>, <c>phoneMobile</c>, <c>phoneHome</c>.
///
/// Der Grund für die Konvention ist Sparsamkeit an der richtigen Stelle. Ein
/// eigenes Schema für Nummern — eine Liste aus Pfad und Art — wäre ein
/// zweiter Weg, ein Feld zu beschreiben, mit eigener Prüfung und eigenen
/// Fehlermeldungen. Die Konvention nutzt das Mapping, das es schon gibt, und
/// steht in einer Zeile Dokumentation.
/// </summary>
public sealed class HttpContactSearchProvider : IContactSearchProvider
{
    /// <summary>Feldname, unter dem der Anzeigename erwartet wird.</summary>
    private const string DisplayNameField = "displayName";

    private const string CompanyField = "company";
    private const string EmailField = "email";
    private const string ExternalIdField = "externalId";

    /// <summary>Alles, was so beginnt, wird zu einer Rufnummer.</summary>
    private const string PhonePrefix = "phone";

    private readonly DataSourceDefinition _source;
    private readonly SearchContactsCapability _capability;
    private readonly HttpRequestTemplate _request;
    private readonly CompiledMapping _mapping;
    private readonly TemplateRenderer? _openTemplate;
    private readonly IntegrationHttpClient _http;

    private HttpContactSearchProvider(
        DataSourceDefinition source,
        SearchContactsCapability capability,
        HttpRequestTemplate request,
        CompiledMapping mapping,
        TemplateRenderer? openTemplate,
        IntegrationHttpClient http)
    {
        _source = source;
        _capability = capability;
        _request = request;
        _mapping = mapping;
        _openTemplate = openTemplate;
        _http = http;

        Traits = new ContactSearchTraits(
            IsLocal: false,
            MinQueryLength: 1,
            Timeout: TimeSpan.FromMilliseconds(
                capability.TimeoutMs ?? source.Http?.TimeoutMs ?? 3000));
    }

    public string SourceId => _source.Id;

    public string DisplayName => _source.DisplayName;

    public ContactSearchTraits Traits { get; }

    /// <summary>
    /// Baut einen Anbieter aus einer geprüften Quellenbeschreibung.
    ///
    /// <c>null</c>, wenn die Quelle keine Suche anbietet oder ihre
    /// Beschreibung nicht übersetzbar ist. Beides ist kein Fehler an dieser
    /// Stelle — der Validator hat es längst gemeldet, und eine Quelle, die
    /// nicht kann, wird eben nicht gefragt.
    /// </summary>
    public static HttpContactSearchProvider? TryCreate(
        DataSourceDefinition source,
        IntegrationHttpClient http)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.SearchContacts is not { } capability || source.Http is null)
        {
            return null;
        }

        try
        {
            if (!MappingEngine.TryCompile(capability.ToMapping(), out var mapping, out _))
            {
                return null;
            }

            var openTemplate = source.OpenContact is { } open
                ? TemplateRenderer.Compile(open.UrlTemplate)
                : null;

            return new HttpContactSearchProvider(
                source,
                capability,
                HttpRequestTemplate.Compile(capability.Request),
                mapping,
                openTemplate,
                http);
        }
        catch (Exception ex) when (ex is ExpressionParseException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<ContactSearchPage> SearchAsync(
        ContactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scope = new QueryScope(query);

        var result = await _http.SendAsync(
            _source,
            _request,
            scope,
            Traits.Timeout,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsUsable)
        {
            return new ContactSearchPage(
                SourceId,
                result.Outcome switch
                {
                    HttpOutcome.Timeout => SearchState.Timeout,
                    HttpOutcome.Skipped => SearchState.Skipped,
                    _ => SearchState.Error,
                },
                [],
                result.Message);
        }

        var items = MappingEngine.MapItems(
            _mapping,
            result.Body,
            query.Limit,
            scope.AsDictionary());

        var contacts = new List<Contact>();

        foreach (var item in items)
        {
            if (ToContact(item) is { } contact)
            {
                contacts.Add(contact);
            }
        }

        return new ContactSearchPage(
            SourceId,
            contacts.Count == 0 ? SearchState.Empty : SearchState.Success,
            contacts);
    }

    /// <summary>
    /// Macht aus gemappten Feldern einen Kontakt — oder nichts.
    ///
    /// <b>Ohne Namen kein Kontakt</b>, und ohne Nummer auch nicht: dieselbe
    /// Regel wie bei Outlook (<c>OutlookContactSource.ToContact</c>). Ein
    /// Eintrag, den man weder ansprechen noch anrufen kann, ist für ein
    /// Telefon wertlos und würde die Liste nur füllen.
    /// </summary>
    private Contact? ToContact(MappingResult item)
    {
        if (item.IsEmpty)
        {
            return null;
        }

        var fields = item.Fields;

        var name = Text(fields, DisplayNameField);
        var numbers = ReadNumbers(fields);

        if (string.IsNullOrWhiteSpace(name) || numbers.Count == 0)
        {
            return null;
        }

        var externalId = Text(fields, ExternalIdField);

        return new Contact(
            // Mit Quellenpräfix, damit zwei Systeme mit derselben internen
            // Nummer nicht denselben Kontakt zu bezeichnen scheinen.
            Id: $"{SourceId}:{externalId ?? name}",
            DisplayName: name!,
            Numbers: numbers,
            Source: ContactSourceKind.External,
            SipAddress: null,
            Company: Text(fields, CompanyField),
            SourceId: SourceId,
            Email: Text(fields, EmailField),
            ExternalId: externalId,
            OpenUri: BuildOpenUri(fields, externalId));
    }

    /// <summary>
    /// Sammelt alle Felder, die mit <c>phone</c> beginnen, als Nummern.
    ///
    /// Die Reihenfolge ist die der Feldnamen und damit vorhersehbar — die
    /// erste ist die, die ein Klick wählt.
    /// </summary>
    private static List<ContactNumber> ReadNumbers(IReadOnlyDictionary<string, ContextValue> fields)
    {
        var numbers = new List<ContactNumber>();

        foreach (var (key, value) in fields.OrderBy(static f => f.Key, StringComparer.Ordinal))
        {
            if (!key.StartsWith(PhonePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = value.AsText();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            numbers.Add(new ContactNumber(text, KindOf(key)));
        }

        return numbers;
    }

    /// <summary>
    /// Die Art einer Nummer aus dem Feldnamen: <c>phoneMobile</c> wird mobil.
    /// Was nicht erkannt wird, ist „sonstige" — nicht geschäftlich, denn eine
    /// falsche Beschriftung ist schlechter als eine unbestimmte.
    /// </summary>
    private static ContactNumberKind KindOf(string field)
    {
        var rest = field[PhonePrefix.Length..];

        return rest.ToUpperInvariant() switch
        {
            "BUSINESS" or "WORK" or "OFFICE" => ContactNumberKind.Business,
            "MOBILE" or "CELL" => ContactNumberKind.Mobile,
            "HOME" or "PRIVATE" => ContactNumberKind.Home,
            _ => ContactNumberKind.Other,
        };
    }

    /// <summary>
    /// Die Adresse, unter der sich der Kontakt im Fremdsystem öffnen lässt.
    ///
    /// <b>Hier wird ein zweites Mal geprüft</b>, obwohl der Validator die
    /// Vorlage schon geprüft hat: dort ging es um den festen Anfang, hier um
    /// das Ergebnis nach dem Einsetzen. Nur <c>http</c> und <c>https</c> —
    /// alles andere landete sonst in <c>ShellExecute</c>, und das startet,
    /// was auch immer Windows hinter einem Schema vermutet (§21.2).
    /// </summary>
    private Uri? BuildOpenUri(IReadOnlyDictionary<string, ContextValue> fields, string? externalId)
    {
        if (_openTemplate is null)
        {
            return null;
        }

        var scope = new ContactScope(fields, externalId);
        var rendered = _openTemplate.Render(scope, TemplateEscaping.UrlComponent);

        if (!Uri.TryCreate(rendered, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp
            ? uri
            : null;
    }

    private static string? Text(IReadOnlyDictionary<string, ContextValue> fields, string key) =>
        fields.TryGetValue(key, out var value) && !value.IsEmpty ? value.AsText() : null;

    /// <summary>
    /// Was die Anfragevorlage sieht: <c>query.text</c> und <c>query.limit</c>.
    /// </summary>
    private sealed class QueryScope(ContactQuery query) : IExpressionScope
    {
        public ContextValue Resolve(string path) => path switch
        {
            "query.text" => ContextValue.FromText(query.Text),
            "query.limit" => ContextValue.FromNumber(query.Limit),
            _ => ContextValue.Null,
        };

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;

        /// <summary>
        /// Dieselben Werte für das Mapping, das ein Wörterbuch erwartet — so
        /// sieht auch ein berechnetes Feld den Suchtext.
        /// </summary>
        public Dictionary<string, ContextValue> AsDictionary() =>
            new(StringComparer.Ordinal)
            {
                ["query.text"] = ContextValue.FromText(query.Text),
                ["query.limit"] = ContextValue.FromNumber(query.Limit),
            };
    }

    /// <summary>
    /// Was die Öffnen-Adresse sieht: die gemappten Felder des Treffers, dazu
    /// <c>externalId</c> auch dann, wenn es nicht gemappt wurde.
    /// </summary>
    private sealed class ContactScope(
        IReadOnlyDictionary<string, ContextValue> fields,
        string? externalId) : IExpressionScope
    {
        public ContextValue Resolve(string path)
        {
            if (fields.TryGetValue(path, out var value))
            {
                return value;
            }

            return path == ExternalIdField && externalId is { Length: > 0 }
                ? ContextValue.FromText(externalId)
                : ContextValue.Null;
        }

        public ContextValue ResolveJsonPath(string path) => ContextValue.Null;
    }
}
