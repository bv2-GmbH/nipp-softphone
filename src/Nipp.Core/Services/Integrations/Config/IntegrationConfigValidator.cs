using System.Text.RegularExpressions;
using Nipp.Core.Services.Integrations.Expressions;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Mapping;
using Nipp.Core.Services.Integrations.Secrets;

namespace Nipp.Core.Services.Integrations.Config;

/// <summary>Wie schwer ein Befund wiegt.</summary>
public enum IssueSeverity
{
    /// <summary>Die Quelle bleibt aus, bis das behoben ist.</summary>
    Error,

    /// <summary>Die Quelle läuft, aber etwas ist bedenklich oder unvollständig.</summary>
    Warning,
}

/// <summary>
/// Ein Befund an der Konfiguration.
/// </summary>
/// <param name="Path">Wo es steht, etwa <c>dataSources[crm].http.baseUrl</c>.</param>
/// <param name="Severity">Wie schwer.</param>
/// <param name="Message">Was nicht stimmt und was zu tun ist (§15).</param>
public sealed record ValidationIssue(string Path, IssueSeverity Severity, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// Prüft eine Integrationskonfiguration (§21.3).
///
/// <b>Alle Befunde auf einmal.</b> Wer eine Quelle einrichtet, will nicht
/// fünfmal speichern, um fünf Fehler zu finden. Deshalb wird gesammelt statt
/// beim ersten Befund abgebrochen — dasselbe Vorgehen wie in der
/// <see cref="MappingEngine"/>.
///
/// Geprüft wird beim Laden, beim Speichern und beim Einlesen einer Datei. Eine
/// Quelle mit einem Fehler bleibt aus; eine mit einer Warnung läuft.
/// </summary>
public sealed partial class IntegrationConfigValidator(IntegrationSecrets secrets)
{
    /// <summary>
    /// Namensräume, die schon belegt sind. Eine Quelle mit der Kennung
    /// <c>number</c> würde auf einer Karte die Rufnummer verdecken.
    /// </summary>
    private static readonly string[] ReservedIds = ["number", "query", "status", "contacts", "call"];

    /// <summary>Untere und obere Grenze für eine Zeitgrenze, in Millisekunden.</summary>
    private const int MinTimeoutMs = 200;
    private const int MaxTimeoutMs = 10_000;

    private const int MinResponseBytes = 4 * 1024;
    private const int MaxResponseBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Prüft die ganze Datei.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Validate(IntegrationConfig? config)
    {
        var issues = new List<ValidationIssue>();

        if (config is null)
        {
            return issues;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in config.DataSources)
        {
            var path = $"dataSources[{source.Id}]";

            if (!seen.Add(source.Id ?? string.Empty))
            {
                issues.Add(new ValidationIssue(
                    path,
                    IssueSeverity.Error,
                    $"Die Kennung '{source.Id}' kommt mehrfach vor. Jede Quelle braucht eine eigene."));
            }

            ValidateSource(source, path, issues);
        }

        ValidateSearchSettings(config.ContactSearch, issues);

        // Die Karten. Sie hängen an keiner einzelnen Quelle, deshalb erst
        // hier — und ihre Befunde tragen den Pfad `cards[...]`, damit
        // `UsableSources` sie nicht für Quellenfehler hält und deswegen eine
        // funktionierende Quelle abschaltet.
        issues.AddRange(Cards.CardDefinitionValidator.Validate(config.Cards));

        return issues;
    }

    /// <summary>Prüft eine einzelne Quelle — auch für den Test-Knopf in den Einstellungen.</summary>
    public IReadOnlyList<ValidationIssue> ValidateSource(DataSourceDefinition source)
    {
        var issues = new List<ValidationIssue>();

        ValidateSource(source, $"dataSources[{source?.Id}]", issues);

        return issues;
    }

    private void ValidateSource(DataSourceDefinition source, string path, List<ValidationIssue> issues)
    {
        if (source is null)
        {
            return;
        }

        ValidateId(source, path, issues);

        if (string.IsNullOrWhiteSpace(source.DisplayName))
        {
            issues.Add(new ValidationIssue(
                $"{path}.displayName",
                IssueSeverity.Error,
                "Der Anzeigename fehlt. Er steht in den Einstellungen und in Fehlermeldungen."));
        }

        if (!string.Equals(source.Type, "http", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                $"{path}.type",
                IssueSeverity.Error,
                $"Den Typ '{source.Type}' gibt es nicht. Heute ist nur 'http' möglich."));

            return;
        }

        if (source.Http is null)
        {
            issues.Add(new ValidationIssue(
                $"{path}.http",
                IssueSeverity.Error,
                "Für eine http-Quelle fehlt der Abschnitt 'http' mit der Adresse."));

            return;
        }

        var baseUrl = ValidateConnection(source.Http, $"{path}.http", issues);

        if (source.Capabilities.Count == 0)
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Warning,
                "Diese Quelle bietet nichts an. Mindestens 'lookupByPhone' oder "
                    + "'searchContacts' einrichten, sonst wird sie nie gefragt."));
        }

        ValidateCapability(
            source.LookupByPhone?.Request,
            source.LookupByPhone?.ToMapping(),
            source.LookupByPhone?.TimeoutMs,
            $"{path}.lookupByPhone",
            issues);

        ValidateCapability(
            source.SearchContacts?.Request,
            source.SearchContacts?.ToMapping(),
            source.SearchContacts?.TimeoutMs,
            $"{path}.searchContacts",
            issues);

        ValidateOpenContact(source, baseUrl, $"{path}.openContact", issues);
    }

    private static void ValidateId(DataSourceDefinition source, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            issues.Add(new ValidationIssue(
                $"{path}.id",
                IssueSeverity.Error,
                "Die Kennung fehlt. Sie ist zugleich der Namensraum auf der Karte."));

            return;
        }

        if (!IdPattern().IsMatch(source.Id))
        {
            issues.Add(new ValidationIssue(
                $"{path}.id",
                IssueSeverity.Error,
                $"'{source.Id}' ist keine gültige Kennung. Erlaubt sind Kleinbuchstaben, Ziffern, "
                    + "Strich und Unterstrich, beginnend mit einem Buchstaben, höchstens 32 Zeichen."));
        }

        if (ReservedIds.Contains(source.Id, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                $"{path}.id",
                IssueSeverity.Error,
                $"'{source.Id}' ist vergeben: unter diesem Namen stehen auf der Karte bereits "
                    + "eigene Angaben. Eine andere Kennung wählen."));
        }
    }

    /// <summary>
    /// Prüft die Verbindung und liefert die Basisadresse zurück — sie wird für
    /// die Prüfung der Öffnen-Adresse noch gebraucht.
    /// </summary>
    private Uri? ValidateConnection(
        HttpConnection connection,
        string path,
        List<ValidationIssue> issues)
    {
        Uri? baseUrl = null;

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var parsed))
        {
            issues.Add(new ValidationIssue(
                $"{path}.baseUrl",
                IssueSeverity.Error,
                "Die Adresse ist keine vollständige URL. Erwartet wird etwas wie "
                    + "https://crm.example.ch/api."));
        }
        else if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            issues.Add(new ValidationIssue(
                $"{path}.baseUrl",
                IssueSeverity.Error,
                $"'{parsed.Scheme}' ist kein erlaubtes Protokoll. Nur https, in Ausnahmen http."));
        }
        else
        {
            baseUrl = parsed;

            if (parsed.Scheme == Uri.UriSchemeHttp && !connection.AllowInsecureHttp)
            {
                issues.Add(new ValidationIssue(
                    $"{path}.baseUrl",
                    IssueSeverity.Error,
                    "Die Adresse ist unverschlüsselt. Über http liest jeder im Netz mit, wer "
                        + "anruft und wer dahintersteckt. Entweder https einrichten oder "
                        + "'allowInsecureHttp' ausdrücklich setzen."));
            }

            if (!string.IsNullOrEmpty(parsed.UserInfo))
            {
                issues.Add(new ValidationIssue(
                    $"{path}.baseUrl",
                    IssueSeverity.Error,
                    "In der Adresse stehen Zugangsdaten. Sie gehören unter 'auth' und damit in "
                        + "den verschlüsselten Speicher, nicht in diese Datei."));
            }
        }

        ValidateHeaders(connection, path, issues);

        ValidateRange(connection.TimeoutMs, MinTimeoutMs, MaxTimeoutMs, $"{path}.timeoutMs", "Die Zeitgrenze", issues);

        ValidateRange(
            connection.MaxResponseBytes,
            MinResponseBytes,
            MaxResponseBytes,
            $"{path}.maxResponseBytes",
            "Die Grösse der Antwort",
            issues);

        ValidateAuth(connection.Auth, $"{path}.auth", issues);

        return baseUrl;
    }

    private void ValidateAuth(AuthDefinition auth, string path, List<ValidationIssue> issues)
    {
        // Ein Schema wirkt nur bei Bearer. Anderswo tut die Angabe nichts —
        // und eine Angabe, die nichts tut, kostet den nächsten Betreiber eine
        // Stunde Fehlersuche.
        if (auth.Type != AuthKind.Bearer && !string.IsNullOrWhiteSpace(auth.Scheme))
        {
            issues.Add(new ValidationIssue(
                $"{path}.scheme",
                IssueSeverity.Warning,
                $"«scheme» wirkt nur bei «bearer» und wird bei «{auth.Type}» nicht "
                    + "beachtet. Bei einem API-Schlüssel bestimmt «name» die Kopfzeile."));
        }

        switch (auth.Type)
        {
            case AuthKind.ApiKey:
                if (string.IsNullOrWhiteSpace(auth.Name))
                {
                    issues.Add(new ValidationIssue(
                        $"{path}.name",
                        IssueSeverity.Error,
                        "Für einen API-Schlüssel fehlt der Name der Kopfzeile oder des Parameters."));
                }

                RequireSecret(auth.SecretRef, $"{path}.secretRef", issues);

                if (auth.In == ApiKeyLocation.Query)
                {
                    issues.Add(new ValidationIssue(
                        path,
                        IssueSeverity.Warning,
                        "Der Schlüssel steht als Anfrageparameter in der Adresse und landet damit "
                            + "in den Protokollen von Servern und Proxys. Wenn die Schnittstelle "
                            + "es zulässt, besser als Kopfzeile."));
                }

                break;

            case AuthKind.Bearer:
                RequireSecret(auth.SecretRef, $"{path}.secretRef", issues);

                // Ein Schema mit Leerzeichen oder Sonderzeichen lässt
                // AuthenticationHeaderValue werfen — und das erst bei der
                // ersten Anfrage, weit weg von der Ursache. Hier prüfen.
                if (auth.Scheme is { Length: > 0 } scheme
                    && !scheme.Trim().All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                {
                    issues.Add(new ValidationIssue(
                        $"{path}.scheme",
                        IssueSeverity.Error,
                        "Das Schema darf nur Buchstaben, Ziffern, Bindestrich und Unterstrich "
                            + "enthalten — es steht vor dem Wert in der Authorization-Kopfzeile, "
                            + "etwa «Bearer» oder «Token». Der Schlüssel selbst gehört nicht hierher."));
                }

                break;

            case AuthKind.Basic:
                RequireSecret(auth.UsernameSecretRef, $"{path}.usernameSecretRef", issues);
                RequireSecret(auth.PasswordSecretRef, $"{path}.passwordSecretRef", issues);
                break;

            case AuthKind.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Ein Verweis muss angegeben <b>und</b> hinterlegt sein.
    ///
    /// Ein fehlender Wert ist eine <b>Warnung</b>, kein Fehler: die
    /// Konfiguration ist richtig, es fehlt nur das Geheimnis auf diesem Gerät.
    /// Genau das ist der Normalfall nach einer Provisionierung, und die
    /// Meldung sagt, was zu tun ist.
    /// </summary>
    private void RequireSecret(string? reference, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Error,
                "Es fehlt der Verweis auf das Geheimnis. In dieser Datei steht nie ein "
                    + "Schlüssel, sondern nur sein Name."));

            return;
        }

        if (!secrets.IsConfigured(reference))
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Warning,
                $"Zum Verweis '{reference}' ist auf diesem Gerät nichts hinterlegt. Die Quelle "
                    + "wird übersprungen, bis der Wert in den Einstellungen eingetragen ist."));
        }
    }

    private static void ValidateCapability(
        RequestDefinition? request,
        MappingDefinition? mapping,
        int? timeout,
        string path,
        List<ValidationIssue> issues)
    {
        if (request is null)
        {
            return;
        }

        try
        {
            _ = HttpRequestTemplate.Compile(request);
        }
        catch (Exception ex) when (ex is ExpressionParseException or ArgumentException)
        {
            issues.Add(new ValidationIssue($"{path}.request", IssueSeverity.Error, ex.Message));
        }

        if (!MappingEngine.TryCompile(mapping, out var compiled, out var errors))
        {
            foreach (var error in errors)
            {
                issues.Add(new ValidationIssue($"{path}.mapping", IssueSeverity.Error, error));
            }
        }
        else if (compiled.IsEmpty)
        {
            issues.Add(new ValidationIssue(
                $"{path}.mapping",
                IssueSeverity.Warning,
                "Es ist kein Feld gemappt. Die Quelle wird gefragt, liefert aber nichts, was "
                    + "sich anzeigen liesse."));
        }

        if (timeout is { } value)
        {
            ValidateRange(value, MinTimeoutMs, MaxTimeoutMs, $"{path}.timeoutMs", "Die Zeitgrenze", issues);
        }
    }

    /// <summary>
    /// Prüft die Adresse zum Öffnen eines Kontakts.
    ///
    /// <b>Der Rechnername muss zur Quelle passen.</b> Ohne diese Prüfung
    /// liesse sich über eine verteilte Konfigurationsdatei ein beliebiger Link
    /// unter einer vertrauten Beschriftung unterbringen — „Kontakt im CRM
    /// öffnen" führte dann irgendwohin (§21.2).
    /// </summary>
    private static void ValidateOpenContact(
        DataSourceDefinition source,
        Uri? baseUrl,
        string path,
        List<ValidationIssue> issues)
    {
        if (source.OpenContact is not { } open)
        {
            return;
        }

        TemplateRenderer template;

        try
        {
            template = TemplateRenderer.Compile(open.UrlTemplate);
        }
        catch (ExpressionParseException ex)
        {
            issues.Add(new ValidationIssue($"{path}.urlTemplate", IssueSeverity.Error, ex.Message));
            return;
        }

        // Der feste Anfang der Vorlage genügt für die Prüfung: dort steht das
        // Protokoll und der Rechnername, und beides darf keine Vorlage sein.
        var literal = template.Source;
        var placeholder = literal.IndexOf("{{", StringComparison.Ordinal);
        var prefix = placeholder < 0 ? literal : literal[..placeholder];

        if (!Uri.TryCreate(prefix.TrimEnd('/'), UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
        {
            issues.Add(new ValidationIssue(
                $"{path}.urlTemplate",
                IssueSeverity.Error,
                "Die Adresse muss mit http:// oder https:// und einem festen Rechnernamen "
                    + "beginnen — nicht mit einer Vorlage."));

            return;
        }

        if (baseUrl is not null
            && !string.Equals(target.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                $"{path}.urlTemplate",
                IssueSeverity.Warning,
                $"Die Adresse zeigt auf '{target.Host}', die Schnittstelle aber auf "
                    + $"'{baseUrl.Host}'. Das ist möglich, sollte aber beabsichtigt sein: "
                    + "die Schaltfläche trägt den Namen dieser Quelle."));
        }
    }

    private static void ValidateSearchSettings(ContactSearchSettings settings, List<ValidationIssue> issues)
    {
        if (settings.MinQueryLength < 1)
        {
            issues.Add(new ValidationIssue(
                "contactSearch.minQueryLength",
                IssueSeverity.Error,
                "Mindestens ein Zeichen. Bei null würde jede geleerte Eingabe eine Suche über "
                    + "alle Quellen auslösen."));
        }

        if (settings.DebounceMs is < 0 or > 5000)
        {
            issues.Add(new ValidationIssue(
                "contactSearch.debounceMs",
                IssueSeverity.Error,
                "Die Wartezeit muss zwischen 0 und 5000 ms liegen."));
        }

        if (settings.ResultLimitPerSource < 1)
        {
            issues.Add(new ValidationIssue(
                "contactSearch.resultLimitPerSource",
                IssueSeverity.Error,
                "Mindestens ein Treffer je Quelle."));
        }
    }

    /// <summary>
    /// Kopfzeilennamen, die ein Geheimnis tragen würden.
    ///
    /// <para>Das Feld <c>headers</c> ist als „nicht für Geheimnisse"
    /// dokumentiert, wurde aber ungeprüft gesendet — <c>"Authorization":
    /// "Token abc"</c> funktionierte also. Und <c>integrations.json</c> ist die
    /// Datei, die ausgegeben, im Ticket abgelegt und über ein Profil verteilt
    /// wird. Ein Geheimnis gehört unter <c>auth</c> und damit in die
    /// DPAPI-Ablage (§21.2).</para>
    /// </summary>
    private static readonly string[] ForbiddenHeaderNames =
        ["authorization", "proxy-authorization", "cookie", "set-cookie"];

    /// <summary>Namensteile, die auf ein versehentlich abgelegtes Geheimnis hindeuten.</summary>
    private static readonly string[] SuspiciousHeaderParts = ["key", "token", "secret", "password"];

    private static void ValidateHeaders(
        HttpConnection connection,
        string path,
        List<ValidationIssue> issues)
    {
        foreach (var (name, _) in connection.Headers)
        {
            var lower = name.Trim().ToLowerInvariant();

            if (ForbiddenHeaderNames.Contains(lower))
            {
                issues.Add(new ValidationIssue(
                    $"{path}.headers.{name}",
                    IssueSeverity.Error,
                    $"'{name}' gehört nicht unter 'headers'. Diese Datei wird ausgegeben und "
                        + "verteilt; ein Geheimnis darin läge im Klartext. Zugangsdaten gehören "
                        + "unter 'auth' und landen dann im verschlüsselten Speicher."));

                continue;
            }

            if (SuspiciousHeaderParts.Any(p => lower.Contains(p, StringComparison.Ordinal)))
            {
                issues.Add(new ValidationIssue(
                    $"{path}.headers.{name}",
                    IssueSeverity.Warning,
                    $"'{name}' klingt nach einem Geheimnis. Wenn es eines ist, gehört es unter "
                        + "'auth' — 'headers' steht im Klartext in dieser Datei."));
            }
        }
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string path,
        string what,
        List<ValidationIssue> issues)
    {
        if (value < minimum || value > maximum)
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Error,
                $"{what} muss zwischen {minimum} und {maximum} liegen, angegeben ist {value}."));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,31}$")]
    private static partial Regex IdPattern();
}
