using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Liest ein Provisionierungsprofil aus XML (AP8.3).
///
/// Eine reine Funktion ohne Netz und ohne Dateisystem — §13 verlangt Tests
/// „inklusive kaputtes XML", und die gibt es nur, wenn das Parsen für sich
/// steht.
///
/// <b>Sicherheit.</b> Das XML kommt von einem Webserver, also von aussen. Zwei
/// Angriffe sind bei XML Standard und werden hier hart ausgeschlossen:
/// <list type="bullet">
///   <item>
///     <b>XXE</b> — eine Entität, die <c>file:///C:/Windows/win.ini</c> oder
///     eine interne URL einbindet. Deshalb <c>DtdProcessing.Prohibit</c> und
///     kein <c>XmlResolver</c>.
///   </item>
///   <item>
///     <b>Entitätenexplosion</b> („Milliarden Lacher"). Ohne DTD gibt es keine
///     benutzerdefinierten Entitäten und damit auch diesen Fall nicht.
///   </item>
/// </list>
/// </summary>
public static class ProvisioningParser
{
    /// <summary>
    /// Zerlegt ein Profil. Wirft nie — ein kaputtes Profil darf den Start
    /// nicht verhindern (§17).
    /// </summary>
    /// <param name="xml">Der Inhalt der Profildatei.</param>
    /// <param name="profile">Das gelesene Profil, oder <see cref="ProvisioningProfile.Empty"/>.</param>
    /// <param name="error">Warum es nicht ging — gehört nach §15 in die Meldung, nicht nur ins Log.</param>
    public static bool TryParse(string? xml, out ProvisioningProfile profile, out string? error)
    {
        profile = ProvisioningProfile.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(xml))
        {
            error = "Das Profil ist leer.";
            return false;
        }

        XDocument document;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };

            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, settings);

            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            // Zeile und Spalte mitgeben: wer ein Profil von Hand schreibt,
            // sucht sonst in einer 200-Zeilen-Datei nach dem fehlenden „>".
            error = $"Das Profil ist kein gültiges XML: {ex.Message} (Zeile {ex.LineNumber}, Spalte {ex.LinePosition})";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Das Profil liess sich nicht lesen: {ex.Message}";
            return false;
        }

        var root = document.Root;

        if (root is null || root.Name.LocalName != "nipp-provisioning")
        {
            error = "Das Profil beginnt nicht mit <nipp-provisioning>. "
                + "Vermutlich ist es für ein anderes Programm gedacht.";
            return false;
        }

        var version = ReadInt(root.Attribute("version")?.Value) ?? 1;

        if (version > 1)
        {
            // Nicht abbrechen: ein neueres Profil enthält vermutlich Felder,
            // die diese Fassung nicht kennt, aber die bekannten gelten weiter.
            error = $"Das Profil ist für Schemaversion {version} geschrieben, "
                + "nipp kennt Version 1. Unbekannte Angaben werden übergangen.";
        }

        profile = new ProvisioningProfile(
            Version: version,
            ProfileName: root.Attribute("profile")?.Value,
            Accounts: ReadAccounts(root),
            Team: ReadTeam(root),
            LockedFields: ReadLockedFields(root),
            Values: ReadValues(root),
            IntegrationsUri: ReadIntegrationsUri(root));

        return true;
    }

    private static List<ProvisionedAccount> ReadAccounts(XElement root)
    {
        var accounts = new List<ProvisionedAccount>();

        foreach (var element in root.Elements("accounts").Elements("account"))
        {
            var username = element.Attribute("username")?.Value;
            var domain = element.Attribute("domain")?.Value;

            // Ohne beides ist es kein Konto. Ein halbes Konto anzulegen würde
            // nur eine Fehlermeldung bei der Registrierung erzeugen.
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(domain))
            {
                continue;
            }

            accounts.Add(new ProvisionedAccount(
                Username: username.Trim(),
                Domain: domain.Trim(),
                Password: element.Attribute("password")?.Value,
                AuthUserId: element.Attribute("auth-user-id")?.Value,
                DisplayName: element.Attribute("display-name")?.Value,
                Transport: ReadTransport(element.Attribute("transport")?.Value),
                OutboundProxy: element.Attribute("outbound-proxy")?.Value,
                ExpiresSeconds: ReadInt(element.Attribute("expires")?.Value)));

            // §20.2: mehr als zehn Konten nimmt nipp nicht an.
            if (accounts.Count == 10)
            {
                break;
            }
        }

        return accounts;
    }

    private static List<TeamExtension> ReadTeam(XElement root)
    {
        var team = new List<TeamExtension>();

        foreach (var element in root.Elements("team").Elements("extension"))
        {
            var name = element.Attribute("name")?.Value;
            var number = element.Attribute("number")?.Value;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var mobile = element.Attribute("mobile")?.Value;
            var group = element.Attribute("group")?.Value;

            team.Add(new TeamExtension(
                DisplayName: name.Trim(),
                Extension: number.Trim(),
                SipAddress: element.Attribute("sip")?.Value,

                // Leer heisst „nicht gesetzt": ein Profil, das `mobile=""`
                // schreibt, meint keine Handynummer und keine leere.
                Mobile: string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim(),
                Group: string.IsNullOrWhiteSpace(group) ? null : group.Trim()));
        }

        return team;
    }

    /// <summary>
    /// Die Adresse der Integrationskonfiguration (§21.3):
    /// <c>&lt;integrations src="https://…/integrations.json"/&gt;</c>.
    ///
    /// <b>Nur https</b>, und ohne Ausnahme. Bei der Provisionierung selbst gibt
    /// es einen Schalter für Anlagen ohne TLS (ADR-012); hier nicht. Eine
    /// Integrationsdatei bestimmt, welche Adressen nipp mit Rufnummern
    /// beliefert — wer sie unterwegs austauschen kann, leitet die Kundendaten
    /// eines ganzen Betriebs um.
    /// </summary>
    private static string? ReadIntegrationsUri(XElement root)
    {
        var value = root.Elements("integrations").FirstOrDefault()?.Attribute("src")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
                ? value
                : null;
    }

    private static List<string> ReadLockedFields(XElement root) =>
        [.. root.Elements("locked")
            .Elements("field")
            .Select(static e => e.Value.Trim())
            .Where(static v => v.Length > 0)];

    /// <summary>
    /// Liest die flachen Wertepaare. Erlaubt sind beide Schreibweisen:
    /// <c>&lt;set path="…" value="…"/&gt;</c> und
    /// <c>&lt;set path="…"&gt;Wert&lt;/set&gt;</c> — die zweite, weil Werte mit
    /// Anführungszeichen sonst unlesbar werden.
    /// </summary>
    private static Dictionary<string, string> ReadValues(XElement root)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in root.Elements("settings").Elements("set"))
        {
            var path = element.Attribute("path")?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            values[path] = element.Attribute("value")?.Value ?? element.Value.Trim();
        }

        return values;
    }

    private static SipTransport? ReadTransport(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "UDP" => SipTransport.Udp,
        "TCP" => SipTransport.Tcp,
        "TLS" => SipTransport.Tls,
        _ => null,
    };

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
