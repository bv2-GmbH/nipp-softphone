using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>Was mit dem eigenen Rufton zu geschehen hat.</summary>
public enum RingbackAction
{
    /// <summary>Nichts.</summary>
    None,

    /// <summary>Den Rufton anfangen.</summary>
    Start,

    /// <summary>Aufhören.</summary>
    Stop,

    /// <summary>Von vorn anfangen — die Datei ist zu Ende, es läutet aber noch.</summary>
    Restart,
}

/// <summary>
/// Warum der eigene Rufton anfing oder aufhörte.
///
/// <para><b>Warum das eine Aufzählung ist und kein Text im Aufrufer.</b> Die
/// Startzeile im Protokoll behauptete bis zum 10.09.2026 „die Gegenstelle
/// schickt Early Media ohne Audio" — und lag zweimal falsch, weil sie den
/// Grund nicht kannte, sondern annahm. Eine Protokollzeile, die eine Ursache
/// behauptet, die sie nicht gemessen hat, ist schlechter als keine. Also
/// entscheidet die Stelle, die es weiss, und der Aufrufer schreibt nur
/// hin.</para>
/// </summary>
public enum RingbackGrund
{
    /// <summary>Noch nichts entschieden.</summary>
    Unbekannt,

    /// <summary>Start: von der Gegenstelle kommt gar kein Audiostrom.</summary>
    KeinStrom,

    /// <summary>
    /// Start: ein Strom kommt an, blieb aber über die lange Karenzzeit ohne
    /// messbaren Pegel.
    /// </summary>
    StillerStrom,

    /// <summary>Ende: die Anlage läutet selbst, und es ist hörbar.</summary>
    HoerbaresAudio,

    /// <summary>
    /// Start: die Gegenseite kündigt gar kein Early Media an (180 ohne SDP).
    ///
    /// <para><b>Dieser Grund war bis zum 25.09.2026 ein Ende</b>, mit der
    /// Begründung «dann spielt das SDK seinen eigenen Ton». Das war eine
    /// Annahme, und sie war falsch: das SDK baut den Ruftonstrom, hängt ihn
    /// aber an eine <b>Leersenke</b> statt an das Ausgabegerät, weil der
    /// Anrufstrom die Wiedergabekarte 532 ms vorher reserviert hat. Gemessen
    /// über drei Tage, in jedem einzelnen Fall (ADR-075).</para>
    /// </summary>
    KeinEarlyMedia,

    /// <summary>Ende: es wählt kein Anruf mehr.</summary>
    KeinWaehlenderAnruf,

    /// <summary>Ende: ein anderer Anruf wählt jetzt.</summary>
    AndererAnruf,
}

/// <summary>
/// Der Zustand eines wählenden Anrufs, soweit der Rufton ihn angeht.
/// </summary>
/// <param name="Call">Welcher Anruf.</param>
/// <param name="IsEarlyMedia">
/// Ob das SDK im Zustand <c>OutgoingEarlyMedia</c> ist. <b>Der entscheidende
/// Unterschied:</b> bei <c>OutgoingRinging</c> spielt das SDK seinen eigenen
/// Rufton, und ein zweiter wäre einer zu viel.
/// </param>
/// <param name="DownloadKbitPerSecond">
/// Was gerade an Audio hereinkommt. Kommt etwas, ist es das Läuten der Anlage,
/// und nipp hat zu schweigen.
/// </param>
/// <param name="PlayerIdle">Ob der eigene Tonspieler am Ende der Datei steht.</param>
/// <param name="PlayVolumeDb">
/// Der gemessene Pegel des empfangenen Signals in dBm0, oder <c>null</c>, wenn
/// er sich nicht ermitteln liess.
///
/// <para><b>Warum das zur Bandbreite dazukommt.</b> Am 08.09.2026 startete der
/// eigene Rufton und war <b>402 Millisekunden</b> spaeter wieder aus — ohne
/// dass sich der Anrufzustand geaendert haette. Die Bandbreite allein hatte
/// entschieden: es kam etwas an, also schwieg nipp. Zu hoeren war trotzdem
/// nichts, und der Jitter-Puffer konvergierte nie. <b>Ankommen und hoerbar
/// sein sind zweierlei</b>; ein Strom aus Stille ist kein Laeuten.</para>
/// </param>
/// <param name="ReceivedPackets">
/// Die Zahl der bisher empfangenen RTP-Pakete, oder <c>null</c>, wenn sie sich
/// nicht ermitteln liess.
///
/// <para><b>Warum das die Bandbreite nicht ersetzt, sondern ablöst.</b>
/// <c>DownloadBandwidth</c> ist ein <b>Sekundenmittel</b>, das liblinphone
/// genau einmal pro Sekunde neu berechnet — dieselbe Stelle, die
/// <c>Bandwidth usage for CallSession</c> schreibt; davor steht dort 0. Eine
/// Entscheidung nach 800 Millisekunden kann darauf nicht ruhen. Am 10.09.2026
/// gemessen: bei zwei Anrufen las nipp im Entscheidungsmoment eine 0, während
/// der Strom der Anlage bereits lief — einmal seit rund 200 ms —, und legte
/// seinen eigenen Ton für 168 beziehungsweise 193 ms über deren Rufton. Der
/// Paketzähler wird dagegen pro Paket geführt. Ist er nicht zu haben,
/// entscheidet wie bisher die Bandbreite.</para>
/// </param>
/// <param name="RingingSince">
/// Wann das SDK diesen Anruf als läutend gemeldet hat, oder <c>null</c>.
///
/// <para>Die Karenzzeit soll ab dem SIP-Ereignis laufen und nicht ab dem
/// ersten Durchlauf, der den Anruf zufällig sieht — am 10.09.2026 lagen
/// dazwischen 260 ms, weil der Aufbau des Audiostroms die Ereignisschleife
/// aufhielt. Ohne diesen Wert bedeutet die Konstante nicht, was sie
/// sagt.</para>
/// </param>
public sealed record RingbackSample(
    CallHandle Call,
    bool IsEarlyMedia,
    float DownloadKbitPerSecond,
    bool PlayerIdle,
    float? PlayVolumeDb = null,
    uint? ReceivedPackets = null,
    DateTimeOffset? RingingSince = null);

/// <summary>
/// Entscheidet, ob nipp beim Wählen selbst einen Rufton spielt (§9.4).
///
/// <para><b>Der Befund vom 07.09.2026.</b> Bei externen Anrufen war beim
/// Wählen nichts zu hören, während es beim Gegenüber längst läutete. Im
/// Protokoll fehlt für diese Anrufe <c>startRingbackTone</c> vollständig: der
/// Anruf geht <c>OutgoingProgress → OutgoingEarlyMedia</c>, weil die Anlage
/// ein <c>183 Session Progress</c> <b>mit SDP</b> schickt. Damit hält das SDK
/// Early Media für die Audioquelle und schweigt selbst — nur sendet die Anlage
/// dann kein RTP. Belegt am Jitter-Puffer, der nie konvergiert („stays
/// unconverged for one second", Puffergrösse 9 761 581 ms).</para>
///
/// <para><b>Warum das nicht am SDK zu richten ist.</b> Es gibt keinen
/// Schalter, ausgehendes Early Media abzulehnen — geprüft in
/// <c>include/linphone/core.h</c>: <c>set_ringback</c> setzt nur die Datei,
/// <c>set_remote_ringback_tone</c> ist der Ton <b>für die Gegenseite</b>, und
/// <c>set_ring_during_incoming_early_media</c> gilt nur für eingehende Anrufe.
/// Bleibt: selbst spielen, wenn nichts hereinkommt.</para>
///
/// <para><b>Der Befund vom 10.09.2026, und er dreht die Frage um.</b> „Beim
/// Wählen höre ich einen Ton, aber nicht den der Telefonanlage." Gemessen war
/// es zweimal ein Überlappen von <b>168 beziehungsweise 193 Millisekunden</b>
/// am Anfang des Anlagentons — nicht die Kadenz, wie zuerst vermutet: über
/// 5,07 Sekunden Early Media kamen 252 Pakete an, der Strom war lückenlos.
/// <b>Die Ursache war, dass nipp blind entschied.</b> Die Bandbreite, auf die
/// sich die Karenzzeit stützte, ist ein Sekundenmittel und stand im
/// Entscheidungsmoment beide Male auf 0, während der Strom der Anlage schon
/// lief. Seither entscheidet der Paketzähler, und ein <b>ankommender</b> Strom
/// bekommt eine eigene, längere Karenzzeit: <b>solange unentschieden ist, ob
/// die Anlage läutet, hat sie Vorrang.</b></para>
///
/// <para><b>Und die Karenzzeit selbst war zu kurz</b> — der Strom setzte 0,91
/// und 1,04 Sekunden nach dem Läuten ein, entschieden wurde nach 0,8. Ein
/// frischer Sensor allein hätte daran nichts geändert: wer zu früh entscheidet,
/// sieht auch mit dem besten Messgerät nichts.</para>
///
/// <para><b>Reine Zustandsmaschine, kein SDK-Typ.</b> Die Regel entscheidet
/// über einen Ton, den niemand in einem Test hört — also muss sie ohne Gerät
/// prüfbar sein. Das Spielen selbst macht <c>SipService</c> im <c>Pump()</c>,
/// nie in einem SDK-Callback (§14.1).</para>
/// </summary>
public sealed class RingbackWatch
{
    /// <summary>
    /// Wie lange gewartet wird, bevor nipp selbst anfängt — <b>wenn gar kein
    /// Strom hereinkommt.</b>
    ///
    /// <para>Nicht sofort: der Audiostrom der Anlage braucht einen Moment, bis
    /// die ersten Pakete durch sind, und ein Ton, der wieder abbricht, weil
    /// doch etwas kam, klingt nach Fehler.</para>
    ///
    /// <para><b>Warum nicht mehr 800 ms.</b> Am 10.09.2026 gemessen: der Strom
    /// der Anlage setzte <b>0,91 und 1,04 Sekunden</b> nach dem Läuten ein —
    /// also nach Ablauf der alten Karenzzeit. Ein frischer Sensor hilft dagegen
    /// nichts: <b>wer zu früh entscheidet, sieht auch mit dem besten Messgerät
    /// nichts.</b> 1,4 Sekunden decken beide Messwerte mit Abstand und bleiben
    /// deutlich unter den 2,5 Sekunden, die bei T78b vergingen, ohne dass
    /// überhaupt ein Paket kam.</para>
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(1400);

    /// <summary>
    /// Wie lange gewartet wird, wenn ein Strom <b>ankommt, aber ohne messbaren
    /// Pegel bleibt.</b>
    ///
    /// <para><b>Warum das zwei verschiedene Fälle sind.</b> Kommt nichts an,
    /// wird auch nichts kommen — das ist der Fall aus ADR-029, und dort darf
    /// nipp nach 800 ms einsetzen. Kommt dagegen ein Strom an, der noch still
    /// ist, kann das zweierlei bedeuten: echte Stille (dann soll nipp
    /// einspringen) oder der Rufton der Anlage, dessen erste Pakete noch
    /// unterwegs sind oder dessen Kadenz gerade in der Pause steht. Was davon
    /// zutrifft, entscheidet sich erst mit der Zeit — und **solange es
    /// unentschieden ist, hat der Ton der Anlage Vorrang**.</para>
    ///
    /// <para>Fünf Sekunden: länger als die längste Pause einer üblichen
    /// Ruftonkadenz (Schweiz, Deutschland und die USA läuten 1 s und schweigen
    /// 4 s), plus ein Durchlauf (200 ms) und der Nachlauf des Jitter-Puffers.
    /// Der Preis steht in ADR-029, Nachtrag vom 10.09.2026: schickt eine
    /// Anlage über die ganze Läutdauer RTP mit echter Stille, hört der Benutzer
    /// fünf Sekunden nichts. Dieser Fall ist in der Protokollhistorie des
    /// Projekts nie aufgetreten.</para>
    /// </summary>
    public static readonly TimeSpan SilentStreamGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ab wann eingehendes Audio als „die Anlage läutet selbst" gilt (kbit/s).
    ///
    /// Ein Sprachstrom in PCMU liegt bei 80; unter 5 kommt praktisch nichts an.
    /// </summary>
    public const float NetworkAudioKbitPerSecond = 5.0f;

    /// <summary>
    /// Ab welchem Pegel ein empfangener Strom als hoerbar gilt (dBm0).
    ///
    /// <para>Stille meldet linphone mit sehr tiefen Werten (bis
    /// <c>-120</c>); ein Rufton oder Sprache liegt deutlich darueber. −60
    /// trennt beides mit Abstand nach beiden Seiten.</para>
    /// </summary>
    public const float AudibleVolumeDb = -60.0f;

    private CallHandle? _call;
    private DateTimeOffset _since;
    private bool _playing;
    private bool _networkAudioSeen;
    private uint? _lastPackets;
    private bool _streamSeen;
    private TimeSpan? _streamSeenAfter;

    /// <summary>Ob nipp gerade selbst einen Rufton spielt.</summary>
    public bool IsPlaying => _playing;

    /// <summary>
    /// Warum der letzte Start oder das letzte Ende zustande kam — für das
    /// Protokoll.
    /// </summary>
    public RingbackGrund Grund { get; private set; }

    /// <summary>
    /// Wie lange vor dem Start gewartet wurde. Nur nach einem
    /// <see cref="RingbackAction.Start"/> aussagekräftig.
    /// </summary>
    public TimeSpan Gewartet { get; private set; }

    /// <summary>
    /// Wie lange nach dem Läuten der erste Audiostrom der Gegenstelle einsetzte,
    /// oder <c>null</c>, solange keiner kam.
    ///
    /// <para>Der Messwert, der die Frage vom 10.09.2026 direkt entschieden
    /// hätte, statt ihn aus einem Sekundenmittel zurückzurechnen.</para>
    /// </summary>
    public TimeSpan? StromBeginn => _streamSeenAfter;

    /// <summary>
    /// Ein Durchlauf. <paramref name="sample"/> ist <c>null</c>, wenn gerade
    /// kein Anruf wählt — dann endet ein laufender Ton.
    /// </summary>
    public RingbackAction Update(RingbackSample? sample, DateTimeOffset now)
    {
        if (sample is null)
        {
            return Reset(RingbackGrund.KeinWaehlenderAnruf);
        }

        if (_call != sample.Call)
        {
            // Ein anderer Anruf: alles von vorn. Ein laufender Ton gehörte zum
            // vorigen und muss zuerst enden — der nächste Durchlauf fängt dann
            // sauber an.
            var vorheriger = Reset(RingbackGrund.AndererAnruf);

            _call = sample.Call;
            _since = now;

            if (vorheriger == RingbackAction.Stop)
            {
                return vorheriger;
            }
        }

        // <b>Kommt gerade etwas herein?</b> Der Paketzähler steigt pro Paket,
        // die Bandbreite nur einmal pro Sekunde — deshalb entscheidet der
        // Zähler, wo er zu haben ist, und die Bandbreite bleibt der Rückfall.
        // Ein Zähler, der beim ersten Durchlauf noch keinen Vorgänger hat,
        // sagt noch nichts: dann zählt für diesen Durchlauf die Bandbreite.
        var stromJetzt = sample.ReceivedPackets is { } pakete && _lastPackets is { } vorher
            ? pakete > vorher
            : sample.DownloadKbitPerSecond > NetworkAudioKbitPerSecond;

        _lastPackets = sample.ReceivedPackets;

        // Es kommt HOERBARES Audio herein: das ist das Läuten der Anlage. Ab
        // jetzt schweigt nipp für diesen Anruf, auch wenn der Strom später
        // wieder abreisst — sonst setzte der eigene Ton mitten im fremden ein.
        //
        // Beide Bedingungen zusammen, und das ist die Korrektur vom
        // 08.09.2026: die Bandbreite allein hat den eigenen Rufton nach 402 ms
        // abgewürgt, obwohl nichts zu hören war. Ist der Pegel nicht messbar,
        // zählt wie bisher die Bandbreite — dann ist die Bandbreite das Beste,
        // was wir haben.
        if (sample.DownloadKbitPerSecond > NetworkAudioKbitPerSecond
            && (sample.PlayVolumeDb is null || sample.PlayVolumeDb > AudibleVolumeDb))
        {
            _networkAudioSeen = true;

            return StopIfPlaying(RingbackGrund.HoerbaresAudio);
        }

        if (_networkAudioSeen)
        {
            return RingbackAction.None;
        }

        // <b>Hier stand bis zum 25.09.2026 ein Abbruch</b>: «kein Early Media
        // heisst, das SDK spielt seinen eigenen Rufton — dann ist hier nichts
        // zu tun.» Der Satz klang richtig und war nie gemessen. Gemessen wurde
        // er, als der Befund «beim Rauswählen höre ich nicht immer das Tuten»
        // kam: das SDK baut den Strom, hängt ihn aber an eine <b>Leersenke</b>
        // (MSVoidSink) statt an das Ausgabegerät, weil der Anrufstrom die
        // Wiedergabekarte 532 ms vorher für sich reserviert hat. Beim
        // eingehenden Klingeln endet dieselbe Kette in MSWASAPIWrite — der
        // Unterschied ist eine Zeile im Trace (ADR-075).
        //
        // <b>Ohne Early Media kommt also von niemandem ein Ton</b>, und der
        // Fall fällt jetzt in dieselbe Regel wie «kein Strom»: nach der
        // Karenzzeit spielt nipp selbst. Die Karenzzeit bleibt, und sie hat
        // hier denselben Zweck wie dort — abwarten, ob die Gegenseite doch
        // noch auf Early Media umschwenkt; gemessen setzt deren Strom nach
        // rund 420 ms ein, die Karenz ist dreimal so lang.

        // <b>Ein Strom, der einmal lief, gilt weiter als vorhanden</b> — auch
        // wenn er abreisst. Sonst fiele nipp mitten im Läuten auf die kurze
        // Karenzzeit zurück und setzte in einer Kadenzpause ein, also genau
        // dort, wo der Ton der Anlage gleich weitergeht.
        var seit = sample.RingingSince ?? _since;

        if (stromJetzt && !_streamSeen)
        {
            _streamSeen = true;
            _streamSeenAfter = now - seit;
        }

        if (!_playing)
        {
            var karenz = _streamSeen ? SilentStreamGrace : Grace;
            var gewartet = now - seit;

            if (gewartet < karenz)
            {
                return RingbackAction.None;
            }

            _playing = true;
            Gewartet = gewartet;

            // Drei Gründe, drei verschiedene Zeilen im Protokoll — sonst ist
            // «die Anlage schickt nichts» nicht von «sie kündigt nicht einmal
            // etwas an» zu unterscheiden, und genau das hat hier vier Tage
            // gekostet.
            Grund = _streamSeen
                ? RingbackGrund.StillerStrom
                : sample.IsEarlyMedia
                    ? RingbackGrund.KeinStrom
                    : RingbackGrund.KeinEarlyMedia;

            return RingbackAction.Start;
        }

        // Die Datei ist zehn Sekunden lang; wer länger läuten lässt, soll
        // weiter etwas hören.
        return sample.PlayerIdle ? RingbackAction.Restart : RingbackAction.None;
    }

    /// <summary>
    /// Vergessen, was war — beim Beenden des Dienstes und bei einem neuen Core.
    /// </summary>
    public RingbackAction Reset() => Reset(RingbackGrund.KeinWaehlenderAnruf);

    private RingbackAction Reset(RingbackGrund grund)
    {
        var action = StopIfPlaying(grund);

        _call = null;
        _since = default;
        _networkAudioSeen = false;
        _lastPackets = null;
        _streamSeen = false;
        _streamSeenAfter = null;

        return action;
    }

    private RingbackAction StopIfPlaying(RingbackGrund grund)
    {
        if (!_playing)
        {
            return RingbackAction.None;
        }

        _playing = false;
        Grund = grund;

        return RingbackAction.Stop;
    }
}
