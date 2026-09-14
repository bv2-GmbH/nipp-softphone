using Nipp.Core.Services.Contacts;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Der Fall, um den es geht, steht in der ersten Zeile: die Einstellungen
/// kennen die Adresse nackt, das SDK meldet sie mit Anzeigename. Beides muss
/// dieselbe Nebenstelle sein — sonst bleibt das Besetztlampenfeld auf
/// „unbekannt", obwohl der Zustand längst da ist. Genau so am 05.09.2026 im
/// Log gesehen.
/// </summary>
public sealed class SipUriTests
{
    [Theory]
    [InlineData("\"152\" <sip:152@pbx.example.ch>", "sip:152@pbx.example.ch")]
    [InlineData("<sip:152@pbx.example.ch>", "sip:152@pbx.example.ch")]
    [InlineData("sip:152@pbx.example.ch", "sip:152@pbx.example.ch")]
    // Ohne Schema eingetragen — der Benutzer tippt das gern so.
    [InlineData("152@pbx.example.ch", "sip:152@pbx.example.ch")]
    // Transportparameter gehören nicht zur Identität.
    [InlineData("sip:152@pbx.example.ch;transport=udp", "sip:152@pbx.example.ch")]
    [InlineData("<sip:152@pbx.example.ch;transport=tls>;expires=600", "sip:152@pbx.example.ch")]
    // Kopfzeilen auch nicht.
    [InlineData("sip:152@pbx.example.ch?subject=x", "sip:152@pbx.example.ch")]
    // sips bleibt sips — wer das schreibt, meint es.
    [InlineData("sips:152@pbx.example.ch", "sips:152@pbx.example.ch")]
    // Leerraum aussen herum.
    [InlineData("  sip:152@pbx.example.ch  ", "sip:152@pbx.example.ch")]
    public void BringtSchreibweisenAufEineForm(string input, string expected) =>
        Assert.Equal(expected, SipUri.Normalize(input));

    [Fact]
    public void EinstellungUndSdkMeldungSindDieselbeNebenstelle() =>
        Assert.True(SipUri.Same("sip:152@pbx.example.ch", "\"152\" <sip:152@pbx.example.ch>"));

    [Fact]
    public void HostIstNichtSchreibungsempfindlich() =>
        Assert.True(SipUri.Same("sip:152@PBX.Example.CH", "sip:152@pbx.example.ch"));

    [Fact]
    public void VerschiedeneNebenstellenBleibenVerschieden()
    {
        Assert.False(SipUri.Same("sip:152@pbx.example.ch", "sip:153@pbx.example.ch"));
        Assert.False(SipUri.Same("sip:152@pbx.example.ch", "sip:152@andere.example.ch"));
    }

    [Fact]
    public void LeeresIstNichtsUndGleichtNichts()
    {
        Assert.Equal(string.Empty, SipUri.Normalize(null));
        Assert.Equal(string.Empty, SipUri.Normalize("   "));
        Assert.Equal(string.Empty, SipUri.Normalize("<>"));
        Assert.False(SipUri.Same(null, null));
        Assert.False(SipUri.Same("", ""));
    }
}
