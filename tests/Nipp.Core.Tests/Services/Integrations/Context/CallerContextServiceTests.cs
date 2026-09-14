using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Integrations.Context;

/// <summary>
/// Der Anruferkontext (§21.1).
///
/// <b>Diese Klasse ist die einzige Stelle, an der die Integrationsplattform
/// den Anrufpfad berührt</b> — deshalb prüfen diese Tests vor allem, was
/// <i>nicht</i> passieren darf: kein Warten im Ereignis, keine Ausnahme nach
/// draussen, keine Quelle, die eine andere aufhält, und keine Abfrage für eine
/// interne Nummer.
/// </summary>
public sealed class CallerContextServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _zeit = new();

    /// <summary>Eine Quelle, die der Test steuert.</summary>
    private sealed class Quelle(
        string id,
        Func<PhoneNumberKey, CancellationToken, Task<ContextFragment>> antwort)
        : ICallerContextProvider
    {
        public string SourceId => id;

        public string DisplayName => id.ToUpperInvariant();

        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

        public int Priority { get; init; } = 100;

        public bool NurExterne { get; init; }

        public int Aufrufe { get; private set; }

        public bool AppliesTo(PhoneNumberKey number, CallDirection direction) =>
            !NurExterne || number.IsLookupCandidate(allowInternal: false);

        public Task<ContextFragment> LookupAsync(PhoneNumberKey number, CancellationToken cancellationToken)
        {
            Aufrufe++;
            return antwort(number, cancellationToken);
        }
    }

    private sealed class Registry(params ICallerContextProvider[] quellen)
        : ICallerContextProviderRegistry
    {
        public IReadOnlyList<ICallerContextProvider> ContextProviders { get; } = quellen;
    }

    private static ContextFragment Fragment(string id, params (string Feld, string Wert)[] felder) =>
        new(
            id,
            id.ToUpperInvariant(),
            SourceState.Success,
            felder.ToDictionary(
                f => f.Feld,
                f => ContextValue.FromText(f.Wert),
                StringComparer.Ordinal));

    private static Quelle Liefert(string id, params (string, string)[] felder) =>
        new(id, (_, _) => Task.FromResult(Fragment(id, felder)));

    private (CallerContextService Dienst, ISipService Sip) Bauen(
        ICallerContextProviderRegistry registry,
        CallerLookupSettings? lookup = null)
    {
        var geheimnisse = new IntegrationSecrets(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

        var config = new IntegrationConfigStore(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));

        config.Save(new IntegrationConfig { CallerLookup = lookup ?? new CallerLookupSettings() });

        var settings = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "sip.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

        settings.Load();

        var sip = Substitute.For<ISipService>();

        var dienst = new CallerContextService(
            sip,
            registry,
            config,
            settings,
            NullLogger<CallerContextService>.Instance,
            _zeit);

        dienst.Start();

        return (dienst, sip);
    }

    private static CallInfo Anruf(
        CallHandle handle,
        string nummer = "0791234567",
        CallDirection richtung = CallDirection.Incoming,
        CallStatus status = CallStatus.Incoming) =>
        new(
            handle,
            nummer,
            RemoteDisplayName: null,
            richtung,
            status,
            StatusMessage: null,
            StartedAt: DateTimeOffset.UtcNow,
            ConnectedAt: null,
            IsMuted: false,
            IsRecording: false,
            Codec: null,
            Encryption: MediaEncryptionMode.Unknown);

    /// <summary>
    /// Löst ein Anrufereignis aus — wie es der Telefoniedienst täte.
    ///
    /// <c>Raise.Event</c> und nicht <c>Raise.EventWith</c>: <c>CallStateEventArgs</c>
    /// ist ein Record und erbt nicht von <c>System.EventArgs</c>. Dasselbe
    /// Muster benutzen die Tests des <c>ActiveCallViewModel</c>.
    /// </summary>
    private static void Melde(ISipService sip, CallInfo call, CallStatus? vorher = null) =>
        sip.CallStateChanged += Raise.Event<EventHandler<CallStateEventArgs>>(
            sip,
            new CallStateEventArgs(call, vorher));

    private static async Task AbwartenAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
            await Task.Delay(1);
        }
    }

    // --- Der Normalfall ---

    [Fact]
    public async Task Ein_Anruf_fragt_die_Quellen_und_meldet_das_Ergebnis()
    {
        var (dienst, sip) = Bauen(new Registry(Liefert("crm", ("customerName", "Hans Muster"))));
        using var _ = dienst;

        var staende = new List<ContextSnapshot>();
        dienst.ContextChanged += (_, stand) => staende.Add(stand);

        var handle = CallHandle.New();
        Melde(sip, Anruf(handle));

        await AbwartenAsync();

        // Der erste Stand geht sofort hinaus, mit "wird geladen".
        Assert.Equal(SourceState.Loading, staende[0].Sources["crm"].State);

        var letzter = staende[^1];

        Assert.Equal(SourceState.Success, letzter.Sources["crm"].State);
        Assert.Equal("Hans Muster", letzter.Resolve("crm.customerName").AsText());
        Assert.True(letzter.IsComplete);
    }

    /// <summary>
    /// Die Lehre aus docs/plans/REVIEW.md §8: die Anrufkennung entscheidet, nicht der
    /// Zustandsübergang. Ein Anruf durchläuft mehrere Zustände und darf
    /// trotzdem nur einmal nachgeschlagen werden.
    /// </summary>
    [Fact]
    public async Task Ein_Anruf_wird_genau_einmal_nachgeschlagen()
    {
        var quelle = Liefert("crm", ("customerName", "Hans"));
        var (dienst, sip) = Bauen(new Registry(quelle));
        using var _ = dienst;

        var handle = CallHandle.New();

        Melde(sip, Anruf(handle, status: CallStatus.Incoming));
        Melde(sip, Anruf(handle, status: CallStatus.Connected), CallStatus.Incoming);
        Melde(sip, Anruf(handle, status: CallStatus.OnHold), CallStatus.Connected);

        await AbwartenAsync();

        Assert.Equal(1, quelle.Aufrufe);
    }

    /// <summary>
    /// Ein ausgehender Anruf meldet andere Zustände in anderer Reihenfolge —
    /// und muss genauso funktionieren. Genau diese Asymmetrie hat in
    /// docs/plans/REVIEW.md §8 dreimal zu einem Fehler geführt.
    /// </summary>
    [Fact]
    public async Task Auch_ein_ausgehender_Anruf_wird_nachgeschlagen()
    {
        var quelle = Liefert("crm", ("customerName", "Hans"));
        var (dienst, sip) = Bauen(new Registry(quelle));
        using var _ = dienst;

        Melde(sip, Anruf(CallHandle.New(), richtung: CallDirection.Outgoing, status: CallStatus.Dialing));

        await AbwartenAsync();

        Assert.Equal(1, quelle.Aufrufe);
    }

    [Fact]
    public async Task Zwei_Gespraeche_bekommen_eigene_Ergebnisse()
    {
        var (dienst, sip) = Bauen(new Registry(Liefert("crm", ("customerName", "Wer auch immer"))));
        using var _ = dienst;

        var ersterHandle = CallHandle.New();
        var zweiterHandle = CallHandle.New();

        Melde(sip, Anruf(ersterHandle, "0791111111"));
        Melde(sip, Anruf(zweiterHandle, "0792222222"));

        await AbwartenAsync();

        Assert.NotNull(dienst.SnapshotFor(ersterHandle));
        Assert.NotNull(dienst.SnapshotFor(zweiterHandle));
        Assert.NotEqual(
            dienst.SnapshotFor(ersterHandle)!.Number.E164,
            dienst.SnapshotFor(zweiterHandle)!.Number.E164);
    }

    // --- Isolation ---

    /// <summary>
    /// <b>Die wichtigste Zusage</b> (§21.2): eine langsame Quelle verzögert
    /// weder eine andere noch die Anzeige.
    /// </summary>
    [Fact]
    public async Task Eine_langsame_Quelle_haelt_die_schnelle_nicht_auf()
    {
        var langsam = new TaskCompletionSource<ContextFragment>();

        var registry = new Registry(
            Liefert("crm", ("customerName", "Hans Muster")),
            new Quelle("erp", (_, _) => langsam.Task));

        var (dienst, sip) = Bauen(registry);
        using var _ = dienst;

        var handle = CallHandle.New();
        Melde(sip, Anruf(handle));

        await AbwartenAsync();

        var stand = dienst.SnapshotFor(handle)!;

        Assert.Equal(SourceState.Success, stand.Sources["crm"].State);
        Assert.Equal(SourceState.Loading, stand.Sources["erp"].State);
        Assert.False(stand.IsComplete);

        langsam.SetResult(Fragment("erp", ("customerNumber", "4711")));
        await AbwartenAsync();

        Assert.True(dienst.SnapshotFor(handle)!.IsComplete);
    }

    /// <summary>
    /// Eine Quelle, die wirft, darf weder den Anruf noch die anderen Quellen
    /// mitnehmen. Eine Ausnahme aus einem SDK-Ereignishandler beendet die
    /// Anwendung.
    /// </summary>
    [Fact]
    public async Task Eine_kaputte_Quelle_kostet_nur_sich_selbst()
    {
        var registry = new Registry(
            Liefert("crm", ("customerName", "Hans Muster")),
            new Quelle("erp", (_, _) => throw new InvalidOperationException("kaputt")));

        var (dienst, sip) = Bauen(registry);
        using var _ = dienst;

        var handle = CallHandle.New();

        // Der Aufruf selbst darf nicht werfen — er kommt aus einem
        // SDK-Ereignishandler.
        Melde(sip, Anruf(handle));

        await AbwartenAsync();

        var stand = dienst.SnapshotFor(handle)!;

        Assert.Equal(SourceState.Success, stand.Sources["crm"].State);
        Assert.Equal(SourceState.Error, stand.Sources["erp"].State);
        Assert.NotNull(stand.Sources["erp"].Message);
    }

    [Fact]
    public async Task Eine_Quelle_die_zu_lange_braucht_laeuft_in_ihre_Zeitgrenze()
    {
        var quelle = new Quelle("erp", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Fragment("erp");
        })
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var (dienst, sip) = Bauen(new Registry(quelle));
        using var _ = dienst;

        var handle = CallHandle.New();
        Melde(sip, Anruf(handle));

        await AbwartenAsync();

        Assert.Equal(SourceState.Timeout, dienst.SnapshotFor(handle)!.Sources["erp"].State);
    }

    /// <summary>
    /// Ein beendetes Gespräch bricht ab, was noch läuft — sonst arbeiteten
    /// Anfragen für Anrufe weiter, die längst vorbei sind.
    /// </summary>
    [Fact]
    public async Task Ein_beendetes_Gespraech_bricht_die_Abfragen_ab()
    {
        var abgebrochen = false;

        var quelle = new Quelle("erp", async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                abgebrochen = true;
                throw;
            }

            return Fragment("erp");
        });

        var (dienst, sip) = Bauen(new Registry(quelle));
        using var _ = dienst;

        var handle = CallHandle.New();

        Melde(sip, Anruf(handle));
        await AbwartenAsync();

        Melde(sip, Anruf(handle, status: CallStatus.Ended), CallStatus.Connected);
        await AbwartenAsync();

        Assert.True(abgebrochen, "Die Abfrage lief nach dem Gespräch weiter.");
        Assert.Null(dienst.SnapshotFor(handle));
    }

    // --- Was nicht gefragt wird ---

    /// <summary>
    /// §21.4: für einen Kollegen auf Nebenstelle 151 hat kein CRM eine
    /// Antwort, und die Nebenstelle eines Mitarbeiters hat auf einem fremden
    /// Server nichts zu suchen. Notrufnummern gelten ebenfalls als intern.
    /// </summary>
    [Theory]
    [InlineData("151")]
    [InlineData("112")]
    public async Task Interne_Nummern_gehen_nicht_an_externe_Quellen(string nummer)
    {
        var extern_ = new Quelle("crm", (_, _) => Task.FromResult(Fragment("crm")))
        {
            NurExterne = true,
        };

        var (dienst, sip) = Bauen(new Registry(extern_));
        using var _ = dienst;

        Melde(sip, Anruf(CallHandle.New(), nummer));
        await AbwartenAsync();

        Assert.Equal(0, extern_.Aufrufe);
    }

    [Fact]
    public async Task Abgeschaltet_wird_gar_nicht_nachgeschlagen()
    {
        var quelle = Liefert("crm", ("customerName", "Hans"));

        var (dienst, sip) = Bauen(
            new Registry(quelle),
            new CallerLookupSettings { Enabled = false });

        using var _ = dienst;

        Melde(sip, Anruf(CallHandle.New()));
        await AbwartenAsync();

        Assert.Equal(0, quelle.Aufrufe);
    }

    [Fact]
    public async Task Ausgehende_Anrufe_lassen_sich_ausnehmen()
    {
        var quelle = Liefert("crm", ("customerName", "Hans"));

        var (dienst, sip) = Bauen(
            new Registry(quelle),
            new CallerLookupSettings { LookupOutgoing = false });

        using var _ = dienst;

        Melde(sip, Anruf(CallHandle.New(), richtung: CallDirection.Outgoing, status: CallStatus.Dialing));
        await AbwartenAsync();

        Assert.Equal(0, quelle.Aufrufe);
    }

    // --- Zwischenspeicher ---

    /// <summary>
    /// Der Fall, für den es den Zwischenspeicher gibt: derselbe Anrufer ruft
    /// gleich noch einmal an, weil das Gespräch abgebrochen ist.
    /// </summary>
    [Fact]
    public async Task Derselbe_Anrufer_kommt_beim_zweiten_Mal_aus_dem_Zwischenspeicher()
    {
        var quelle = Liefert("crm", ("customerName", "Hans Muster"));
        var (dienst, sip) = Bauen(new Registry(quelle));
        using var _ = dienst;

        var ersterHandle = CallHandle.New();
        Melde(sip, Anruf(ersterHandle, "0791234567"));
        await AbwartenAsync();

        Melde(sip, Anruf(ersterHandle, "0791234567", status: CallStatus.Ended), CallStatus.Connected);
        await AbwartenAsync();

        var zweiterHandle = CallHandle.New();
        Melde(sip, Anruf(zweiterHandle, "0791234567"));
        await AbwartenAsync();

        Assert.Equal(1, quelle.Aufrufe);

        var stand = dienst.SnapshotFor(zweiterHandle)!;

        Assert.True(stand.Sources["crm"].FromCache);
        Assert.Equal("Hans Muster", stand.Resolve("crm.customerName").AsText());
    }

    [Fact]
    public async Task Nach_Ablauf_wird_erneut_gefragt()
    {
        var quelle = Liefert("crm", ("customerName", "Hans Muster"));

        var (dienst, sip) = Bauen(
            new Registry(quelle),
            new CallerLookupSettings { CacheSeconds = 60 });

        using var _ = dienst;

        Melde(sip, Anruf(CallHandle.New(), "0791234567"));
        await AbwartenAsync();

        _zeit.Advance(TimeSpan.FromSeconds(61));

        Melde(sip, Anruf(CallHandle.New(), "0791234567"));
        await AbwartenAsync();

        Assert.Equal(2, quelle.Aufrufe);
    }

    /// <summary>
    /// Ein Fehler gehört nicht in den Zwischenspeicher — sonst bliebe eine
    /// kurze Störung minutenlang sichtbar.
    /// </summary>
    [Fact]
    public async Task Ein_Fehler_wird_nicht_zwischengespeichert()
    {
        var aufrufe = 0;

        var quelle = new Quelle("crm", (_, _) =>
        {
            aufrufe++;
            return aufrufe == 1
                ? throw new InvalidOperationException("kurz gestört")
                : Task.FromResult(Fragment("crm", ("customerName", "Hans Muster")));
        });

        var (dienst, sip) = Bauen(new Registry(quelle));
        using var _ = dienst;

        Melde(sip, Anruf(CallHandle.New(), "0791234567"));
        await AbwartenAsync();

        var zweiterHandle = CallHandle.New();
        Melde(sip, Anruf(zweiterHandle, "0791234567"));
        await AbwartenAsync();

        Assert.Equal(2, aufrufe);
        Assert.Equal(SourceState.Success, dienst.SnapshotFor(zweiterHandle)!.Sources["crm"].State);
    }

    // --- Das Kontextmodell ---

    [Fact]
    public async Task Die_Rufnummer_ist_auf_der_Karte_ansprechbar()
    {
        var (dienst, sip) = Bauen(new Registry(Liefert("crm", ("customerName", "Hans"))));
        using var _ = dienst;

        var handle = CallHandle.New();
        Melde(sip, Anruf(handle, "0791234567"));
        await AbwartenAsync();

        var stand = dienst.SnapshotFor(handle)!;

        Assert.Equal("+41791234567", stand.Resolve("number.e164").AsText());
        Assert.Equal("0791234567", stand.Resolve("number.national").AsText());
    }

    [Fact]
    public async Task Ein_unbekannter_Pfad_ist_leer()
    {
        var (dienst, sip) = Bauen(new Registry(Liefert("crm", ("customerName", "Hans"))));
        using var _ = dienst;

        var handle = CallHandle.New();
        Melde(sip, Anruf(handle));
        await AbwartenAsync();

        var stand = dienst.SnapshotFor(handle)!;

        Assert.Equal(ContextValue.Null, stand.Resolve("erp.customerNumber"));
        Assert.Equal(ContextValue.Null, stand.Resolve("ohnePunkt"));
        Assert.Equal(ContextValue.Null, stand.Resolve(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
