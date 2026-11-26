using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Protokollmeldungen der Provisionierung.
///
/// Kein Passwort und keine vollstaendige URL in den Meldungen — der Hostname
/// reicht, um ein Problem einzukreisen, und ein Profilpfad kann eine Kennung
/// enthalten, die nicht ins Diagnosepaket gehoert (§9.6, §11).
/// </summary>
internal static partial class ProvisioningLog
{
    [LoggerMessage(EventId = 3500, Level = LogLevel.Debug,
        Message = "Keine mitgelieferte Konfiguration unter {Path} — das ist der Normalfall")]
    public static partial void NoFactoryConfig(ILogger logger, string path);

    [LoggerMessage(EventId = 3501, Level = LogLevel.Information,
        Message = "Mitgelieferte Konfiguration angewendet ({Path}, Profil {Profile})")]
    public static partial void FactoryConfigApplied(ILogger logger, string path, string profile);

    [LoggerMessage(EventId = 3502, Level = LogLevel.Warning,
        Message = "Mitgelieferte Konfiguration {Path} ist unbrauchbar: {Reason}")]
    public static partial void FactoryConfigBroken(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 3510, Level = LogLevel.Information,
        Message = "Profil von {Host} angewendet ({Profile}, {Accounts} Konten)")]
    public static partial void ProfileApplied(ILogger logger, string host, string profile, int accounts);

    [LoggerMessage(EventId = 3511, Level = LogLevel.Warning,
        Message = "Profil von {Host} ist unbrauchbar: {Reason}")]
    public static partial void ProfileBroken(ILogger logger, string host, string reason);

    [LoggerMessage(EventId = 3512, Level = LogLevel.Warning,
        Message = "Profil von {Host} nicht abrufbar: {Reason}. Die lokalen Einstellungen gelten weiter.")]
    public static partial void FetchFailed(ILogger logger, string host, string reason);

    // L9 (LOGBEFUNDE-PLAN): ein Arbeitsplatz hatte die Adresse einer Kontaktsuche
    // eingetragen und las bei jedem Start nur "404 (Not Found)". Bei 404 und 410
    // liegt unter der Adresse vermutlich gar kein Profil — das sagt diese Zeile.
    // Bei allem anderen (Netz, 5xx) kann eines existieren, dort bleibt FetchFailed.
    [LoggerMessage(EventId = 3521, Level = LogLevel.Warning,
        Message = "Unter der Adresse von {Host} antwortet der Server mit {Status} — dort liegt "
            + "vermutlich gar kein Provisionierungsprofil. Die Adresse in den Einstellungen "
            + "pruefen. Die lokalen Einstellungen gelten weiter.")]
    public static partial void ProfileNotFound(ILogger logger, string host, int status);

    [LoggerMessage(EventId = 3513, Level = LogLevel.Warning,
        Message = "Provisioning-Server {Host} antwortet nicht. Die lokalen Einstellungen gelten weiter.")]
    public static partial void FetchTimedOut(ILogger logger, string host);

    [LoggerMessage(EventId = 3514, Level = LogLevel.Warning,
        Message = "Die Provisioning-Adresse '{Uri}' ist keine gueltige http- oder https-Adresse")]
    public static partial void UriInvalid(ILogger logger, string uri);

    [LoggerMessage(EventId = 3515, Level = LogLevel.Warning,
        Message = "Das Profil wird unverschluesselt von {Host} geholt. Es kann Zugangsdaten enthalten — "
            + "https waere hier richtig.")]
    public static partial void UriNotEncrypted(ILogger logger, string host);

    [LoggerMessage(EventId = 3516, Level = LogLevel.Warning,
        Message = "Das Profil nennt die unbekannte Einstellung '{Path}'. Sie wird uebergangen — "
            + "vermutlich ein Tippfehler oder eine neuere Schemaversion.")]
    public static partial void UnknownSetting(ILogger logger, string path);

    [LoggerMessage(EventId = 3517, Level = LogLevel.Warning,
        Message = "Das Profil von {Host} soll unverschluesselt geholt werden. Abgelehnt: ein Profil "
            + "legt Konten, Zugangsdaten und die Adresse kuenftiger Profile fest. In den "
            + "Einstellungen unter 'Erweitert' ausdruecklich erlaubbar.")]
    public static partial void InsecureUriRejected(ILogger logger, string host);

    [LoggerMessage(EventId = 3518, Level = LogLevel.Warning,
        Message = "Das Profil aus dem Netz will '{Path}' setzen. Uebergangen — diese Einstellung "
            + "darf nur die mitgelieferte Konfiguration aendern.")]
    public static partial void SettingNotAllowedRemotely(ILogger logger, string path);

    [LoggerMessage(EventId = 3519, Level = LogLevel.Information,
        Message = "Passwort fuer {Identity} kommt aus der lokalen Ablage — das Profil nennt keines")]
    public static partial void PasswordFromStore(ILogger logger, string identity);

    // Ohne Pfad in der Meldung: er stammt aus dem Profil und koennte auf eine
    // Freigabe zeigen, die hier nicht auch noch protokolliert gehoert.
    [LoggerMessage(EventId = 3520, Level = LogLevel.Warning,
        Message = "Der Klingelton aus dem Profil wurde uebergangen: er zeigt aus dem "
            + "Installationsverzeichnis hinaus. Ein Profil aus dem Netz darf nur einen "
            + "mitgelieferten Klang setzen (Paragraph 9.4).")]
    public static partial void RingtoneNotAllowedRemotely(ILogger logger);

    // ADR-083. Ohne den Wert: bei update.token war er ein Geheimnis.
    [LoggerMessage(EventId = 3522, Level = LogLevel.Information,
        Message = "Das Profil nennt '{Path}'. Das wird nicht mehr gebraucht und ist ohne Wirkung "
            + "— der Schluessel kann aus dem Profil entfernt werden (ADR-083).")]
    public static partial void ObsoleteSetting(ILogger logger, string path);

    // ADR-054: der Benutzer gewinnt. Die Zeile ist die einzige Spur dafuer,
    // dass ein Profilwert bewusst nicht angewendet wurde — ohne sie waere
    // "das Profil wirkt nicht" nicht von "das Profil kam nicht an" zu
    // unterscheiden.
    [LoggerMessage(EventId = 3299, Level = LogLevel.Debug,
        Message = "Profilwert fuer {Path} uebersprungen — vom Benutzer eingestellt. "
            + "Ein gesperrtes Feld wuerde ihn zurueckholen.")]
    public static partial void UserValueKept(ILogger logger, string path);
}
