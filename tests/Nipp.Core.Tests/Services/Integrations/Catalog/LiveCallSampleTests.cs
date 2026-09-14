using Nipp.Core.Services.Integrations.Catalog;

namespace Nipp.Core.Tests.Services.Integrations.Catalog;

/// <summary>
/// Die Antwort eines echten Abrufs kommt in der Vorschau an — auch eine lange
/// (13.09.2026).
///
/// <para><b>Der Befund.</b> Im Karten-Designer liess sich eine Rufnummer
/// eingeben und ein echter Abruf auslösen; die Vorschau zeigte danach weiter
/// die erfundenen Beispieldaten. Die Ursache lag <b>nicht</b> im Zeichnen: der
/// Designer bekam die <em>Anzeigefassung</em> der Antwort — bei 8192 Zeichen
/// abgeschnitten und mit «… (gekürzt)» versehen. Das ist kein gültiges JSON
/// mehr; <c>JsonNode.Parse</c> warf, und der Fänger hier kehrte <b>still</b>
/// zurück.</para>
///
/// <para><b>Und es sah aus wie ein Erfolg:</b> der Aufrufer zählte den Abruf
/// mit, ohne das Ergebnis anzusehen, und meldete «1 von 1 Quellen haben
/// geantwortet».</para>
///
/// <para>Diese Tests prüfen die Stelle, an der es <em>brach</em> — der ältere
/// Test <c>Ein_echter_Testabruf_steht_in_der_Vorschau_und_wird_benannt</c>
/// ruft <c>SetFromLiveCall</c> mit sauberem JSON und war deshalb die ganze
/// Zeit grün.</para>
/// </summary>
public sealed class LiveCallSampleTests
{
    [Fact]
    public void Eine_saubere_Antwort_wird_uebernommen()
    {
        var proben = new TestSampleStore();

        Assert.True(proben.SetFromLiveCall("crm", """{ "name": "Echt Gemessen" }"""));
        Assert.True(proben.IsFromLiveCall("crm"));
    }

    [Fact]
    public void Eine_lange_Antwort_wird_ebenfalls_uebernommen()
    {
        // <b>Das ist der Befund.</b> Über 8192 Zeichen — genau die Grenze, an
        // der die Anzeigefassung abschneidet. Ungekürzt ist auch das gültiges
        // JSON und muss ankommen.
        var proben = new TestSampleStore();
        var lang = new string('x', 9000);

        Assert.True(proben.SetFromLiveCall("crm", $$"""{ "name": "Echt Gemessen", "notiz": "{{lang}}" }"""));
        Assert.True(proben.IsFromLiveCall("crm"));

        var abgelegt = proben.For("crm");

        Assert.NotNull(abgelegt);
        Assert.Equal("Echt Gemessen", abgelegt!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Eine_abgeschnittene_Antwort_wird_abgelehnt_und_sagt_es()
    {
        // Die Nachbildung der Anzeigefassung: gültiges JSON, hart
        // abgeschnitten, mit dem Hinweis am Ende.
        var proben = new TestSampleStore();

        Assert.False(proben.SetFromLiveCall("crm", "{ \"name\": \"Echt Gem\n… (gekürzt)"));
        Assert.False(proben.IsFromLiveCall("crm"));
    }

    [Fact]
    public void Eine_abgelehnte_Antwort_laesst_die_vorhandene_stehen()
    {
        // Die Gegenprobe: eine unlesbare Antwort ist kein Grund, das
        // wegzuwerfen, was schon da war. Eine leere Vorschau wäre die
        // schlechtere Auskunft als eine alte.
        var proben = new TestSampleStore();

        Assert.True(proben.SetFromLiveCall("crm", """{ "name": "Erster Abruf" }"""));
        Assert.False(proben.SetFromLiveCall("crm", "kein json"));

        var abgelegt = proben.For("crm");

        Assert.NotNull(abgelegt);
        Assert.Equal("Erster Abruf", abgelegt!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Eine_leere_Antwort_wird_abgelehnt(string? antwort)
    {
        var proben = new TestSampleStore();

        Assert.False(proben.SetFromLiveCall("crm", antwort));
    }
}
