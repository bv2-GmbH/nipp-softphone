using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// §10: Klick-to-Call aus Outlook und dem CRM läuft über
/// <c>tel:</c>-Links. Was da ankommt, ist erstaunlich uneinheitlich — Grund
/// genug, die Zerlegung zu testen statt zu hoffen.
/// </summary>
public sealed class CallUriTests
{
    [Theory]
    [InlineData("tel:+41445128430", "+41445128430")]
    [InlineData("tel:0445128430", "0445128430")]
    [InlineData("callto:0791234567", "0791234567")]
    [InlineData("sip:151@pbx.example.ch", "151@pbx.example.ch")]
    [InlineData("sips:151@pbx.example.ch", "151@pbx.example.ch")]
    public void Die_ueblichen_Formen_werden_verstanden(string uri, string expected)
    {
        Assert.Equal(expected, WindowsIntegration.ParseCallUri(uri));
    }

    [Fact]
    public void Kodierte_Zeichen_werden_aufgeloest()
    {
        // Outlook kodiert das Pluszeichen gern.
        Assert.Equal("+41445128430", WindowsIntegration.ParseCallUri("tel:%2B41445128430"));
    }

    [Theory]
    [InlineData("tel:+41445128430;phone-context=example.ch", "+41445128430")]
    [InlineData("sip:151@pbx.example.ch;transport=udp", "151@pbx.example.ch")]
    [InlineData("tel:+41445128430?subject=Rueckruf", "+41445128430")]
    public void Angehaengte_Parameter_werden_abgeschnitten(string uri, string expected)
    {
        Assert.Equal(expected, WindowsIntegration.ParseCallUri(uri));
    }

    [Fact]
    public void Grossschreibung_im_Schema_stoert_nicht()
    {
        Assert.Equal("+41445128430", WindowsIntegration.ParseCallUri("TEL:+41445128430"));
    }

    [Fact]
    public void Leerraum_aussen_stoert_nicht()
    {
        Assert.Equal("+41445128430", WindowsIntegration.ParseCallUri("  tel:+41445128430  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+41445128430")]              // ohne Schema
    [InlineData("https://example.ch")]        // fremdes Schema
    [InlineData("mailto:a@example.ch")]
    [InlineData("tel:")]                      // Schema ohne Ziel
    public void Was_kein_Anruf_ist_ergibt_null(string? uri)
    {
        Assert.Null(WindowsIntegration.ParseCallUri(uri));
    }
}
