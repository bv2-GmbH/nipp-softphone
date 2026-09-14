using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Die zwei Regeln der Headset-Taste.
///
/// <para>Sie stehen hier, weil sie am Gerät kaum zu prüfen sind: ein falsch
/// gedeuteter Tastendruck fällt erst auf, wenn er ein Gespräch beendet, das
/// weitergehen sollte. Und weil in nipp genau diese Art Regel — welcher
/// Anrufzustand bedeutet was — schon dreimal falsch war.</para>
/// </summary>
public class HeadsetPolicyTests
{
    private static CallInfo Anruf(CallStatus status, bool muted = false, string nummer = "151") =>
        new(
            Handle: new CallHandle(Guid.NewGuid()),
            RemoteNumber: nummer,
            RemoteDisplayName: null,
            Direction: status == CallStatus.Incoming ? CallDirection.Incoming : CallDirection.Outgoing,
            Status: status,
            StatusMessage: null,
            StartedAt: DateTimeOffset.UtcNow,
            ConnectedAt: status == CallStatus.Connected ? DateTimeOffset.UtcNow : null,
            IsMuted: muted,
            IsRecording: false,
            Codec: null,
            Encryption: MediaEncryptionMode.None);

    // ---- Was dem Gerät gemeldet wird ----

    [Fact]
    public void Ohne_Anruf_ist_alles_aus()
    {
        var state = HeadsetPolicy.StateFor([]);

        Assert.False(state.ImGespraech);
        Assert.False(state.Klingelt);
        Assert.False(state.Stumm);
    }

    [Fact]
    public void Ein_verbundenes_Gespraech_meldet_abgenommen()
    {
        var state = HeadsetPolicy.StateFor([Anruf(CallStatus.Connected)]);

        Assert.True(state.ImGespraech);
        Assert.False(state.Klingelt);
    }

    /// <summary>
    /// <b>Der Fall, um den es hier eigentlich geht.</b> Ein ausgehender Anruf
    /// muss als abgenommen gelten, sobald gewählt wird — sonst hätte die Taste
    /// keine Wirkung, solange es beim Gegenüber klingelt. Und genau dann bricht
    /// man einen Anruf am häufigsten ab.
    /// </summary>
    [Theory]
    [InlineData(CallStatus.Dialing)]
    [InlineData(CallStatus.Ringing)]
    public void Ein_ausgehender_Anruf_gilt_ab_dem_Waehlen_als_abgenommen(CallStatus status)
    {
        var state = HeadsetPolicy.StateFor([Anruf(status)]);

        Assert.True(state.ImGespraech);
        Assert.False(state.Klingelt);
    }

    [Fact]
    public void Ein_eingehender_Anruf_meldet_klingeln_und_nicht_abgenommen()
    {
        var state = HeadsetPolicy.StateFor([Anruf(CallStatus.Incoming)]);

        Assert.True(state.Klingelt);
        Assert.False(state.ImGespraech);
    }

    /// <summary>
    /// Beide Lampen zugleich wären für das Gerät ein Widerspruch: es müsste
    /// entscheiden, ob die Taste annimmt oder auflegt. Klingeln gewinnt.
    /// </summary>
    [Fact]
    public void Klingeln_schlaegt_ein_gleichzeitig_laufendes_Gespraech()
    {
        var state = HeadsetPolicy.StateFor(
            [Anruf(CallStatus.Connected), Anruf(CallStatus.Incoming)]);

        Assert.True(state.Klingelt);
        Assert.False(state.ImGespraech);
    }

    [Fact]
    public void Ein_gehaltenes_Gespraech_bleibt_abgenommen()
    {
        var state = HeadsetPolicy.StateFor([Anruf(CallStatus.OnHold)]);

        Assert.True(state.ImGespraech);
    }

    [Fact]
    public void Stumm_wird_nur_vom_verbundenen_Gespraech_uebernommen()
    {
        Assert.True(HeadsetPolicy.StateFor([Anruf(CallStatus.Connected, muted: true)]).Stumm);

        // Ein eingehender Anruf hat noch kein Mikrofon im Spiel; ein dort
        // gesetztes Flag darf die Lampe nicht anschalten.
        Assert.False(HeadsetPolicy.StateFor([Anruf(CallStatus.Incoming, muted: true)]).Stumm);
    }

    [Fact]
    public void Beendete_Anrufe_zaehlen_nicht_mehr()
    {
        var state = HeadsetPolicy.StateFor(
            [Anruf(CallStatus.Ended), Anruf(CallStatus.Failed)]);

        Assert.False(state.ImGespraech);
        Assert.False(state.Klingelt);
    }

    // ---- Was ein Tastendruck bedeutet ----
    //
    // <b>Ohne Richtung, seit dem 09.09.2026.</b> Vorher hing die Bedeutung am
    // gemeldeten Gabelzustand: „abgenommen" nahm an, „aufgelegt" legte auf.
    // Das setzte voraus, dass der Zustand des Geraets und der von nipp
    // uebereinstimmen — und diese Voraussetzung war nicht zu halten. Jetzt
    // entscheidet allein der Anrufzustand, und ob eine Meldung ueberhaupt ein
    // Tastendruck war, entscheidet HookWatch.

    [Fact]
    public void Die_Taste_nimmt_an_waehrend_es_klingelt()
    {
        var klingelt = Anruf(CallStatus.Incoming);

        var (action, call) = HeadsetPolicy.Interpret([klingelt]);

        Assert.Equal(HeadsetAction.Annehmen, action);
        Assert.Equal(klingelt.Handle, call?.Handle);
    }

    /// <summary>
    /// Ein Druck ins Leere ist bedeutungslos, nicht falsch.
    /// </summary>
    [Fact]
    public void Die_Taste_ohne_Anruf_tut_nichts()
    {
        var (action, call) = HeadsetPolicy.Interpret([]);

        Assert.Equal(HeadsetAction.Nichts, action);
        Assert.Null(call);
    }

    [Fact]
    public void Die_Taste_beendet_das_verbundene_Gespraech()
    {
        var laufend = Anruf(CallStatus.Connected);

        var (action, call) = HeadsetPolicy.Interpret([laufend]);

        Assert.Equal(HeadsetAction.Auflegen, action);
        Assert.Equal(laufend.Handle, call?.Handle);
    }

    /// <summary>
    /// <b>Bei zwei Gesprächen zählt das im Vordergrund.</b> Dasselbe, das der
    /// Knopf in der Oberfläche auflegt — zwei verschiedene Bedeutungen für
    /// „auflegen" wären schlimmer als eine unvollständige.
    /// </summary>
    [Fact]
    public void Auflegen_trifft_das_verbundene_und_nicht_das_gehaltene()
    {
        var gehalten = Anruf(CallStatus.OnHold, nummer: "152");
        var verbunden = Anruf(CallStatus.Connected, nummer: "153");

        var (action, call) = HeadsetPolicy.Interpret([gehalten, verbunden]);

        Assert.Equal(HeadsetAction.Auflegen, action);
        Assert.Equal(verbunden.Handle, call?.Handle);
    }

    /// <summary>
    /// Ein ausgehender Anruf, der noch klingelt, lässt sich mit der Taste
    /// abbrechen. Das ist der Fall, für den die Regel „Dialing gilt als
    /// abgenommen" überhaupt gebaut ist.
    /// </summary>
    [Theory]
    [InlineData(CallStatus.Dialing)]
    [InlineData(CallStatus.Ringing)]
    public void Die_Taste_bricht_einen_ausgehenden_Anruf_ab(CallStatus status)
    {
        var (action, _) = HeadsetPolicy.Interpret([Anruf(status)]);

        Assert.Equal(HeadsetAction.Auflegen, action);
    }

    /// <summary>
    /// <b>Klingeln schlägt Gespräch</b> — wie bei den Lampen. Sonst hätte die
    /// Taste beim Klingeln zwei Bedeutungen, und das laufende Gespräch geht
    /// ohnehin auf Halten (§8.6).
    /// </summary>
    [Fact]
    public void Klingelt_es_neben_einem_Gespraech_nimmt_die_Taste_an()
    {
        var verbunden = Anruf(CallStatus.Connected, nummer: "152");
        var klingelt = Anruf(CallStatus.Incoming, nummer: "153");

        var (action, call) = HeadsetPolicy.Interpret([verbunden, klingelt]);

        Assert.Equal(HeadsetAction.Annehmen, action);
        Assert.Equal(klingelt.Handle, call?.Handle);
    }

    [Fact]
    public void Beendete_Anrufe_geben_der_Taste_keine_Bedeutung()
    {
        var (action, call) = HeadsetPolicy.Interpret(
            [Anruf(CallStatus.Ended), Anruf(CallStatus.Failed)]);

        Assert.Equal(HeadsetAction.Nichts, action);
        Assert.Null(call);
    }

    /// <summary>
    /// <b>T82, und jetzt als Test.</b> Ein Gespräch wird in der Oberfläche
    /// beendet — das Gerät erfährt davon nichts und steht weiter auf
    /// „abgenommen". Beim nächsten Anruf muss die Taste trotzdem annehmen.
    ///
    /// <para>Vorher war das die Stelle, an der ab dem ersten in der
    /// Oberfläche beendeten Gespräch jeder zweite Druck falsch war (ADR-028).
    /// Der Test kommt ohne Gerätezustand aus, weil die Regel es tut: <b>das
    /// ist die Reparatur.</b></para>
    /// </summary>
    [Fact]
    public void Nach_einem_in_der_Oberflaeche_beendeten_Gespraech_nimmt_die_Taste_an()
    {
        // Das Gespraech lief und wurde in der Oberflaeche beendet.
        var beendet = Anruf(CallStatus.Connected) with { Status = CallStatus.Ended };
        Assert.Equal(HeadsetAction.Nichts, HeadsetPolicy.Interpret([beendet]).Action);

        // Der naechste Anruf klingelt. Egal, was das Geraet von seiner Gabel
        // haelt: die Taste nimmt an.
        var klingelt = Anruf(CallStatus.Incoming, nummer: "154");

        var (action, call) = HeadsetPolicy.Interpret([beendet, klingelt]);

        Assert.Equal(HeadsetAction.Annehmen, action);
        Assert.Equal(klingelt.Handle, call?.Handle);
    }

    /// <summary>
    /// <b>Die Rundreise.</b> Klingeln, annehmen, auflegen — und danach steht
    /// alles wieder aus. Ein Zustand, der nach dem Auflegen „abgenommen"
    /// bliebe, hätte den nächsten Tastendruck um eins versetzt, und ab da wäre
    /// jeder zweite falsch.
    /// </summary>
    [Fact]
    public void Nach_dem_Auflegen_ist_der_Zustand_wieder_leer()
    {
        var klingelt = Anruf(CallStatus.Incoming);
        Assert.True(HeadsetPolicy.StateFor([klingelt]).Klingelt);

        var angenommen = klingelt with { Status = CallStatus.Connected };
        Assert.True(HeadsetPolicy.StateFor([angenommen]).ImGespraech);

        var beendet = angenommen with { Status = CallStatus.Ended };
        var state = HeadsetPolicy.StateFor([beendet]);

        Assert.False(state.ImGespraech);
        Assert.False(state.Klingelt);
    }
}
