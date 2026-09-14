using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Meldungen der <see cref="SdkLogBridge"/>.
///
/// Eigene Datei ohne <c>using Linphone</c>: der <c>[LoggerMessage]</c>-Generator
/// bricht sonst an der Namensfalle <c>Linphone.LogLevel</c> gegen
/// <c>Microsoft.Extensions.Logging.LogLevel</c> mit sieben Meldungen, von
/// denen keine auf die Ursache zeigt (docs/sdk-api-notes.md).
/// </summary>
internal static partial class SdkLogBridgeLog
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
        Message = "SDK-Protokoll angeschlossen")]
    public static partial void Attached(ILogger logger);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning,
        Message = "SDK-Protokoll nicht anschliessbar: {Reason}. Telefonie laeuft, es fehlt nur die Diagnose.")]
    public static partial void AttachFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information,
        Message = "SDK-Protokollstufe nach Einstellung '{Verbosity}' gesetzt")]
    public static partial void VerbosityApplied(ILogger logger, string verbosity);

    /// <summary>
    /// Eine Zeile aus dem SDK. Das Level kommt zur Laufzeit — der Generator
    /// erlaubt das, wenn es der erste Parameter nach dem Logger ist.
    /// </summary>
    [LoggerMessage(EventId = 2103, Message = "[{Domain}] {Text}")]
    public static partial void SdkMessage(ILogger logger, LogLevel level, string domain, string text);
}
