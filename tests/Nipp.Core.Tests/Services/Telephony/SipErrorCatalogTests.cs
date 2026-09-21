using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Prüft den Fehlermeldungs-Katalog gegen die Anforderung aus §15: jede
/// Meldung sagt, <b>was passiert ist</b> und <b>was zu tun ist</b>.
///
/// Die Meldungen sind kein Beiwerk — sie entscheiden, ob ein Supporter im
/// Kundeneinsatz die Ursache findet oder den Client für kaputt erklärt.
/// </summary>
public sealed class SipErrorCatalogTests
{
    [Theory]
    [InlineData("Unauthorized", "Zugangsdaten")]
    [InlineData("403 Forbidden", "Zugangsdaten")]
    [InlineData("404 Not Found", "nicht bekannt")]
    [InlineData("Request timed out", "Zeitüberschreitung")]
    [InlineData("Could not resolve DNS", "auflösen")]
    [InlineData("TLS handshake failed", "Serverzertifikat")]
    [InlineData("503 Service Unavailable", "nicht verfügbar")]
    public void Registrierungsfehler_nennt_die_Ursache(string sdkMessage, string expectedFragment)
    {
        var message = SipErrorCatalog.DescribeRegistrationFailure(sdkMessage, "pbx.example.ch");

        Assert.Contains(expectedFragment, message, StringComparison.Ordinal);
        Assert.Contains("pbx.example.ch", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Ohne hinterlegtes Passwort sagt die Meldung das</b>, statt drei
    /// richtige Angaben nachschauen zu lassen (Befund A1-9).
    ///
    /// <para>Der Fall tritt nach einem Provisionierungsprofil mit
    /// <c>&lt;accounts&gt;</c> auf: es ersetzt die Kontenliste, und die
    /// Geheimnisse der ersetzten Konten gehen mit. Am 17. und am 21.09.2026 hat
    /// das je eine Anmeldung gekostet, und beide Male stand in der Oberfläche
    /// nur «Zugangsdaten abgelehnt».</para>
    /// </summary>
    [Theory]
    [InlineData("401 Unauthorized")]
    [InlineData("403 Forbidden")]
    public void Ohne_hinterlegtes_Passwort_sagt_die_Meldung_das(string sdkMessage)
    {
        var message = SipErrorCatalog.DescribeRegistrationFailure(
            sdkMessage,
            "pbx.example.ch",
            hasPassword: false);

        Assert.Contains("kein Passwort", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Zugangsdaten abgelehnt", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mit hinterlegtem Passwort bleibt es bei der alten Meldung — dann ist
    /// wirklich eine der drei Angaben falsch.
    /// </summary>
    [Fact]
    public void Mit_hinterlegtem_Passwort_bleibt_es_bei_den_Zugangsdaten()
    {
        var message = SipErrorCatalog.DescribeRegistrationFailure(
            "401 Unauthorized",
            "pbx.example.ch",
            hasPassword: true);

        Assert.Contains("Zugangsdaten abgelehnt", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrierungsfehler_ohne_Begleittext_bleibt_brauchbar()
    {
        // Das SDK liefert nicht immer eine Meldung. Auch dann darf nicht nur
        // "Fehler" dastehen.
        var message = SipErrorCatalog.DescribeRegistrationFailure(null, "pbx.example.ch");

        Assert.Contains("pbx.example.ch", message, StringComparison.Ordinal);
        Assert.Contains("prüfen", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrierungsfehler_sagt_immer_was_zu_tun_ist()
    {
        // §15: die Meldung nennt die Abhilfe. Ohne "prüfen" oder eine andere
        // Handlungsanweisung ist sie unvollständig.
        string[] sdkMessages =
        [
            "Unauthorized", "404 Not Found", "Request timed out", "DNS failure",
            "TLS handshake failed", "503 Service Unavailable", "486 Busy Here",
            "Etwas völlig Unerwartetes", "",
        ];

        foreach (var sdkMessage in sdkMessages)
        {
            var message = SipErrorCatalog.DescribeRegistrationFailure(sdkMessage, "pbx.example.ch");

            // Ohne Rücksicht auf Gross- und Kleinschreibung: „Prüfen" steht am
            // Satzanfang genauso wie „prüfen" mitten im Satz.
            Assert.True(
                message.Contains("prüfen", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("versuchen", StringComparison.OrdinalIgnoreCase),
                $"Die Meldung zu '{sdkMessage}' nennt keine Abhilfe: {message}");
        }
    }

    [Theory]
    [InlineData("486 Busy Here", "besetzt")]
    [InlineData("603 Declined", "abgelehnt")]
    [InlineData("404 Not Found", "unbekannt")]
    [InlineData("480 Temporarily Unavailable", "nicht abgenommen")]
    [InlineData("488 Not Acceptable Here", "Codec")]
    public void Anruffehler_nennt_die_Ursache(string sdkMessage, string expectedFragment)
    {
        var message = SipErrorCatalog.DescribeCallFailure(sdkMessage, "0445128430", encryptionMandatory: false);

        Assert.Contains(expectedFragment, message, StringComparison.Ordinal);
        Assert.Contains("0445128430", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Erzwungene_Verschluesselung_wird_als_Ursache_genannt()
    {
        // §14.7: "SRTP erzwingen bricht Gespräche zu Gegenstellen ohne SRTP
        // hart ab. Fehlermeldung muss den Grund nennen, sonst wird der Client
        // für kaputt erklärt." Gegen die aktuelle Anlage von bv2 ist das der
        // Regelfall (ADR-007) — deshalb ein eigener Test.
        var message = SipErrorCatalog.DescribeCallFailure(
            "488 Not Acceptable Here - media encryption",
            "0794108712",
            encryptionMandatory: true);

        Assert.Contains("Verschlüsselung", message, StringComparison.Ordinal);
        Assert.Contains("erzwingen", message, StringComparison.Ordinal);
        Assert.Contains("ausschalten", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Erzwingen_wird_nicht_die_Verschluesselung_beschuldigt()
    {
        // Dieselbe SDK-Meldung, aber ohne erzwungene Verschlüsselung: dann ist
        // die Ursache ein Codec-Problem, und die Meldung darf nicht auf eine
        // Einstellung zeigen, die gar nicht aktiv ist.
        var message = SipErrorCatalog.DescribeCallFailure(
            "488 Not Acceptable Here - media encryption",
            "0794108712",
            encryptionMandatory: false);

        Assert.DoesNotContain("erzwingen", message, StringComparison.Ordinal);
        Assert.Contains("Codec", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dritter_Anruf_wird_klar_abgelehnt()
    {
        // §8.2: "Mehr als zwei wird abgelehnt mit klarer Meldung."
        var message = SipErrorCatalog.DescribeTooManyCalls();

        Assert.Contains("zwei", message, StringComparison.Ordinal);
        Assert.Contains("beenden", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Geraeteverlust_beruhigt_statt_zu_alarmieren()
    {
        // §9.4: Das Gespräch läuft weiter. Die Meldung erklärt, warum es
        // anders klingt — sie meldet keinen Fehler.
        var message = SipErrorCatalog.DescribeAudioDeviceLost("Jabra Evolve 65", "Lautsprecher");

        Assert.Contains("Jabra Evolve 65", message, StringComparison.Ordinal);
        Assert.Contains("Lautsprecher", message, StringComparison.Ordinal);
        Assert.Contains("weiter", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fehlendes_SDK_verweist_auf_die_Installation()
    {
        var nichtGeladen = new SdkLoadResult(
            Loaded: false,
            SdkVersion: null,
            GrammarFileCount: 0,
            PluginFileCount: 0,
            Error: "DllNotFoundException: liblinphone.dll");

        var message = SipErrorCatalog.DescribeSdkUnavailable(nichtGeladen);

        Assert.Contains("Installation", message, StringComparison.Ordinal);
        Assert.Contains("liblinphone.dll", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fehlende_Audio_Plugins_werden_als_solche_benannt()
    {
        // Der Fall aus §14.2: die Kette lädt, aber libmswasapi.dll fehlt.
        // Dann ist das Gespräch stumm — und genau das muss dastehen.
        var ohnePlugins = new SdkLoadResult(
            Loaded: true,
            SdkVersion: "5.5.0",
            GrammarFileCount: 8,
            PluginFileCount: 0,
            Error: null);

        var message = SipErrorCatalog.DescribeSdkUnavailable(ohnePlugins);

        Assert.Contains("stumm", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Vollstaendiges_SDK_meldet_Bereitschaft()
    {
        var vollstaendig = new SdkLoadResult(
            Loaded: true,
            SdkVersion: "5.5.0",
            GrammarFileCount: 8,
            PluginFileCount: 2,
            Error: null);

        Assert.True(vollstaendig.IsComplete);
        Assert.Contains("bereit", SipErrorCatalog.DescribeSdkUnavailable(vollstaendig), StringComparison.Ordinal);
    }
}
