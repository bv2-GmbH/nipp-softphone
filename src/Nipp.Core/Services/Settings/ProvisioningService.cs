using System.Globalization;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Holt das Provisionierungsprofil und wendet es an (§17, AP8.1, AP8.2).
///
/// Drei Ebenen, in dieser Reihenfolge — jede spätere überschreibt die frühere:
/// <list type="number">
///   <item><b>Factory-Config</b> unter <c>%PROGRAMDATA%\bv2\nipp</c>, mitgeliefert (AP8.1).</item>
///   <item><b>Remote-Profil</b> von der Provisioning-URL, beim Start abgerufen (AP8.2).</item>
///   <item><b>Was der Benutzer eingestellt hat</b> — ausser bei gesperrten Feldern (AP8.4).</item>
/// </list>
///
/// <b>Abweichung von §9 und §17 (ADR-010).</b> Dort steht
/// <c>Core.ProvisioningUri</c>: das SDK holt das Profil selbst und schreibt es
/// in seine eigene <c>linphonerc</c>. Das ist hier nicht umsetzbar, ohne zwei
/// Konfigurationswege nebeneinander zu haben — nipp führt seine Einstellungen
/// in <c>settings.json</c> und überträgt sie über <c>SettingsApplier</c> in den
/// Core. Ein Profil, das direkt in die <c>linphonerc</c> schreibt, würde beim
/// nächsten Speichern der Oberfläche stillschweigend überschrieben, und
/// niemand könnte erklären, warum. Deshalb ein eigenes Format, das denselben
/// Weg nimmt wie jede andere Einstellung.
///
/// <b>Ein Fehler beim Abruf darf den Start nicht verhindern</b> (§17). Diese
/// Klasse wirft nicht; sie meldet über <see cref="LastError"/>, was schiefging.
/// </summary>
public sealed class ProvisioningService : IDisposable
{
    /// <summary>
    /// Wie lange auf den Provisioning-Server gewartet wird. Kurz gehalten: der
    /// Abruf liegt im Startpfad, und ein nicht erreichbarer Server darf nipp
    /// nicht zehn Sekunden lang festhalten.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly SettingsService _settings;
    private readonly PolicyService _policy;
    private readonly SecretStore _secrets;
    private readonly ILogger<ProvisioningService> _logger;
    private readonly HttpClient _http;
    private bool _disposed;

    public ProvisioningService(
        SettingsService settings,
        PolicyService policy,
        SecretStore secrets,
        ILogger<ProvisioningService> logger)
    {
        _settings = settings;
        _policy = policy;
        _secrets = secrets;
        _logger = logger;

        _http = new HttpClient { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("nipp/1.0");
    }

    /// <summary>§10: die mitgelieferte Factory-Config.</summary>
    public static string FactoryConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "bv2",
        "nipp",
        "nipp-factory.xml");

    /// <summary>Was beim letzten Versuch schiefging, oder <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>Name des zuletzt angewendeten Profils, für die Diagnose (§9.6).</summary>
    public string? LastProfileName { get; private set; }

    /// <summary>Wann zuletzt erfolgreich abgerufen wurde.</summary>
    public DateTimeOffset? LastSuccess { get; private set; }

    /// <summary>
    /// Die Adresse der Integrationskonfiguration aus dem zuletzt angewendeten
    /// Profil (§21.3), oder <c>null</c>.
    ///
    /// <b>Hier wird sie nur gemerkt, nicht abgerufen.</b> Der Abruf gehört
    /// hinter den Start: nichts an den Integrationen darf zwischen dem Start
    /// und dem ersten moeglichen Anruf stehen (§21.2). Die App holt sie im
    /// Hintergrund, sobald das Fenster steht.
    /// </summary>
    public string? LastIntegrationsUri { get; private set; }

    /// <summary>
    /// Wendet Factory-Config und Remote-Profil an. Rückgabe sagt, ob etwas
    /// übernommen wurde — nicht, ob alles glatt lief; dafür ist
    /// <see cref="LastError"/> da.
    /// </summary>
    public async Task<bool> ApplyAsync(CancellationToken cancellationToken = default)
    {
        LastError = null;

        var applied = ApplyFactoryConfig();
        applied |= await ApplyRemoteProfileAsync(cancellationToken).ConfigureAwait(false);

        return applied;
    }

    /// <summary>AP8.1: die mitgelieferte Datei, falls vorhanden.</summary>
    private bool ApplyFactoryConfig()
    {
        var path = FactoryConfigPath;

        if (!File.Exists(path))
        {
            // Der Normalfall ausserhalb einer verwalteten Installation.
            ProvisioningLog.NoFactoryConfig(_logger, path);
            return false;
        }

        try
        {
            var xml = File.ReadAllText(path);

            if (!ProvisioningParser.TryParse(xml, out var profile, out var error))
            {
                LastError = $"Die mitgelieferte Konfiguration ist unbrauchbar: {error}";
                ProvisioningLog.FactoryConfigBroken(_logger, path, error ?? "unbekannt");
                return false;
            }

            // Die mitgelieferte Datei ist vertrauenswürdig: sie liegt unter
            // %PROGRAMDATA% und wurde mit der Installation verteilt. Nur sie
            // darf deshalb die Provisioning-Adresse selbst setzen.
            ApplyProfile(profile, trusted: true);
            ProvisioningLog.FactoryConfigApplied(_logger, path, profile.ProfileName ?? "ohne Namen");

            return true;
        }
        catch (IOException ex)
        {
            LastError = $"Die mitgelieferte Konfiguration liess sich nicht lesen: {ex.Message}";
            ProvisioningLog.FactoryConfigBroken(_logger, path, ex.Message);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            LastError = $"Keine Leseberechtigung für die mitgelieferte Konfiguration: {ex.Message}";
            ProvisioningLog.FactoryConfigBroken(_logger, path, ex.Message);
            return false;
        }
    }

    /// <summary>AP8.2: das Profil vom Server, wenn eine URL eingetragen ist.</summary>
    private async Task<bool> ApplyRemoteProfileAsync(CancellationToken cancellationToken)
    {
        if (await FetchRemoteProfileAsync(cancellationToken).ConfigureAwait(false) is not { } profile)
        {
            return false;
        }

        ApplyProfile(profile);
        return true;
    }

    /// <summary>
    /// Holt das Profil vom Server und liest es — <b>ohne</b> es anzuwenden.
    ///
    /// <b>Warum der Abruf vom Anwenden getrennt ist.</b> Das Anwenden
    /// speichert, und am Speichern hängt eine Kette, die bis in den
    /// SDK-Core reicht. Nach einem <c>await</c> mit
    /// <c>ConfigureAwait(false)</c> läuft die Fortsetzung auf einem
    /// Threadpool-Thread — SDK-Aufrufe gehören aber auf den Thread der
    /// Ereignisschleife (§6). Wer zur Laufzeit abruft, holt hier und wendet
    /// dort an, wo es hingehört.
    ///
    /// Im Startpfad ist das ohne Belang: dort gibt es noch keine Abonnenten
    /// von <see cref="SettingsService.Changed"/>, weil die Telefonie erst
    /// danach hochfährt.
    /// </summary>
    /// <returns>Das gelesene Profil, oder <c>null</c> — dann sagt <see cref="LastError"/> warum.</returns>
    public async Task<ProvisioningProfile?> FetchRemoteProfileAsync(CancellationToken cancellationToken = default)
    {
        var uri = _settings.Current.Advanced.ProvisioningUri;

        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            LastError = $"Die Provisioning-Adresse '{uri}' ist keine gültige http- oder https-Adresse.";
            ProvisioningLog.UriInvalid(_logger, uri);
            return null;
        }

        if (parsed.Scheme == Uri.UriSchemeHttp)
        {
            // Bis zum 05.09.2026 stand hier nur eine Protokollzeile, und der
            // Abruf lief. Das ist zu wenig: ein Profil bestimmt Konten,
            // Zugangsdaten und — vor dieser Änderung — die Adresse künftiger
            // Profile. Wer im Netz zwischen Arbeitsplatz und Server sitzt,
            // übernimmt damit dauerhaft die Telefonie. Deshalb https, mit
            // einer ausdrücklichen Ausnahme für Anlagen ohne TLS (ADR-012).
            if (!_settings.Current.Advanced.AllowInsecureProvisioning)
            {
                LastError = $"Das Provisionierungsprofil soll unverschlüsselt von {parsed.Host} "
                    + "geholt werden. Das ist abgeschaltet, weil ein Profil Konten und "
                    + "Zugangsdaten festlegt. Entweder https auf dem Server einrichten oder "
                    + "in den Einstellungen unter «Erweitert» unverschlüsselte Provisionierung "
                    + "ausdrücklich erlauben.";
                ProvisioningLog.InsecureUriRejected(_logger, parsed.Host);
                return null;
            }

            ProvisioningLog.UriNotEncrypted(_logger, parsed.Host);
        }

        try
        {
            var xml = await _http.GetStringAsync(parsed, cancellationToken).ConfigureAwait(false);

            if (!ProvisioningParser.TryParse(xml, out var profile, out var error))
            {
                LastError = $"Das Profil von {parsed.Host} ist unbrauchbar: {error}";
                ProvisioningLog.ProfileBroken(_logger, parsed.Host, error ?? "unbekannt");
                return null;
            }

            LastProfileName = profile.ProfileName;
            LastSuccess = DateTimeOffset.UtcNow;

            ProvisioningLog.ProfileApplied(
                _logger, parsed.Host, profile.ProfileName ?? "ohne Namen", profile.Accounts.Count);

            return profile;
        }
        catch (HttpRequestException ex)
        {
            // §17: „Fehler beim Abruf dürfen den Start nicht verhindern —
            // dann gilt die lokale Config, mit Hinweis im UI."
            LastError = $"Das Provisioning-Profil ist nicht erreichbar ({ex.Message}). "
                + "nipp arbeitet mit den zuletzt gespeicherten Einstellungen weiter.";
            ProvisioningLog.FetchFailed(_logger, parsed.Host, ex.Message);
            return null;
        }
        catch (TaskCanceledException)
        {
            LastError = $"Der Provisioning-Server {parsed.Host} antwortet nicht "
                + $"(mehr als {Timeout.TotalSeconds:0} Sekunden). "
                + "nipp arbeitet mit den zuletzt gespeicherten Einstellungen weiter.";
            ProvisioningLog.FetchTimedOut(_logger, parsed.Host);
            return null;
        }
    }

    /// <summary>
    /// Überträgt ein Profil in die Einstellungen.
    ///
    /// Konten werden <b>ersetzt</b>, nicht ergänzt: ein Profil, das Konten
    /// nennt, bestimmt die Kontoliste. Sonst sammelten sich bei jedem Wechsel
    /// des Profils Altlasten an, die niemand mehr zuordnen kann. Nennt es
    /// keine, bleibt die bestehende Liste unberührt.
    /// </summary>
    /// <remarks>
    /// Heisst bewusst nicht <c>Apply</c>: der Analyzer haelt jede Methode
    /// <c>X</c> neben einem <c>XAsync</c> fuer deren blockierende Fassung
    /// (CA1849) — was hier falsch waere, denn das Uebernehmen eines fertigen
    /// Profils braucht kein Netz.
    /// </remarks>
    /// <param name="profile">Das gelesene Profil.</param>
    /// <param name="trusted">
    /// Ob das Profil aus der Auslieferung stammt (Factory-Config unter
    /// %PROGRAMDATA%) und nicht über das Netz kam. Nur ein vertrauenswürdiges
    /// Profil darf die Provisioning-Adresse selbst festlegen — siehe
    /// <see cref="ApplyValues"/>.
    /// </param>
    public void ApplyProfile(ProvisioningProfile profile, bool trusted = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _policy.Apply(profile.LockedFields);

        if (profile.IntegrationsUri is { Length: > 0 } integrations)
        {
            LastIntegrationsUri = integrations;
        }

        if (profile.IsEmpty)
        {
            return;
        }

        var current = _settings.Current;
        var updated = current;

        // ADR-054: auch fuer die Listen gilt, was der Benutzer angefasst hat.
        // Bis zum 13.09.2026 ersetzte das Profil die Kontenliste vollstaendig
        // — ein von Hand angelegtes zweites Konto war nach jedem Start weg,
        // lautlos.
        if (profile.Accounts.Count > 0 && DarfSetzen(current, ProvisioningCatalog.AccountsPath))
        {
            updated = updated with { Accounts = [.. profile.Accounts.Select(ToAccount)] };

            foreach (var account in profile.Accounts.Where(static a => !string.IsNullOrEmpty(a.Password)))
            {
                // §11: das Passwort landet über DPAPI im SecretStore, nicht in
                // settings.json — auch wenn es im Profil im Klartext stand.
                _secrets.Set($"sip:{account.Username}@{account.Domain}", account.Password!);
            }
        }

        if (profile.Team.Count > 0 && DarfSetzen(current, ProvisioningCatalog.TeamPath))
        {
            // Die Gruppen kommen aus dem Profil, in seiner Reihenfolge — ein
            // Profil, das Nebenstellen setzt, setzt damit auch die Gruppen
            // (ADR-041). Normalisiert, weil sonst die gespeicherte Reihenfolge
            // von der angezeigten abweicht und ein Ziehvorgang in der
            // Kontaktliste eine Ordnung zurückschriebe, die niemand
            // hergestellt hat.
            var (gruppen, mitglieder) = TeamGroups.Normalize(
                TeamGroups.Collect(null, profile.Team),
                profile.Team);

            updated = updated with
            {
                Contacts = updated.Contacts with { Groups = gruppen, Team = mitglieder },
            };
        }

        updated = ApplyValues(updated, profile.Values, trusted);

        if (!ReferenceEquals(updated, current))
        {
            _settings.SaveFromProfile(updated);
        }
    }

    /// <summary>
    /// Macht aus einem Profilkonto ein Konto der Einstellungen.
    ///
    /// <b>Zum Passwort.</b> Nennt das Profil keines — der in
    /// <c>docs/provisioning.md</c> ausdrücklich empfohlene Weg, weil das
    /// Profil auf einem Webserver liegt —, dann gilt das bereits über DPAPI
    /// hinterlegte. Ohne diese Zeile ersetzte die Provisionierung die
    /// Kontoliste durch Konten mit <b>leerem</b> Passwort; der Start meldete
    /// sich damit an, und ein so provisioniertes Konto registrierte sich nie.
    /// Der Fehler fiel nicht auf, weil das Geheimnis im SecretStore
    /// unangetastet blieb — nur gelesen hat es niemand mehr.
    /// </summary>
    private SipAccountSettings ToAccount(ProvisionedAccount account)
    {
        var identity = $"sip:{account.Username}@{account.Domain}";
        var password = account.Password;

        if (string.IsNullOrEmpty(password) && _secrets.Get(identity) is { Length: > 0 } stored)
        {
            password = stored;
            ProvisioningLog.PasswordFromStore(_logger, identity);
        }

        return new SipAccountSettings
        {
            Username = account.Username,
            Domain = account.Domain,
            Password = password ?? string.Empty,
            AuthUserId = account.AuthUserId,
            DisplayName = account.DisplayName,
            Transport = account.Transport ?? SipTransport.Tls,
            OutboundProxy = account.OutboundProxy,
            ExpiresSeconds = account.ExpiresSeconds ?? 600,
        };
    }

    /// <summary>
    /// Überträgt die flachen Wertepaare.
    ///
    /// Ein Profil darf nur nennen, was es kennt — unbekannte Pfade werden
    /// übergangen und protokolliert. Ein Tippfehler im Profil soll auffallen,
    /// aber nicht die übrigen Vorgaben verwerfen.
    /// </summary>
    /// <summary>
    /// Ein Klingelton aus einem Profil (§9.4, §16.2).
    ///
    /// <para><b>Ein absoluter Pfad nur aus der mitgelieferten Konfiguration.</b>
    /// Ein Profil aus dem Netz, das einen beliebigen Pfad setzen darf, kann
    /// darin eine UNC-Freigabe hinterlegen — und dann fragt nipp beim nächsten
    /// Klingeln einen fremden Server, mit den Anmeldedaten des angemeldeten
    /// Benutzers im Gepäck. Ein relativer Pfad kann dagegen nur ins eigene
    /// Installationsverzeichnis zeigen; deshalb ist er auch aus dem Netz in
    /// Ordnung, und deshalb speichert die Oberfläche mitgelieferte Klänge
    /// relativ (siehe <c>NippSounds</c>).</para>
    ///
    /// <para><c>..</c> ist damit ausgeschlossen: sonst wäre jeder relative
    /// Pfad wieder ein beliebiger.</para>
    /// </summary>
    private string? Ringtone(string raw, bool trusted)
    {
        var wert = raw.Trim();

        if (wert.Length == 0)
        {
            // Leer heisst „Standard" — das ist eine gueltige Angabe.
            return null;
        }

        var absolut = Path.IsPathRooted(wert) || wert.StartsWith(@"\\", StringComparison.Ordinal);
        var hinaus = wert.Split('/', '\\').Contains("..");

        if (hinaus || (absolut && !trusted))
        {
            ProvisioningLog.RingtoneNotAllowedRemotely(_logger);
            return null;
        }

        return wert;
    }

    /// <summary>
    /// Ob das Profil diese Liste setzen darf (ADR-054).
    ///
    /// <para>Dieselbe Regel wie fuer Einzelwerte: hat der Benutzer sie
    /// angefasst, bleibt seine Fassung — ausser der Pfad ist gesperrt.</para>
    /// </summary>
    private bool DarfSetzen(NippSettings current, string pfad)
    {
        if (_policy.IsLocked(pfad))
        {
            return true;
        }

        if (!current.UserOverrides.Contains(pfad, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        ProvisioningLog.UserValueKept(_logger, pfad);
        return false;
    }

    private NippSettings ApplyValues(
        NippSettings settings,
        IReadOnlyDictionary<string, string> values,
        bool trusted)
    {
        foreach (var (path, raw) in values)
        {
            var key = path.ToLowerInvariant();

            // Ein Profil aus dem Netz darf die Adresse künftiger Profile nicht
            // umschreiben: wer einmal antworten konnte, würde damit dauerhaft
            // bestimmen, woher dieser Arbeitsplatz seine Konten bezieht. Die
            // mitgelieferte Konfiguration darf es — sie kommt mit der
            // Installation (ADR-012).
            if (!trusted && ProvisioningCatalog.Find(key) is { FactoryOnly: true })
            {
                ProvisioningLog.SettingNotAllowedRemotely(_logger, path);
                continue;
            }

            // ADR-039: das Update-Token ist ein Geheimnis und gehoert nicht in
            // settings.json, sondern ueber DPAPI in den SecretStore — derselbe
            // Weg wie das SIP-Passwort weiter oben. Deshalb steht es vor dem
            // switch: der arbeitet mit Werten, hier faellt ein Seiteneffekt an.
            //
            // Nur aus einer vertrauenswuerdigen Quelle. Ein Profil aus dem Netz,
            // das das Token setzen koennte, bestimmte damit, aus welchem Repo
            // dieser Arbeitsplatz seine naechste Fassung bezieht.
            if (string.Equals(key, "update.token", StringComparison.Ordinal))
            {
                if (!trusted)
                {
                    ProvisioningLog.SettingNotAllowedRemotely(_logger, path);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    _secrets.Set(Updates.VelopackUpdateGateway.TokenKey, raw);
                }

                continue;
            }

            var eintrag = ProvisioningCatalog.Find(key);

            if (eintrag is null)
            {
                ProvisioningLog.UnknownSetting(_logger, path);
                continue;
            }

            // ADR-054: der Benutzer gewinnt. Was er selbst eingestellt hat,
            // bleibt — ausser der Pfad ist gesperrt; dann ist das Profil der
            // Weg des Administrators, seinen Wert zurueckzuholen.
            if (!_policy.IsLocked(key)
                && settings.UserOverrides.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                ProvisioningLog.UserValueKept(_logger, path);
                continue;
            }

            // Der Klingelton traegt seine eigene Pruefung: ein Profil aus dem
            // Netz darf keinen absoluten Pfad setzen. Sie steht hier und nicht
            // im Katalog, weil sie das Vertrauen in die Quelle braucht.
            var wert = string.Equals(key, "audio.ringtone", StringComparison.Ordinal)
                ? Ringtone(raw, trusted)
                : raw;

            if (wert is null)
            {
                continue;
            }

            settings = eintrag.Apply(settings, wert) ?? settings;

            // Eine Sperre holt den Profilwert zurueck: die Markierung des
            // Benutzers faellt weg, sonst gaelte sie beim naechsten Start
            // wieder.
            if (_policy.IsLocked(key)
                && settings.UserOverrides.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                settings = settings with
                {
                    UserOverrides = [.. settings.UserOverrides.Where(
                        u => !string.Equals(u, key, StringComparison.OrdinalIgnoreCase))],
                };
            }

        }

        return settings;
    }

    private static int? Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool? Bool(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "1" or "JA" or "YES" or "EIN" => true,
        "FALSE" or "0" or "NEIN" or "NO" or "AUS" => false,
        _ => null,
    };

    private static List<string> Split(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
