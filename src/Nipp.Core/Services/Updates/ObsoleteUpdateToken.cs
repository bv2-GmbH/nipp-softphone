using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Das Update-Token, das es nicht mehr gibt (ADR-083, abgelöst ADR-039).
///
/// <para>Solange das Repo privat war, kam ein GitHub-Zugriffstoken über die
/// Provisionierung an den Arbeitsplatz und lag über DPAPI im
/// <see cref="SecretStore"/>. Seit dem 14.09.2026 ist das Repo öffentlich,
/// und seit dem 26.11.2026 ist entschieden, dass das Token entfällt.</para>
///
/// <para><b>Wichtiger als das Wegfallen ist, dass es nie mehr gesendet
/// wird.</b> Schickt ein Client ein <i>widerrufenes</i> Token mit, antwortet
/// GitHub auch für öffentliche Ressourcen mit 401 «Bad credentials» — die
/// Update-Prüfung bräche auf jedem Arbeitsplatz still, der noch eines
/// gespeichert hat. Deshalb liest nichts im Programm den Eintrag noch; diese
/// Klasse räumt ihn nur weg.</para>
/// </summary>
public static partial class ObsoleteUpdateToken
{
    /// <summary>
    /// Der Schlüssel, unter dem das Token früher im <see cref="SecretStore"/>
    /// lag. Nur noch zum Aufräumen — gelesen wird er nirgends.
    /// </summary>
    public const string Key = "update:github-token";

    /// <summary>
    /// Entfernt einen vorhandenen Eintrag. Einmalig wirksam: danach gibt es
    /// nichts mehr zu entfernen, und der Aufruf bleibt still. Wirft nie —
    /// ein Aufräumen darf den Start nicht kosten.
    /// </summary>
    /// <returns><c>true</c>, wenn ein Eintrag entfernt wurde.</returns>
    public static bool Remove(SecretStore secrets, ILogger logger)
    {
        try
        {
            if (string.IsNullOrEmpty(secrets.Get(Key)))
            {
                return false;
            }

            secrets.Remove(Key);
            Removed(logger);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RemoveFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    // Ohne den Wert — auch nicht gekürzt (§21.2).
    [LoggerMessage(EventId = 4010, Level = LogLevel.Information,
        Message = "Ein gespeichertes Update-Token wurde entfernt. nipp braucht keines mehr: "
            + "das Repo ist oeffentlich, und gesendet wird nie eines.")]
    private static partial void Removed(ILogger logger);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Warning,
        Message = "Das gespeicherte Update-Token liess sich nicht entfernen ({Kind}). "
            + "Es wird trotzdem nie gesendet.")]
    private static partial void RemoveFailed(ILogger logger, string kind);
}
