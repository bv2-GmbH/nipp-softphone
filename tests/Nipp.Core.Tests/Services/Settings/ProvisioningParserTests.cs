using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// §13 nennt „Provisioning-XML-Parsing (inkl. kaputtes XML)" als verbindlich
/// zu testen — und der zweite Teil ist der wichtigere: ein Profil kommt von
/// einem Webserver, den nipp nicht kontrolliert. Ein Tippfehler dort darf
/// nicht dazu führen, dass niemand mehr telefonieren kann.
/// </summary>
public sealed class ProvisioningParserTests
{
    private const string Vollstaendig = """
        <?xml version="1.0" encoding="utf-8"?>
        <nipp-provisioning version="1" profile="bv2-standard">
          <accounts>
            <account username="151" domain="pbx.example.ch" display-name="Dominic"
                     transport="tls" outbound-proxy="sip:pbx.example.ch:5061"
                     expires="900" voicemail="*98" auth-user-id="151-auth" />
            <account username="152" domain="pbx.example.ch" />
          </accounts>
          <team>
            <extension name="Reto" number="153" sip="sip:153@pbx.example.ch" />
            <extension name="Empfang" number="150" />
          </team>
          <settings>
            <set path="network.sip-port" value="5061" />
            <set path="advanced.country-prefix" value="+41" />
            <set path="codecs.order">opus,G722,PCMA,PCMU</set>
          </settings>
          <locked>
            <field>network.sip-port</field>
            <field>accounts</field>
          </locked>
        </nipp-provisioning>
        """;

    [Fact]
    public void LiestEinVollstaendigesProfil()
    {
        Assert.True(ProvisioningParser.TryParse(Vollstaendig, out var profile, out var error));
        Assert.Null(error);

        Assert.Equal(1, profile.Version);
        Assert.Equal("bv2-standard", profile.ProfileName);

        Assert.Equal(2, profile.Accounts.Count);

        var first = profile.Accounts[0];
        Assert.Equal("151", first.Username);
        Assert.Equal("pbx.example.ch", first.Domain);
        Assert.Equal("Dominic", first.DisplayName);
        Assert.Equal(SipTransport.Tls, first.Transport);
        Assert.Equal(900, first.ExpiresSeconds);
        Assert.Equal("151-auth", first.AuthUserId);

        // Das Attribut «voicemail» steht weiterhin in der Beispieldatei und
        // wird seit dem 13.09.2026 nicht mehr gelesen (ADR-062). Der Parser
        // holt jedes Attribut einzeln — ein unbekanntes bleibt folgenlos
        // liegen, und ein Profil von vorher wird deshalb unverändert
        // angenommen. <b>Das ist die Zusage, die dieser Test mitprüft:</b>
        // die beiden Konten oben sind vollständig gelesen worden.

        // Ein Konto ohne Zusatzangaben ist erlaubt — die Standardwerte kommen
        // beim Übernehmen, nicht beim Lesen.
        Assert.Null(profile.Accounts[1].Transport);
        Assert.Null(profile.Accounts[1].ExpiresSeconds);

        Assert.Equal(2, profile.Team.Count);
        Assert.Equal("sip:153@pbx.example.ch", profile.Team[0].SipAddress);
        Assert.Null(profile.Team[1].SipAddress);

        Assert.Equal("5061", profile.Values["network.sip-port"]);
        Assert.Equal("opus,G722,PCMA,PCMU", profile.Values["codecs.order"]);

        Assert.Contains("accounts", profile.LockedFields);
    }

    [Theory]
    // Nicht geschlossenes Element.
    [InlineData("<nipp-provisioning><accounts></nipp-provisioning>")]
    // Gar kein XML.
    [InlineData("Das ist eine Fehlerseite des Webservers, kein Profil.")]
    // Leer.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    // HTML statt XML — der häufigste Fall in der Praxis: der Server liefert
    // eine Anmeldeseite statt der Datei.
    [InlineData("<html><body><h1>401 Unauthorized</h1></body></html>")]
    // Richtiges XML, falsches Wurzelelement.
    [InlineData("<config><section name=\"proxy_0\"/></config>")]
    public void LehntKaputtesXmlAbOhneZuWerfen(string? xml)
    {
        var ok = ProvisioningParser.TryParse(xml, out var profile, out var error);

        Assert.False(ok);
        Assert.True(profile.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// XXE: eine externe Entität, die eine lokale Datei einbindet. Ohne
    /// <c>DtdProcessing.Prohibit</c> läse der Parser hier <c>win.ini</c> und
    /// schickte sie beim nächsten Diagnosepaket mit.
    /// </summary>
    [Fact]
    public void LaedtKeineExternenEntitaeten()
    {
        const string angriff = """
            <?xml version="1.0"?>
            <!DOCTYPE nipp-provisioning [
              <!ENTITY geheim SYSTEM "file:///C:/Windows/win.ini">
            ]>
            <nipp-provisioning version="1" profile="&geheim;" />
            """;

        Assert.False(ProvisioningParser.TryParse(angriff, out var profile, out var error));
        Assert.True(profile.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// Ein halbes Konto ist kein Konto. Ohne Benutzername oder Domäne würde
    /// nipp einen REGISTER an nirgendwo schicken und eine Fehlermeldung
    /// anzeigen, für die niemand etwas kann.
    /// </summary>
    [Fact]
    public void UebergehtUnvollstaendigeKonten()
    {
        const string xml = """
            <nipp-provisioning version="1">
              <accounts>
                <account username="151" />
                <account domain="pbx.example.ch" />
                <account username="152" domain="pbx.example.ch" />
              </accounts>
            </nipp-provisioning>
            """;

        Assert.True(ProvisioningParser.TryParse(xml, out var profile, out _));
        Assert.Single(profile.Accounts);
        Assert.Equal("152", profile.Accounts[0].Username);
    }

    /// <summary>§20.2 lässt höchstens zehn Konten zu — auch aus einem Profil.</summary>
    [Fact]
    public void NimmtHoechstensZehnKonten()
    {
        var accounts = string.Concat(Enumerable.Range(1, 15)
            .Select(i => $"<account username=\"1{i:00}\" domain=\"pbx.example.ch\" />"));

        var xml = $"<nipp-provisioning version=\"1\"><accounts>{accounts}</accounts></nipp-provisioning>";

        Assert.True(ProvisioningParser.TryParse(xml, out var profile, out _));
        Assert.Equal(10, profile.Accounts.Count);
    }

    /// <summary>
    /// Ein neueres Schema wird gelesen, was davon bekannt ist. Abzubrechen
    /// hiesse, dass ein Profil mit einem einzigen neuen Feld eine ältere
    /// Installation vollständig lahmlegt.
    /// </summary>
    [Fact]
    public void LiestEinNeueresSchemaSoweitMoeglich()
    {
        const string xml = """
            <nipp-provisioning version="7" profile="zukunft">
              <accounts><account username="151" domain="pbx.example.ch" /></accounts>
              <hologramm aktiv="true" />
            </nipp-provisioning>
            """;

        Assert.True(ProvisioningParser.TryParse(xml, out var profile, out var error));
        Assert.Single(profile.Accounts);

        // Kein Fehler im Sinne von „gescheitert", aber ein Hinweis.
        Assert.Contains("Version", error, StringComparison.Ordinal);
    }

    [Fact]
    public void EinLeeresProfilIstLeerAberGueltig()
    {
        Assert.True(ProvisioningParser.TryParse(
            "<nipp-provisioning version=\"1\" />", out var profile, out var error));

        Assert.Null(error);
        Assert.True(profile.IsEmpty);
    }
}

/// <summary>
/// AP8.4: gesperrte Felder. Getestet wird vor allem die Vererbung nach unten —
/// <c>network</c> muss <c>network.sip-port</c> mitsperren, sonst müsste ein
/// Profil jedes Feld einzeln aufzählen und würde bei jeder neuen Einstellung
/// unvollständig.
/// </summary>
public sealed class PolicyServiceTests
{
    [Fact]
    public void SperrtGenanntesFeld()
    {
        var policy = new PolicyService();
        policy.Apply(["network.sip-port"]);

        Assert.True(policy.IsLocked("network.sip-port"));
        Assert.False(policy.IsLocked("network.keep-alive-seconds"));
        Assert.True(policy.HasAnyLock);
    }

    [Fact]
    public void EinOberpfadSperrtAllesDarunter()
    {
        var policy = new PolicyService();
        policy.Apply(["network"]);

        Assert.True(policy.IsLocked("network"));
        Assert.True(policy.IsLocked("network.sip-port"));
        Assert.True(policy.IsLocked("network.rtp.port-min"));
        Assert.False(policy.IsLocked("audio.playback-volume"));
    }

    [Fact]
    public void OhneProfilIstNichtsGesperrt()
    {
        var policy = new PolicyService();

        Assert.False(policy.HasAnyLock);
        Assert.False(policy.IsLocked("network.sip-port"));
        Assert.False(policy.IsLocked(null));
        Assert.False(policy.IsLocked(""));
    }

    [Fact]
    public void EinNeuesProfilErsetztDieAltenSperren()
    {
        var policy = new PolicyService();
        var changes = 0;
        policy.Changed += (_, _) => changes++;

        policy.Apply(["network"]);
        policy.Apply(["audio"]);

        Assert.False(policy.IsLocked("network.sip-port"));
        Assert.True(policy.IsLocked("audio.playback-volume"));
        Assert.Equal(2, changes);
    }

    /// <summary>Gleiche Sperren nochmals anzuwenden löst keine Aktualisierung aus.</summary>
    [Fact]
    public void MeldetNurEchteAenderungen()
    {
        var policy = new PolicyService();
        var changes = 0;
        policy.Changed += (_, _) => changes++;

        policy.Apply(["network", "audio"]);
        policy.Apply(["audio", "network"]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void SperrenSindNichtGrossKleinEmpfindlich()
    {
        var policy = new PolicyService();
        policy.Apply(["Network.Sip-Port"]);

        Assert.True(policy.IsLocked("network.sip-port"));
    }
}
