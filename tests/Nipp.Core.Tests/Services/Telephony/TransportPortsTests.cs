using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// §9.2 und der Nachtrag zu ADR-019: der SIP-Port kommt beim SDK an.
///
/// <para><b>Der Befund.</b> Der Port hatte ein Feld im Modell, ein
/// Eingabefeld, einen Profilschlüssel, eine Prüfung im Validator und einen
/// Neustart-Hinweis — und <c>core.Transports</c> kam im ganzen
/// Telefonie-Ordner nicht vor. Wer wegen einer Firewall umstellte, startete
/// neu, und nichts änderte sich. ADR-019 behauptete dabei ausdrücklich, die
/// §9-Einstellungen seien im <c>SettingsApplier</c> vollständig umgesetzt.</para>
/// </summary>
public sealed class TransportPortsTests
{
    [Fact]
    public void Der_Port_gilt_fuer_den_Transport_des_ersten_Kontos()
    {
        var ports = TransportPorts.From(Mit(SipTransport.Tls, port: 5061));

        Assert.Equal((TransportPorts.SdkDefault, TransportPorts.SdkDefault, 5061), ports);
    }

    [Theory]
    [InlineData(SipTransport.Udp)]
    [InlineData(SipTransport.Tcp)]
    [InlineData(SipTransport.Tls)]
    public void Genau_ein_Transport_traegt_den_Port(SipTransport transport)
    {
        // Zwei Sockets können nicht denselben Port belegen — «alle drei auf
        // 5061» wäre also kein Wunsch, sondern ein Fehlstart.
        var (udp, tcp, tls) = TransportPorts.From(Mit(transport, port: 5080));

        var gesetzt = new[] { udp, tcp, tls }.Count(p => p != TransportPorts.SdkDefault);

        Assert.Equal(1, gesetzt);
    }

    [Fact]
    public void Ohne_Konto_bleibt_alles_beim_Standard()
    {
        // Eine Portwahl ohne Transport wäre geraten.
        var settings = new NippSettings
        {
            Network = new NetworkSettings { SipPort = 5080 },
        };

        Assert.Equal(
            (TransportPorts.SdkDefault, TransportPorts.SdkDefault, TransportPorts.SdkDefault),
            TransportPorts.From(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Ein_unmoeglicher_Port_wird_nicht_gesetzt(int port)
    {
        // Der Validator fängt das beim Speichern ab; hier steht die
        // Gegenprobe, weil eine Datei von Hand geändert werden kann und ein
        // Core, der auf Port 70000 zu lauschen versucht, gar nicht startet.
        Assert.Equal(
            (TransportPorts.SdkDefault, TransportPorts.SdkDefault, TransportPorts.SdkDefault),
            TransportPorts.From(Mit(SipTransport.Tls, port)));
    }

    private static NippSettings Mit(SipTransport transport, int port) => new()
    {
        Network = new NetworkSettings { SipPort = port },
        Accounts =
        [
            new SipAccountSettings
            {
                Username = "151",
                Domain = "pbx.example.test",
                Password = string.Empty,
                Transport = transport,
            },
        ],
    };
}
