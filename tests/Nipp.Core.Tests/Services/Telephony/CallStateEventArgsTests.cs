using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Die Übergänge eines Anrufs (§8.6, §20.3).
///
/// <b>Warum es diese Tests gibt.</b> Die Frage „beginnt hier ein Anruf zu
/// klingeln?" stand wörtlich gleich in <c>MainWindow</c> und im
/// <c>ToastService</c> — und war an beiden Stellen falsch. Sie verglich den
/// Vorzustand mit dem Wert, den der Telefoniedienst dem Anruf beim Anlegen
/// bereits gibt, und traf deshalb <b>nie</b> zu: kein Toast, kein Wechsel in
/// die Gesprächsansicht. Ein eingehender Anruf war unsichtbar, und weil nipp
/// im Infobereich lebt, blieb es dabei.
///
/// Jetzt steht die Regel einmal, am Ereignis selbst, und wird hier geprüft.
/// </summary>
public sealed class CallStateEventArgsTests
{
    private static CallInfo Call(CallStatus status, CallDirection direction = CallDirection.Incoming) =>
        new(
            Handle: CallHandle.New(),
            RemoteNumber: "151",
            RemoteDisplayName: null,
            Direction: direction,
            Status: status,
            StatusMessage: null,
            StartedAt: DateTimeOffset.UtcNow,
            ConnectedAt: status == CallStatus.Connected ? DateTimeOffset.UtcNow : null,
            IsMuted: false,
            IsRecording: false,
            Codec: null,
            Encryption: MediaEncryptionMode.Unknown);

    [Fact]
    public void Ein_neuer_eingehender_Anruf_ist_ein_neuer_eingehender_Anruf()
    {
        // Der Fehler, der nipp als Telefon unbrauchbar machte: der Dienst legt
        // den Anruf schon mit „Incoming" an, meldete denselben Wert aber auch
        // als Vorzustand. Ein neuer Anruf hat keinen — deshalb null.
        var e = new CallStateEventArgs(Call(CallStatus.Incoming), Previous: null);

        Assert.True(e.IsNewIncoming);
    }

    [Fact]
    public void Eine_zweite_Meldung_zum_selben_klingelnden_Anruf_ist_nicht_neu()
    {
        // Sonst erschiene bei jedem Zustandsbericht ein neuer Toast — das SDK
        // meldet waehrend des Klingelns mehrfach.
        var e = new CallStateEventArgs(Call(CallStatus.Incoming), CallStatus.Incoming);

        Assert.False(e.IsNewIncoming);
    }

    [Fact]
    public void Ein_ausgehender_Anruf_ist_kein_eingehender()
    {
        var e = new CallStateEventArgs(
            Call(CallStatus.Dialing, CallDirection.Outgoing),
            Previous: null);

        Assert.False(e.IsNewIncoming);
    }

    [Fact]
    public void Ein_ausgehender_Anruf_loest_keinen_Toast_aus()
    {
        // Die ganze Kette eines ausgehenden Anrufs, wie sie im Protokoll steht:
        // Dialing -> Ringing -> Connected. Kein Schritt davon darf einen Toast
        // erzeugen — der gehoert eingehenden Anrufen (§8.6).
        //
        // Dass die Gespraechsansicht trotzdem erscheinen muss, haengt an einer
        // anderen Regel: das Fenster merkt sich, welche Anrufe es schon gezeigt
        // hat. Genau hier lag der Fehler — die Navigation hing frueher an
        // diesen Uebergaengen, und "Ringing -> Connected" stand in keiner
        // Liste.
        var kette = new[]
        {
            new CallStateEventArgs(Call(CallStatus.Dialing, CallDirection.Outgoing), null),
            new CallStateEventArgs(Call(CallStatus.Ringing, CallDirection.Outgoing), CallStatus.Dialing),
            new CallStateEventArgs(Call(CallStatus.Connected, CallDirection.Outgoing), CallStatus.Ringing),
        };

        Assert.All(kette, static e => Assert.False(e.IsNewIncoming));
    }

    [Fact]
    public void Ein_beendetes_Gespraech_endet_genau_einmal()
    {
        // §20.3: sonst stuende derselbe Anruf zweimal in der Liste.
        var erste = new CallStateEventArgs(Call(CallStatus.Ended), CallStatus.Connected);
        var zweite = new CallStateEventArgs(Call(CallStatus.Ended), CallStatus.Ended);

        Assert.True(erste.HasJustEnded);
        Assert.False(zweite.HasJustEnded);
    }

    [Fact]
    public void Ein_Anruf_der_sofort_scheitert_endet_ebenfalls()
    {
        // Ohne Vorzustand — etwa ein eingehender Anruf, der schon beim ersten
        // Bericht abgebrochen ist. Er gehoert in die Anrufliste.
        var e = new CallStateEventArgs(Call(CallStatus.Failed), Previous: null);

        Assert.True(e.HasJustEnded);
    }
}
