using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nipp.Core.Services.Telephony;
using Windows.Networking.Connectivity;

// <b>Ein Alias, und er ist noetig:</b> der WinRT-Typ
// Windows.Networking.Connectivity.NetworkInformation heisst genau wie der
// .NET-Namensraum System.Net.NetworkInformation. Ohne Alias ist jede
// Verwendung mehrdeutig — dieselbe Falle wie Nipp.Core gegen Linphone.Core.
using Netz = System.Net.NetworkInformation;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Meldet der Telefonie, wenn sich Netzwerk oder Energiezustand ändern
/// (AP3.6, §9.2, §10).
///
/// Zwei Ereignisse, ein Zweck: die Registrierung muss erneuert werden, sobald
/// die Verbindung wieder steht. Ohne das bleibt nipp nach einem Wechsel
/// WLAN→VPN oder nach dem Aufwachen scheinbar registriert, ist aber für die
/// Anlage weg — der Benutzer merkt es erst, wenn ein Anruf nicht ankommt.
///
/// <b>Entprellung:</b> Windows meldet bei einem einzigen Wechsel oft mehrere
/// Ereignisse in schneller Folge (Adapter runter, Adapter rauf, Adresse neu).
/// Jedes davon einzeln an das SDK durchzureichen erzeugt eine Kette von
/// Neuregistrierungen, die die Anlage als Anmeldesturm sieht. Deshalb wird
/// zusammengefasst.
/// </summary>
public sealed class ConnectivityMonitor : IDisposable
{
    /// <summary>
    /// Wartezeit, bevor ein Wechsel weitergereicht wird. Lang genug, damit die
    /// üblichen Mehrfachmeldungen zusammenfallen, kurz genug, dass ein
    /// Benutzer die Neuregistrierung nicht abwartet.
    /// </summary>
    public static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromSeconds(2);

    private readonly ISipService _sip;
    private readonly ILogger<ConnectivityMonitor> _logger;

    /// <summary>
    /// Woher der Netzzustand kommt. Im Betrieb die WinRT-Abfrage; in Tests
    /// eine Attrappe — anders liesse sich der Befund vom 08.09.2026 nicht
    /// nachstellen, denn der hängt genau daran, dass sich der Zustand
    /// zwischen Ereignis und Meldung ändert.
    /// </summary>
    private readonly Func<bool> _isInternetAvailable;

    /// <summary>
    /// Die lokalen Adressen, mit denen gesprochen wird. Im Betrieb die echte
    /// Aufzählung; in Tests eine Attrappe — sonst hinge der Befund vom
    /// 13.09.2026 an der Netzlage der Maschine, auf der der Test läuft.
    /// </summary>
    private readonly Func<IReadOnlyList<string>> _localAddresses;

    private readonly TimeSpan _debounceDelay;
    // System.Threading.Lock gibt es erst ab .NET 9; §4 legt .NET 8 LTS fest.
    private readonly object _gate = new();

    /// <summary>
    /// Der Thread, auf dem die Ereignisschleife läuft (§6).
    ///
    /// <b>Warum das nötig ist.</b> Beide Ereignisquellen melden sich auf
    /// fremden Threads: <c>NetworkStatusChanged</c> auf einem Threadpool-Thread,
    /// <c>PowerModeChanged</c> auf dem Thread von <c>SystemEvents</c>. Von dort
    /// aus <c>Core.NetworkReachable</c> zu setzen, während der UI-Thread
    /// <c>Iterate()</c> ruft, ist ein Zugriff auf nativen Zustand aus zwei
    /// Threads — das quittiert das SDK nicht mit einer Ausnahme, sondern
    /// gelegentlich mit einem Absturz im nativen Code.
    ///
    /// <c>SynchronizationContext</c> statt <c>DispatcherQueue</c>: Nipp.Core
    /// bleibt ohne UI-Abhängigkeit (§6). Denselben Weg geht
    /// <c>ShellViewModel</c>.
    /// </summary>
    private SynchronizationContext? _uiContext;

    /// <summary>
    /// Laesst nur eine Meldung zur Zeit an das SDK. Ohne ihn liefen bei
    /// einem Dock-Wechsel vier Meldungen gleichzeitig, und die aelteste kam
    /// zuletzt an.
    /// </summary>
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    private CancellationTokenSource? _pending;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Die Netzlage, die dem SDK zuletzt gemeldet wurde — <c>null</c>, solange
    /// nichts gemeldet wurde.
    ///
    /// <para><b>Warum es dieses Feld gibt.</b> Ein Adapter, der seine Adresse
    /// selbst wechselt, erzeugt Netzereignisse, ohne dass sich für die
    /// Telefonie etwas ändert. Gemessen am 13.09.2026: neben dem WLAN lief ein
    /// Mobilfunkadapter, der binnen Minuten von 10.26.178.37 auf 10.255.230.35
    /// sprang. <b>Jedes dieser Ereignisse meldete «erreichbar=true» an ein SDK,
    /// dem das längst gesagt worden war</b> — und jede Meldung ist dort eine
    /// Neuregistrierung. Das Konto lief im Sekundentakt
    /// <c>Ok → Progress → Failed → Ok</c>, die Präsenz aller zehn Nebenstellen
    /// stand auf «offline», und die Anlage sah einen Anmeldesturm.</para>
    ///
    /// <para><b>Die Entprellung allein reicht dagegen nicht.</b> Sie fasst
    /// Ereignisse zusammen, die innerhalb von zwei Sekunden kommen; hier kamen
    /// sie über Minuten verteilt. Was fehlte, war nicht ein längeres Fenster,
    /// sondern die Frage, <b>ob sich überhaupt etwas geändert hat</b>.</para>
    /// </summary>
    private string? _lastReported;

    public ConnectivityMonitor(
        ISipService sip,
        ILogger<ConnectivityMonitor> logger,
        Func<bool>? isInternetAvailable = null,
        TimeSpan? debounceDelay = null,
        Func<IReadOnlyList<string>>? localAddresses = null)
    {
        _sip = sip;
        _logger = logger;
        _isInternetAvailable = isInternetAvailable ?? IsInternetAvailable;
        _debounceDelay = debounceDelay ?? DefaultDebounceDelay;
        _localAddresses = localAddresses ?? LocalAddresses;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        _started = true;

        // Start() läuft auf dem UI-Thread (App.StartTelephony) — hier und nur
        // hier ist der richtige Kontext zu bekommen.
        _uiContext = SynchronizationContext.Current;

        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        ConnectivityLog.Started(_logger);
    }

    private void OnNetworkStatusChanged(object sender) => NotifyNetworkChanged();

    /// <summary>
    /// Ein Netzwechsel wurde gemeldet.
    ///
    /// <para>Öffentlich, damit die Entprellung prüfbar ist: das auslösende
    /// Ereignis ist statisch (<c>NetworkInformation.NetworkStatusChanged</c>)
    /// und lässt sich in einem Test nicht auslösen. Im Betrieb ruft nur der
    /// Ereignishandler hier herein.</para>
    /// </summary>
    public void NotifyNetworkChanged()
    {
        // Läuft auf einem Thread-Pool-Thread, nicht auf dem UI-Thread.
        // Deshalb hier nichts am SDK anfassen, sondern nur planen.
        // Der Wert ist nur fuers Protokoll — was gemeldet wird, entscheidet
        // sich erst beim Anwenden (siehe ApplyAsync).
        ConnectivityLog.NetworkChanged(_logger, _isInternetAvailable());
        ScheduleReachabilityUpdate();
    }

    /// <summary>
    /// Das System geht in den Standby.
    ///
    /// <para>Vor dem Standby abmelden wäre sauberer, ist aber nicht
    /// zuverlässig: Windows gibt keine Garantie, dass noch Zeit für
    /// Netzwerkverkehr bleibt. Stattdessen wird sofort „nicht erreichbar"
    /// gemeldet, damit das SDK beim Aufwachen neu aufbaut.</para>
    ///
    /// <para><b>Hier gilt der vorgegebene Wert</b> und nicht die frische
    /// Abfrage: das Netz ist in diesem Moment technisch noch da, und genau
    /// darum geht es nicht. Öffentlich aus demselben Grund wie
    /// <see cref="NotifyNetworkChanged"/>.</para>
    /// </summary>
    public void NotifySuspending()
    {
        ConnectivityLog.Suspending(_logger);
        ScheduleReachabilityUpdate(false, immediate: true);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                NotifySuspending();
                break;

            case PowerModes.Resume:
                ConnectivityLog.Resuming(_logger);
                ScheduleReachabilityUpdate();
                break;

            case PowerModes.StatusChange:
                // Akku/Netzbetrieb — für die Registrierung ohne Bedeutung.
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Plant die Meldung an das SDK, entprellt. Ein neuer Wechsel innerhalb der
    /// Wartezeit ersetzt den vorherigen.
    ///
    /// <para><paramref name="reachable"/> ist nur für den <b>sofortigen</b> Fall
    /// (Standby) verbindlich. Sonst wird der Zustand erst beim Anwenden
    /// ermittelt — warum, steht bei <see cref="ApplyAsync"/>.</para>
    /// </summary>
    private void ScheduleReachabilityUpdate(bool? reachable = null, bool immediate = false)
    {
        CancellationTokenSource source;

        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            source = _pending;
        }

        _ = ApplyAsync(reachable, immediate, source.Token);
    }

    /// <summary>
    /// Meldet den Netzzustand an das SDK — <b>den aktuellen</b>, nicht den von
    /// vorhin.
    ///
    /// <para><b>Der Fehler, den das behebt.</b> Vorher trug diese Methode den
    /// Wert mit, der beim <i>Ereignis</i> galt. Gemessen am 08.09.2026 beim
    /// Wechsel WLAN → LAN:</para>
    ///
    /// <code>
    /// 08:03:42.593  Netzwerkwechsel erkannt, erreichbar: true
    /// 08:03:44.127  Netzwerkwechsel erkannt, erreichbar: true
    /// 08:03:44.279  an das SDK gemeldet: erreichbar=false   (viermal)
    /// </code>
    ///
    /// <para>nipp sagte dem SDK „kein Netz", 1,7 Sekunden nachdem das Netz
    /// wieder da war. Die Folge stand direkt darunter: Registrierung verloren,
    /// neu aufgebaut, wieder verloren — und jedes Präsenz-Abonnement der
    /// Team-Nebenstellen quittierte die Anlage mit <c>481 Call/transaction does
    /// not exist</c>.</para>
    ///
    /// <para><b>Zwei Ursachen, beide hier behoben.</b> Erstens der eingefrorene
    /// Wert: bei einem Dock-Wechsel meldet Windows ein Dutzend Ereignisse, und
    /// wenn die Meldungen sich stauen, gewinnt die älteste. Jetzt wird der
    /// Zustand <b>im Moment des Meldens</b> gelesen. Zweitens liefen die
    /// Meldungen nebeneinander: <c>Cancel()</c> richtet nichts mehr aus,
    /// sobald der Aufruf schon auf dem UI-Thread wartet — und der war
    /// blockiert (234 Überläufe der Ereignisschleife an jenem Tag). Ein
    /// <see cref="SemaphoreSlim"/> lässt jetzt nur eine Meldung zur Zeit
    /// hinein; wer wartet und dabei überholt wird, gibt auf.</para>
    /// </summary>
    private async Task ApplyAsync(bool? reachable, bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(_debounceDelay, cancellationToken).ConfigureAwait(false);
            }

            await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Nach dem Warten noch einmal hinsehen: zwischen Delay und
                // Semaphor koennen Sekunden vergangen sein.
                cancellationToken.ThrowIfCancellationRequested();

                // Der springende Punkt. Fuer Standby gilt der uebergebene Wert
                // (das System sagt uns dort etwas, was keine Abfrage weiss),
                // sonst zaehlt die Lage jetzt.
                var current = reachable ?? _isInternetAvailable();
                var signatur = Signatur(current);

                // Nur melden, was sich geändert hat (13.09.2026, siehe
                // _lastReported). Eine Meldung an das SDK ist keine Auskunft,
                // sondern ein Auftrag: sie kostet eine Neuregistrierung.
                if (string.Equals(_lastReported, signatur, StringComparison.Ordinal))
                {
                    ConnectivityLog.ReachabilityUnchanged(_logger, current);
                    return;
                }

                await ApplyOnEventLoopThreadAsync(current, cancellationToken).ConfigureAwait(false);

                _lastReported = signatur;
                ConnectivityLog.ReachabilityApplied(_logger, current);
            }
            finally
            {
                _applyGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Ein neuerer Wechsel hat diesen ersetzt — genau der Zweck der
            // Entprellung, kein Fehler.
        }
        catch (Exception ex)
        {
            ConnectivityLog.ApplyFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Meldet den Netzwerkzustand dort, wo das SDK ihn erwartet: auf dem
    /// Thread der Ereignisschleife (§6, §14.1).
    ///
    /// <c>SetNetworkReachableAsync</c> ist im Dienst synchron erledigt, wenn es
    /// zurückkommt — das <c>GetResult()</c> im Post blockiert den UI-Thread
    /// also nicht messbar, und der Aufrufer erfährt eine Ausnahme trotzdem.
    /// </summary>
    private Task ApplyOnEventLoopThreadAsync(bool reachable, CancellationToken cancellationToken)
    {
        var context = _uiContext;

        if (context is null || context == SynchronizationContext.Current)
        {
            return _sip.SetNetworkReachableAsync(reachable, cancellationToken);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Post(
            _ =>
            {
                try
                {
                    _sip.SetNetworkReachableAsync(reachable, cancellationToken)
                        .GetAwaiter()
                        .GetResult();

                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            null);

        return completion.Task;
    }

    /// <summary>
    /// Was für die Telefonie eine Netzlage ausmacht: ob Internet da ist, und
    /// <b>mit welchen lokalen Adressen</b> gesprochen wird.
    ///
    /// <para><b>Die Adressen gehören dazu und sind nicht Beiwerk.</b> Bei einem
    /// Wechsel WLAN → LAN bleibt «erreichbar» true, und trotzdem <em>muss</em>
    /// das SDK neu registrieren: der Contact-Header trägt die alte Adresse, und
    /// die Anlage schickt eingehende Anrufe dorthin. Ein Vergleich, der nur die
    /// Erreichbarkeit ansieht, würde genau den Fall verschlucken, für den es
    /// diesen Dienst gibt (der Befund vom 08.09.2026).</para>
    ///
    /// <para>Verbindungslokale Adressen (169.254.*, fe80::) bleiben draussen —
    /// sie entstehen und vergehen an Adaptern ohne Netz und sind genau das
    /// Rauschen, das hier gefiltert werden soll. Sortiert, weil die Reihenfolge
    /// der Schnittstellen nichts bedeutet.</para>
    /// </summary>
    private string Signatur(bool reachable)
    {
        var adressen = new List<string>(_localAddresses());

        adressen.Sort(StringComparer.Ordinal);

        return reachable + "|" + string.Join(",", adressen);
    }

    /// <summary>
    /// Die aktiven lokalen Adressen. Verbindungslokale (169.254.*, fe80::)
    /// bleiben draussen — sie entstehen und vergehen an Adaptern ohne Netz und
    /// sind genau das Rauschen, das gefiltert werden soll.
    /// </summary>
    private static List<string> LocalAddresses()
    {
        var adressen = new List<string>();

        try
        {
            foreach (var schnittstelle in Netz.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (schnittstelle.OperationalStatus != Netz.OperationalStatus.Up
                    || schnittstelle.NetworkInterfaceType == Netz.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var adresse in schnittstelle.GetIPProperties().UnicastAddresses)
                {
                    if (adresse.Address.IsIPv6LinkLocal
                        || adresse.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    adressen.Add(adresse.Address.ToString());
                }
            }
        }
        catch (Netz.NetworkInformationException)
        {
            // Lässt sich die Liste nicht bilden, gilt die Lage als neu: lieber
            // einmal zu viel melden als eine echte Änderung verschlucken.
            return [Guid.NewGuid().ToString()];
        }

        return adressen;
    }

    private static bool IsInternetAvailable()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile?.GetNetworkConnectivityLevel() >= NetworkConnectivityLevel.InternetAccess;
        }
        catch (Exception)
        {
            // Wenn sich die Lage nicht feststellen lässt, gilt das Netz als
            // vorhanden: ein fälschlich abgemeldeter Client ist schlimmer als
            // ein Registrierungsversuch ins Leere.
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_started)
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }

        _applyGate.Dispose();
    }
}

internal static partial class ConnectivityLog
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Information,
        Message = "Überwachung von Netzwerk und Energiezustand gestartet")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Information,
        Message = "Netzwerkwechsel erkannt, Internet erreichbar: {Reachable}")]
    public static partial void NetworkChanged(ILogger logger, bool reachable);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Information,
        Message = "Netzwerkzustand an das SDK gemeldet: erreichbar={Reachable}")]
    public static partial void ReachabilityApplied(ILogger logger, bool reachable);

    [LoggerMessage(EventId = 2206, Level = LogLevel.Debug,
        Message = "Netzwechsel ohne Wirkung — Erreichbarkeit und lokale Adressen "
            + "sind unveraendert (erreichbar={Reachable}). Nicht an das SDK gemeldet")]
    public static partial void ReachabilityUnchanged(ILogger logger, bool reachable);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Information,
        Message = "System geht in den Standby")]
    public static partial void Suspending(ILogger logger);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Information,
        Message = "System ist aufgewacht, Registrierung wird erneuert")]
    public static partial void Resuming(ILogger logger);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Error,
        Message = "Netzwerkzustand liess sich nicht an das SDK melden")]
    public static partial void ApplyFailed(ILogger logger, Exception exception);
}
