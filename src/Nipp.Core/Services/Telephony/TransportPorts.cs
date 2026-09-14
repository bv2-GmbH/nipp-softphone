using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Auf welchen Ports der Core lauscht (§9.2, ADR-019 Nachtrag).
///
/// <para><b>Der Befund.</b> Der SIP-Port hatte ein Feld im Modell
/// (<c>NippSettings.Network.SipPort</c>, Standard 5061), ein Eingabefeld in den
/// Einstellungen, einen Profilschlüssel und einen Neustart-Hinweis — und
/// <b>erreichte das SDK nie</b>. `core.Transports` kam im ganzen
/// Telefonie-Ordner nicht vor; die einzige Fundstelle war ein Kommentar, der
/// behauptete, die Änderung brauche einen Neustart. Wer wegen einer Firewall
/// den Port umstellte, startete neu, und nichts änderte sich. Dasselbe Muster
/// wie <c>App.SdkStatus</c> und <c>CardKind.History</c>, zum fünften Mal.</para>
///
/// <para><b>Warum nur ein Port.</b> Das SDK hat drei — UDP, TCP und TLS —, und
/// zwei Sockets können nicht denselben Port belegen. Die Einstellung nennt
/// einen; er gilt für den Transport, den das <b>erste Konto</b> benutzt. Die
/// beiden anderen bleiben auf dem Standard des SDK. Wer je Transport einen
/// eigenen Port braucht, braucht dafür drei Felder — und niemand hat danach
/// gefragt.</para>
///
/// <para><b>Ohne Konto</b> bleibt alles auf dem Standard: eine Portwahl ohne
/// Transport wäre geraten.</para>
///
/// <para>Reine Funktion, damit sie ohne SDK prüfbar ist. <c>SipService</c>
/// wendet nur an.</para>
/// </summary>
public static class TransportPorts
{
    /// <summary>
    /// Der Wert, den das SDK als «nimm deinen Standard» versteht.
    /// </summary>
    public const int SdkDefault = 0;

    /// <summary>
    /// Die drei Ports für <c>Core.Transports</c>, aus den Einstellungen.
    /// </summary>
    /// <returns>
    /// <c>Udp</c>, <c>Tcp</c> und <c>Tls</c>. Genau einer trägt den
    /// eingestellten Port, die anderen <see cref="SdkDefault"/>.
    /// </returns>
    public static (int Udp, int Tcp, int Tls) From(NippSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var port = settings.Network.SipPort;

        if (port is <= 0 or > 65535 || settings.Accounts.Count == 0)
        {
            return (SdkDefault, SdkDefault, SdkDefault);
        }

        return settings.Accounts[0].Transport switch
        {
            SipTransport.Udp => (port, SdkDefault, SdkDefault),
            SipTransport.Tcp => (SdkDefault, port, SdkDefault),
            _ => (SdkDefault, SdkDefault, port),
        };
    }
}
