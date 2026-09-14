using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nipp.Core.Services.Integrations.Mapping;

namespace Nipp.Core.Services.Integrations.Config;

/// <summary>
/// Was eine Quelle kann (§21.3).
///
/// Welche Fähigkeit eine Quelle anbietet, steht <b>in der Konfiguration</b> —
/// der zugehörige Block ist da oder nicht. Nichts davon wird programmiert:
/// genau darin besteht die Zusage, dass ein CRM Anruferkontext und Suche
/// liefern kann, ein ERP nur Anruferkontext, und Outlook nur Suche.
/// </summary>
public enum Capability
{
    LookupByPhone,
    SearchContacts,
    OpenContact,
}

/// <summary>Die ganze Integrationskonfiguration — der Inhalt von <c>integrations.json</c> (ADR-017).</summary>
public sealed record IntegrationConfig
{
    /// <summary>
    /// Schemaversion. Wird erhöht, wenn ein Feld seine Bedeutung ändert —
    /// nicht, wenn eines dazukommt. Dieselbe Regel wie bei
    /// <c>NippSettings</c>.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    public CallerLookupSettings CallerLookup { get; init; } = new();

    public ContactSearchSettings ContactSearch { get; init; } = new();

    public List<DataSourceDefinition> DataSources { get; init; } = [];

    /// <summary>
    /// Die Karten (§21, <c>docs/plans/INTEGRATION-PLAN.md</c> D.2).
    ///
    /// <para><b>Leer heisst: es gelten die mitgelieferten.</b> Eine Karte
    /// hier ersetzt die mitgelieferte ihrer Art, siehe
    /// <see cref="Cards.CardResolver"/>. Je Art zählt eine; eine zweite ist
    /// ein Befund, keine stille Auswahl.</para>
    ///
    /// <para><b>Warum die Karten in dieser Datei stehen und nicht in
    /// <c>settings.json</c>:</b> sie sprechen über Felder, die es nur gibt,
    /// wenn eine Quelle sie liefert. Karte und Quelle gehören zusammen,
    /// werden zusammen verteilt und zusammen ausgegeben — eine Karte ohne die
    /// Quellen, auf die sie zeigt, ist eine leere Karte.</para>
    /// </summary>
    public List<Cards.CardDefinition> Cards { get; init; } = [];
}

/// <summary>Wann und wofür bei einem Anruf nachgeschlagen wird (§21.4).</summary>
public sealed record CallerLookupSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Bei eingehenden Anrufen. Der Hauptfall.</summary>
    public bool LookupIncoming { get; init; } = true;

    /// <summary>
    /// Bei ausgehenden Anrufen. Entscheidung vom 06.09.2026: ein — wer wählt,
    /// sieht Kundennummer und offene Aufträge schon während des Rufaufbaus.
    /// </summary>
    public bool LookupOutgoing { get; init; } = true;

    /// <summary>
    /// Ob interne Nummern an externe Systeme gehen. <b>Standard aus</b>
    /// (§21.4): für einen Kollegen auf Nebenstelle 151 hat kein CRM eine
    /// Antwort, und die Nebenstelle eines Mitarbeiters hat auf einem fremden
    /// Server nichts zu suchen. Notrufnummern gelten ebenfalls als intern.
    /// </summary>
    public bool LookupInternalNumbers { get; init; }

    /// <summary>
    /// Wie lange ein Ergebnis im Arbeitsspeicher gilt.
    ///
    /// Kurz gehalten: hier liegen personenbezogene Daten aus fremden Systemen.
    /// Fünf Minuten decken den Fall ab, der zählt — derselbe Anrufer ruft
    /// gleich noch einmal an —, ohne dass sich ein Adressbuch ansammelt.
    /// </summary>
    public int CacheSeconds { get; init; } = 300;

    public int CacheMaxEntries { get; init; } = 200;
}

/// <summary>Wie die Kontaktsuche über mehrere Quellen läuft (§21.4).</summary>
public sealed record ContactSearchSettings
{
    /// <summary>
    /// Wartezeit nach dem letzten Tastendruck, bevor gesucht wird. Ohne sie
    /// stellte jeder Buchstabe eine Anfrage an jedes fremde System.
    /// </summary>
    public int DebounceMs { get; init; } = 300;

    public int MinQueryLength { get; init; } = 2;

    public int ResultLimitPerSource { get; init; } = 25;

    public int TotalLimit { get; init; } = 100;

    public int CacheSeconds { get; init; } = 60;

    public MergeSettings Merge { get; init; } = new();
}

/// <summary>
/// Wie Treffer aus mehreren Quellen zusammengeführt werden.
///
/// Die Voreinstellung ist bewusst <b>konservativ</b>: lieber zwei Zeilen für
/// dieselbe Person als eine Zeile für zwei Personen. Eine falsche
/// Zusammenführung führt dazu, dass jemand die falsche Nummer anruft.
/// </summary>
public sealed record MergeSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Über die normalisierte Rufnummer. Das sicherste Merkmal.</summary>
    public bool ByPhone { get; init; } = true;

    /// <summary>Über die E-Mail-Adresse. Ebenfalls eindeutig, wenn vorhanden.</summary>
    public bool ByEmail { get; init; } = true;

    /// <summary>
    /// Über Name und Firma. <b>Standard aus.</b> In einer Firma mit zwei
    /// Mitarbeitern gleichen Namens — oder bei einer Zentrale, an der viele
    /// hängen — führt das zu falschen Treffern.
    /// </summary>
    public bool ByNameAndCompany { get; init; }
}

/// <summary>
/// Eine externe Quelle (§21.3).
///
/// <b>Daten, kein Verhalten.</b> Was diese Beschreibung bedeutet, entscheidet
/// der Connector zu <see cref="Type"/> — heute genau einer: <c>http</c>.
/// </summary>
public sealed record DataSourceDefinition
{
    /// <summary>
    /// Kurzname, zugleich der <b>Namensraum im Kontext</b> (<c>crm.company</c>)
    /// und das Präfix von Kontaktkennungen (<c>crm:4711</c>). Kleinbuchstaben,
    /// Ziffern, Strich und Unterstrich.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Was der Benutzer sieht — in den Einstellungen und auf der Karte.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Welcher Connector die Quelle bedient. Heute nur <c>http</c>.</summary>
    public string Type { get; init; } = "http";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Reihenfolge bei Konflikten: kleinere Zahl gewinnt. Bestimmt, welcher
    /// Anzeigename bei einem zusammengeführten Kontakt gilt und in welcher
    /// Reihenfolge Trefferlisten stehen.
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>Verbindung und Anmeldung. Pflicht bei <c>type: http</c>.</summary>
    public HttpConnection? Http { get; init; }

    public LookupByPhoneCapability? LookupByPhone { get; init; }

    public SearchContactsCapability? SearchContacts { get; init; }

    public OpenContactCapability? OpenContact { get; init; }

    /// <summary>
    /// Welche Fähigkeiten diese Quelle tatsächlich anbietet.
    ///
    /// <para><b>Wird nicht ausgegeben.</b> Der Wert ist abgeleitet — er sagt
    /// nur, welche Blöcke oben dastehen. In der Datei wäre er eine zweite,
    /// scheinbar änderbare Wahrheit: wer <c>capabilities</c> von Hand
    /// bearbeitet, bewirkt nichts und sucht den Fehler dann im Mapping.</para>
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Capability> Capabilities =>
    [
        .. LookupByPhone is null ? Array.Empty<Capability>() : [Capability.LookupByPhone],
        .. SearchContacts is null ? Array.Empty<Capability>() : [Capability.SearchContacts],
        .. OpenContact is null ? Array.Empty<Capability>() : [Capability.OpenContact],
    ];
}

/// <summary>Verbindung zu einer HTTP-Schnittstelle.</summary>
public sealed record HttpConnection
{
    /// <summary>
    /// Die Basisadresse, etwa <c>https://crm.muster.ch/api/v2</c>. <b>https</b>,
    /// ausser <see cref="AllowInsecureHttp"/> ist ausdrücklich gesetzt.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Zeitgrenze je Anfrage. Beim Anruferkontext ist sie ein Versprechen an
    /// den Benutzer: länger als das wartet die Karte nicht auf diese Quelle.
    /// </summary>
    public int TimeoutMs { get; init; } = 1500;

    /// <summary>
    /// Grösste angenommene Antwort. Wird beim Lesen abgebrochen, nicht erst
    /// danach geprüft — sonst läge eine 500-MB-Antwort im Speicher, bevor
    /// jemand merkt, dass sie zu gross ist.
    /// </summary>
    public int MaxResponseBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Unverschlüsseltes http zulassen. <b>Standard aus</b>, dieselbe Haltung
    /// wie bei der Provisionierung (ADR-012): über http liest jeder im Netz
    /// mit, welche Nummer angerufen wird und wer dahintersteckt.
    /// </summary>
    public bool AllowInsecureHttp { get; init; }

    /// <summary>Feste Kopfzeilen. Für <c>Accept</c> und Ähnliches — nicht für Geheimnisse.</summary>
    public Dictionary<string, string> Headers { get; init; } = [];

    public AuthDefinition Auth { get; init; } = new();
}

/// <summary>Art der Anmeldung (§21.3).</summary>
public enum AuthKind
{
    None,
    ApiKey,
    Bearer,
    Basic,
}

/// <summary>Wo ein API-Schlüssel steht.</summary>
public enum ApiKeyLocation
{
    Header,

    /// <summary>
    /// Als Anfrageparameter. <b>Wird beim Prüfen bemängelt</b>: Adressen
    /// landen in Server- und Proxy-Protokollen, und damit auch der Schlüssel.
    /// Bleibt möglich, weil es APIs gibt, die es nicht anders können.
    /// </summary>
    Query,
}

/// <summary>
/// Wie sich nipp bei der Quelle anmeldet.
///
/// <b>Hier steht nie ein Geheimnis</b>, sondern nur ein Verweis darauf. Der
/// Wert liegt im <c>SecretStore</c> unter DPAPI (§10, §21.2) — diese Datei
/// wird exportiert, in Tickets gelegt und über das Netz verteilt.
/// </summary>
public sealed record AuthDefinition
{
    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public AuthKind Type { get; init; } = AuthKind.None;

    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public ApiKeyLocation In { get; init; } = ApiKeyLocation.Header;

    /// <summary>Name der Kopfzeile oder des Parameters, etwa <c>X-Api-Key</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Verweis auf das Geheimnis, etwa <c>crm.apiKey</c>.</summary>
    public string? SecretRef { get; init; }

    /// <summary>
    /// Das Schema vor dem Wert in der <c>Authorization</c>-Kopfzeile. Nur bei
    /// <see cref="AuthKind.Bearer"/> von Bedeutung; ohne Angabe
    /// <c>Bearer</c>.
    ///
    /// <b>Warum das konfigurierbar sein muss.</b> „Bearer" ist nicht das
    /// einzige gebräuchliche Schema. Das Django REST Framework verlangt in
    /// seiner verbreitetsten Anmeldung <c>Authorization: Token &lt;key&gt;</c>
    /// — das CRM ist genau so eine API. Wer das Schema fest verdrahtet,
    /// zwingt den Betreiber, <c>Token </c> in den Geheimniswert zu tippen:
    /// dann steht ein Teil des Protokolls in der verschlüsselten Ablage, wo
    /// ihn niemand vermutet und niemand korrigieren kann.
    ///
    /// <b>Irreführend dabei:</b> das CRM antwortet auf eine Anfrage ohne
    /// Anmeldung mit <c>WWW-Authenticate: Bearer realm="api"</c> und
    /// akzeptiert dann ausschliesslich <c>Token</c>. Der Kopfzeile ist also
    /// nicht zu glauben — ausprobieren.
    /// </summary>
    public string? Scheme { get; init; }

    /// <summary>Bei <see cref="AuthKind.Basic"/>: Verweis auf den Benutzernamen.</summary>
    public string? UsernameSecretRef { get; init; }

    /// <summary>Bei <see cref="AuthKind.Basic"/>: Verweis auf das Passwort.</summary>
    public string? PasswordSecretRef { get; init; }

    /// <summary>Alle Verweise dieser Anmeldung — für die Prüfung, ob sie hinterlegt sind.</summary>
    public IReadOnlyList<string> SecretRefs =>
    [
        .. new[] { SecretRef, UsernameSecretRef, PasswordSecretRef }
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => r!),
    ];
}

/// <summary>Eine Anfrage, als Beschreibung mit Vorlagen.</summary>
public sealed record RequestDefinition
{
    public string Method { get; init; } = "GET";

    /// <summary>
    /// Pfad unterhalb der Basisadresse, mit Vorlagen:
    /// <c>/contacts/{{number.e164}}</c>. Eingesetzte Werte werden
    /// <b>URL-kodiert</b>.
    /// </summary>
    public string Path { get; init; } = "/";

    /// <summary>Anfrageparameter, Werte als Vorlagen.</summary>
    public Dictionary<string, string> Query { get; init; } = [];

    /// <summary>
    /// Anfragekörper als JSON. Jede Zeichenfolge darin ist eine Vorlage —
    /// auch tief verschachtelt.
    /// </summary>
    public JsonNode? Body { get; init; }
}

/// <summary>Fähigkeit: Kontext zu einer Rufnummer.</summary>
public sealed record LookupByPhoneCapability
{
    public required RequestDefinition Request { get; init; }

    /// <summary>
    /// Feldname zu Herkunft.
    ///
    /// <b>Ohne Zwischenebene</b>, und das ist eine bewusste Entscheidung: eine
    /// Konfigurationsdatei wird von Hand gepflegt und gelesen. Ein
    /// zusätzliches <c>"fields"</c> um die Felder herum wäre eine Ebene, die
    /// nichts trennt — <c>emptyWhen</c> steht daneben, nicht darin.
    ///
    /// Der erste Entwurf hatte sie, und die Beispielkonfiguration im Plan
    /// nicht. Der Test gegen die erwartete Antwortform hat den Unterschied
    /// gefunden, bevor jemand danach einen Endpunkt gebaut hat.
    /// </summary>
    public Dictionary<string, FieldMapping> Mapping { get; init; } = [];

    /// <summary>
    /// Wann eine Antwort als „nichts gefunden" gilt, obwohl sie technisch in
    /// Ordnung ist — etwa <c>status == 404</c> oder <c>isEmpty($.contact)</c>.
    /// </summary>
    public string? EmptyWhen { get; init; }

    /// <summary>Eigene Zeitgrenze; ohne Angabe gilt die der Verbindung.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Die Form, in der die Mapping-Engine damit arbeitet.</summary>
    public MappingDefinition ToMapping() =>
        new() { Fields = Mapping, EmptyWhen = EmptyWhen };
}

/// <summary>Fähigkeit: Kontakte suchen.</summary>
public sealed record SearchContactsCapability
{
    public required RequestDefinition Request { get; init; }

    /// <summary>
    /// Der Pfad auf die Liste der Treffer, etwa <c>$.results[*]</c>. Leer
    /// heisst: die Antwort ist ein einzelner Datensatz.
    /// </summary>
    public string? ItemsPath { get; init; }

    /// <summary>
    /// Das Mapping eines <b>einzelnen Treffers</b>. Erwartete Felder:
    /// <c>displayName</c>, <c>company</c>, <c>email</c>, <c>externalId</c> und
    /// alles, was mit <c>phone</c> beginnt, für die Nummern.
    /// </summary>
    public Dictionary<string, FieldMapping> Mapping { get; init; } = [];

    /// <summary>
    /// Zeitgrenze. Grosszügiger als beim Anruferkontext: eine Suche ist eine
    /// bewusste Handlung, ein klingelndes Telefon nicht.
    ///
    /// <para><b>Wird immer geschrieben, auch als <c>null</c></b>, aus demselben
    /// Grund wie bei <see cref="Cards.CardField.EmptyText"/>: <c>null</c> heisst
    /// hier „es gilt die Zeitgrenze der Verbindung", und die Vorgabe ist nicht
    /// <c>null</c>. Ohne diese Ausnahme würde ein ausdrückliches <c>null</c>
    /// beim Speichern verschwinden und beim Lesen wieder 3000 werden.</para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? TimeoutMs { get; init; } = 3000;

    /// <summary>Die Form, in der die Mapping-Engine damit arbeitet.</summary>
    public MappingDefinition ToMapping() =>
        new() { Fields = Mapping, ItemsPath = ItemsPath };
}

/// <summary>Fähigkeit: den Kontakt im Fremdsystem öffnen.</summary>
public sealed record OpenContactCapability
{
    /// <summary>
    /// Adresse mit Vorlagen, etwa
    /// <c>https://crm.muster.ch/contacts/{{externalId}}</c>.
    ///
    /// Nur <c>http</c> und <c>https</c>, und der Rechnername muss zur Quelle
    /// passen — sonst liesse sich über eine Konfigurationsdatei ein beliebiger
    /// Link unter einer vertrauten Beschriftung unterbringen (§21.2).
    /// </summary>
    public required string UrlTemplate { get; init; }
}
