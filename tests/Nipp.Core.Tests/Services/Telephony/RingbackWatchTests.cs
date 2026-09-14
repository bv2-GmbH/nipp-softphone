using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Wann nipp beim Wählen selbst einen Rufton spielt.
///
/// <para>Diese Regel steht hier, weil ein Ton in einem Test nicht zu hören
/// ist — und weil die Sache am Gerät schon einmal falsch abgenommen wurde:
/// T78 galt als bestanden, obwohl die geprüften Anrufe intern waren und gar
/// keinen Rufzustand hatten. Der Fehler zeigt sich nur bei externen Anrufen,
/// bei denen die Anlage Early Media ohne Audio schickt.</para>
///
/// <para>Die beiden Fehler, die hier weh täten: zwei Ruftöne gleichzeitig
/// (der eigene über den der Anlage), und ein Rufton, der ins angenommene
/// Gespräch hineinspielt.</para>
/// </summary>
public class RingbackWatchTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly CallHandle Anruf = CallHandle.New();

    private static RingbackSample EarlyMedia(
        float kbit = 0f,
        bool playerIdle = false,
        CallHandle? call = null,
        float? volumeDb = null,
        uint? packets = null,
        DateTimeOffset? ringingSince = null) =>
        new(
            call ?? Anruf,
            IsEarlyMedia: true,
            kbit,
            playerIdle,
            volumeDb,
            packets,
            ringingSince);

    private static RingbackSample Laeutet(float kbit = 0f) =>
        new(Anruf, IsEarlyMedia: false, kbit, PlayerIdle: false);

    [Fact]
    public void Ohne_waehlenden_Anruf_geschieht_nichts()
    {
        var watch = new RingbackWatch();

        Assert.Equal(RingbackAction.None, watch.Update(null, T0));
        Assert.False(watch.IsPlaying);
    }

    [Fact]
    public void In_der_Karenzzeit_wird_noch_nicht_gespielt()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);

        Assert.Equal(
            RingbackAction.None,
            watch.Update(EarlyMedia(), T0 + TimeSpan.FromMilliseconds(700)));
    }

    [Fact]
    public void Early_Media_ohne_Audio_startet_den_eigenen_Rufton()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);

        Assert.Equal(
            RingbackAction.Start,
            watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace));
        Assert.True(watch.IsPlaying);
    }

    /// <summary>
    /// Der Fall, den es nie geben darf: das SDK spielt bei
    /// <c>OutgoingRinging</c> selbst, und ein zweiter Ton darüber wäre lauter,
    /// nicht deutlicher.
    /// </summary>
    [Fact]
    public void Ohne_Early_Media_bleibt_nipp_still()
    {
        var watch = new RingbackWatch();

        watch.Update(Laeutet(), T0);

        Assert.Equal(RingbackAction.None, watch.Update(Laeutet(), T0 + TimeSpan.FromSeconds(5)));
        Assert.False(watch.IsPlaying);
    }

    [Fact]
    public void Schickt_die_Anlage_Audio_wird_nicht_gestartet()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(kbit: 80f), T0);

        Assert.Equal(
            RingbackAction.None,
            watch.Update(EarlyMedia(kbit: 80f), T0 + TimeSpan.FromSeconds(5)));
        Assert.False(watch.IsPlaying);
    }

    [Fact]
    public void Kommt_Audio_erst_spaeter_endet_der_eigene_Rufton()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        Assert.Equal(
            RingbackAction.Stop,
            watch.Update(EarlyMedia(kbit: 80f), T0 + TimeSpan.FromSeconds(2)));
        Assert.False(watch.IsPlaying);
    }

    /// <summary>
    /// Reisst der Strom der Anlage wieder ab, fängt nipp nicht doch noch an.
    /// Ein Rufton, der mitten in ein fremdes Läuten einsetzt, klingt nach
    /// Fehler — und wäre einer.
    /// </summary>
    [Fact]
    public void Nach_Audio_von_der_Anlage_bleibt_es_dabei()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(kbit: 80f), T0);

        Assert.Equal(
            RingbackAction.None,
            watch.Update(EarlyMedia(), T0 + TimeSpan.FromSeconds(3)));
        Assert.False(watch.IsPlaying);
    }

    [Fact]
    public void Endet_der_Anruf_endet_der_Ton()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        Assert.Equal(RingbackAction.Stop, watch.Update(null, T0 + TimeSpan.FromSeconds(2)));
        Assert.False(watch.IsPlaying);
    }

    [Fact]
    public void Am_Ende_der_Datei_wird_neu_begonnen()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        Assert.Equal(
            RingbackAction.Restart,
            watch.Update(EarlyMedia(playerIdle: true), T0 + TimeSpan.FromSeconds(11)));
        Assert.True(watch.IsPlaying);
    }

    [Fact]
    public void Ein_zweiter_Anruf_beendet_den_Ton_des_ersten()
    {
        var watch = new RingbackWatch();
        var anderer = CallHandle.New();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        Assert.Equal(
            RingbackAction.Stop,
            watch.Update(EarlyMedia(call: anderer), T0 + TimeSpan.FromSeconds(2)));

        // Und der neue Anruf bekommt seine eigene Karenzzeit, nicht die des
        // vorigen.
        Assert.Equal(
            RingbackAction.None,
            watch.Update(EarlyMedia(call: anderer), T0 + TimeSpan.FromSeconds(2.2)));
        Assert.Equal(
            RingbackAction.Start,
            watch.Update(
                EarlyMedia(call: anderer),
                T0 + TimeSpan.FromSeconds(2) + RingbackWatch.Grace));
    }

    [Fact]
    public void Zuruecksetzen_beendet_einen_laufenden_Ton()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        Assert.Equal(RingbackAction.Stop, watch.Reset());
        Assert.Equal(RingbackAction.None, watch.Reset());
    }

    [Fact]
    public void Ein_stiller_Strom_bringt_den_eigenen_Rufton_nicht_zum_Schweigen()
    {
        // Der gemessene Fall vom 08.09.2026: der eigene Rufton lief 402 ms und
        // war dann aus, ohne dass sich der Anrufzustand geaendert haette. Es
        // kam Bandbreite an — hoerbar war nichts, der Jitter-Puffer
        // konvergierte nie. Ankommen und Laeuten sind zweierlei.
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        Assert.Equal(RingbackAction.Start, watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace));

        var weiter = watch.Update(
            EarlyMedia(kbit: 40f, volumeDb: -90f),
            T0 + TimeSpan.FromSeconds(2));

        Assert.Equal(RingbackAction.None, weiter);
        Assert.True(watch.IsPlaying, "Ein Strom aus Stille ist kein Laeuten der Anlage.");
    }

    [Fact]
    public void Hoerbares_Audio_der_Anlage_beendet_den_eigenen_Rufton()
    {
        // Die Gegenprobe, und sie ist der Grund, warum die Regel ueberhaupt
        // existiert: zwei Ruftoene uebereinander klingen kaputt.
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        var stopp = watch.Update(
            EarlyMedia(kbit: 80f, volumeDb: -25f),
            T0 + TimeSpan.FromSeconds(2));

        Assert.Equal(RingbackAction.Stop, stopp);
        Assert.False(watch.IsPlaying);
    }

    [Fact]
    public void Ohne_messbaren_Pegel_entscheidet_weiterhin_die_Bandbreite()
    {
        // Faellt die Pegelmessung aus, ist die Bandbreite das Beste, was wir
        // haben — dann gilt die alte Regel unveraendert.
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(), T0);
        watch.Update(EarlyMedia(), T0 + RingbackWatch.Grace);

        var stopp = watch.Update(
            EarlyMedia(kbit: 80f, volumeDb: null),
            T0 + TimeSpan.FromSeconds(2));

        Assert.Equal(RingbackAction.Stop, stopp);
    }

    /// <summary>
    /// <b>Der gemessene Fall vom 10.09.2026</b>, mit den echten Zahlen: die
    /// Anlage schickt Early Media, ihr Strom setzt nach 1,04 s ein, hörbar wird
    /// er nach 1,24 s. nipp hatte damals nach 0,8 s begonnen und seinen Ton
    /// 168 ms über den der Anlage gelegt — weil es auf ein Sekundenmittel
    /// schaute, das noch auf 0 stand.
    /// </summary>
    [Fact]
    public void Der_Fall_vom_10_09_2026_nipp_schweigt_wenn_der_Strom_anlaeuft()
    {
        var watch = new RingbackWatch();
        uint pakete = 0;

        for (var ms = 0; ms <= 2000; ms += 200)
        {
            // Ab 1,04 s kommen Pakete, ab 1,24 s ist ein Pegel messbar.
            if (ms >= 1040)
            {
                pakete += 10;
            }

            var pegel = ms >= 1240 ? -5.3f : (float?)null;
            var jetzt = T0 + TimeSpan.FromMilliseconds(ms);

            var action = watch.Update(
                EarlyMedia(
                    kbit: ms >= 1240 ? 15.6f : 0f,
                    volumeDb: pegel,
                    packets: pakete,
                    ringingSince: T0),
                jetzt);

            Assert.NotEqual(RingbackAction.Start, action);
        }

        Assert.False(watch.IsPlaying, "Die Anlage laeutet — nipp hat zu schweigen.");

        // Und der Beginn ihres Stroms steht als Messwert bereit, statt aus
        // einem Sekundenmittel zurückgerechnet zu werden.
        Assert.Equal(TimeSpan.FromMilliseconds(1200), watch.StromBeginn);
    }

    /// <summary>
    /// Ein Strom, der ankommt, aber still bleibt: nipp wartet die lange
    /// Karenzzeit ab, weil es die Pause einer Ruftonkadenz sein kann.
    /// </summary>
    [Fact]
    public void Ein_ankommender_Strom_verschiebt_den_Start()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(packets: 0, ringingSince: T0), T0);
        watch.Update(
            EarlyMedia(packets: 10, volumeDb: -95f, ringingSince: T0),
            T0 + TimeSpan.FromMilliseconds(200));

        Assert.Equal(
            RingbackAction.None,
            watch.Update(
                EarlyMedia(packets: 20, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.Grace));

        Assert.Equal(
            RingbackAction.None,
            watch.Update(
                EarlyMedia(packets: 30, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.SilentStreamGrace - TimeSpan.FromMilliseconds(200)));

        Assert.Equal(
            RingbackAction.Start,
            watch.Update(
                EarlyMedia(packets: 40, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.SilentStreamGrace));
        Assert.Equal(RingbackGrund.StillerStrom, watch.Grund);
    }

    /// <summary>
    /// T78b: die Anlage schickte 2,5 s lang kein einziges Paket. Dann gilt die
    /// kurze Karenzzeit — dort ist nichts abzuwarten, weil nichts kommt.
    /// </summary>
    [Fact]
    public void Ohne_jeden_Strom_bleibt_die_kurze_Karenz()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(packets: 0, ringingSince: T0), T0);

        Assert.Equal(
            RingbackAction.Start,
            watch.Update(EarlyMedia(packets: 0, ringingSince: T0), T0 + RingbackWatch.Grace));
        Assert.Equal(RingbackGrund.KeinStrom, watch.Grund);
        Assert.Equal(RingbackWatch.Grace, watch.Gewartet);
    }

    /// <summary>
    /// Die Fähigkeit aus ADR-029 bleibt: ein Strom, der über die lange
    /// Karenzzeit stumm bleibt, ist echte Stille — dann spielt nipp doch.
    /// </summary>
    [Fact]
    public void Ein_Strom_der_stumm_bleibt_bringt_den_eigenen_Rufton_doch_noch()
    {
        var watch = new RingbackWatch();
        uint pakete = 0;
        RingbackAction letzte = RingbackAction.None;

        for (var ms = 0; ms <= 5000; ms += 200)
        {
            pakete += 10;

            letzte = watch.Update(
                EarlyMedia(
                    kbit: 64f,
                    volumeDb: -95f,
                    packets: pakete,
                    ringingSince: T0),
                T0 + TimeSpan.FromMilliseconds(ms));
        }

        Assert.Equal(RingbackAction.Start, letzte);
        Assert.True(watch.IsPlaying);
    }

    /// <summary>
    /// Die Kadenz der Anlage: 1 s Ton, dann 4 s Pause. Nach dem ersten
    /// hörbaren Ton schweigt nipp endgültig — die Pause ist keine Stille,
    /// sondern der Takt.
    /// </summary>
    [Fact]
    public void Eine_Pause_in_der_Kadenz_der_Anlage_startet_nichts()
    {
        var watch = new RingbackWatch();
        uint pakete = 0;

        for (var ms = 0; ms <= 6000; ms += 200)
        {
            pakete += 10;

            // Hörbar von 0,8 bis 1,8 s, danach vier Sekunden Pause.
            var hoerbar = ms is >= 800 and <= 1800;

            var action = watch.Update(
                EarlyMedia(
                    kbit: 64f,
                    volumeDb: hoerbar ? -6f : -95f,
                    packets: pakete,
                    ringingSince: T0),
                T0 + TimeSpan.FromMilliseconds(ms));

            Assert.NotEqual(RingbackAction.Start, action);
        }

        Assert.False(watch.IsPlaying);
    }

    /// <summary>
    /// Reisst ein Strom ab, fällt nipp <b>nicht</b> auf die kurze Karenzzeit
    /// zurück: dass überhaupt etwas kam, bleibt die bessere Auskunft.
    /// </summary>
    [Fact]
    public void Ein_Strom_der_abreisst_bekommt_nicht_die_kurze_Karenz_zurueck()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(packets: 0, ringingSince: T0), T0);
        watch.Update(
            EarlyMedia(packets: 10, volumeDb: -95f, ringingSince: T0),
            T0 + TimeSpan.FromMilliseconds(400));

        // Ab hier steht der Zähler still — der Strom ist weg.
        Assert.Equal(
            RingbackAction.None,
            watch.Update(
                EarlyMedia(packets: 10, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.Grace));

        Assert.Equal(
            RingbackAction.Start,
            watch.Update(
                EarlyMedia(packets: 10, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.SilentStreamGrace));
    }

    /// <summary>
    /// Die Rückfallebene: ohne Paketzähler entscheidet die Bandbreite darüber,
    /// ob ein Strom läuft — wie vor dem 10.09.2026.
    /// </summary>
    [Fact]
    public void Ohne_Paketzaehler_entscheidet_die_Bandbreite_ueber_den_Strom()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(kbit: 64f, volumeDb: -95f, ringingSince: T0), T0);

        Assert.Equal(
            RingbackAction.None,
            watch.Update(
                EarlyMedia(kbit: 64f, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.Grace));

        Assert.Equal(
            RingbackAction.Start,
            watch.Update(
                EarlyMedia(kbit: 64f, volumeDb: -95f, ringingSince: T0),
                T0 + RingbackWatch.SilentStreamGrace));
    }

    /// <summary>
    /// Die Karenzzeit läuft ab dem SIP-Ereignis. Am 10.09.2026 lagen zwischen
    /// dem Läuten und dem ersten Durchlauf 260 ms, weil der Aufbau des
    /// Audiostroms die Ereignisschleife aufhielt.
    /// </summary>
    [Fact]
    public void Die_Karenz_laeuft_ab_dem_Laeuten_nicht_ab_dem_ersten_Takt()
    {
        var watch = new RingbackWatch();
        var laeutetSeit = T0 - TimeSpan.FromMilliseconds(260);

        watch.Update(EarlyMedia(packets: 0, ringingSince: laeutetSeit), T0);

        Assert.Equal(
            RingbackAction.Start,
            watch.Update(
                EarlyMedia(packets: 0, ringingSince: laeutetSeit),
                laeutetSeit + RingbackWatch.Grace));
    }

    /// <summary>
    /// Ein zweiter Anruf fängt beim Zähler von vorn an — sonst wirkte der
    /// Stand des Vorgängers wie ein laufender Strom.
    /// </summary>
    [Fact]
    public void Ein_zweiter_Anruf_setzt_den_Paketzaehler_zurueck()
    {
        var watch = new RingbackWatch();
        var anderer = CallHandle.New();

        watch.Update(EarlyMedia(packets: 500, ringingSince: T0), T0);
        watch.Update(
            EarlyMedia(packets: 510, volumeDb: -95f, ringingSince: T0),
            T0 + TimeSpan.FromMilliseconds(200));

        var zweiterBeginn = T0 + TimeSpan.FromSeconds(2);

        watch.Update(
            EarlyMedia(call: anderer, packets: 0, ringingSince: zweiterBeginn),
            zweiterBeginn);

        // Der neue Anruf hat keinen Strom, also die kurze Karenzzeit — trotz
        // des hohen Zählerstands beim vorigen.
        Assert.Equal(
            RingbackAction.Start,
            watch.Update(
                EarlyMedia(call: anderer, packets: 0, ringingSince: zweiterBeginn),
                zweiterBeginn + RingbackWatch.Grace));
        Assert.Equal(RingbackGrund.KeinStrom, watch.Grund);
    }

    /// <summary>
    /// Der Beginn des fremden Stroms wird gemessen und nicht geschätzt — das
    /// ist die Zahl, die am 10.09.2026 im Protokoll fehlte.
    /// </summary>
    [Fact]
    public void Der_Beginn_des_fremden_Stroms_wird_gemessen()
    {
        var watch = new RingbackWatch();

        watch.Update(EarlyMedia(packets: 0, ringingSince: T0), T0);
        Assert.Null(watch.StromBeginn);

        watch.Update(
            EarlyMedia(packets: 10, volumeDb: -95f, ringingSince: T0),
            T0 + TimeSpan.FromMilliseconds(1040));

        Assert.Equal(TimeSpan.FromMilliseconds(1040), watch.StromBeginn);
    }
}
