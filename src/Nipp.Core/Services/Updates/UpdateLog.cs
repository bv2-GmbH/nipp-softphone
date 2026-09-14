using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Protokollmeldungen der Update-Prüfung (ADR-039).
///
/// <para>Hier ist die Regel aus §21.2 leicht einzuhalten, weil es nichts
/// Personenbezogenes gibt: protokolliert werden Fassungen, Kanäle und
/// Fehlerarten. <b>Kein Token</b> — auch nicht gekürzt, auch nicht als
/// „vorhanden/fehlt" mit Länge.</para>
///
/// <para>EventId-Bereich 4000. Die Bereiche bis 3919 sind belegt.</para>
/// </summary>
internal static partial class UpdateLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug,
        Message = "Update-Pruefung beim Start ist abgeschaltet")]
    public static partial void CheckDisabled(ILogger logger);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug,
        Message = "Keine Update-Ablage — nipp laeuft nicht installiert. Es gibt nichts zu aktualisieren")]
    public static partial void NotInstalled(ILogger logger);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Update verfuegbar: {Version} aus {Channel} (Rueckstufung: {IsDowngrade})")]
    public static partial void Found(ILogger logger, string version, string channel, bool isDowngrade);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information,
        Message = "Kein Update in {Channel} — die laufende Fassung ist aktuell")]
    public static partial void UpToDate(ILogger logger, string channel);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information,
        Message = "Update-Pruefung nicht moeglich ({Kind}): {Reason}")]
    public static partial void CheckFailed(ILogger logger, string kind, string reason);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Information,
        Message = "Update {Version} geladen, bereit zum Neustart")]
    public static partial void Downloaded(ILogger logger, string version);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Warning,
        Message = "Update konnte nicht geladen werden ({Kind}): {Reason}")]
    public static partial void DownloadFailed(ILogger logger, string kind, string reason);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Information,
        Message = "Update {Version} wird angewandt, nipp startet neu")]
    public static partial void Applying(ILogger logger, string version);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Information,
        Message = "Update nicht angewandt: es laeuft ein Gespraech")]
    public static partial void ApplyBlockedByCall(ILogger logger);

    [LoggerMessage(EventId = 4009, Level = LogLevel.Debug,
        Message = "Update-Ablage nicht lesbar ({Kind}) — Pruefung uebersprungen")]
    public static partial void GatewayUnavailable(ILogger logger, string kind);
}
