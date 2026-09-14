using Linphone;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;

using LinphoneCore = Linphone.Core;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Überträgt die Einstellungen auf den laufenden Core (AP5.5, §9).
///
/// §12 (M4) verlangt: „sofortige Wirksamkeit ohne Neustart (wo möglich),
/// Neustart-Hinweis wo nicht." Diese Klasse weiss, was zu welcher Gruppe
/// gehört — <see cref="Apply"/> setzt alles Sofortwirksame,
/// <see cref="RequiresRestart"/> sagt, was liegen bleibt.
///
/// §6: eine der Dateien mit <c>using Linphone</c>, deshalb unter
/// <c>Services/Telephony/</c>.
/// </summary>
public sealed class SettingsApplier(ILogger<SettingsApplier> logger, RootCertificates rootCertificates)
{
    /// <summary>
    /// Wendet an, was sich im Betrieb ändern lässt.
    /// </summary>
    public void Apply(LinphoneCore core, NippSettings settings)
    {
        ApplyNetwork(core, settings.Network);
        ApplyNatMedia(core, settings.NatMedia);
        ApplyAudio(core, settings.Audio);
        ApplyCodecs(core, settings.Codecs);

        SettingsApplierLog.Applied(logger);
    }

    /// <summary>
    /// Nur die Audiowahl — für den Gerätewechsel im Betrieb (§9.4).
    ///
    /// <para><b>Warum es das braucht.</b> Der Hotplug rief bisher
    /// <see cref="Apply"/> mit einem frisch gebauten <c>NippSettings</c>, in dem
    /// nur <c>Audio</c> gefüllt war. <c>Apply</c> überträgt aber immer auch
    /// Netzwerk, NAT und Codecs — und die trugen dann die Werkseinstellungen.
    /// Wer seine Codec-Reihenfolge, STUN, den RTP-Portbereich, „Verschlüsselung
    /// erzwingen" oder die Zertifikatsprüfung verstellt hatte, verlor das beim
    /// Ein- oder Ausstecken eines Kopfhörers — still, und das Protokoll meldete
    /// dazu „Einstellungen auf den laufenden Core uebertragen".</para>
    /// </summary>
    public void ApplyAudioOnly(LinphoneCore core, AudioSettings audio)
    {
        ApplyAudio(core, audio);

        SettingsApplierLog.AudioApplied(logger);
    }

    private void ApplyNetwork(LinphoneCore core, NetworkSettings network)
    {
        core.SetAudioPortRange(network.RtpPortMin, network.RtpPortMax);

        // §9.2 nennt DSCP für Signalisierung und Medien. Der Wert wird gesetzt
        // und vom SDK unter Windows nicht umgesetzt — belle-sip meldet im
        // Protokoll „belle_sip_socket_set_dscp(): not implemented". Wer
        // Priorisierung braucht, macht sie über eine QoS-Richtlinie im
        // Betriebssystem; die Einstellung bleibt, damit sie auf anderen
        // Plattformen wirkt und niemand sie zweimal einbaut.
        core.SipDscp = network.SipDscp;
        core.AudioDscp = network.AudioDscp;

        core.Ipv6Enabled = network.EnableIpv6;

        // §9.2: Keep-Alive-Intervall. Das SDK kennt nur einen Schalter am Core;
        // das Intervall selbst steht in der Registrierungsdauer des Kontos.
        core.KeepAliveEnabled = network.KeepAliveSeconds > 0;

        ApplyCertificates(core, network);
    }

    /// <summary>
    /// §9.2 und §14.6: Serverzertifikat prüfen.
    ///
    /// Das SDK braucht die Wurzelzertifikate als <b>Datei</b>;
    /// <see cref="RootCertificates"/> schreibt sie aus dem Windows-Speicher.
    /// Ohne beides zusammen war „Serverzertifikat prüfen" eine Einstellung
    /// ohne Wirkung.
    /// </summary>
    private void ApplyCertificates(LinphoneCore core, NetworkSettings network)
    {
        if (rootCertificates.Ensure() is { } bundle)
        {
            core.RootCa = bundle;
        }

        // VerifyServerCn prüft zusätzlich, ob das Zertifikat auf die Domäne
        // ausgestellt ist. Ohne diese Prüfung genügt irgendein gültiges
        // Zertifikat — das ist keine Prüfung, sondern eine Formalität.
        core.VerifyServerCertificates(network.VerifyServerCertificate);
        core.VerifyServerCn(network.VerifyServerCertificate);

        SettingsApplierLog.CertificateVerification(logger, network.VerifyServerCertificate);
    }

    private void ApplyNatMedia(LinphoneCore core, NatMediaSettings nat)
    {
        var encryption = nat.Encryption switch
        {
            MediaEncryptionSetting.None => MediaEncryption.None,
            MediaEncryptionSetting.Zrtp => MediaEncryption.ZRTP,
            MediaEncryptionSetting.Dtls => MediaEncryption.DTLS,
            _ => MediaEncryption.SRTP,
        };

        if (core.MediaEncryptionSupported(encryption))
        {
            core.MediaEncryption = encryption;
        }
        else
        {
            SettingsApplierLog.EncryptionUnsupported(logger, encryption.ToString());
        }

        // §14.7 und ADR-007: mit Mandatory scheitern Gespräche zu Gegenstellen
        // ohne Verschlüsselung hart. Gegen die aktuelle Anlage von bv2 wäre
        // das jedes Gespräch — deshalb ein Hinweis im Log, wenn jemand es
        // einschaltet.
        core.MediaEncryptionMandatory = nat.EncryptionMandatory;
        if (nat.EncryptionMandatory)
        {
            SettingsApplierLog.EncryptionMandatoryWarning(logger);
        }

        core.AdaptiveRateControlEnabled = nat.AdaptiveBitrate;

        var natPolicy = core.NatPolicy ?? core.CreateNatPolicy();
        natPolicy.StunServer = nat.StunServer ?? string.Empty;
        natPolicy.IceEnabled = nat.EnableIce;
        natPolicy.TurnEnabled = nat.EnableTurn;
        core.NatPolicy = natPolicy;

        // §2: Video bleibt aus, unabhängig von allem anderen.
        core.VideoCaptureEnabled = false;
        core.VideoDisplayEnabled = false;
    }

    private void ApplyAudio(LinphoneCore core, AudioSettings audio)
    {
        // §9.4 rechnet in 0–100, das SDK in Dezibel (siehe AudioGain).
        core.PlaybackGainDb = AudioGain.ToDecibel(audio.PlaybackVolume);
        core.MicGainDb = AudioGain.ToDecibel(audio.MicrophoneLevel);

        core.EchoCancellationEnabled = audio.EchoCancellation;
        core.NoiseSuppressionEnabled = audio.NoiseSuppression;

        // §9.4: automatische Aussteuerung. Stand bisher im Modell und wurde
        // nirgends übertragen — der Schalter tat nichts.
        //
        // Der Standard steht auf AUS, abweichend von §9.4 (ADR-011): die
        // SDK-Dokumentation nennt den Algorithmus selbst „very experimental,
        // not usable in its current state". Ein Schalter, der ab Werk eine
        // Funktion einschaltet, von der ihr Hersteller abrät, ist keine
        // Vorgabe, sondern eine Falle.
        core.AgcEnabled = audio.AutomaticGainControl;

        ApplyRingtone(core, audio);
        ApplyRingback(core);

        // Geräte nur setzen, wenn eine Wahl getroffen wurde. §9.4: ohne Wahl
        // „dem Windows-Standard folgen" — und den kennt das SDK selbst besser.
        ApplyDevice(core, audio.InputDeviceId, isInput: true);
        ApplyDevice(core, audio.OutputDeviceId, isInput: false);

        ApplyRingerDevice(core, audio.RingerDeviceId);
        ApplyToneCards(core);
    }

    /// <summary>
    /// Sorgt dafuer, dass die <b>Toene</b> des SDK eine Soundkarte haben.
    ///
    /// <para><b>Der Befund.</b> Beim Waehlen war das Freizeichen nicht zu
    /// hoeren. Im Protokoll stand der Ton als gespielt — die Datei war offen,
    /// der Stream lief. Nur endete seine Filterkette in einem
    /// <c>MSVoidSink</c>: berechnet und verworfen. Beim eingehenden Klingeln
    /// endete die gleiche Kette in <c>MSWASAPIWrite</c>, und das war hoerbar.
    /// Der Unterschied lag also nicht am Ton, sondern an der Senke.</para>
    ///
    /// <para><b>Die Ursache: zwei Geraete-APIs.</b> Das SDK hat eine alte
    /// (<c>Core.PlaybackDevice</c>, ein Kartenname) und eine neue
    /// (<c>Core.DefaultOutputAudioDevice</c>, ein <c>AudioDevice</c>). nipp
    /// benutzt die neue, weil §9.4 Geraete anhand ihrer Kennung waehlt und
    /// <c>Core.RingerDevice</c> ausdruecklich veraltet ist. Der Tonspieler des
    /// SDK greift aber auf die alte zu: das Klingeln nimmt
    /// <c>ring_sndcard</c>, das Freizeichen <c>play_sndcard</c>. Die Ringer-
    /// Karte war belegt, die Wiedergabe-Karte nicht — deshalb klingelte es und
    /// deshalb war das Freizeichen stumm. <b>Der Gespraechston war davon nie
    /// betroffen</b>, der laeuft ueber die neue API.</para>
    ///
    /// <para><b>Warum das Setzen der neuen API nicht genuegt.</b> Sie fuellt
    /// die alten Felder nicht mit. Wer ein Geraet in den Einstellungen waehlt,
    /// aendert deshalb den Gespraechston, nicht aber den Ton beim Waehlen.</para>
    /// </summary>
    //
    // <b>Hier steht mit Absicht die veraltete API.</b> CS0612 ist berechtigt und
    // gilt fuer den Rest von nipp weiter: Geraete werden ueber
    // <c>AudioDevice</c> gewaehlt. Nur der Tonspieler des SDK liest diese
    // Felder, und solange er das tut, hilft kein moderner Ersatz. Die
    // Abschaltung umfasst deshalb genau diese zwei Methoden — nicht die Datei.
#pragma warning disable CS0612 // Veralteter Typ oder Member wird verwendet
    private void ApplyToneCards(LinphoneCore core)
    {
        try
        {
            // Was die neue API als Ausgabe fuehrt, ist die Wahrheit — hier wird
            // nur die alte darauf nachgezogen.
            //
            // Die Namen sind dabei NICHT verlaesslich gleich: die alte API
            // fuehrt oft ein Treiberpraefix ("WASAPI: Kopfhoerer (Jabra Link
            // 400)"), die neue nicht. Deshalb entscheidet ToneCardChooser, und
            // zwar mit mehreren Kandidaten — der Setter unten wirft, wenn er
            // einen Namen nicht kennt.
            var gewuenscht = core.DefaultOutputAudioDevice?.DeviceName;
            var kandidaten = ToneCardChooser.Choose(gewuenscht, core.PlaybackDevice, ReadToneCards(core));

            if (kandidaten.Count == 0)
            {
                SettingsApplierLog.ToneCard(logger, core.PlaybackDevice ?? "(keine)");
                return;
            }

            foreach (var kandidat in kandidaten)
            {
                try
                {
                    core.PlaybackDevice = kandidat;
                    SettingsApplierLog.ToneCardChanged(logger, kandidat);
                    return;
                }
                catch (Exception ex)
                {
                    // Nicht abbrechen: der naechste Kandidat ist der Grund,
                    // warum es eine Liste ist.
                    SettingsApplierLog.ToneCardRejected(logger, kandidat, ex.GetType().Name);
                }
            }

            SettingsApplierLog.ToneCardFailed(logger, $"{kandidaten.Count} Kandidaten abgelehnt");
        }
        catch (Exception ex)
        {
            // Eine veraltete API, die ein Geraet nicht kennt, darf nichts
            // kosten: ohne diesen Schritt telefoniert nipp unveraendert, nur
            // ohne Freizeichen.
            SettingsApplierLog.ToneCardFailed(logger, ex.GetType().Name);
        }
    }

    private static List<ToneCard> ReadToneCards(LinphoneCore core)
    {
        var karten = new List<ToneCard>();

        foreach (var name in core.SoundDevicesList)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                karten.Add(new ToneCard(name, CanPlay(core, name)));
            }
        }

        return karten;
    }

    private static bool CanPlay(LinphoneCore core, string? card)
    {
        if (string.IsNullOrEmpty(card))
        {
            return false;
        }

        try
        {
            return core.SoundDeviceCanPlayback(card);
        }
        catch (Exception)
        {
            return false;
        }
    }
#pragma warning restore CS0612

    /// <summary>
    /// Setzt den Klingelton (§9.4).
    ///
    /// <b>Warum das ausdrücklich geschehen muss.</b> Ohne gesetzten Ton sucht
    /// das SDK seinen eigenen Standard — <c>notes_of_the_optimistic.mkv</c>
    /// relativ zu <c>RingResourcesDir</c> — und fand ihn nicht: im Protokoll
    /// stand bei jedem Start „Default local ringtone file … does not exist",
    /// und ein eingehender Anruf klingelte am Gerät nicht.
    ///
    /// <para>Seit dem 07.09.2026 ist der Standard der <b>eigene</b> Klang
    /// (<c>Assets/Sounds/nipp-ring.wav</c>). Die Telefonglocke des SDK
    /// (<c>oldphone-mono.wav</c>) war der Ton davor und aus dem Alltag als
    /// „nervend" gemeldet; sie bleibt als Wahl und als Rückfall. Welcher gilt,
    /// entscheidet <c>NippSounds</c>.</para>
    /// </summary>
    /// <summary>
    /// Nur den Klingelton, ohne alles andere (ADR-055).
    ///
    /// <para>Gebraucht, wenn «Nicht stören» abläuft: <c>SipService</c> hat den
    /// Ton auf leer gesetzt und braucht den eingestellten zurück. <b>Welche
    /// Datei gilt, entscheidet diese Klasse</b> — ein Pfad, den der Aufrufer
    /// sich merkt, liefe auseinander, sobald jemand den Klingelton
    /// wechselt.</para>
    /// </summary>
    public void ApplyRingtoneOnly(LinphoneCore core, AudioSettings audio) =>
        ApplyRingtone(core, audio);

    private void ApplyRingtone(LinphoneCore core, AudioSettings audio)
    {
        var ringtone = NippSounds.Ringtone(audio.RingtonePath);

        if (ringtone is null)
        {
            SettingsApplierLog.RingtoneMissing(logger);
            return;
        }

        core.Ring = ringtone;
        SettingsApplierLog.RingtoneApplied(logger, ringtone);
    }

    /// <summary>
    /// Setzt den Rufton beim Wählen (§9.4).
    ///
    /// <para><b>Das wurde nie gesetzt.</b> Bis zum 07.09.2026 stand
    /// <c>Core.Ringback</c> nirgends in nipp; der Ton kam allein daraus, dass
    /// das SDK <c>ringback.wav</c> relativ zu <c>SoundResourcesDir</c> selbst
    /// findet. Das funktionierte — nur wusste niemand, welche Datei da spielt,
    /// und im Protokoll stand dazu keine Zeile. Bei der Suche nach dem stummen
    /// Freizeichen hat genau das Zeit gekostet.</para>
    ///
    /// <para>Der eigene Rufton ist zudem <b>zehn Sekunden</b> lang und bildet
    /// das Schweizer Läuten nach (1 s an, 4 s aus). Der des SDK ist 1,5
    /// Sekunden kurz — für den eigenen Tonspieler bei Early Media
    /// (<c>RingbackWatch</c>) wäre das ein Stakkato.</para>
    /// </summary>
    private void ApplyRingback(LinphoneCore core)
    {
        var ringback = NippSounds.Ringback();

        if (ringback is null)
        {
            SettingsApplierLog.RingbackMissing(logger);
            return;
        }

        core.Ringback = ringback;
        SettingsApplierLog.RingbackApplied(logger, ringback);
    }

    /// <summary>
    /// Setzt das Klingelgerät (§9.4).
    ///
    /// <c>Core.RingerDevice</c> ist seit dem 29.08.2025 veraltet; der Ersatz
    /// ist eine Kombination aus <c>ExtendedAudioDevices</c> und
    /// <c>AudioDevice.UseForRinging</c>. Das Flag wird an allen Geräten
    /// zurückgesetzt und nur am gewählten gesetzt — sonst klingelt es
    /// irgendwann auf mehreren.
    /// </summary>
    private void ApplyRingerDevice(LinphoneCore core, string? ringerDeviceId)
    {
        if (string.IsNullOrEmpty(ringerDeviceId))
        {
            return;
        }

        var found = false;

        foreach (var device in core.ExtendedAudioDevices)
        {
            var isChosen = device.Id == ringerDeviceId;
            device.UseForRinging = isChosen;
            found |= isChosen;
        }

        if (!found)
        {
            SettingsApplierLog.DeviceMissing(logger, ringerDeviceId);
        }
    }

    private void ApplyDevice(LinphoneCore core, string? deviceId, bool isInput)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return;
        }

        var device = core.ExtendedAudioDevices.FirstOrDefault(d => d.Id == deviceId);

        if (device is null)
        {
            // §9.4: ein verschwundenes Gerät darf nichts abreissen lassen.
            // Hier heisst das: die Wahl ignorieren und beim Standard bleiben,
            // statt zu scheitern.
            SettingsApplierLog.DeviceMissing(logger, deviceId);
            return;
        }

        if (isInput)
        {
            core.DefaultInputAudioDevice = device;
        }
        else
        {
            core.DefaultOutputAudioDevice = device;
        }
    }

    /// <summary>
    /// Setzt Auswahl und Reihenfolge der Codecs (§9.5, AP5.8).
    ///
    /// §14.10: „Codec-Reihenfolge wird gern still ignoriert, wenn man die
    /// Liste falsch setzt. Immer im SIP-Log gegenprüfen, nicht dem UI-Zustand
    /// glauben." Deshalb protokolliert diese Methode, was sie tatsächlich
    /// gesetzt hat.
    /// </summary>
    private void ApplyCodecs(LinphoneCore core, CodecSettings codecs)
    {
        if (!codecs.IsValid)
        {
            // Doppelte Absicherung: der SettingsService lässt so etwas gar
            // nicht erst speichern, aber ein Provisioning-Profil könnte es
            // versuchen (§11).
            SettingsApplierLog.CodecsInvalid(logger);
            return;
        }

        var available = core.AudioPayloadTypes.ToList();

        foreach (var payload in available)
        {
            var wanted = codecs.Enabled.Contains(payload.MimeType, StringComparer.OrdinalIgnoreCase);
            payload.Enable(wanted);
        }

        // Die Reihenfolge im SDP folgt der Liste, die dem Core zugewiesen wird.
        var ordered = codecs.Order
            .Select(name => available.FirstOrDefault(p =>
                string.Equals(p.MimeType, name, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is not null)
            .Select(p => p!)
            .Concat(available.Where(p =>
                !codecs.Order.Contains(p.MimeType, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        core.AudioPayloadTypes = ordered;

        var effective = string.Join(", ", core.AudioPayloadTypes
            .Where(p => p.Enabled())
            .Select(p => p.MimeType));

        SettingsApplierLog.CodecsApplied(logger, effective);

        // §9.5 nennt feste Payload-Type-Nummern (Opus 96, speex 102). Die sind
        // nicht setzbar — das SDK vergibt dynamische Typen erst beim SDP.
        // Der Hinweis steht hier, damit niemand danach sucht.
        SettingsApplierLog.PayloadNumbersDynamic(logger);
    }

    /// <summary>
    /// Was einen Neustart braucht. §12 (M4) verlangt einen Hinweis dafür.
    ///
    /// Transport und SIP-Port hängen an den <c>Transports</c> des Core und an
    /// den Konto-Parametern; sie im Betrieb zu ändern hiesse, alle Konten neu
    /// aufzubauen. Das ist möglich, aber die Registrierung bricht dabei kurz
    /// ab — ehrlicher ist ein Hinweis.
    ///
    /// <para><b>Die Zertifikatsprüfung stand hier bis zum 13.09.2026 zu
    /// Unrecht</b> (Befund B13). <see cref="Apply"/> setzt
    /// <c>VerifyServerCertificates</c> bei jedem Durchlauf sofort; der Hinweis
    /// verlangte einen Neustart für etwas, das schon gewirkt hatte. Ein
    /// Hinweis, der etwas anderes behauptet als der Code tut, schickt die
    /// Fehlersuche in die falsche Richtung — dasselbe Muster wie bei der
    /// Protokollzeile zum Rufton.</para>
    /// </summary>
    public static IReadOnlyList<string> RequiresRestart(NippSettings previous, NippSettings current)
    {
        var reasons = new List<string>();

        if (previous.Network.SipPort != current.Network.SipPort)
        {
            reasons.Add("SIP-Port");
        }

        if (!previous.Accounts.Select(a => a.Transport)
            .SequenceEqual(current.Accounts.Select(a => a.Transport)))
        {
            reasons.Add("Transport");
        }

        return reasons;
    }
}
