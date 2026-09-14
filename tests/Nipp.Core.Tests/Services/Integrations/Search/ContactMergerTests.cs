using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Search;

namespace Nipp.Core.Tests.Services.Integrations.Search;

/// <summary>
/// Das Zusammenführen von Treffern aus mehreren Quellen (§21.4).
///
/// <b>Der Fehler, um den es hier geht, ist nicht die doppelte Zeile.</b> Zwei
/// Zeilen für dieselbe Person sind unschön. Eine Zeile für zwei Personen führt
/// dazu, dass jemand die falsche Nummer anruft — und er merkt es erst, wenn
/// sich jemand anderes meldet.
///
/// Die Tests prüfen deshalb in beide Richtungen: dass zusammengeführt wird,
/// was nachweislich dieselbe Person ist, und vor allem, dass alles andere
/// getrennt bleibt.
/// </summary>
public sealed class ContactMergerTests
{
    private static readonly MergeSettings Standard = new();

    private static readonly MergeSettings MitNamen = new() { ByNameAndCompany = true };

    private static readonly MergeSettings Aus = new() { Enabled = false };

    private static Contact Kontakt(
        string quelle,
        string name,
        string? nummer = null,
        string? firma = null,
        string? email = null) =>
        new(
            $"{quelle}:{name}",
            name,
            nummer is null ? [] : [new ContactNumber(nummer, ContactNumberKind.Business)],
            quelle == "outlook" ? ContactSourceKind.Outlook : ContactSourceKind.External,
            Company: firma,
            SourceId: quelle,
            Email: email,
            ExternalId: name);

    private static IReadOnlyList<Contact> Fuehre(
        IReadOnlyList<Contact> bestehend,
        IReadOnlyList<Contact> neu,
        MergeSettings? einstellungen = null) =>
        new ContactMerger().Merge(bestehend, neu, einstellungen ?? Standard);

    /// <summary>
    /// <b>Der Zentrale-Fall.</b> Zwei Personen derselben Firma tragen im CRM
    /// beide die Hauptnummer als Geschäftsnummer. Genau der Fall, in dem eine
    /// Zeile für zwei Personen entstand — mit dem Namen der einen und der
    /// Nummer der anderen.
    ///
    /// Belegt an echten Daten: unter der Zürcher Nummer 044 395 40 1x hängen im
    /// das CRM mehrere Personen derselben Firma.
    /// </summary>
    [Fact]
    public void Eine_Firmenzentrale_fuehrt_zwei_Personen_nicht_zusammen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Peter Meier", "0443954016")],
            [
                Kontakt("crm", "Hans Muster", "0443954016"),
                Kontakt("crm", "Toni Muster", "0443954016"),
            ]);

        // Drei Einträge, nicht zwei: die Nummer gehört im CRM zwei Personen und
        // bezeichnet damit keine von ihnen.
        Assert.Equal(3, ergebnis.Count);
        Assert.Contains(ergebnis, c => c.DisplayName == "Hans Muster");
        Assert.Contains(ergebnis, c => c.DisplayName == "Toni Muster");
        Assert.Contains(ergebnis, c => c.DisplayName == "Peter Meier");
    }

    /// <summary>
    /// Die Gegenprobe: dass dieselbe Nummer in <b>zwei verschiedenen</b> Quellen
    /// steht, ist der Normalfall und muss weiterhin zusammenführen — auch bei
    /// verschieden geschriebenen Namen.
    /// </summary>
    [Fact]
    public void Dieselbe_Nummer_in_zwei_Quellen_fuehrt_weiterhin_zusammen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0445128430")],
            [Kontakt("crm", "H. Muster", "044 512 84 30")]);

        Assert.Single(ergebnis);
    }

    // --- Was zusammengeführt wird ---

    /// <summary>
    /// Das sicherste Merkmal. Verglichen wird über <c>ClipResolver</c> —
    /// dieselbe Regel, nach der auch ein eingehender Anruf einem Kontakt
    /// zugeordnet wird.
    /// </summary>
    [Fact]
    public void Gleiche_Nummer_fuehrt_zusammen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "044 512 84 30")],
            [Kontakt("crm", "H. Muster", "0445128430")]);

        var zusammengefuehrt = Assert.Single(ergebnis);

        // Der bestehende Eintrag behält seinen Namen und seinen Platz.
        Assert.Equal("Hans Muster", zusammengefuehrt.DisplayName);
        Assert.True(zusammengefuehrt.IsMerged);
        Assert.Equal(["outlook", "crm"], zusammengefuehrt.AllOrigins.Select(static o => o.SourceId));
    }

    /// <summary>
    /// Auch über Schreibweisen hinweg: <c>+41445128430</c> und
    /// <c>044 512 84 30</c> sind dieselbe Nummer.
    /// </summary>
    [Theory]
    [InlineData("+41445128430")]
    [InlineData("0041445128430")]
    [InlineData("044 512 84 30")]
    public void Die_Schreibweise_der_Nummer_spielt_keine_Rolle(string andereSchreibweise)
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0445128430")],
            [Kontakt("crm", "Hans Muster", andereSchreibweise)]);

        Assert.Single(ergebnis);
    }

    [Fact]
    public void Gleiche_E_Mail_fuehrt_zusammen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0791111111", email: "hans@muster.ch")],
            [Kontakt("crm", "Hans M.", "0442222222", email: "HANS@MUSTER.CH")]);

        Assert.Single(ergebnis);
    }

    [Fact]
    public void Beim_Zusammenfuehren_werden_die_Nummern_vereinigt()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0445128430")],
            [Kontakt("crm", "Hans Muster", "0445128430", email: "hans@muster.ch")]);

        var zusammengefuehrt = Assert.Single(ergebnis);

        // Dieselbe Nummer in zwei Schreibweisen bleibt eine Nummer.
        Assert.Single(zusammengefuehrt.Numbers);

        // Was der bestehende Eintrag nicht wusste, kommt dazu.
        Assert.Equal("hans@muster.ch", zusammengefuehrt.Email);
    }

    [Fact]
    public void Eine_zweite_Nummer_kommt_dazu()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0445128430", email: "hans@muster.ch")],
            [Kontakt("crm", "Hans Muster", "0791234567", email: "hans@muster.ch")]);

        var zusammengefuehrt = Assert.Single(ergebnis);

        Assert.Equal(2, zusammengefuehrt.Numbers.Count);
    }

    // --- Was getrennt bleibt ---

    [Fact]
    public void Verschiedene_Personen_bleiben_getrennt()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0445128430")],
            [Kontakt("crm", "Anna Beispiel", "0791234567")]);

        Assert.Equal(2, ergebnis.Count);
    }

    /// <summary>
    /// <b>Der wichtigste Fall.</b> Zwei Personen derselben Firma sind zwei
    /// Personen — auch wenn sie zufällig gleich heissen. Deshalb ist das
    /// Merkmal „Name und Firma" ab Werk aus.
    /// </summary>
    [Fact]
    public void Gleicher_Name_und_gleiche_Firma_fuehren_ab_Werk_nicht_zusammen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Peter Meier", "0441111111", firma: "Muster AG")],
            [Kontakt("crm", "Peter Meier", "0442222222", firma: "Muster AG")]);

        Assert.Equal(2, ergebnis.Count);
    }

    [Fact]
    public void Mit_ausdruecklicher_Erlaubnis_fuehren_Name_und_Firma_zusammen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Peter Meier", "0441111111", firma: "Muster AG")],
            [Kontakt("crm", "Peter Meier", "0442222222", firma: "Muster AG")],
            MitNamen);

        Assert.Single(ergebnis);
    }

    /// <summary>
    /// Der Name allein genügt nie — „Peter Meier" gibt es mehrfach, und ohne
    /// Firma ist nicht zu entscheiden, welcher gemeint ist.
    /// </summary>
    [Fact]
    public void Der_Name_allein_genuegt_auch_mit_Erlaubnis_nicht()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Peter Meier", "0441111111")],
            [Kontakt("crm", "Peter Meier", "0442222222")],
            MitNamen);

        Assert.Equal(2, ergebnis.Count);
    }

    /// <summary>
    /// Eine dreistellige Nebenstelle passte sonst auf jede Nummer, die
    /// zufällig so endet — <c>ClipResolver</c> vergleicht kurze Nummern
    /// deshalb nur exakt.
    /// </summary>
    [Fact]
    public void Eine_kurze_Nebenstelle_trifft_nicht_auf_jede_lange_Nummer()
    {
        var ergebnis = Fuehre(
            [Kontakt("team", "Support", "430")],
            [Kontakt("crm", "Muster AG", "0445128430")]);

        Assert.Equal(2, ergebnis.Count);
    }

    /// <summary>
    /// Wenn ein System zwei Einträge liefert, hat es dafür seine Gründe, und
    /// nipp weiss sie nicht.
    /// </summary>
    [Fact]
    public void Zwei_Eintraege_derselben_Quelle_werden_nie_zusammengefuehrt()
    {
        var ergebnis = Fuehre(
            [Kontakt("crm", "Hans Muster", "0445128430")],
            [Kontakt("crm", "Hans Muster (privat)", "0445128430")]);

        Assert.Equal(2, ergebnis.Count);
    }

    [Fact]
    public void Ohne_gemeinsames_Merkmal_bleibt_es_bei_zwei_Zeilen()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster")],
            [Kontakt("crm", "Hans Muster")]);

        Assert.Equal(2, ergebnis.Count);
    }

    // --- Abschaltbar ---

    [Fact]
    public void Abgeschaltet_wird_nichts_zusammengefuehrt()
    {
        var ergebnis = Fuehre(
            [Kontakt("outlook", "Hans Muster", "0445128430")],
            [Kontakt("crm", "Hans Muster", "0445128430")],
            Aus);

        Assert.Equal(2, ergebnis.Count);
    }

    [Fact]
    public void In_eine_leere_Liste_wird_einfach_eingefuegt()
    {
        var ergebnis = Fuehre([], [Kontakt("crm", "Hans Muster", "0445128430")]);

        Assert.Single(ergebnis);
    }

    /// <summary>
    /// Passt ein Kandidat auf zwei bestehende Einträge, ist mindestens eine
    /// der beiden Zuordnungen falsch. Drei Einträge zu einer Zeile zu
    /// verschmelzen machte den Fehler nur grösser — es gewinnt der erste.
    /// </summary>
    [Fact]
    public void Bei_zwei_moeglichen_Treffern_gewinnt_der_erste()
    {
        var ergebnis = Fuehre(
            [
                Kontakt("outlook", "Hans Muster", "0445128430"),
                Kontakt("team", "Hans M.", "0445128430"),
            ],
            [Kontakt("crm", "H. Muster", "0445128430")]);

        Assert.Equal(2, ergebnis.Count);
        Assert.True(ergebnis[0].IsMerged);
        Assert.False(ergebnis[1].IsMerged);
    }
}
