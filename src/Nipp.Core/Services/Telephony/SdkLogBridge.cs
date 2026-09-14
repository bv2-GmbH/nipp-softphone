using Linphone;
using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Settings;

// Namensfalle (docs/sdk-api-notes.md): Linphone.LogLevel und
// Microsoft.Extensions.Logging.LogLevel heissen gleich. Beide Aliasse machen
// die Absicht an jeder Stelle eindeutig.
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using SdkLogLevel = Linphone.LogLevel;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Leitet die Meldungen des Linphone SDK in das Protokoll von nipp (§4, §9.6).
///
/// <b>Warum das fehlte und warum es wichtig ist.</b> Der Kommentar an
/// <c>App.CreateLogger</c> sprach von „zwei Ströme, ein gemeinsamer
/// Log-Ordner" — der zweite Strom, das SDK, wurde aber nie angeschlossen. Was
/// das SDK auf der Leitung sieht (SIP-Nachrichten, Codec-Verhandlung,
/// Audiogeräte), landete nirgends. Die Frage „was hat die Anlage auf unseren
/// SUBSCRIBE geantwortet?" war damit nicht zu beantworten, obwohl das SDK die
/// Antwort die ganze Zeit hatte.
///
/// Die Lautstärke hängt an <see cref="LogVerbosity"/> aus den Einstellungen:
/// <list type="bullet">
///   <item><b>Debug</b> — SIP-Nachrichten im Klartext (Level <c>Message</c>
///   des SDK; <c>Debug</c> und <c>Trace</c> würden das Protokoll mit
///   Pufferzuständen fluten und bleiben aus).</item>
///   <item><b>Info</b> und <b>Aus</b> — nur Warnungen und Fehler des SDK. Ein
///   SDK-Fehler gehört ins Protokoll, egal was eingestellt ist; sonst fehlt
///   beim Diagnosepaket genau die Zeile, um die es geht.</item>
/// </list>
/// </summary>
public sealed class SdkLogBridge
{
    private readonly ILogger<SdkLogBridge> _logger;
    private bool _attached;

    public SdkLogBridge(ILogger<SdkLogBridge> logger) => _logger = logger;

    /// <summary>
    /// Hängt sich an den <c>LoggingService</c> des SDK. Der ist ein Singleton
    /// der Factory — es braucht keinen Core dafür, aber die Factory muss
    /// geladen sein.
    /// </summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        try
        {
            LoggingService.Instance.Listener.OnLogMessageWritten = OnLogMessageWritten;
            _attached = true;

            SdkLogBridgeLog.Attached(_logger);
        }
        catch (Exception ex)
        {
            // Ohne SDK-Protokoll telefoniert nipp weiter; es fehlt dann die
            // Diagnose, nicht die Funktion.
            SdkLogBridgeLog.AttachFailed(_logger, ex.Message);
        }
    }

    /// <summary>Stellt die Lautstärke des SDK nach der Einstellung ein.</summary>
    public void SetVerbosity(LogVerbosity verbosity)
    {
        try
        {
            // Die Eigenschaft ist im Wrapper nur setzbar, und sie setzt eine
            // Untergrenze: alles ab diesem Level wird gemeldet.
            LoggingService.Instance.LogLevel = verbosity == LogVerbosity.Debug
                ? SdkLogLevel.Message
                : SdkLogLevel.Warning;

            SdkLogBridgeLog.VerbosityApplied(_logger, verbosity.ToString());
        }
        catch (Exception ex)
        {
            SdkLogBridgeLog.AttachFailed(_logger, ex.Message);
        }
    }

    private void OnLogMessageWritten(LoggingService service, string domain, SdkLogLevel level, string message)
    {
        // Das SDK schliesst seine Zeilen oft mit einem Zeilenumbruch ab; im
        // Protokoll gäbe das Leerzeilen.
        var text = message.TrimEnd('\r', '\n');

        if (text.Length == 0)
        {
            return;
        }

        SdkLogBridgeLog.SdkMessage(_logger, Map(level), domain ?? "sdk", LogMasking.SipLine(text));
    }

    /// <summary>
    /// SDK-Level auf .NET-Level. <c>Message</c> ist das normale Betriebslevel
    /// des SDK und landet bewusst auf <c>Debug</c>: es ist Diagnose, nicht
    /// Betrieb — und §9.6 nennt genau diese Stufe.
    /// </summary>
    private static MsLogLevel Map(SdkLogLevel level) => level switch
    {
        SdkLogLevel.Fatal => MsLogLevel.Critical,
        SdkLogLevel.Error => MsLogLevel.Error,
        SdkLogLevel.Warning => MsLogLevel.Warning,
        SdkLogLevel.Message => MsLogLevel.Debug,
        _ => MsLogLevel.Trace,
    };
}
