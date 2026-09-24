using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Die Zustandsmaschine der Anrufe (W2.1, Etappe B1).
///
/// <para><b>Warum es diese Datei gibt.</b> Die Regeln hier haben dieses
/// Projekt je einen Tag gekostet, und bis zum 24.09.2026 standen sie als
/// Kommentare in einer 165 Zeilen langen Callback-Methode — prüfbar nur an
/// einer echten Anlage, weil jeder zweite Ausdruck darin einen laufenden
/// <c>Call</c> des SDK anfasste. Jetzt liest die Bridge das SDK einmal in
/// einen <c>CallSnapshot</c>, und was daraus folgt, steht in
/// <see cref="CallFlow"/> und hier.</para>
///
/// <para><b>Was diese Tests nicht ersetzen:</b> den Tag am Gerät. Sie prüfen,
/// was entschieden wird — nicht, ob das SDK danach tut, was es soll. Das
/// steht in <c>docs/test-matrix.md</c> (T04 bis T09) und bleibt dort.</para>
/// </summary>
public class CallFlowTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);

    private static CallSnapshot Snapshot(
        CallStatus? status,
        bool eingehendNeu = false,
        string number = "0791234567",
        string? displayName = null,
        bool earlyMedia = false,
        string? codec = null,
        CallEndReason? endReason = null,
        string message = "") =>
        new(
            Number: number,
            DisplayName: displayName,
            Status: status,
            Message: message,
            IsIncomingNew: eingehendNeu,
            EarlyMedia: earlyMedia,
            Codec: codec,
            Encryption: MediaEncryptionMode.None,
            EndReason: endReason,
            SdkAccountIdentity: null,
            ToAddress: null,
            RawState: status?.ToString() ?? "Zwischenschritt");

    private static CallInfo Anruf(
        CallStatus status,
        CallDirection direction = CallDirection.Incoming,
        DateTimeOffset? connectedAt = null,
        string? codec = null,
        string? displayName = null) =>
        new(
            Handle: CallHandle.New(),
            RemoteNumber: "0791234567",
            RemoteDisplayName: displayName,
            Direction: direction,
            Status: status,
            StatusMessage: null,
            StartedAt: Jetzt.AddMinutes(-1),
            ConnectedAt: connectedAt,
            IsMuted: false,
            IsRecording: false,
            Codec: codec,
            Encryption: MediaEncryptionMode.None);

    private static CallDecision Entscheide(
        CallSnapshot snapshot,
        CallInfo? tracked = null,
        int activeCalls = 0,
        bool autoAnswer = false,
        int max = 2,
        bool ringbackPlaying = false) =>
        CallFlow.Decide(snapshot, tracked, activeCalls, autoAnswer, max, ringbackPlaying);

    // --- Regel 1 ----------------------------------------------------------

    /// <summary>
    /// <b>Ein neuer Anruf hat keinen Vorzustand</b> — sonst ist ein
    /// eingehender Anruf unsichtbar.
    ///
    /// <para>Der Anruf wird mit <c>CallStatus.Incoming</c> angelegt, damit die
    /// Momentaufnahme von Anfang an stimmt. Würde derselbe Wert auch als
    /// «vorher» gemeldet, sähe jeder Empfänger, der auf den Übergang nach
    /// <c>Incoming</c> prüft, einen Anruf, der schon immer geklingelt hat: der
    /// Toast bleibt aus, und das Fenster wechselt nicht in die
    /// Gesprächsansicht.</para>
    /// </summary>
    [Fact]
    public void Ein_neuer_Anruf_hat_keinen_Vorzustand()
    {
        var entscheidung = Entscheide(Snapshot(CallStatus.Incoming, eingehendNeu: true));

        Assert.Equal(CallAction.Anlegen, entscheidung.Action);
        Assert.Null(entscheidung.Previous);
    }

    /// <summary>
    /// Die Gegenprobe: ein bekannter Anruf meldet seinen bisherigen Zustand
    /// als «vorher».
    /// </summary>
    [Fact]
    public void Ein_bekannter_Anruf_meldet_seinen_bisherigen_Zustand()
    {
        var entscheidung = Entscheide(Snapshot(CallStatus.Connected), Anruf(CallStatus.Incoming));

        Assert.Equal(CallAction.Aktualisieren, entscheidung.Action);
        Assert.Equal(CallStatus.Incoming, entscheidung.Previous);
    }

    // --- Regel 2 ----------------------------------------------------------

    /// <summary>
    /// <b>Der dritte Anruf wird abgelehnt</b> (§8.2) — und zwar als
    /// Entscheidung, nicht als Tat: das Ausführen ist Sache des Dienstes,
    /// weil ein <c>Decline</c> aus dem Callback heraus dieselbe Reentranz
    /// erzeugt, die bei der automatischen Annahme schon einmal einen echten
    /// Fehler ergeben hat.
    /// </summary>
    [Fact]
    public void Der_dritte_Anruf_wird_abgelehnt()
    {
        var entscheidung = Entscheide(
            Snapshot(CallStatus.Incoming, eingehendNeu: true),
            activeCalls: 2,
            max: 2);

        Assert.Equal(CallAction.Ablehnen, entscheidung.Action);
        Assert.False(entscheidung.AutoAnswer);
    }

    /// <summary>Der zweite geht durch — die Grenze ist zwei, nicht einer.</summary>
    [Fact]
    public void Der_zweite_Anruf_wird_angelegt()
    {
        var entscheidung = Entscheide(
            Snapshot(CallStatus.Incoming, eingehendNeu: true),
            activeCalls: 1,
            max: 2);

        Assert.Equal(CallAction.Anlegen, entscheidung.Action);
    }

    // --- Regel 3 ----------------------------------------------------------

    /// <summary>
    /// <b>Die automatische Annahme wird vorgemerkt, nicht ausgeführt.</b>
    /// </summary>
    [Fact]
    public void Die_automatische_Annahme_wird_vorgemerkt()
    {
        var entscheidung = Entscheide(
            Snapshot(CallStatus.Incoming, eingehendNeu: true),
            autoAnswer: true);

        Assert.Equal(CallAction.Anlegen, entscheidung.Action);
        Assert.True(entscheidung.AutoAnswer);
    }

    /// <summary>
    /// <b>Und sie gilt nicht für den abgelehnten dritten.</b> Automatisch
    /// annehmen, was gerade als drittes Gespräch abgelehnt wird, wäre ein
    /// Widerspruch — und im schlimmsten Fall ein offenes Mikrofon.
    /// </summary>
    [Fact]
    public void Der_abgelehnte_dritte_wird_nicht_automatisch_angenommen()
    {
        var entscheidung = Entscheide(
            Snapshot(CallStatus.Incoming, eingehendNeu: true),
            activeCalls: 2,
            autoAnswer: true,
            max: 2);

        Assert.Equal(CallAction.Ablehnen, entscheidung.Action);
        Assert.False(entscheidung.AutoAnswer);
    }

    // --- Regel 4 ----------------------------------------------------------

    /// <summary>
    /// <b>Ein Zwischenzustand ohne eigene Aussage lässt den bisherigen
    /// stehen.</b> Das SDK meldet mehr Schritte, als es Zustände gibt.
    /// </summary>
    [Fact]
    public void Ein_Zwischenzustand_laesst_den_bisherigen_stehen()
    {
        var bekannt = Anruf(CallStatus.Connected, connectedAt: Jetzt.AddMinutes(-1));
        var neu = CallFlow.Apply(bekannt, Snapshot(status: null), Jetzt);

        Assert.Equal(CallStatus.Connected, neu.Status);
    }

    /// <summary>
    /// Auch die Entscheidung darf daran nichts festmachen: ein
    /// Zwischenzustand beendet keinen Anruf und stoppt keinen Rufton.
    /// </summary>
    [Fact]
    public void Ein_Zwischenzustand_beendet_nichts()
    {
        var entscheidung = Entscheide(Snapshot(status: null), Anruf(CallStatus.Ringing), ringbackPlaying: true);

        Assert.False(entscheidung.Remove);
        Assert.False(entscheidung.StopRingback);
    }

    // --- Regel 5 ----------------------------------------------------------

    /// <summary>
    /// <b>Der Endgrund wird beim letzten Ereignis festgehalten</b> (§20.3) —
    /// danach ist der Anruf aus der Verwaltung, und «besetzt» oder
    /// «abgelehnt» stünde in der Anrufliste nirgends.
    /// </summary>
    [Fact]
    public void Der_Endgrund_wird_beim_letzten_Ereignis_festgehalten()
    {
        var neu = CallFlow.Apply(
            Anruf(CallStatus.Ringing),
            Snapshot(CallStatus.Ended, endReason: CallEndReason.Busy),
            Jetzt);

        Assert.Equal(CallStatus.Ended, neu.Status);
        Assert.Equal(CallEndReason.Busy, neu.EndReason);
    }

    /// <summary>
    /// <b>Und er steht nur dort.</b> Ein laufendes Gespräch trägt keinen
    /// Endgrund — sonst steht in der Anrufliste ein Grund für etwas, das
    /// gerade noch läuft.
    /// </summary>
    [Fact]
    public void Ein_laufendes_Gespraech_traegt_keinen_Endgrund()
    {
        var neu = CallFlow.Apply(
            Anruf(CallStatus.Connected),
            Snapshot(CallStatus.Connected, endReason: CallEndReason.Busy),
            Jetzt);

        Assert.Null(neu.EndReason);
    }

    /// <summary>Ein beendeter Anruf geht aus der Verwaltung.</summary>
    [Theory]
    [InlineData(CallStatus.Ended)]
    [InlineData(CallStatus.Failed)]
    public void Ein_beendeter_Anruf_wird_entfernt(CallStatus status)
    {
        var entscheidung = Entscheide(Snapshot(status), Anruf(CallStatus.Connected));

        Assert.True(entscheidung.Remove);
        Assert.True(entscheidung.ResumeLast);
    }

    // --- Regel 6 ----------------------------------------------------------

    /// <summary>
    /// <b>Ein eigener Rufton endet, sobald der Anruf nicht mehr läutet</b> —
    /// vorgemerkt, denn auch das Stoppen fasst das SDK an.
    /// </summary>
    [Fact]
    public void Der_eigene_Rufton_endet_sobald_es_nicht_mehr_laeutet()
    {
        var entscheidung = Entscheide(
            Snapshot(CallStatus.Connected),
            Anruf(CallStatus.Ringing, CallDirection.Outgoing),
            ringbackPlaying: true);

        Assert.True(entscheidung.StopRingback);
    }

    /// <summary>
    /// Solange es läutet, läuft er weiter — in beiden Zuständen, die «es
    /// läutet» bedeuten.
    /// </summary>
    [Theory]
    [InlineData(CallStatus.Dialing)]
    [InlineData(CallStatus.Ringing)]
    public void Waehrend_es_laeutet_laeuft_der_Rufton_weiter(CallStatus status)
    {
        var entscheidung = Entscheide(
            Snapshot(status),
            Anruf(CallStatus.Dialing, CallDirection.Outgoing),
            ringbackPlaying: true);

        Assert.False(entscheidung.StopRingback);
    }

    /// <summary>
    /// Und ohne laufenden Rufton gibt es nichts zu stoppen — eine
    /// Vormerkung, die ins Leere geht, kostet einen SDK-Aufruf pro Ereignis.
    /// </summary>
    [Fact]
    public void Ohne_laufenden_Rufton_wird_nichts_vorgemerkt()
    {
        var entscheidung = Entscheide(
            Snapshot(CallStatus.Connected),
            Anruf(CallStatus.Ringing, CallDirection.Outgoing),
            ringbackPlaying: false);

        Assert.False(entscheidung.StopRingback);
    }

    // --- Die Rennen, die am Gerät teuer sind -------------------------------

    /// <summary>
    /// <b>Ein unbekannter Anruf, der nicht eingehend ist, wird ignoriert.</b>
    /// Das ist der Rückfrageanruf einer abgeschlossenen Weiterleitung und
    /// jedes Nachzügler-Ereignis eines Anrufs, den nipp schon aus der
    /// Verwaltung genommen hat.
    /// </summary>
    [Theory]
    [InlineData(CallStatus.Connected)]
    [InlineData(CallStatus.Ended)]
    [InlineData(CallStatus.Ringing)]
    public void Ein_unbekannter_Anruf_ohne_Eingang_wird_ignoriert(CallStatus status)
    {
        var entscheidung = Entscheide(Snapshot(status));

        Assert.Equal(CallAction.Ignorieren, entscheidung.Action);
    }

    /// <summary>
    /// <b>Derselbe Zustand zweimal</b> ist kein Sonderfall, sondern der
    /// Normalfall: das SDK meldet <c>StreamsRunning</c> mehrfach. Es bleibt
    /// beim Aktualisieren, und «vorher» ist gleich «nachher» — daran erkennt
    /// der Dienst, dass keine Protokollzeile nötig ist.
    /// </summary>
    [Fact]
    public void Derselbe_Zustand_zweimal_bleibt_ein_Aktualisieren()
    {
        var bekannt = Anruf(CallStatus.Connected, connectedAt: Jetzt.AddMinutes(-1));
        var entscheidung = Entscheide(Snapshot(CallStatus.Connected), bekannt);

        Assert.Equal(CallAction.Aktualisieren, entscheidung.Action);
        Assert.Equal(CallStatus.Connected, entscheidung.Previous);
    }

    /// <summary>
    /// <b>Der Zeitpunkt des Verbindens wird einmal gesetzt.</b> Ein Halten
    /// und Zurückholen ist kein zweiter Gesprächsbeginn — an dieser Zeit
    /// hängt die Dauer in der Anrufliste.
    /// </summary>
    [Fact]
    public void Der_Verbindungszeitpunkt_wird_nur_einmal_gesetzt()
    {
        var zuerst = CallFlow.Apply(Anruf(CallStatus.Ringing), Snapshot(CallStatus.Connected), Jetzt);
        var gehalten = CallFlow.Apply(zuerst, Snapshot(CallStatus.OnHold), Jetzt.AddSeconds(30));
        var zurueck = CallFlow.Apply(gehalten, Snapshot(CallStatus.Connected), Jetzt.AddSeconds(60));

        Assert.Equal(Jetzt, zuerst.ConnectedAt);
        Assert.Equal(Jetzt, zurueck.ConnectedAt);
    }

    /// <summary>
    /// <b>Was nicht mitkommt, bleibt stehen.</b> Codec und Anzeigename meldet
    /// das SDK nicht bei jedem Ereignis; ein <c>null</c> ist dann keine
    /// Aussage, sondern eine Lücke.
    /// </summary>
    [Fact]
    public void Codec_und_Name_bleiben_stehen_wenn_sie_fehlen()
    {
        var bekannt = Anruf(CallStatus.Connected, codec: "PCMU", displayName: "Anna Muster");
        var neu = CallFlow.Apply(bekannt, Snapshot(CallStatus.Connected), Jetzt);

        Assert.Equal("PCMU", neu.Codec);
        Assert.Equal("Anna Muster", neu.RemoteDisplayName);
    }

    /// <summary>
    /// Die Gegenprobe dazu: was mitkommt, gewinnt. Sonst bliebe ein einmal
    /// falsch gelesener Codec bis zum Ende des Gesprächs stehen.
    /// </summary>
    [Fact]
    public void Ein_gemeldeter_Codec_gewinnt()
    {
        var bekannt = Anruf(CallStatus.Connected, codec: "PCMU");
        var neu = CallFlow.Apply(bekannt, Snapshot(CallStatus.Connected, codec: "opus"), Jetzt);

        Assert.Equal("opus", neu.Codec);
    }

    /// <summary>
    /// <b>Ein Anruf, der endet, während ein zweiter klingelt</b>, nimmt den
    /// gewöhnlichen Weg: er geht raus, der andere bleibt unberührt. Diese
    /// Klasse entscheidet je Ereignis über genau einen Anruf, und das ist der
    /// Grund, warum dieser Fall keiner ist.
    /// </summary>
    [Fact]
    public void Ein_endender_Anruf_laesst_den_zweiten_unberuehrt()
    {
        var erster = Anruf(CallStatus.Connected);
        var zweiter = Anruf(CallStatus.Incoming);

        var entscheidung = Entscheide(Snapshot(CallStatus.Ended), erster, activeCalls: 2);
        var fuerDenZweiten = Entscheide(Snapshot(CallStatus.Incoming), zweiter, activeCalls: 2);

        Assert.True(entscheidung.Remove);
        Assert.Equal(CallAction.Aktualisieren, fuerDenZweiten.Action);
        Assert.False(fuerDenZweiten.Remove);
    }
}
