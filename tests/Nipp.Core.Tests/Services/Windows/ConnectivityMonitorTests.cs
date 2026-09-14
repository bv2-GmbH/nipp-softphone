using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Windows;
using NSubstitute;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Was beim Wechsel WLAN → LAN an das SDK gemeldet wird.
///
/// <para><b>Der Anlass ist gemessen, nicht ausgedacht.</b> Am 08.09.2026 stand
/// im Protokoll:</para>
///
/// <code>
/// 08:03:42.593  Netzwerkwechsel erkannt, erreichbar: true
/// 08:03:44.127  Netzwerkwechsel erkannt, erreichbar: true
/// 08:03:44.279  an das SDK gemeldet: erreichbar=false   (viermal)
/// </code>
///
/// <para>nipp meldete „kein Netz", nachdem das Netz wieder da war — der Wert
/// stammte vom Ereignis, nicht vom Zeitpunkt der Meldung. Danach verlor das
/// Konto zweimal die Registrierung, und die Präsenz-Abos der Team-Nebenstellen
/// waren weg. Wer in diesem Moment angerufen wurde, hatte Pech.</para>
/// </summary>
public sealed class ConnectivityMonitorTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);

    /// <summary>So lange darf ein Test auf die entprellte Meldung warten.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Gemeldet_wird_der_Zustand_beim_Melden_nicht_der_beim_Ereignis()
    {
        // Genau der Fall vom 08.09.2026: das Netz ist im Moment des
        // Ereignisses weg und beim Melden wieder da.
        var reachable = false;
        using var sip = new Recorder();
        using var monitor = new ConnectivityMonitor(
            sip.Sip, NullLogger<ConnectivityMonitor>.Instance, () => reachable, Debounce);

        monitor.NotifyNetworkChanged();
        reachable = true;

        var reported = await sip.WaitForNextAsync(Patience);

        Assert.True(
            reported,
            "Gemeldet wurde der eingefrorene Wert von vorhin. Genau daran verlor nipp die Registrierung.");
    }

    [Fact]
    public async Task Ein_Sturm_von_Wechseln_erzeugt_genau_eine_Meldung()
    {
        // Ein Dock-Wechsel meldete am 08.09.2026 fuenfzehn Ereignisse in 90
        // Sekunden. Jedes einzeln durchzureichen sieht die Anlage als
        // Anmeldesturm.
        using var sip = new Recorder();
        using var monitor = new ConnectivityMonitor(
            sip.Sip, NullLogger<ConnectivityMonitor>.Instance, () => true, Debounce);

        for (var i = 0; i < 15; i++)
        {
            monitor.NotifyNetworkChanged();
        }

        await sip.WaitForNextAsync(Patience);
        await Task.Delay(Debounce * 4);

        Assert.Equal(1, sip.Count);
    }

    [Fact]
    public async Task Der_Standby_meldet_sofort_und_ungefragt_nicht_erreichbar()
    {
        // Beim Suspend zaehlt nicht, was eine Abfrage sagt: Windows gibt keine
        // Garantie, dass noch Zeit fuer Netzverkehr bleibt. Der Wert ist
        // deshalb vorgegeben — und darf nicht durch eine frische Abfrage
        // ersetzt werden.
        using var sip = new Recorder();
        using var monitor = new ConnectivityMonitor(
            sip.Sip, NullLogger<ConnectivityMonitor>.Instance, () => true, Debounce);

        monitor.NotifySuspending();

        var reported = await sip.WaitForNextAsync(Patience);

        Assert.False(reported);
    }

    [Fact]
    public async Task Zwei_Wechsel_nacheinander_melden_beide_den_dann_gueltigen_Zustand()
    {
        // Kabel raus, Kabel rein — mit genug Abstand sind das zwei Meldungen,
        // und jede traegt den Zustand ihres Augenblicks.
        var reachable = true;
        using var sip = new Recorder();
        using var monitor = new ConnectivityMonitor(
            sip.Sip, NullLogger<ConnectivityMonitor>.Instance, () => reachable, Debounce);

        reachable = false;
        monitor.NotifyNetworkChanged();
        Assert.False(await sip.WaitForNextAsync(Patience));

        reachable = true;
        monitor.NotifyNetworkChanged();
        Assert.True(await sip.WaitForNextAsync(Patience));
    }

    [Fact]
    public async Task Ein_Wechsel_ohne_Wirkung_wird_nicht_gemeldet()
    {
        // <b>Der Befund vom 13.09.2026.</b> Neben dem WLAN lief ein
        // Mobilfunkadapter, der seine Adresse selbst wechselte. Jedes seiner
        // Ereignisse meldete «erreichbar=true» an ein SDK, dem das längst
        // gesagt worden war — und eine Meldung ist dort keine Auskunft,
        // sondern ein Auftrag: sie kostet eine Neuregistrierung. Das Konto
        // lief im Sekundentakt Ok -> Progress -> Failed -> Ok, und die Präsenz
        // aller zehn Nebenstellen stand auf «offline».
        //
        // <b>Die Entprellung fing das nicht:</b> sie fasst zusammen, was
        // innerhalb von zwei Sekunden kommt, und hier kam es über Minuten.
        string[] adressen = ["192.168.1.10"];

        using var sip = new Recorder();
        using var monitor = new ConnectivityMonitor(
            sip.Sip,
            NullLogger<ConnectivityMonitor>.Instance,
            () => true,
            Debounce,
            () => adressen);

        monitor.NotifyNetworkChanged();
        Assert.True(await sip.WaitForNextAsync(Patience));

        // Zweites Ereignis, dieselbe Lage — mit genug Abstand, damit die
        // Entprellung es nicht ohnehin schluckt.
        await Task.Delay(Debounce * 4);
        monitor.NotifyNetworkChanged();
        await Task.Delay(Debounce * 6);

        Assert.Equal(1, sip.Count);
    }

    [Fact]
    public async Task Eine_neue_lokale_Adresse_wird_gemeldet()
    {
        // <b>Die Gegenprobe, und sie ist die wichtigere Hälfte.</b> Beim
        // Wechsel WLAN -> LAN bleibt «erreichbar» true, und trotzdem MUSS das
        // SDK neu registrieren: der Contact-Header trägt die alte Adresse, und
        // die Anlage schickt eingehende Anrufe dorthin. Eine Bremse, die nur
        // auf die Erreichbarkeit sieht, verschluckt genau den Fall, für den es
        // diesen Dienst gibt (der Befund vom 08.09.2026).
        string[] adressen = ["192.168.1.10"];

        using var sip = new Recorder();
        using var monitor = new ConnectivityMonitor(
            sip.Sip,
            NullLogger<ConnectivityMonitor>.Instance,
            () => true,
            Debounce,
            () => adressen);

        monitor.NotifyNetworkChanged();
        Assert.True(await sip.WaitForNextAsync(Patience));

        await Task.Delay(Debounce * 4);
        adressen = ["10.0.0.5"];
        monitor.NotifyNetworkChanged();

        Assert.True(await sip.WaitForNextAsync(Patience));
        Assert.Equal(2, sip.Count);
    }

    /// <summary>
    /// Merkt sich, was an das SDK gemeldet wurde, und lässt darauf warten.
    ///
    /// <para>Warten statt schlafen: die Entprellung ist zeitgesteuert, und ein
    /// fester <c>Task.Delay</c> im Test wäre entweder zu kurz (dann flackert
    /// er auf einer langsamen Maschine) oder zu lang (dann kostet er bei jedem
    /// Lauf Sekunden).</para>
    /// </summary>
    private sealed class Recorder : IDisposable
    {
        private readonly SemaphoreSlim _signal = new(0);

        public List<bool> Calls { get; } = [];

        public ISipService Sip { get; }

        public Recorder()
        {
            var sip = Substitute.For<ISipService>();

            sip.When(x => x.SetNetworkReachableAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    lock (Calls)
                    {
                        Calls.Add(call.Arg<bool>());
                    }

                    _signal.Release();
                });

            Sip = sip;
        }

        public async Task<bool> WaitForNextAsync(TimeSpan timeout)
        {
            Assert.True(
                await _signal.WaitAsync(timeout),
                $"Innerhalb von {timeout.TotalSeconds} s wurde nichts an das SDK gemeldet.");

            lock (Calls)
            {
                return Calls[^1];
            }
        }

        public int Count
        {
            get
            {
                lock (Calls)
                {
                    return Calls.Count;
                }
            }
        }

        public void Dispose() => _signal.Dispose();
    }
}
