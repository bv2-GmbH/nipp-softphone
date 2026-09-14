using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Welche Soundkarte die Töne des SDK bekommen.
///
/// <para>Der Grund für diese Tests steht im Protokoll vom 07.09.2026: die
/// alte Geräte-API des SDK führt Namen mit Treiberpräfix („WASAPI: …"), die
/// neue ohne. Zugewiesen wurde bisher der Name der neuen — und der Setter der
/// alten <b>wirft</b>, wenn er ihn nicht kennt. Für „Default Playback" ging
/// das gut; ein namentlich gewähltes Headset wäre stumm geblieben.</para>
/// </summary>
public class ToneCardChooserTests
{
    private const string Headset = "Kopfhörer (Jabra Link 400)";
    private const string HeadsetKarte = "WASAPI: Kopfhörer (Jabra Link 400)";
    private const string Standard = "Default Playback";

    private static readonly ToneCard[] Karten =
    [
        new(Standard, CanPlay: true),
        new(HeadsetKarte, CanPlay: true),
        new("WASAPI: Mikrofon (Jabra Link 400)", CanPlay: false),
    ];

    [Fact]
    public void Der_exakte_Name_kommt_zuerst()
    {
        var kandidaten = ToneCardChooser.Choose(Standard, currentCard: HeadsetKarte, Karten);

        Assert.Equal(Standard, kandidaten[0]);
    }

    /// <summary>Der eigentliche Fall: Wunsch ohne Präfix, Karte mit.</summary>
    [Fact]
    public void Ein_Name_ohne_Treiberpraefix_findet_die_Karte_mit()
    {
        var kandidaten = ToneCardChooser.Choose(Headset, currentCard: Standard, Karten);

        Assert.Equal(HeadsetKarte, kandidaten[0]);
    }

    [Fact]
    public void Karten_die_nicht_wiedergeben_koennen_kommen_nicht_vor()
    {
        var kandidaten = ToneCardChooser.Choose("Mikrofon", currentCard: null, Karten);

        Assert.DoesNotContain("WASAPI: Mikrofon (Jabra Link 400)", kandidaten);
    }

    [Fact]
    public void Steht_die_gewuenschte_Karte_schon_ist_nichts_zu_tun()
    {
        var kandidaten = ToneCardChooser.Choose(Headset, currentCard: HeadsetKarte, Karten);

        Assert.Empty(kandidaten);
    }

    /// <summary>
    /// §9.4: ohne Wahl folgt nipp dem Windows-Standard, und den kennt das SDK
    /// selbst besser. Also nichts anfassen.
    /// </summary>
    [Fact]
    public void Ohne_Wahl_bleibt_eine_brauchbare_Karte_stehen()
    {
        var kandidaten = ToneCardChooser.Choose(null, currentCard: Standard, Karten);

        Assert.Empty(kandidaten);
    }

    /// <summary>
    /// Der Fall, der das Freizeichen ursprünglich gekostet hat: <c>play_sndcard</c>
    /// war leer. Das ist kein Standard, sondern die Leere — dann klingt gar
    /// kein Ton.
    /// </summary>
    [Fact]
    public void Ohne_Wahl_und_ohne_Karte_wird_die_erste_brauchbare_genommen()
    {
        var kandidaten = ToneCardChooser.Choose(null, currentCard: null, Karten);

        Assert.Equal(Standard, kandidaten[0]);
    }

    [Fact]
    public void Eine_unbrauchbare_aktuelle_Karte_wird_ersetzt()
    {
        var kandidaten = ToneCardChooser.Choose(
            null, currentCard: "WASAPI: Mikrofon (Jabra Link 400)", Karten);

        Assert.NotEmpty(kandidaten);
        Assert.Equal(Standard, kandidaten[0]);
    }

    /// <summary>
    /// Ein Gerät, das es in der alten API nicht gibt, darf nicht in Stille
    /// enden: dann gilt irgendeine Karte, die spielen kann.
    /// </summary>
    [Fact]
    public void Ein_unbekannter_Wunsch_faellt_auf_die_brauchbaren_zurueck()
    {
        var kandidaten = ToneCardChooser.Choose(
            "Lautsprecher (irgendein Dock)", currentCard: null, Karten);

        Assert.NotEmpty(kandidaten);
        Assert.All(kandidaten, k => Assert.Contains(k, Karten.Where(c => c.CanPlay).Select(c => c.Name)));
    }

    [Fact]
    public void Ohne_jede_brauchbare_Karte_gibt_es_keinen_Kandidaten()
    {
        ToneCard[] nurEingang = [new("WASAPI: Mikrofon", CanPlay: false)];

        Assert.Empty(ToneCardChooser.Choose(Headset, currentCard: null, nurEingang));
    }

    [Fact]
    public void Kein_Kandidat_kommt_zweimal_vor()
    {
        var kandidaten = ToneCardChooser.Choose(Headset, currentCard: null, Karten);

        Assert.Equal(kandidaten.Count, kandidaten.Distinct(StringComparer.Ordinal).Count());
    }
}
