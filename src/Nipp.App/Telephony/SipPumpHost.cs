using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Nipp.Core.Services.Telephony;

namespace Nipp.App.Telephony;

/// <summary>
/// Treibt die Ereignisschleife des SDK (§6, AP3.3).
///
/// <c>Core.Iterate()</c> muss regelmässig gerufen werden, sonst verarbeitet das
/// SDK keine Netzwerkereignisse und feuert keine Callbacks — auf Desktop macht
/// es das <b>nicht</b> selbst.
///
/// <b>Vorgabe aus §6:</b> ein <see cref="DispatcherQueueTimer"/> auf dem
/// UI-Thread mit 20 ms. Damit laufen alle SDK-Callbacks auf dem UI-Thread und
/// es braucht kein Marshalling. Der Preis: nichts Blockierendes in einem
/// Callback (§14.1).
///
/// <b>Warum diese Klasse in Nipp.App liegt und nicht in Nipp.Core:</b>
/// <see cref="DispatcherQueue"/> gehört zu WinUI. Core soll keine
/// UI-Abhängigkeit bekommen und sagt über <see cref="ISipEventPump"/> nur, was
/// es braucht — den Takt liefert die App.
///
/// <b>Warum am App-Lebenszyklus und nicht am Fenster:</b> §10 verlangt, dass
/// das Schliessen des Fensters die App nicht beendet. Die Telefonie läuft im
/// Infobereich weiter, also darf der Takt nicht mit dem Fenster verschwinden.
/// </summary>
public sealed class SipPumpHost : IDisposable
{
    /// <summary>§6 gibt 20 ms vor.</summary>
    private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(20);

    private readonly ISipEventPump _pump;
    private readonly ILogger<SipPumpHost> _logger;
    private DispatcherQueueTimer? _timer;
    private bool _disposed;

    /// <summary>
    /// Zählt Durchläufe, in denen die Schleife länger als das Intervall
    /// gebraucht hat. Ein wachsender Wert bedeutet, dass irgendwo im Callback
    /// blockiert wird — der Fallstrick aus §14.1. Landet in der Diagnose (§9.6).
    /// </summary>
    public long OverrunCount { get; private set; }

    public bool IsRunning => _timer?.IsRunning ?? false;

    public SipPumpHost(ISipEventPump pump, ILogger<SipPumpHost> logger)
    {
        _pump = pump;
        _logger = logger;
    }

    /// <summary>
    /// Startet den Takt. Muss auf dem UI-Thread laufen, weil die
    /// <see cref="DispatcherQueue"/> von dort kommt.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timer is not null)
        {
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "SipPumpHost.Start() muss auf dem UI-Thread laufen — nur dort gibt es eine "
                    + "DispatcherQueue. §6 verlangt ausserdem ausdrücklich den UI-Thread, "
                    + "damit die SDK-Callbacks ohne Marshalling ankommen.");

        _timer = queue.CreateTimer();
        _timer.Interval = PumpInterval;
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _timer.Start();

        PumpLog.Started(_logger, PumpInterval.TotalMilliseconds);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        // Stopwatch und nicht TickCount64: dessen Aufloesung liegt bei rund
        // 15,6 ms, und damit laesst sich ein Takt von 20 ms nicht messen —
        // die Ueberlaufzaehlung war vorher mehr Zufall als Messung.
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            _pump.Pump();
        }
        catch (Exception ex)
        {
            // Eine Ausnahme aus der Ereignisschleife darf den Takt nicht
            // anhalten: sonst steht die Telefonie still, ohne dass es auffällt.
            // Protokollieren und weiterlaufen.
            PumpLog.PumpFailed(_logger, ex);
        }

        var elapsed = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (elapsed > PumpInterval.TotalMilliseconds)
        {
            OverrunCount++;

            // Nicht bei jedem Überlauf melden — das würde das Log fluten und
            // die Lage verschlimmern. Nur bei Zehnerpotenzen.
            if (OverrunCount is 1 or 10 or 100 or 1_000 or 10_000)
            {
                PumpLog.Overrun(_logger, elapsed, OverrunCount);
            }
        }
    }

    /// <summary>Hält den Takt an. Die Telefonie verarbeitet danach nichts mehr.</summary>
    public void Stop()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;

        PumpLog.Stopped(_logger, OverrunCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}

/// <summary>
/// Protokollmeldungen des Takts, quellgeneriert (CA1848). Hier besonders
/// angebracht: der Timer läuft 50-mal pro Sekunde.
/// </summary>
internal static partial class PumpLog
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
        Message = "Ereignisschleife gestartet, Intervall {IntervalMs} ms")]
    public static partial void Started(ILogger logger, double intervalMs);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information,
        Message = "Ereignisschleife gestoppt, {Overruns} Überläufe insgesamt")]
    public static partial void Stopped(ILogger logger, long overruns);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error,
        Message = "Fehler in der Ereignisschleife — der Takt läuft weiter")]
    public static partial void PumpFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning,
        Message = "Ereignisschleife brauchte {ElapsedMs} ms statt 20 ms ({Count}. Überlauf). "
            + "Deutet auf eine blockierende Operation in einem SDK-Callback (§14.1).")]
    public static partial void Overrun(ILogger logger, long elapsedMs, long count);
}
