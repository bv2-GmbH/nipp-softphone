using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Updates;

namespace Nipp.Core.Tests.Services.Updates;

/// <summary>
/// Die Regeln der Update-Prüfung (ADR-039).
///
/// <para>Geprüft wird, was etwas entscheidet: welcher Kanal, wann geladen
/// wird, wann angewandt werden darf, und dass ein Fehler nichts kostet. Der
/// Netzzugriff selbst steckt hinter <see cref="IUpdateGateway"/> und ist hier
/// eine Attrappe — deshalb laufen diese Tests ohne Netz, ohne GitHub und ohne
/// installiertes nipp.</para>
/// </summary>
public sealed class UpdateServiceTests
{
    private static UpdateService Create(
        FakeGateway gateway, Func<bool>? callInProgress = null) =>
        new(NullLogger<UpdateService>.Instance, gateway, callInProgress ?? (() => false));

    [Fact]
    public async Task Beim_Start_wird_nichts_geladen_sondern_nur_gefunden()
    {
        // ADR-039: der Unterschied zwischen "fragen" und "still laden" ist die
        // ganze Entscheidung. Ein Download beim Start waere ein anderes
        // Produkt.
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.1", false, new object()) };
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings(), onStart: true);

        Assert.Equal(UpdateState.Available, service.State);
        Assert.Equal("0.9.1", service.Available?.Version);
        Assert.Equal(0, gateway.DownloadCalls);
    }

    [Fact]
    public async Task Ist_die_Pruefung_abgeschaltet_wird_beim_Start_nicht_gefragt()
    {
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.1", false, new object()) };
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings { CheckOnStart = false }, onStart: true);

        Assert.Equal(0, gateway.CheckCalls);
        Assert.Equal(UpdateState.Unknown, service.State);
    }

    [Fact]
    public async Task Auf_Knopfdruck_wird_auch_dann_gefragt_wenn_der_Start_nicht_prueft()
    {
        // "Aus" heisst: nicht von selbst. Wer den Knopf drueckt, hat gefragt.
        var gateway = new FakeGateway();
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings { CheckOnStart = false }, onStart: false);

        Assert.Equal(1, gateway.CheckCalls);
        Assert.Equal(UpdateState.UpToDate, service.State);
    }

    [Fact]
    public async Task Der_eingestellte_Kanal_wird_abgefragt()
    {
        var gateway = new FakeGateway();
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings { Channel = UpdateChannel.Beta }, onStart: true);

        Assert.Equal(UpdateChannel.Beta, gateway.LastChannel);
        Assert.Equal(UpdateChannel.Beta, service.Channel);
    }

    [Fact]
    public async Task Laeuft_nipp_nicht_installiert_passiert_gar_nichts()
    {
        // Der Entwicklungsalltag: gestartet aus dem Ausgabeverzeichnis. Ohne
        // diese Pruefung wirft die Update-Ablage, statt nichts zu finden.
        var gateway = new FakeGateway { IsInstalled = false };
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings(), onStart: true);

        Assert.Equal(0, gateway.CheckCalls);
        Assert.Equal(UpdateState.Unknown, service.State);
    }

    [Fact]
    public async Task Ein_gescheiterter_Abruf_ist_ein_Zustand_und_keine_Ausnahme()
    {
        // Kein Netz ist der Normalfall, nicht die Ausnahme. Wer hier wirft,
        // nimmt den Start mit — und ein Softphone, das wegen GitHub nicht
        // startet, ist kaputt gebaut.
        var gateway = new FakeGateway { Fail = new HttpRequestException("kein Netz") };
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings(), onStart: true);

        Assert.Equal(UpdateState.Failed, service.State);
        Assert.Null(service.Available);
    }

    [Fact]
    public async Task Nach_dem_Laden_ist_das_Update_bereit()
    {
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.1", false, new object()) };
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings(), onStart: true);
        await service.DownloadAsync();

        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal(100, service.DownloadPercent);
        Assert.True(service.CanApply);
    }

    [Fact]
    public async Task Waehrend_eines_Gespraechs_wird_nicht_angewandt()
    {
        // Der wichtigste Test dieser Datei. Ein Telefon, das sich mitten im
        // Gespraech neu startet, ist ein Verbindungsabbruch mit Ansage.
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.1", false, new object()) };
        var inCall = true;
        var service = Create(gateway, () => inCall);

        await service.CheckAsync(new UpdateSettings(), onStart: true);
        await service.DownloadAsync();

        Assert.False(service.CanApply);
        Assert.NotNull(service.BlockedReason);
        Assert.False(service.ApplyAndRestart());
        Assert.Equal(0, gateway.ApplyCalls);

        inCall = false;
        Assert.True(service.CanApply);
        Assert.Null(service.BlockedReason);
        Assert.True(service.ApplyAndRestart());
        Assert.Equal(1, gateway.ApplyCalls);
    }

    [Fact]
    public async Task Ein_Gespraech_das_erst_beim_Knopfdruck_beginnt_haelt_das_Update_auf()
    {
        // Zwischen dem Zeichnen eines Knopfes und seinem Druck kann ein Anruf
        // hereinkommen. Die Oberflaeche ist der falsche Ort fuer diese
        // Sicherheit — deshalb prueft ApplyAndRestart selbst noch einmal.
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.1", false, new object()) };
        var inCall = false;
        var service = Create(gateway, () => inCall);

        await service.CheckAsync(new UpdateSettings(), onStart: true);
        await service.DownloadAsync();
        Assert.True(service.CanApply);

        inCall = true;

        Assert.False(service.ApplyAndRestart());
        Assert.Equal(0, gateway.ApplyCalls);
    }

    [Fact]
    public async Task Ohne_gefundenes_Update_laesst_sich_nichts_laden_oder_anwenden()
    {
        var gateway = new FakeGateway();
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings(), onStart: true);
        await service.DownloadAsync();

        Assert.Equal(UpdateState.UpToDate, service.State);
        Assert.Equal(0, gateway.DownloadCalls);
        Assert.False(service.ApplyAndRestart());
    }

    [Fact]
    public async Task Eine_Rueckstufung_von_beta_auf_stable_gilt_als_gueltiges_Update()
    {
        // Der Rueckweg aus dem Beta-Kanal fuehrt auf eine NIEDRIGERE
        // Versionsnummer. Wer das als Fehler behandelt, baut eine
        // Einbahnstrasse.
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.0", true, new object()) };
        var service = Create(gateway);

        await service.CheckAsync(new UpdateSettings { Channel = UpdateChannel.Stable }, onStart: false);

        Assert.Equal(UpdateState.Available, service.State);
        Assert.True(service.Available?.IsDowngrade);
    }

    [Fact]
    public async Task Der_Zustand_wird_gemeldet()
    {
        // Die Oberflaeche haengt daran; ohne Ereignis bliebe die Zeile stehen,
        // bis jemand die Seite neu oeffnet.
        var gateway = new FakeGateway { Update = new AvailableUpdate("0.9.1", false, new object()) };
        var service = Create(gateway);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.CheckAsync(new UpdateSettings(), onStart: true);

        Assert.True(changes >= 2, $"Erwartet: Checking und Available. Gemeldet: {changes}");
    }

    [Fact]
    public void Die_Kanalnamen_stehen_an_einer_Stelle()
    {
        // Sie stehen gleichlautend im Release-Skript. Ein Tippfehler hier
        // heisst: die App sucht einen Feed, den niemand hochlaedt, und meldet
        // wahrheitsgemaess "kein Update".
        Assert.Equal("win-stable", UpdateChannels.NameOf(UpdateChannel.Stable));
        Assert.Equal("win-beta", UpdateChannels.NameOf(UpdateChannel.Beta));
    }

    [Fact]
    public async Task Zustandsaenderungen_werden_auf_dem_Thread_der_Oberflaeche_gemeldet()
    {
        // Der Absturz vom 08.09.2026, auf einem Windows-10-Arbeitsplatz mit
        // installiertem nipp: "stuertzt ab, wenn ich nach Updates suche".
        //
        // Die Pruefung wartet auf das Netz; nach dem await laeuft alles
        // Weitere auf einem Threadpool-Thread — auch das Changed-Ereignis.
        // Im ViewModel wird daraus ein OnPropertyChanged, und eine gebundene
        // WinUI-Oberflaeche vom falschen Thread anzufassen beendet den
        // Prozess.
        //
        // Auf der Entwicklungsmaschine war das unsichtbar: dort ist nipp
        // nicht installiert, der Dienst kehrt vor dem ersten await zurueck,
        // und der Threadwechsel findet gar nicht statt.
        var kontext = new SammelnderKontext();
        var vorher = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(kontext);

        UpdateService service;
        using var gateway = new VerzoegertesGateway();

        try
        {
            // Wie im Betrieb: der Dienst entsteht auf dem UI-Thread.
            service = new UpdateService(
                NullLogger<UpdateService>.Instance, gateway, () => false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(vorher);
        }

        var fremdeThreads = new List<int>();
        service.Changed += (_, _) =>
        {
            if (SynchronizationContext.Current != kontext)
            {
                lock (fremdeThreads)
                {
                    fremdeThreads.Add(Environment.CurrentManagedThreadId);
                }
            }
        };

        await service.CheckAsync(new UpdateSettings(), onStart: true);

        Assert.True(
            kontext.Posts > 0,
            "Nach dem Warten auf das Netz muss die Meldung ueber den UI-Kontext laufen.");

        Assert.True(
            fremdeThreads.Count == 0,
            $"{fremdeThreads.Count} Meldung(en) kamen an der Oberflaeche vorbei. Genau das war der Absturz.");
    }

    /// <summary>
    /// Ein Kontext, der mitzaehlt und die Arbeit sofort erledigt — er steht
    /// fuer den UI-Thread, den es im Test nicht gibt.
    /// </summary>
    private sealed class SammelnderKontext : SynchronizationContext
    {
        private int _posts;

        public int Posts => _posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);

            // Den eigenen Kontext setzen, damit der Empfaenger sieht, dass er
            // "auf dem UI-Thread" laeuft.
            var vorher = Current;
            SetSynchronizationContext(this);

            try
            {
                d(state);
            }
            finally
            {
                SetSynchronizationContext(vorher);
            }
        }
    }

    /// <summary>
    /// Eine Quelle, die den Thread wechselt — ohne echtes Warten gaebe es
    /// keinen Threadwechsel und der Test waere wertlos.
    /// </summary>
    private sealed class VerzoegertesGateway : IUpdateGateway, IDisposable
    {
        private readonly SemaphoreSlim _bremse = new(0, 1);

        public bool IsInstalled => true;

        public string? CurrentVersion => "0.9.0";

        public async Task<AvailableUpdate?> CheckAsync(
            UpdateChannel channel, CancellationToken cancellationToken)
        {
            _ = Task.Run(
                async () =>
                {
                    await Task.Delay(20, CancellationToken.None);
                    _bremse.Release();
                },
                CancellationToken.None);

            await _bremse.WaitAsync(cancellationToken).ConfigureAwait(false);

            return new AvailableUpdate("0.9.1", false, new object());
        }

        public Task DownloadAsync(
            AvailableUpdate update, Action<int>? progress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void ApplyAndRestart(AvailableUpdate update)
        {
        }

        public void Dispose() => _bremse.Dispose();
    }

    /// <summary>Eine Update-Quelle, die tut, was der Test ihr sagt.</summary>
    private sealed class FakeGateway : IUpdateGateway
    {
        public bool IsInstalled { get; set; } = true;

        public string? CurrentVersion => "0.9.0";

        public AvailableUpdate? Update { get; set; }

        public Exception? Fail { get; set; }

        public int CheckCalls { get; private set; }

        public int DownloadCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public UpdateChannel? LastChannel { get; private set; }

        public Task<AvailableUpdate?> CheckAsync(
            UpdateChannel channel, CancellationToken cancellationToken)
        {
            CheckCalls++;
            LastChannel = channel;

            return Fail is not null
                ? Task.FromException<AvailableUpdate?>(Fail)
                : Task.FromResult(Update);
        }

        public Task DownloadAsync(
            AvailableUpdate update, Action<int>? progress, CancellationToken cancellationToken)
        {
            DownloadCalls++;
            progress?.Invoke(50);
            return Fail is not null ? Task.FromException(Fail) : Task.CompletedTask;
        }

        public void ApplyAndRestart(AvailableUpdate update) => ApplyCalls++;
    }
}
