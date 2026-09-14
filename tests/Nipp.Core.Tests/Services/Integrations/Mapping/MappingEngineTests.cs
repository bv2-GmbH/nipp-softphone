using System.Text.Json.Nodes;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Mapping;

namespace Nipp.Core.Tests.Services.Integrations.Mapping;

/// <summary>
/// Das Mapping fremder Antworten auf eigene Felder (§21.1, ADR-016).
///
/// <b>Die Beispiele sind die aus dem Auftrag</b> — CRM und ERP mit ihren
/// verschiedenen Strukturen, die auf denselben Namensraum abgebildet werden.
/// Wenn diese Tests grün sind, ist der Kern der Entkopplung belegt: die
/// Oberfläche kennt <c>customerName</c>, nicht <c>$.contact.fullName</c>.
///
/// Der zweite Schwerpunkt sind die Fehlerfälle. Fremde APIs liefern fehlende
/// Felder, falsche Typen und unerwartete Formen — und keiner dieser Fälle darf
/// mehr kosten als ein leeres Feld und eine Zeile in der Diagnose.
/// </summary>
public sealed class MappingEngineTests
{
    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    private static CompiledMapping Übersetzt(MappingDefinition definition)
    {
        Assert.True(
            MappingEngine.TryCompile(definition, out var mapping, out var fehler),
            $"Das Mapping liess sich nicht übersetzen: {string.Join(" | ", fehler)}");

        return mapping;
    }

    private static MappingDefinition Felder(params (string Name, FieldMapping Mapping)[] felder) =>
        new() { Fields = felder.ToDictionary(f => f.Name, f => f.Mapping, StringComparer.Ordinal) };

    // --- Die Beispiele aus dem Auftrag ---

    [Fact]
    public void Eine_Crm_Antwort_wird_auf_eigene_Feldnamen_abgebildet()
    {
        var antwort = Json("""
            {
              "contact": {
                "fullName": "Hans Muster",
                "company": { "name": "Muster AG" },
                "owner": { "name": "Peter Meier" },
                "id": "4711"
              }
            }
            """);

        var mapping = Übersetzt(Felder(
            ("customerName", new FieldMapping(Path: "$.contact.fullName")),
            ("company", new FieldMapping(Path: "$.contact.company.name")),
            ("accountManager", new FieldMapping(Path: "$.contact.owner.name")),
            ("customerId", new FieldMapping(Path: "$.contact.id"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal("Hans Muster", ergebnis.Fields["customerName"].AsText());
        Assert.Equal("Muster AG", ergebnis.Fields["company"].AsText());
        Assert.Equal("Peter Meier", ergebnis.Fields["accountManager"].AsText());
        Assert.Empty(ergebnis.Diagnostics);
    }

    [Fact]
    public void Eine_Erp_Antwort_wird_auf_denselben_Namensraum_abgebildet()
    {
        var antwort = Json("""
            {
              "debtor": { "number": "4711", "name": "Muster AG" },
              "orders": { "open": 3 },
              "statistics": { "revenue": 125000.50 }
            }
            """);

        var mapping = Übersetzt(Felder(
            ("customerNumber", new FieldMapping(Path: "$.debtor.number")),
            ("openOrders", new FieldMapping(Path: "$.orders.open")),
            ("revenue", new FieldMapping(Path: "$.statistics.revenue")),
            ("customerLabel", new FieldMapping(Expr: "concat(customerNumber, ' - ', $.debtor.name)"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal("4711", ergebnis.Fields["customerNumber"].AsText());
        Assert.Equal(ContextValue.FromNumber(3), ergebnis.Fields["openOrders"]);

        // Der Umsatz behält seine Nachkommastellen: decimal, nicht double.
        Assert.Equal(ContextValue.FromNumber(125000.50m), ergebnis.Fields["revenue"]);
        Assert.Equal("4711 - Muster AG", ergebnis.Fields["customerLabel"].AsText());
    }

    /// <summary>
    /// Berechnete Felder sehen die Pfad-Felder — unabhängig davon, in welcher
    /// Reihenfolge sie in der Datei stehen. Deshalb zwei Durchgänge.
    /// </summary>
    [Fact]
    public void Ein_berechnetes_Feld_sieht_die_gelesenen_Felder_auch_wenn_es_vor_ihnen_steht()
    {
        var antwort = Json("""{ "firstName": "Hans", "lastName": "Muster" }""");

        var mapping = Übersetzt(Felder(
            ("displayName", new FieldMapping(Expr: "concat(vorname, ' ', nachname)")),
            ("vorname", new FieldMapping(Path: "$.firstName")),
            ("nachname", new FieldMapping(Path: "$.lastName"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal("Hans Muster", ergebnis.Fields["displayName"].AsText());
    }

    // --- Typen ---

    /// <summary>
    /// Ohne Angabe gilt, was im JSON steht. Eine Rufnummer in
    /// Anführungszeichen bleibt Text — die Falle, die auch in der
    /// Ausdruckssprache geprüft wird.
    /// </summary>
    [Fact]
    public void Ohne_Angabe_bleibt_der_Typ_aus_dem_Json_erhalten()
    {
        var antwort = Json("""
            { "nummer": "+41791234567", "anzahl": 3, "aktiv": true, "nichts": null }
            """);

        var mapping = Übersetzt(Felder(
            ("nummer", new FieldMapping(Path: "$.nummer")),
            ("anzahl", new FieldMapping(Path: "$.anzahl")),
            ("aktiv", new FieldMapping(Path: "$.aktiv")),
            ("nichts", new FieldMapping(Path: "$.nichts"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.IsType<TextValue>(ergebnis.Fields["nummer"]);
        Assert.IsType<NumberValue>(ergebnis.Fields["anzahl"]);
        Assert.IsType<BooleanValue>(ergebnis.Fields["aktiv"]);
        Assert.Equal(ContextValue.Null, ergebnis.Fields["nichts"]);
    }

    /// <summary>
    /// Der ausdrückliche Weg für APIs, die Zahlen als Zeichenfolgen liefern.
    /// Die Absicht steht dann in der Konfiguration, nicht in einer Vermutung
    /// des Programms.
    /// </summary>
    [Fact]
    public void Mit_as_number_wird_eine_Zeichenfolge_zur_Zahl()
    {
        var antwort = Json("""{ "openOrders": "3" }""");

        var mapping = Übersetzt(Felder(
            ("openOrders", new FieldMapping(Path: "$.openOrders", As: ValueKind.Number))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal(ContextValue.FromNumber(3), ergebnis.Fields["openOrders"]);
    }

    [Fact]
    public void Ein_Datum_wird_nur_in_Iso_Form_angenommen()
    {
        var antwort = Json("""{ "iso": "2026-09-06T10:15:00Z", "mehrdeutig": "06.09.2026" }""");

        var mapping = Übersetzt(Felder(
            ("iso", new FieldMapping(Path: "$.iso", As: ValueKind.Date)),
            ("mehrdeutig", new FieldMapping(Path: "$.mehrdeutig", As: ValueKind.Date))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.IsType<DateValue>(ergebnis.Fields["iso"]);
        Assert.Equal(ContextValue.Null, ergebnis.Fields["mehrdeutig"]);
        Assert.Contains(ergebnis.Diagnostics, d => d.Contains("ISO", StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_falscher_Typ_kostet_das_Feld_und_nicht_die_Antwort()
    {
        var antwort = Json("""{ "anzahl": "keine Zahl", "name": "Muster AG" }""");

        var mapping = Übersetzt(Felder(
            ("anzahl", new FieldMapping(Path: "$.anzahl", As: ValueKind.Number)),
            ("name", new FieldMapping(Path: "$.name"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal(ContextValue.Null, ergebnis.Fields["anzahl"]);
        Assert.Equal("Muster AG", ergebnis.Fields["name"].AsText());
        Assert.Single(ergebnis.Diagnostics);
    }

    // --- Listen ---

    [Fact]
    public void Ein_Pfad_mit_Wildcard_ergibt_eine_Liste()
    {
        var antwort = Json("""{ "tags": ["VIP", "Neukunde"] }""");

        var mapping = Übersetzt(Felder(
            ("tags", new FieldMapping(Path: "$.tags[*]", As: ValueKind.List)),
            ("anzahl", new FieldMapping(Expr: "count(tags)")),
            ("beschriftung", new FieldMapping(Expr: "join(tags, ', ')"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal(ContextValue.FromNumber(2), ergebnis.Fields["anzahl"]);
        Assert.Equal("VIP, Neukunde", ergebnis.Fields["beschriftung"].AsText());
    }

    /// <summary>
    /// Ein Filterausdruck ist der Grund, warum JSONPath aus einer Bibliothek
    /// kommt und nicht selbst gebaut ist (ADR-016).
    /// </summary>
    [Fact]
    public void Ein_Filter_im_Pfad_funktioniert()
    {
        var antwort = Json("""
            {
              "phones": [
                { "type": "business", "number": "0445128430" },
                { "type": "mobile", "number": "0791234567" }
              ]
            }
            """);

        var mapping = Übersetzt(Felder(
            ("mobil", new FieldMapping(Path: "$.phones[?(@.type == 'mobile')].number"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal("0791234567", ergebnis.Fields["mobil"].AsText());
    }

    /// <summary>
    /// Ein Pfad, der auf ein Objekt zeigt, ergibt nichts. Ein JSON-Auszug auf
    /// einer Karte wäre kein Feld, sondern die Antwort im Rohzustand — genau
    /// das soll das Mapping verhindern (§21.1).
    /// </summary>
    [Fact]
    public void Ein_Pfad_auf_ein_Objekt_ergibt_nichts_und_erklaert_warum()
    {
        var antwort = Json("""{ "contact": { "name": "Hans" } }""");

        var mapping = Übersetzt(Felder(("kontakt", new FieldMapping(Path: "$.contact"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal(ContextValue.Null, ergebnis.Fields["kontakt"]);
        Assert.Contains(ergebnis.Diagnostics, d => d.Contains("Objekt", StringComparison.Ordinal));
    }

    [Fact]
    public void Mehrere_Treffer_auf_einem_Einzelfeld_nehmen_den_ersten_und_melden_es()
    {
        var antwort = Json("""{ "namen": ["Hans", "Peter"] }""");

        var mapping = Übersetzt(Felder(("name", new FieldMapping(Path: "$.namen[*]"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal("Hans", ergebnis.Fields["name"].AsText());
        Assert.Contains(ergebnis.Diagnostics, d => d.Contains("erste", StringComparison.Ordinal));
    }

    // --- Fehlende Daten ---

    [Fact]
    public void Ein_fehlender_Pfad_ist_kein_Fehler_sondern_ein_leeres_Feld()
    {
        var antwort = Json("""{ "contact": { "fullName": "Hans Muster" } }""");

        var mapping = Übersetzt(Felder(
            ("customerName", new FieldMapping(Path: "$.contact.fullName")),
            ("company", new FieldMapping(Path: "$.contact.company.name"))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        Assert.Equal("Hans Muster", ergebnis.Fields["customerName"].AsText());
        Assert.Equal(ContextValue.Null, ergebnis.Fields["company"]);
        Assert.Empty(ergebnis.Diagnostics);
    }

    [Fact]
    public void Eine_leere_Antwort_kostet_nichts()
    {
        var mapping = Übersetzt(Felder(("name", new FieldMapping(Path: "$.contact.fullName"))));

        var ergebnis = MappingEngine.Map(mapping, body: null);

        Assert.Equal(ContextValue.Null, ergebnis.Fields["name"]);
    }

    /// <summary>
    /// Eine technisch erfolgreiche Antwort ohne Inhalt soll als „nichts
    /// gefunden" gelten und nicht als Kundenkarte ohne Kunden.
    /// </summary>
    [Fact]
    public void EmptyWhen_erkennt_eine_Antwort_ohne_Inhalt()
    {
        var mapping = Übersetzt(new MappingDefinition
        {
            Fields = { ["name"] = new FieldMapping(Path: "$.contact.fullName") },
            EmptyWhen = "isEmpty($.contact)",
        });

        Assert.True(MappingEngine.Map(mapping, Json("""{ "contact": null }""")).IsEmpty);
        Assert.False(MappingEngine.Map(mapping, Json("""{ "contact": { "fullName": "H" } }""")).IsEmpty);
    }

    [Fact]
    public void EmptyWhen_sieht_auch_den_Http_Status()
    {
        var mapping = Übersetzt(new MappingDefinition
        {
            Fields = { ["name"] = new FieldMapping(Path: "$.name") },
            EmptyWhen = "status == 404",
        });

        var umgebung = new Dictionary<string, ContextValue>(StringComparer.Ordinal)
        {
            ["status"] = ContextValue.FromNumber(404),
        };

        Assert.True(MappingEngine.Map(mapping, Json("{}"), umgebung).IsEmpty);
    }

    [Fact]
    public void Die_Umgebung_liefert_die_Rufnummer_an_berechnete_Felder()
    {
        var mapping = Übersetzt(Felder(
            ("suchbegriff", new FieldMapping(Expr: "concat('Kunde zu ', number.e164)"))));

        var umgebung = new Dictionary<string, ContextValue>(StringComparer.Ordinal)
        {
            ["number.e164"] = ContextValue.FromText("+41791234567"),
        };

        var ergebnis = MappingEngine.Map(mapping, Json("{}"), umgebung);

        Assert.Equal("Kunde zu +41791234567", ergebnis.Fields["suchbegriff"].AsText());
    }

    // --- Trefferlisten für die Kontaktsuche ---

    [Fact]
    public void Eine_Trefferliste_wird_in_einzelne_Datensaetze_zerlegt()
    {
        var antwort = Json("""
            {
              "items": [
                { "id": "1", "firstName": "Hans", "lastName": "Muster" },
                { "id": "2", "firstName": "Anna", "lastName": "Beispiel" }
              ]
            }
            """);

        var mapping = Übersetzt(new MappingDefinition
        {
            ItemsPath = "$.items[*]",
            Fields =
            {
                ["externalId"] = new FieldMapping(Path: "$.id"),
                ["displayName"] = new FieldMapping(Expr: "concat($.firstName, ' ', $.lastName)"),
            },
        });

        var treffer = MappingEngine.MapItems(mapping, antwort, limit: 10);

        Assert.Equal(2, treffer.Count);
        Assert.Equal("Hans Muster", treffer[0].Fields["displayName"].AsText());
        Assert.Equal("2", treffer[1].Fields["externalId"].AsText());
    }

    [Fact]
    public void Die_Trefferliste_wird_auf_das_Limit_gekuerzt()
    {
        var antwort = Json("""{ "items": [{"id":"1"},{"id":"2"},{"id":"3"}] }""");

        var mapping = Übersetzt(new MappingDefinition
        {
            ItemsPath = "$.items[*]",
            Fields = { ["externalId"] = new FieldMapping(Path: "$.id") },
        });

        Assert.Equal(2, MappingEngine.MapItems(mapping, antwort, limit: 2).Count);
    }

    [Fact]
    public void Ohne_ItemsPath_ist_die_Antwort_ein_einziger_Datensatz()
    {
        var mapping = Übersetzt(Felder(("name", new FieldMapping(Path: "$.name"))));

        var treffer = MappingEngine.MapItems(mapping, Json("""{ "name": "Hans" }"""), limit: 10);

        Assert.Single(treffer);
        Assert.Equal("Hans", treffer[0].Fields["name"].AsText());
    }

    // --- Prüfung der Konfiguration ---

    [Fact]
    public void Ein_kaputter_Pfad_wird_beim_Uebersetzen_gemeldet()
    {
        var kaputt = Felder(("name", new FieldMapping(Path: "$.[[[")));

        Assert.False(MappingEngine.TryCompile(kaputt, out _, out var fehler));
        Assert.Contains(fehler, f => f.Contains("name", StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_kaputter_Ausdruck_wird_beim_Uebersetzen_gemeldet()
    {
        var kaputt = Felder(("name", new FieldMapping(Expr: "concat(")));

        Assert.False(MappingEngine.TryCompile(kaputt, out _, out var fehler));
        Assert.Single(fehler);
    }

    [Fact]
    public void Ein_Feld_braucht_genau_eine_Herkunft()
    {
        var beides = Felder(("name", new FieldMapping(Path: "$.a", Expr: "'b'")));
        Assert.False(MappingEngine.TryCompile(beides, out _, out var fehlerBeides));
        Assert.Contains(fehlerBeides, f => f.Contains("genau eines", StringComparison.Ordinal));

        var keines = Felder(("name", new FieldMapping()));
        Assert.False(MappingEngine.TryCompile(keines, out _, out _));
    }

    /// <summary>
    /// Ein Feldname, der sich in einem Ausdruck nicht schreiben lässt, wäre
    /// auf einer Karte nicht ansprechbar.
    /// </summary>
    [Theory]
    [InlineData("mit leerzeichen")]
    [InlineData("1zahlZuerst")]
    [InlineData("mit-strich")]
    public void Ein_unbrauchbarer_Feldname_wird_abgelehnt(string name)
    {
        var kaputt = Felder((name, new FieldMapping(Path: "$.a")));

        Assert.False(MappingEngine.TryCompile(kaputt, out _, out var fehler));
        Assert.Contains(fehler, f => f.Contains("Feldname", StringComparison.Ordinal));
    }

    [Fact]
    public void Alle_Fehler_werden_gesammelt_und_nicht_nur_der_erste()
    {
        var kaputt = Felder(
            ("a", new FieldMapping(Path: "$.[[[")),
            ("b", new FieldMapping(Expr: "concat(")),
            ("c", new FieldMapping()));

        Assert.False(MappingEngine.TryCompile(kaputt, out _, out var fehler));
        Assert.Equal(3, fehler.Count);
    }

    [Fact]
    public void Die_Feldnamen_lassen_sich_fuer_die_Verwaltung_abfragen()
    {
        var mapping = Übersetzt(Felder(
            ("customerName", new FieldMapping(Path: "$.a")),
            ("label", new FieldMapping(Expr: "customerName"))));

        Assert.Contains("customerName", mapping.FieldNames);
        Assert.Contains("label", mapping.FieldNames);
    }

    /// <summary>
    /// Ein Pfad, der auf eine riesige Antwort tausende Treffer liefert, wird
    /// gedeckelt: die Arbeit fiele auf dem Thread an, der alle 20 ms
    /// <c>Core.Iterate()</c> bedient (§6).
    /// </summary>
    [Fact]
    public void Sehr_viele_Treffer_werden_gedeckelt()
    {
        var eintraege = string.Join(",", Enumerable.Range(0, 500).Select(static i => $"\"{i}\""));
        var antwort = Json($$"""{ "alle": [{{eintraege}}] }""");

        var mapping = Übersetzt(Felder(
            ("alle", new FieldMapping(Path: "$.alle[*]", As: ValueKind.List))));

        var ergebnis = MappingEngine.Map(mapping, antwort);

        var liste = Assert.IsType<ListValue>(ergebnis.Fields["alle"]);
        Assert.Equal(JsonPathBinding.MaxMatches, liste.Items.Count);
        Assert.Contains(ergebnis.Diagnostics, d => d.Contains("ersten", StringComparison.Ordinal));
    }
}
