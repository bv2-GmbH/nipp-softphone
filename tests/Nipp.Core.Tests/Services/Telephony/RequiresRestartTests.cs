using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Was einen Neustart braucht — und was nicht (§12 M4, W2.1 Etappe B6).
///
/// <para><b>Warum ausgerechnet dieser Hinweis Tests braucht.</b> Er ist eine
/// Zusage an den Benutzer, und eine falsche Zusage schickt die Fehlersuche in
/// die falsche Richtung. Genau das ist passiert (Befund B13): die
/// Zertifikatsprüfung stand hier, obwohl <c>Apply</c> sie bei jedem Durchlauf
/// sofort setzt — der Hinweis verlangte einen Neustart für etwas, das längst
/// gewirkt hatte. Dasselbe Muster wie bei der Protokollzeile zum Rufton.</para>
///
/// <para><b>Die anderen beiden Zusagen aus B6 sind schon geprüft:</b> dass
/// ein unveränderter Satz Einstellungen nichts auslöst, hält
/// <c>SettingsNoOpSaveTests</c> fest (ADR-060); dass der SIP-Port beim Kern
/// ankommt, <c>TransportPortsTests</c>; dass eine Sperre die
/// Benutzermarkierung löscht, <c>ProvisioningUserOverrideTests</c>
/// (ADR-054).</para>
/// </summary>
public class RequiresRestartTests
{
    private static NippSettings Mit(int sipPort = 5061, params SipTransport[] transporte) =>
        new()
        {
            Network = new NetworkSettings { SipPort = sipPort },
            Accounts =
            [
                .. transporte.Select((t, i) => new SipAccountSettings
                {
                    Username = $"10{i}",
                    Domain = "example.test",
                    Password = "geheim",
                    Transport = t,
                }),
            ],
        };

    /// <summary>Ohne Änderung ist kein Neustart nötig — der Normalfall.</summary>
    [Fact]
    public void Ohne_Aenderung_kein_Neustart()
    {
        var vorher = Mit(5061, SipTransport.Tls);
        var nachher = Mit(5061, SipTransport.Tls);

        Assert.Empty(SettingsApplier.RequiresRestart(vorher, nachher));
    }

    /// <summary>
    /// <b>Der SIP-Port braucht einen Neustart</b>: er hängt an den
    /// <c>Transports</c> des Kerns, und die stehen vor <c>Start()</c> fest.
    /// </summary>
    [Fact]
    public void Ein_geaenderter_SIP_Port_braucht_einen_Neustart()
    {
        var gruende = SettingsApplier.RequiresRestart(Mit(5061), Mit(5062));

        Assert.Equal(["SIP-Port"], gruende);
    }

    /// <summary>
    /// <b>Der Transport ebenso</b> — ihn im Betrieb zu ändern hiesse, alle
    /// Konten neu aufzubauen, und die Registrierung bricht dabei kurz ab.
    /// </summary>
    [Fact]
    public void Ein_geaenderter_Transport_braucht_einen_Neustart()
    {
        var gruende = SettingsApplier.RequiresRestart(
            Mit(5061, SipTransport.Tls),
            Mit(5061, SipTransport.Udp));

        Assert.Equal(["Transport"], gruende);
    }

    /// <summary>Beides zusammen nennt beide Gründe — der Hinweis sagt, was gilt.</summary>
    [Fact]
    public void Beides_zusammen_nennt_beide_Gruende()
    {
        var gruende = SettingsApplier.RequiresRestart(
            Mit(5061, SipTransport.Tls),
            Mit(5062, SipTransport.Udp));

        Assert.Equal(["SIP-Port", "Transport"], gruende);
    }

    /// <summary>
    /// <b>Ein zweites Konto ist eine Änderung am Transport</b>, auch wenn das
    /// erste bleibt: die Liste wird als Ganzes verglichen, und ein neues
    /// Konto bringt seinen eigenen mit.
    /// </summary>
    [Fact]
    public void Ein_zusaetzliches_Konto_zaehlt_als_Transportaenderung()
    {
        var gruende = SettingsApplier.RequiresRestart(
            Mit(5061, SipTransport.Tls),
            Mit(5061, SipTransport.Tls, SipTransport.Tls));

        Assert.Equal(["Transport"], gruende);
    }

    /// <summary>
    /// <b>Die Gegenprobe, und sie ist die wichtigere Hälfte</b> (Befund B13):
    /// was sofort wirkt, darf hier <b>nicht</b> stehen. Ein Hinweis, der
    /// etwas anderes behauptet als der Code tut, kostet mehr als er nützt.
    /// </summary>
    [Fact]
    public void Was_sofort_wirkt_verlangt_keinen_Neustart()
    {
        var vorher = new NippSettings
        {
            Network = new NetworkSettings
            {
                SipPort = 5061,
                VerifyServerCertificate = true,
                KeepAliveSeconds = 30,
                EnableIpv6 = false,
            },
            Audio = new AudioSettings { EchoCancellation = true },
        };

        var nachher = vorher with
        {
            Network = vorher.Network with
            {
                // Alle drei setzt Apply bei jedem Durchlauf sofort.
                VerifyServerCertificate = false,
                KeepAliveSeconds = 60,
                EnableIpv6 = true,
            },
            Audio = vorher.Audio with { EchoCancellation = false },
        };

        Assert.Empty(SettingsApplier.RequiresRestart(vorher, nachher));
    }
}
