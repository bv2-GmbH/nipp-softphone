using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Was ein Wechsel der Audiogeräte bedeutet (§9.4, W2.1 Etappe B3).
///
/// <para><b>Warum das geprüft wird.</b> Die Entscheidung lief bisher mitten
/// in einer Reaktion auf einen SDK-Callback und war nur mit einem echten
/// Headset in der Hand zu messen — Einstecken, Abziehen, Andocken. Was hier
/// steht, ist der Teil davon, der keine Hardware braucht: <b>welches Gerät
/// fehlt, und was der Benutzer darüber liest.</b></para>
///
/// <para><b>Was diese Tests nicht ersetzen:</b> T148, T149 und T152 — ob das
/// Umstellen am Gerät auch wirklich gelingt, sagt nur ein Headset.</para>
/// </summary>
public class AudioDeviceChoiceTests
{
    private static AudioDeviceInfo Gerat(string id, string name) => new(id, name, true, true);

    private static readonly AudioDeviceInfo Notebook = Gerat("nb", "Notebook-Lautsprecher");
    private static readonly AudioDeviceInfo Headset = Gerat("hs", "Jabra Link 400");
    private static readonly AudioDeviceInfo Dock = Gerat("dk", "Dockingstation");

    /// <summary>
    /// <b>Ein eingestecktes Gerät ist keine Meldung wert</b> — es
    /// funktioniert einfach. Eine Mitteilung bei jedem Andocken wäre die
    /// Sorte Hinweis, die man nach dem dritten Mal wegklickt, ohne zu lesen.
    /// </summary>
    [Fact]
    public void Ein_neues_Geraet_ergibt_keinen_Hinweis()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook],
            [Notebook, Headset],
            ["nb"],
            imGespraech: false);

        Assert.Empty(urteil.LostNames);
        Assert.Null(urteil.Notice);
    }

    /// <summary>
    /// <b>Das gewählte Headset verschwindet</b> — der Fall, für den es diesen
    /// Hinweis gibt. Genannt wird sein Name, nicht seine Kennung.
    /// </summary>
    [Fact]
    public void Das_gewaehlte_Geraet_verschwindet()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook, Headset],
            [Notebook],
            ["hs"],
            imGespraech: false);

        Assert.Equal(["Jabra Link 400"], urteil.LostNames);
        Assert.Contains("Jabra Link 400", urteil.Notice);
        Assert.Contains("Standardgerät von Windows", urteil.Notice);
    }

    /// <summary>
    /// <b>Derselbe Verlust heisst im Gespräch etwas anderes.</b> Ausserhalb
    /// ist es eine Einstellung, die sich geändert hat; mitten im Gespräch ist
    /// es die Antwort auf «warum höre ich nichts mehr?».
    /// </summary>
    [Fact]
    public void Im_Gespraech_sagt_der_Hinweis_dass_es_weiterlaeuft()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook, Headset],
            [Notebook],
            ["hs"],
            imGespraech: true);

        Assert.Contains("während des Gesprächs", urteil.Notice);
        Assert.Contains("läuft weiter", urteil.Notice);
    }

    /// <summary>
    /// <b>Ein Gerät, das nie da war, verschwindet nicht.</b> Wer ein Headset
    /// eingestellt hat und ohne es arbeitet, bekäme sonst bei jedem
    /// Gerätewechsel dieselbe Meldung.
    /// </summary>
    [Fact]
    public void Ein_nie_vorhandenes_Geraet_wird_nicht_vermisst()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook],
            [Notebook, Dock],
            ["hs"],
            imGespraech: false);

        Assert.Empty(urteil.LostNames);
        Assert.Null(urteil.Notice);
    }

    /// <summary>
    /// <b>Das Headset kommt zurück</b> — und damit ist die Sache erledigt,
    /// ohne Hinweis. Der Fall Andocken/Abziehen/Andocken, dreimal am Tag.
    /// </summary>
    [Fact]
    public void Ein_zurueckgekehrtes_Geraet_ergibt_keinen_Hinweis()
    {
        var weg = AudioDeviceChoice.Evaluate([Notebook, Headset], [Notebook], ["hs"], false);
        var zurueck = AudioDeviceChoice.Evaluate([Notebook], [Notebook, Headset], ["hs"], false);

        Assert.Single(weg.LostNames);
        Assert.Empty(zurueck.LostNames);
        Assert.Null(zurueck.Notice);
    }

    /// <summary>
    /// <b>Dieselbe Kennung zählt einmal.</b> Wer Mikrofon, Lautsprecher und
    /// Klingelgerät auf dasselbe Headset stellt — der Normalfall —, hat
    /// <b>ein</b> Gerät verloren und liest seinen Namen einmal.
    /// </summary>
    [Fact]
    public void Ein_Geraet_fuer_drei_Rollen_wird_einmal_gezaehlt()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook, Headset],
            [Notebook],
            ["hs", "hs", "hs"],
            imGespraech: false);

        Assert.Equal(["Jabra Link 400"], urteil.LostNames);
        Assert.DoesNotContain("2 Audiogeräte", urteil.Notice);
    }

    /// <summary>
    /// Zwei verschiedene Geräte, beide weg: dann wird gezählt statt
    /// aufgezählt — drei Namen in einem Satz liest niemand.
    /// </summary>
    [Fact]
    public void Zwei_verlorene_Geraete_werden_gezaehlt()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook, Headset, Dock],
            [Notebook],
            ["hs", "dk"],
            imGespraech: false);

        Assert.Equal(2, urteil.LostNames.Count);
        Assert.Contains("2 Audiogeräte sind", urteil.Notice);
    }

    /// <summary>
    /// <b>Zwei Geräte mit demselben Namen</b> unterscheiden sich in der
    /// Kennung, und danach geht es. Zwei gleich benannte Docks am selben
    /// Arbeitsplatz gibt es wirklich — und wer das über den Namen
    /// entscheidet, wechselt beim Abziehen des einen auf das andere.
    /// </summary>
    [Fact]
    public void Gleiche_Namen_werden_ueber_die_Kennung_unterschieden()
    {
        var erstes = Gerat("dk1", "Dockingstation");
        var zweites = Gerat("dk2", "Dockingstation");

        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook, erstes, zweites],
            [Notebook, zweites],
            ["dk1"],
            imGespraech: false);

        Assert.Single(urteil.LostNames);
        Assert.NotNull(urteil.Notice);

        // Die Gegenprobe: das verbliebene gleichnamige Gerät gilt nicht als
        // Ersatz — es ist ein anderes Gerät.
        var andersherum = AudioDeviceChoice.Evaluate(
            [Notebook, erstes, zweites],
            [Notebook, zweites],
            ["dk2"],
            imGespraech: false);

        Assert.Empty(andersherum.LostNames);
    }

    /// <summary>
    /// Ohne eigene Wahl gibt es nichts zu verlieren: dann gilt ohnehin der
    /// Windows-Standard (§9.4).
    /// </summary>
    [Fact]
    public void Ohne_eigene_Wahl_gibt_es_keinen_Verlust()
    {
        var urteil = AudioDeviceChoice.Evaluate(
            [Notebook, Headset],
            [Notebook],
            [null, "", null],
            imGespraech: true);

        Assert.Empty(urteil.LostNames);
        Assert.Null(urteil.Notice);
    }
}
