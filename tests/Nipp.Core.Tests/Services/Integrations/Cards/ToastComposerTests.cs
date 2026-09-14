using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Was im Toast eines eingehenden Anrufs steht (§8.6, ADR-030).
///
/// <para>Diese Tests gibt es, weil der Toast in <c>Nipp.App</c> lebt und dort
/// nichts prüfbar ist — <c>Nipp.App</c> hat kein Testprojekt. Beim Toast war
/// das schon einmal teuer: die Regel „beginnt hier ein Anruf zu klingeln?"
/// stand doppelt und war beide Male falsch, wodurch ein eingehender Anruf
/// überhaupt kein Zeichen gab.</para>
///
/// <para>Der wichtigste Fall ist deshalb <see cref="Ohne_Quellen_bleibt_es_beim_bisherigen_Text"/>:
/// fällt die Integration aus, muss der Toast genau das zeigen, was er vor
/// ADR-030 gezeigt hat. §21 verlangt, dass Telefonieren von keiner Integration
/// abhängt.</para>
/// </summary>
public class ToastComposerTests
{
    private const string Nummer = "+41441234567";

    /// <summary>Die Felder, wie die CRM-Vorlage sie liefert.</summary>
    private static readonly (string Feld, string Wert)[] Crm =
    [
        ("contactName", "Hans Muster"),
        ("contactType", "Kunde"),
        ("company", "Muster AG"),
        ("letzteArbeitZeile", "04.09. · Migration Telefonie · A. Beispiel"),
        ("letzteArbeitWer", "A. Beispiel"),
    ];

    /// <summary>Die Felder, wie die das Gesprächsjournal-Vorlage sie liefert.</summary>
    private static readonly (string Feld, string Wert)[] Memory =
    [
        ("displayName", "Hans Muster"),
        ("letzteZusammenfassung", "Musterwerk mit Frau Beispiel besprechen, dieser ruft zurück."),
        ("verlauf", "6 Anrufe, zuletzt 04.09.2026"),
    ];

    private static ContextSnapshot Snapshot(
        params (string Quelle, SourceState Zustand, (string Feld, string Wert)[] Felder)[] quellen)
    {
        var sources = new Dictionary<string, ContextFragment>(StringComparer.Ordinal);

        foreach (var (quelle, zustand, felder) in quellen)
        {
            sources[quelle] = new ContextFragment(
                quelle,
                quelle,
                zustand,
                felder.ToDictionary(
                    f => f.Feld,
                    f => ContextValue.FromText(f.Wert),
                    StringComparer.Ordinal));
        }

        return new ContextSnapshot(
            CallHandle.New(),
            PhoneNumberKey.From(Nummer, new NumberNormalizer("+41")),
            sources);
    }

    // ---- Der Rückfall: die Integration darf nichts kosten (§21) ----

    [Fact]
    public void Ohne_Quellen_bleibt_es_beim_bisherigen_Text()
    {
        var zeilen = ToastComposer.Compose(snapshot: null, "Dominic Brunner", Nummer);

        Assert.Equal("Dominic Brunner", zeilen.Line1);
        Assert.Null(zeilen.Line2);
        Assert.Null(zeilen.Line3);
        Assert.False(zeilen.HasContext);
    }

    [Fact]
    public void Antworten_alle_Quellen_leer_bleibt_der_Name_allein()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Empty, []), ("memory", SourceState.Empty, [])),
            "Dominic Brunner",
            Nummer);

        Assert.Equal("Dominic Brunner", zeilen.Line1);
        Assert.False(zeilen.HasContext);
    }

    [Fact]
    public void Eine_Zeitueberschreitung_zaehlt_nicht_als_Antwort()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Timeout, Crm)),
            "Dominic Brunner",
            Nummer);

        Assert.Equal("Dominic Brunner", zeilen.Line1);
        Assert.False(zeilen.HasContext);
    }

    // ---- Mit Kontext ----

    [Fact]
    public void Beide_Quellen_ergeben_alle_vier_Angaben()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success, Crm), ("memory", SourceState.Success, Memory)),
            "+41 44 123 45 67",
            Nummer);

        Assert.Equal("Hans Muster · Muster AG (Kunde)", zeilen.Line1);
        Assert.Equal("04.09. · Migration Telefonie · A. Beispiel", zeilen.Line2);
        Assert.Equal(
            "Zuletzt: Musterwerk mit Frau Beispiel besprechen, dieser ruft zurück.",
            zeilen.Line3);
        Assert.Equal("+41 44 123 45 67", zeilen.Attribution);
        Assert.True(zeilen.HasContext);
    }

    [Fact]
    public void Nur_das_Anrufgedaechtnis_liefert_Name_und_Gespraech()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("memory", SourceState.Success, Memory)),
            "+41 58 777 13 88",
            Nummer);

        Assert.Equal("Hans Muster", zeilen.Line1);
        Assert.Null(zeilen.Line2);
        Assert.StartsWith("Zuletzt: Musterwerk", zeilen.Line3, StringComparison.Ordinal);
    }

    [Fact]
    public void Nur_das_CRM_liefert_Name_Firma_und_Arbeit()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success, Crm)),
            "+41 44 123 45 67",
            Nummer);

        Assert.Equal("Hans Muster · Muster AG (Kunde)", zeilen.Line1);
        Assert.Equal("04.09. · Migration Telefonie · A. Beispiel", zeilen.Line2);
        Assert.Null(zeilen.Line3);
    }

    /// <summary>
    /// Ein magerer Treffer: Name, sonst nichts. Dann steht auch nichts —
    /// keine leeren Zeilen, keine Platzhalter (dieselbe Regel wie auf der
    /// Karte, T50).
    /// </summary>
    [Fact]
    public void Ein_magerer_Treffer_ergibt_keine_leeren_Zeilen()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success, [("contactName", "4net AG")])),
            "0713142250",
            "+41713142250");

        Assert.Equal("4net AG", zeilen.Line1);
        Assert.Null(zeilen.Line2);
        Assert.Null(zeilen.Line3);
    }

    /// <summary>
    /// Bei einer Firma als Kontakt liefern Name und Firma dasselbe. „4net AG ·
    /// 4net AG" wäre die Folge, wenn niemand vergleicht.
    /// </summary>
    [Fact]
    public void Gleicher_Name_und_Firma_stehen_nicht_zweimal()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success,
                [("contactName", "4net AG"), ("company", "4net AG")])),
            "0713142250",
            "+41713142250");

        Assert.Equal("4net AG", zeilen.Line1);
    }

    /// <summary>
    /// Fehlt die im Mapping fertig gebaute Zeile, wird der Kollege angehängt —
    /// „wer von uns zuletzt dran war" ist der Teil, um den es dabei geht.
    /// </summary>
    [Fact]
    public void Ohne_fertige_Arbeitszeile_kommt_der_Kollege_dazu()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success,
                [
                    ("contactName", "Hans Muster"),
                    ("letzteArbeit", "Telefonanlage umgestellt"),
                    ("letzteArbeitWer", "A. Beispiel"),
                ])),
            "0443954016",
            Nummer);

        Assert.Equal("Telefonanlage umgestellt · A. Beispiel", zeilen.Line2);
    }

    [Fact]
    public void Der_Kollege_wird_nicht_doppelt_angehaengt()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success, Crm)),
            "0443954016",
            Nummer);

        Assert.Equal("04.09. · Migration Telefonie · A. Beispiel", zeilen.Line2);
    }

    /// <summary>
    /// Das CRM kennt den vollen Namen, das lokale Adressbuch oft einen
    /// Spitznamen. Deshalb entscheidet die Reihenfolge der Feldnamen, nicht
    /// die der Quellen.
    /// </summary>
    [Fact]
    public void Der_Name_aus_dem_CRM_geht_dem_lokalen_vor()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(
                ("contacts", SourceState.Success, [("displayName", "Alex")]),
                ("crm", SourceState.Success, [("contactName", "Hans Muster")])),
            "0443954016",
            Nummer);

        Assert.Equal("Hans Muster", zeilen.Line1);
    }

    // ---- Kürzen ----

    [Fact]
    public void Eine_lange_Zusammenfassung_wird_an_der_Wortgrenze_gekuerzt()
    {
        var lang = string.Join(' ', Enumerable.Repeat("Wortgruppe", 30));

        var zeilen = ToastComposer.Compose(
            Snapshot(("memory", SourceState.Success,
                [("displayName", "Muster"), ("letzteZusammenfassung", lang)])),
            "0587771388",
            Nummer);

        Assert.NotNull(zeilen.Line3);
        Assert.True(zeilen.Line3!.Length <= ToastComposer.MaxLineLength + 1);
        Assert.EndsWith("…", zeilen.Line3, StringComparison.Ordinal);

        // Nicht mitten im Wort: genau die Kerbe, in die summary_short von
        // das Gesprächsjournal gefallen ist.
        Assert.EndsWith("Wortgruppe…", zeilen.Line3, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_einzelnes_langes_Wort_wird_hart_geschnitten()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("memory", SourceState.Success,
                [("displayName", "X"), ("letzteZusammenfassung", new string('a', 300))])),
            "0587771388",
            Nummer);

        Assert.NotNull(zeilen.Line3);
        Assert.True(zeilen.Line3!.Length <= ToastComposer.MaxLineLength + 1);
    }

    // ---- Die Nummer ----

    [Fact]
    public void Ohne_Kontext_steht_die_Nummer_nicht_zweimal()
    {
        var zeilen = ToastComposer.Compose(snapshot: null, "+41 44 123 45 67", Nummer);

        // Die Attributionszeile wird nur gesetzt, wenn Kontext dasteht — sonst
        // stünde die Nummer oben und unten.
        Assert.False(zeilen.HasContext);
    }

    [Fact]
    public void Ohne_Nummer_gibt_es_keine_Attributionszeile()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("crm", SourceState.Success, Crm)),
            "Hans Muster",
            number: null);

        Assert.Null(zeilen.Attribution);
    }
}
