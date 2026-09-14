using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;
using NSubstitute;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Die Zustandsmaschine der Gesprächsansicht (§8.2).
///
/// <b>Warum es diese Tests gibt.</b> Genau hier lagen die drei Fehler, die
/// erst die Abnahme vom 04.09.2026 zeigen konnte: die Ansicht blieb leer, wenn
/// ein Anruf schon lief; sie wurde leer, sobald man „Halten" drückte; und die
/// Auswahl ging bei jedem Zustandswechsel verloren, weil <c>CallInfo</c>
/// unveränderlich ist und die Instanz getauscht wird. Alle drei sind behoben —
/// ohne Test bleibt das aber nur bis zur nächsten Änderung so.
///
/// Möglich ist das, weil <c>ISipService</c> die SDK-Grenze markiert (§6): der
/// Dienst lässt sich vollständig ersetzen.
/// </summary>
public sealed class ActiveCallViewModelTests
{
    private static CallInfo Call(
        CallHandle handle,
        CallStatus status = CallStatus.Connected,
        string number = "0791234567",
        bool muted = false) =>
        new(
            Handle: handle,
            RemoteNumber: number,
            RemoteDisplayName: null,
            Direction: CallDirection.Outgoing,
            Status: status,
            StatusMessage: null,
            StartedAt: DateTimeOffset.UtcNow,
            ConnectedAt: status == CallStatus.Connected ? DateTimeOffset.UtcNow : null,
            IsMuted: muted,
            IsRecording: false,
            Codec: "PCMU",
            Encryption: MediaEncryptionMode.None);

    private static ISipService Service(params CallInfo[] active)
    {
        var sip = Substitute.For<ISipService>();
        sip.ActiveCalls.Returns(active);
        return sip;
    }

    /// <summary>
    /// Kontakte und Präsenz braucht das ViewModel seit §22.1 für die
    /// Vorschlagsliste beim Weiterleiten. Beide bekommen hier einen leeren
    /// Stand: die Tests hier prüfen die Zustandsmaschine des Gesprächs, nicht
    /// die Vorschläge, und ein Store ohne Quellen liefert eine leere Liste,
    /// ohne irgendetwas anzufassen.
    ///
    /// <b>Der Pfad kommt aus dem temporären Verzeichnis</b> — kein Test darf
    /// auf die Einstellungen des angemeldeten Benutzers greifen
    /// (<c>TestIsolationTests</c>).
    /// </summary>
    private static ActiveCallViewModel Create(ISipService sip)
    {
        var verzeichnis = Path.Combine(
            Path.GetTempPath(),
            "nipp-tests",
            Guid.NewGuid().ToString("N"));

        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(verzeichnis, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(verzeichnis, "settings.json"));

        var kontakte = new ContactStore([], einstellungen, NullLogger<ContactStore>.Instance);

        return new ActiveCallViewModel(
            sip,
            kontakte,
            new BlfService(sip, kontakte, einstellungen, NullLogger<BlfService>.Instance),

            // Ohne Kontextquelle: die Tests hier pruefen die Zustandsmaschine,
            // nicht die Namensaufloesung. Die hat eigene Tests.
            new CallPartyResolver(new ClipResolver(kontakte)),

            // Seit ADR-046 laesst sich das Wiedergabegeraet auch im Gespraech
            // waehlen; das geht ueber die Einstellungen, wie ueberall sonst.
            einstellungen,
            NullLogger<ActiveCallViewModel>.Instance);
    }

    [Fact]
    public void Ein_bereits_laufendes_Gespraech_wird_uebernommen()
    {
        // Der Fehler aus der Abnahme: das ViewModel entstand erst beim Öffnen
        // der Seite und hatte die Ereignisse des laufenden Anrufs nie gesehen.
        var handle = CallHandle.New();
        var model = Create(Service(Call(handle)));

        Assert.True(model.HasCall);
        Assert.Equal(handle, model.SelectedCall?.Handle);
    }

    [Fact]
    public void Die_Auswahl_ueberlebt_einen_Zustandswechsel()
    {
        // CallInfo ist unveränderlich: „Halten" tauscht die Instanz aus. Wird
        // die Auswahl über die Referenz geführt, ist die Ansicht danach leer.
        var handle = CallHandle.New();
        var sip = Service(Call(handle));
        var model = Create(sip);

        sip.CallStateChanged += Raise.Event<EventHandler<CallStateEventArgs>>(
            sip,
            new CallStateEventArgs(Call(handle, CallStatus.OnHold), CallStatus.Connected));

        Assert.Equal(handle, model.SelectedCall?.Handle);
        Assert.True(model.IsOnHold);
        Assert.Single(model.Calls);
    }

    [Fact]
    public void Ein_zweites_Gespraech_reisst_die_Ansicht_nicht_weg()
    {
        var first = CallHandle.New();
        var second = CallHandle.New();
        var sip = Service(Call(first));
        var model = Create(sip);

        sip.CallStateChanged += Raise.Event<EventHandler<CallStateEventArgs>>(
            sip,
            new CallStateEventArgs(Call(second, CallStatus.Incoming), CallStatus.Incoming));

        Assert.Equal(2, model.Calls.Count);
        Assert.True(model.CanSwap);
        Assert.True(model.CanTransferAttended);

        // Die Wahl bleibt beim laufenden Gespräch.
        Assert.Equal(first, model.SelectedCall?.Handle);
    }

    [Fact]
    public void Endet_das_gewaehlte_Gespraech_rueckt_das_andere_nach()
    {
        var first = CallHandle.New();
        var second = CallHandle.New();
        var sip = Service(Call(first), Call(second));
        var model = Create(sip);

        Assert.Equal(first, model.SelectedCall?.Handle);

        sip.CallStateChanged += Raise.Event<EventHandler<CallStateEventArgs>>(
            sip,
            new CallStateEventArgs(Call(first, CallStatus.Ended), CallStatus.Connected));

        Assert.Equal(second, model.SelectedCall?.Handle);
        Assert.False(model.CanSwap);
    }

    [Fact]
    public async Task Makeln_haelt_zuerst_und_holt_dann()
    {
        // Umgekehrt gäbe es kurz zwei aktive Gespräche — also zwei offene
        // Mikrofone.
        var first = CallHandle.New();
        var second = CallHandle.New();
        var sip = Service(Call(first), Call(second, CallStatus.OnHold));
        var model = Create(sip);

        await model.SwapCommand.ExecuteAsync(null);

        await sip.Received(1).SetHoldAsync(first, true, Arg.Any<CancellationToken>());
        await sip.Received(1).SetHoldAsync(second, false, Arg.Any<CancellationToken>());
        Assert.Equal(second, model.SelectedCall?.Handle);
    }

    [Fact]
    public async Task Stumm_gilt_nur_dem_gewaehlten_Gespraech()
    {
        var first = CallHandle.New();
        var second = CallHandle.New();
        var sip = Service(Call(first), Call(second));
        var model = Create(sip);

        await model.ToggleMuteCommand.ExecuteAsync(null);

        await sip.Received(1).SetMutedAsync(first, true, Arg.Any<CancellationToken>());
        await sip.DidNotReceive().SetMutedAsync(second, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Eine_gescheiterte_Weiterleitung_wird_gemeldet()
    {
        // §15 und der Review-Befund: LastError wurde gesetzt und nirgends
        // angezeigt. Die Anzeige hängt jetzt daran — der Wert muss stimmen.
        var handle = CallHandle.New();
        var sip = Service(Call(handle));

        sip.TransferAsync(
                Arg.Any<CallHandle>(),
                Arg.Any<string>(),
                Arg.Any<TransferMode>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("Kein zweites Gespräch."));

        var model = Create(sip);
        model.TransferTarget = "151";

        await model.TransferBlindCommand.ExecuteAsync(null);

        Assert.Equal("Kein zweites Gespräch.", model.LastError);
    }

    [Fact]
    public void Ein_leeres_Auswaehlen_leert_die_Ansicht_nicht()
    {
        // Eine ListView verliert ihre Auswahl beim Austausch der Instanz und
        // meldet null zurück. Das ist keine Benutzeraktion.
        var handle = CallHandle.New();
        var model = Create(Service(Call(handle)));

        model.SelectCall(null);

        Assert.Equal(handle, model.SelectedCall?.Handle);
    }
}
