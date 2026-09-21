using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Übersetzt SIP-Fehler in Meldungen, die einem Supporter im Kundeneinsatz
/// weiterhelfen.
///
/// §15 gibt die Messlatte vor: „Registrierung fehlgeschlagen: Server nicht
/// erreichbar (Zeitüberschreitung nach 5 s). Netzwerkverbindung und Domain
/// prüfen." statt „Fehler beim Registrieren". Also immer zwei Teile —
/// <b>was passiert ist</b> und <b>was zu tun ist</b>.
///
/// Bewusst hier und nicht in den ViewModels: dieselbe Meldung wird in der
/// Statuszeile, im Toast, im Verlauf und im Diagnosepaket gebraucht.
/// Absichtlich SDK-frei, damit der Katalog ohne SDK testbar bleibt.
/// </summary>
public static class SipErrorCatalog
{
    /// <summary>
    /// Meldung zu einem gescheiterten Registrierungsversuch.
    /// </summary>
    /// <param name="sdkMessage">Begleittext des SDK, etwa „Unauthorized".</param>
    /// <param name="domain">Betroffene Domain, für die Meldung.</param>
    /// <param name="domain">
    /// Die Anlage, um die es geht — oder <c>null</c>, wenn sie nicht bekannt
    /// ist.
    ///
    /// <para>Ohne diese Unterscheidung stand am Gerät „Registrierung bei
    /// fehlgeschlagen" mit einer Lücke, wo die Anlage hätte stehen sollen. Eine
    /// Meldung, die einen Namen ankündigt und dann keinen nennt, sieht nach
    /// einem Fehler in nipp aus — und war in diesem Fall auch einer.</para>
    /// </param>
    /// <param name="hasPassword">
    /// Ob fuer dieses Konto ueberhaupt ein Passwort hinterlegt ist.
    ///
    /// <para><b>Ohne diese Angabe schickt die Meldung auf die falsche Suche</b>
    /// (Befund A1-9). «Zugangsdaten abgelehnt — Benutzername,
    /// Authentifizierungs-ID und Passwort pruefen» laesst jemanden drei richtige
    /// Angaben nachschauen, waehrend in Wahrheit gar keine vierte da ist. Das
    /// passiert regelmaessig nach einem Provisionierungsprofil mit
    /// <c>&lt;accounts&gt;</c>: es ersetzt die Kontenliste, und die Geheimnisse
    /// der ersetzten Konten gehen mit. nipp schreibt das ins Protokoll — aber
    /// niemand liest es in dem Moment; gesehen wird nur, dass das Telefon nicht
    /// mehr angemeldet ist.</para>
    /// </param>
    public static string DescribeRegistrationFailure(
        string? sdkMessage,
        string? domain,
        bool hasPassword = true)
    {
        var reason = Normalize(sdkMessage);

        // „bei pbx.example.ch" oder gar keine Ortsangabe. Der Rest der Meldung
        // ist in beiden Fällen derselbe.
        var wo = string.IsNullOrWhiteSpace(domain) ? string.Empty : $" bei {domain}";

        return reason switch
        {
            // Kein Passwort hinterlegt: dann ist die Suche nach dem Tippfehler
            // im Benutzernamen verschwendete Zeit (Befund A1-9).
            var m when !hasPassword && Contains(m, "unauthorized", "forbidden", "401", "403") =>
                $"Anmeldung{wo} fehlgeschlagen: Für dieses Konto ist kein Passwort "
                    + "hinterlegt. Es in den Einstellungen unter «SIP-Konten» eintragen — "
                    + "ein Profil, das die Konten ersetzt, nimmt gespeicherte Passwörter mit.",

            var m when Contains(m, "unauthorized", "forbidden", "401", "403") =>
                $"Anmeldung{wo} fehlgeschlagen: Zugangsdaten abgelehnt. "
                    + "Benutzername, Authentifizierungs-ID und Passwort prüfen.",

            var m when Contains(m, "not found", "404") =>
                $"Anmeldung{wo} fehlgeschlagen: Das Konto ist auf der Anlage nicht bekannt. "
                    + "SIP-Benutzernamen und Domain prüfen.",

            var m when Contains(m, "timeout", "timed out", "408", "504") =>
                $"Anmeldung{wo} fehlgeschlagen: Server nicht erreichbar (Zeitüberschreitung). "
                    + "Netzwerkverbindung, Domain und Transport prüfen — bei TLS ausserdem den Port.",

            var m when Contains(m, "unreachable", "no route", "dns", "resolve") =>
                $"Anmeldung{wo} fehlgeschlagen: Die Domain lässt sich nicht auflösen. "
                    + "Schreibweise der Domain und die DNS-Einstellungen prüfen.",

            var m when Contains(m, "tls", "certificate", "handshake", "ssl") =>
                $"Anmeldung{wo} fehlgeschlagen: Die verschlüsselte Verbindung wurde abgelehnt. "
                    + "Serverzertifikat prüfen. Wenn die Anlage ein eigenes Zertifikat nutzt, muss die "
                    + "Zertifizierungsstelle in den Einstellungen hinterlegt sein.",

            var m when Contains(m, "service unavailable", "503") =>
                $"Anmeldung{wo} fehlgeschlagen: Die Anlage ist vorübergehend nicht verfügbar (503). "
                    + "Später erneut versuchen; bleibt es dabei, die Anlage prüfen.",

            // §14.11: doppelte Registrierung. Die alte Fassung nannte nur eine
            // Vermutung — der Test aus §15 hat das zu Recht beanstandet.
            var m when Contains(m, "too many", "486", "busy") =>
                $"Anmeldung{wo} fehlgeschlagen: Die Anlage weist die Anmeldung ab. "
                    + "Prüfen, ob dasselbe Konto bereits auf einem Tischtelefon oder einem anderen "
                    + "Gerät registriert ist — je nach Konfiguration lässt die Anlage nur eine "
                    + "Anmeldung zu.",

            "" =>
                $"Anmeldung{wo} fehlgeschlagen. Netzwerkverbindung, Domain und Zugangsdaten prüfen.",

            _ =>
                $"Anmeldung{wo} fehlgeschlagen: {sdkMessage}. "
                    + "Zugangsdaten, Domain und Transport prüfen.",
        };
    }

    /// <summary>
    /// Meldung zu einem gescheiterten oder abgewiesenen Anruf.
    /// </summary>
    /// <param name="sdkMessage">Begleittext des SDK.</param>
    /// <param name="remoteNumber">Angerufene oder anrufende Nummer.</param>
    /// <param name="encryptionMandatory">
    /// Ob „Verschlüsselung erzwingen" aktiv ist. Wenn ja, ist das die
    /// wahrscheinlichste Ursache eines harten Abbruchs — §14.7 verlangt
    /// ausdrücklich, dass die Meldung den Grund nennt, sonst wird der Client
    /// für kaputt erklärt.
    /// </param>
    public static string DescribeCallFailure(string? sdkMessage, string remoteNumber, bool encryptionMandatory)
    {
        var reason = Normalize(sdkMessage);

        // Zuerst der Fall aus §14.7: erzwungene Verschlüsselung gegen eine
        // Gegenstelle, die keine anbietet. Gegen die aktuelle Anlage von bv2
        // wäre das der Regelfall (ADR-007), deshalb steht er ganz oben.
        if (encryptionMandatory && Contains(reason, "encryption", "srtp", "media", "488", "not acceptable"))
        {
            return $"Gespräch mit {remoteNumber} nicht möglich: Die Gegenstelle unterstützt keine "
                + "verschlüsselte Übertragung, und «Verschlüsselung erzwingen» ist eingeschaltet. "
                + "Entweder die Einstellung ausschalten oder ein Ziel wählen, das Verschlüsselung beherrscht.";
        }

        return reason switch
        {
            var m when Contains(m, "busy", "486") =>
                $"{remoteNumber} ist besetzt.",

            var m when Contains(m, "declined", "603", "rejected") =>
                $"{remoteNumber} hat das Gespräch abgelehnt.",

            var m when Contains(m, "not found", "404") =>
                $"{remoteNumber} ist unbekannt. Nummer prüfen — bei internen Zielen die Nebenstelle, "
                    + "bei externen die Vorwahl.",

            var m when Contains(m, "no answer", "timeout", "408", "480", "temporarily unavailable") =>
                $"{remoteNumber} hat nicht abgenommen.",

            var m when Contains(m, "forbidden", "403") =>
                $"Gespräch mit {remoteNumber} nicht erlaubt. Die Anlage weist die Verbindung ab — "
                    + "möglicherweise fehlt die Berechtigung für diese Zielart.",

            var m when Contains(m, "not acceptable", "488", "incompatible", "media") =>
                $"Gespräch mit {remoteNumber} nicht möglich: Es liess sich kein gemeinsamer Codec finden. "
                    + "Codec-Einstellungen prüfen — PCMA und PCMU sollten aktiv sein.",

            var m when Contains(m, "service unavailable", "503") =>
                $"Gespräch mit {remoteNumber} nicht möglich: Die Anlage ist vorübergehend nicht verfügbar (503).",

            var m when Contains(m, "unreachable", "no route", "404 host") =>
                $"Gespräch mit {remoteNumber} nicht möglich: Das Ziel ist nicht erreichbar.",

            "" =>
                $"Gespräch mit {remoteNumber} nicht möglich.",

            _ =>
                $"Gespräch mit {remoteNumber} nicht möglich: {sdkMessage}",
        };
    }

    /// <summary>
    /// Meldung, wenn ein dritter Anruf abgelehnt wird. §8.2: „Mehr als zwei
    /// wird abgelehnt mit klarer Meldung."
    /// </summary>
    public static string DescribeTooManyCalls() =>
        "Es sind schon zwei Gespräche offen. nipp verwaltet höchstens zwei gleichzeitig — "
        + "eines davon beenden oder weiterleiten, dann erneut versuchen.";

    /// <summary>
    /// Meldung, wenn ein Audiogerät während des Gesprächs verschwindet (§9.4).
    /// Das Gespräch läuft weiter — die Meldung erklärt nur, warum es plötzlich
    /// anders klingt.
    /// </summary>
    public static string DescribeAudioDeviceLost(string lostDeviceName, string fallbackDeviceName) =>
        $"«{lostDeviceName}» ist nicht mehr verfügbar. Das Gespräch läuft über "
        + $"«{fallbackDeviceName}» weiter.";

    /// <summary>
    /// Meldung zu einem nicht ladbaren SDK. Tritt beim Kunden auf, wenn eine
    /// native Bibliothek fehlt (§14.2) — die Meldung des Systems ist dort
    /// nutzlos, deshalb diese.
    /// </summary>
    public static string DescribeSdkUnavailable(SdkLoadResult result)
    {
        if (!result.Loaded)
        {
            return "Die Telefonie-Komponenten konnten nicht geladen werden. Die Installation ist "
                + "unvollständig — nipp neu installieren. "
                + $"Technische Ursache: {result.Error}";
        }

        if (result.PluginFileCount == 0)
        {
            return "Die Audio-Komponenten fehlen. Gespräche wären stumm. Die Installation ist "
                + "unvollständig — nipp neu installieren.";
        }

        if (result.GrammarFileCount == 0)
        {
            return "Teile der Telefonie-Komponenten fehlen. Die Installation ist unvollständig — "
                + "nipp neu installieren.";
        }

        return "Die Telefonie ist bereit.";
    }

    private static string Normalize(string? message) =>
        message?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool Contains(string haystack, params string[] needles) =>
        haystack.Length > 0
        && Array.Exists(needles, n => haystack.Contains(n, StringComparison.Ordinal));
}
