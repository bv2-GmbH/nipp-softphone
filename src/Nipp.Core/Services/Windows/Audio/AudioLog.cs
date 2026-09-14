using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Windows.Audio;

/// <summary>
/// Protokollmeldungen rund um die Audio-Sitzungen (CA1848).
/// </summary>
internal static partial class AudioLog
{
    /// <summary>
    /// Die Sitzungen liessen sich nicht lesen.
    ///
    /// <para>Auf Debug und nicht höher: nipp verhält sich dann wie vor
    /// ADR-068, meldet also seine Zustände wie immer. Es ist kein Schaden,
    /// aber es erklärt, warum ein fremdes Gespräch doch getroffen wurde.</para>
    /// </summary>
    [LoggerMessage(EventId = 3130, Level = LogLevel.Debug,
        Message = "Audio-Sitzungen nicht lesbar ({Grund}) — das Geraet gilt als frei")]
    public static partial void SessionsUnavailable(ILogger logger, string grund);
}
