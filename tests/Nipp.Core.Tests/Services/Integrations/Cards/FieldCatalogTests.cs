using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Der Feldkatalog und die zwei quellenunabhängigen Ausdrucksfunktionen
/// (K1, ADR-032).
///
/// <para><b>Was hier auf dem Spiel steht.</b> Bis zum 07.09.2026 stand die
/// Frage „welcher Feldname bedeutet was" an drei Stellen: als fünf Arrays im
/// <c>ToastComposer</c>, als <c>coalesce</c>-Ketten mit bv2-Quellennamen in
/// <c>DefaultCards</c> — und als Verweis in <c>einrichten.md</c>, wo keine
/// Tabelle stand. Die Karten waren bei jedem anderen Kunden leer, und Toast
/// und Karte widersprachen sich bei <c>customerName</c>.</para>
///
/// <para><b>Der erste Test dieser Datei ist die Regression:</b> keiner der
/// alten Feldnamen darf beim Umzug in den Katalog verlorengegangen sein, und
/// die Reihenfolge muss dieselbe sein. Ein verlorener Name wäre ein Toast, der
/// bei einer Quelle plötzlich schweigt.</para>
/// </summary>
public sealed class FieldCatalogTests
{
    /// <summary>
    /// Die fünf Feldlisten, <b>wörtlich</b> wie sie bis zum 07.09.2026 im
    /// <c>ToastComposer</c> standen. Absichtlich als Literale hier und nicht
    /// aus dem Katalog abgeleitet — ein Test, der seine Erwartung aus dem
    /// Prüfling holt, prüft nichts.
    /// </summary>
    public static TheoryData<FieldRole, string[]> AlteListen => new()
    {
        { FieldRole.Name, ["contactName", "displayName", "name", "fullName"] },
        { FieldRole.Company, ["company", "customerName", "primaryCustomer", "organization"] },
        { FieldRole.Type, ["contactType", "category", "kind"] },
        { FieldRole.Work, ["letzteArbeitZeile", "letzteArbeit", "lastWork", "lastActivity"] },
        { FieldRole.Summary, ["letzteZusammenfassung", "lastCallSummary", "summary"] },
        { FieldRole.Colleague, ["letzteArbeitWer", "accountManager", "technician", "owner"] },
    };

    [Theory]
    [MemberData(nameof(AlteListen))]
    public void Die_alten_Feldlisten_stehen_unveraendert_im_Katalog(FieldRole rolle, string[] erwartet)
    {
        var namen = FieldCatalog.NamesFor(rolle);

        // Nicht bloss dieselben Namen, sondern dieselbe Reihenfolge: sie
        // entscheidet, welches Feld gewinnt, wenn eine Quelle mehrere liefert.
        Assert.Equal(erwartet, namen);
    }

    [Fact]
    public void Jeder_Rollenname_im_Ausdruck_liefert_Felder()
    {
        foreach (var name in FieldCatalog.RoleNames)
        {
            var rolle = FieldCatalog.RoleFromExpression(name);

            Assert.NotNull(rolle);
            Assert.NotEmpty(FieldCatalog.NamesFor(rolle!.Value));
        }
    }

    [Fact]
    public void Eine_unbekannte_Rolle_gibt_es_nicht()
    {
        Assert.Null(FieldCatalog.RoleFromExpression("kundennummer"));
        Assert.Null(FieldCatalog.RoleFromExpression("Name"));
        Assert.Null(FieldCatalog.RoleFromExpression(null));
    }

    [Fact]
    public void Die_Rolle_None_hat_keine_Felder()
    {
        Assert.Empty(FieldCatalog.NamesFor(FieldRole.None));
    }

    // --- Beschriftungen ---

    [Fact]
    public void Ein_katalogisiertes_Feld_bekommt_seine_Beschriftung()
    {
        Assert.Equal("Name", FieldCatalog.LabelFor("contactName"));
        Assert.Equal("Offene Aufträge", FieldCatalog.LabelFor("openOrders"));
        Assert.Equal("Letzte Arbeit, ganze Zeile", FieldCatalog.LabelFor("letzteArbeitZeile"));
    }

    /// <summary>
    /// Für ein eigenes Feld wird nicht geraten, sondern getrennt — und das
    /// Ergebnis soll erkennbar maschinell aussehen. Genau solche
    /// Beschriftungen standen am 07.09.2026 auf der Karte, weil keine Zeile
    /// traf; sie waren das Zeichen, dass etwas nicht stimmte.
    /// </summary>
    [Fact]
    public void Ein_eigenes_Feld_bekommt_eine_gebaute_Beschriftung()
    {
        Assert.Equal("Eigenes feld", FieldCatalog.LabelFor("eigenesFeld"));
        Assert.Equal("Sla stufe", FieldCatalog.LabelFor("slaStufe"));
        Assert.Equal(string.Empty, FieldCatalog.LabelFor(""));
    }

    [Fact]
    public void Kein_Feldname_kommt_zweimal_vor()
    {
        var doppelte = FieldCatalog.Entries
            .GroupBy(static e => e.Name, StringComparer.Ordinal)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToList();

        Assert.Empty(doppelte);
    }

    /// <summary>
    /// Die eingebauten Felder müssen es geben, auch ohne eine einzige Quelle
    /// — sonst hätte die Palette auf einem frischen Gerät nichts anzubieten
    /// und die Rückfallebene „sonst die Nummer" liesse sich nicht bauen.
    /// </summary>
    [Fact]
    public void Die_eingebauten_Felder_deckten_Nummer_und_eigene_Kontakte_ab()
    {
        var pfade = FieldCatalog.BuiltIn.Select(static e => e.Path).ToList();

        Assert.Contains("number.e164", pfade);
        Assert.Contains("number.national", pfade);
        Assert.Contains("contacts.displayName", pfade);
        Assert.All(FieldCatalog.BuiltIn, e => Assert.NotEmpty(e.Label));
        Assert.All(FieldCatalog.BuiltIn, e => Assert.NotEmpty(e.Group));
    }

    /// <summary>
    /// <b>Der Verweis, der ins Leere zeigte.</b> Der <c>ToastComposer</c>
    /// verwies im Kommentar auf <c>docs/integrations/einrichten.md</c> für die
    /// Feldnamen — dort stand keine Tabelle. Jetzt gibt es
    /// <c>feldnamen.md</c>, und dieser Test hält sie am Katalog: eine
    /// Anleitung, die einen Feldnamen nicht nennt, ist schlimmer als keine,
    /// weil man ihr glaubt.
    /// </summary>
    [Fact]
    public void Die_Anleitung_nennt_jeden_Feldnamen_des_Katalogs()
    {
        var text = File.ReadAllText(Anleitung);

        foreach (var eintrag in FieldCatalog.Entries)
        {
            Assert.Contains($"`{eintrag.Name}`", text, StringComparison.Ordinal);
        }

        foreach (var eintrag in FieldCatalog.BuiltIn)
        {
            Assert.Contains($"`{eintrag.Path}`", text, StringComparison.Ordinal);
        }

        foreach (var rolle in FieldCatalog.RoleNames)
        {
            Assert.Contains($"role('{rolle}')", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Und die Gegenrichtung: die Anleitung erfindet keinen Feldnamen, den es
    /// nicht gibt. Ein erfundener kostet eine Stunde Fehlersuche im Mapping.
    /// </summary>
    [Fact]
    public void Die_Anleitung_erfindet_keinen_Feldnamen()
    {
        var text = File.ReadAllText(Anleitung);

        // Nur der Abschnitt „Der Katalog", nicht der Rest der Datei: die
        // Tabelle der eingebauten Felder darunter führt Pfade wie
        // `number.e164`, und die stehen bewusst nicht in `Entries`.
        var anfang = text.IndexOf("## Der Katalog", StringComparison.Ordinal);
        var ende = text.IndexOf("## Immer vorhanden", StringComparison.Ordinal);

        Assert.True(anfang >= 0 && ende > anfang, "Die Abschnitte der Anleitung sind umbenannt.");

        var tabelle = text[anfang..ende];

        var bekannt = FieldCatalog.Entries.Select(static e => e.Name).ToHashSet(StringComparer.Ordinal);

        var zeilen = tabelle.Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var zeile in zeilen.Where(static z => z.StartsWith("| `", StringComparison.Ordinal)))
        {
            var name = zeile[3..zeile.IndexOf('`', 3)];

            Assert.Contains(name, bekannt);
        }
    }

    /// <summary>
    /// Der Ordner mit der Anleitung, vom Testverzeichnis aus gefunden —
    /// dasselbe Vorgehen wie in <c>SampleConfigurationTests</c>: geprüft wird,
    /// was im Repo liegt, nicht eine Kopie im Ausgabeverzeichnis.
    /// </summary>
    private static string Anleitung
    {
        get
        {
            var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

            while (verzeichnis is not null)
            {
                var kandidat = Path.Combine(
                    verzeichnis.FullName, "docs", "integrations", "feldnamen.md");

                if (File.Exists(kandidat))
                {
                    return kandidat;
                }

                verzeichnis = verzeichnis.Parent;
            }

            throw new InvalidOperationException(
                "docs/integrations/feldnamen.md nicht gefunden.");
        }
    }

    // --- role() und anyOf() ---

    private static ContextSnapshot Kontext(
        params (string Quelle, int Prioritaet, (string Feld, string Wert)[] Felder)[] quellen)
    {
        var sources = new Dictionary<string, ContextFragment>(StringComparer.Ordinal);

        foreach (var (quelle, prioritaet, felder) in quellen)
        {
            sources[quelle] = new ContextFragment(
                quelle,
                quelle.ToUpperInvariant(),
                SourceState.Success,
                felder.ToDictionary(
                    f => f.Feld,
                    f => ContextValue.FromText(f.Wert),
                    StringComparer.Ordinal),
                Priority: prioritaet);
        }

        return new ContextSnapshot(
            CallHandle.New(),
            PhoneNumberKey.From("0791234567", new NumberNormalizer("+41")),
            sources);
    }

    /// <summary>Wertet einen Ausdruck über eine Karte aus — der Weg, den die Oberfläche nimmt.</summary>
    private static string Auswerten(string ausdruck, ContextSnapshot kontext)
    {
        var karte = new CardDefinition(
            "test",
            "Test",
            CardKind.ActiveExpanded,
            [
                new CardSection("a", null, [
                    new CardRow([new CardColumn(6, [new CardText(ausdruck)])]),
                ]),
            ]);

        Assert.True(CardLayoutEngine.TryCompile(karte, out var uebersetzt, out var fehler),
            string.Join(" | ", fehler));

        var modell = CardLayoutEngine.Build(uebersetzt, kontext);

        return ((CardTextModel)modell.Sections[0].Rows[0].Columns[0].Elements[0]).Text;
    }

    [Fact]
    public void Role_findet_das_Feld_egal_wie_die_Quelle_heisst()
    {
        Assert.Equal(
            "Hans Muster",
            Auswerten("role('name')", Kontext(("wie-auch-immer", 10, [("contactName", "Hans Muster")]))));
    }

    /// <summary>
    /// <b>Die Regel, auf der alles steht:</b> kleinere Priorität gewinnt. Ohne
    /// sie entschiede die Reihenfolge eines Dictionary, und die Karte zeigte
    /// bei zwei Quellen manchmal den einen und manchmal den anderen Namen.
    /// </summary>
    [Fact]
    public void Bei_zwei_Quellen_gewinnt_die_kleinere_Prioritaet()
    {
        var kontext = Kontext(
            ("zweite", 50, [("contactName", "Aus der zweiten")]),
            ("erste", 10, [("contactName", "Aus der ersten")]));

        Assert.Equal("Aus der ersten", Auswerten("role('name')", kontext));
    }

    /// <summary>
    /// <b>Feld vor Quelle</b>, wie im <c>ToastComposer</c>: der genaue Treffer
    /// der zweiten Quelle schlägt den unscharfen der ersten. Sonst gewönne
    /// <c>name</c> aus einer beliebigen Quelle über <c>contactName</c> aus dem
    /// Fachsystem.
    /// </summary>
    [Fact]
    public void Der_genauere_Feldname_gewinnt_ueber_die_naeher_stehende_Quelle()
    {
        var kontext = Kontext(
            ("erste", 10, [("name", "Unscharf")]),
            ("zweite", 50, [("contactName", "Genau")]));

        Assert.Equal("Genau", Auswerten("role('name')", kontext));
    }

    [Fact]
    public void Ein_leeres_Feld_wird_uebersprungen()
    {
        var kontext = Kontext(
            ("erste", 10, [("contactName", "")]),
            ("zweite", 50, [("contactName", "Da steht was")]));

        Assert.Equal("Da steht was", Auswerten("role('name')", kontext));
    }

    [Fact]
    public void Role_ohne_Treffer_ist_leer_und_kein_Fehler()
    {
        Assert.Equal(
            string.Empty,
            Auswerten("role('summary')", Kontext(("crm", 10, [("contactName", "Hans")]))));
    }

    [Fact]
    public void AnyOf_nimmt_den_ersten_Feldnamen_der_etwas_liefert()
    {
        var kontext = Kontext(("crm", 10, [("slaStufe", "Gold")]));

        Assert.Equal("Gold", Auswerten("anyOf('vertragStufe', 'slaStufe')", kontext));
        Assert.Equal(string.Empty, Auswerten("anyOf('gibtEsNicht')", kontext));
    }

    /// <summary>
    /// Eine unbekannte Rolle ist ein Befund beim Auswerten, kein Absturz —
    /// dieselbe Haltung wie überall in der Ausdruckssprache. Die Karte zeigt
    /// dann eine leere Zeile, nicht eine falsche.
    /// </summary>
    [Fact]
    public void Eine_unbekannte_Rolle_ergibt_einen_leeren_Wert()
    {
        Assert.Equal(
            string.Empty,
            Auswerten("role('kundennummer')", Kontext(("crm", 10, [("contactName", "Hans")]))));
    }

    /// <summary>
    /// Ausserhalb einer Karte gibt es keine mehreren Quellen — ein Mapping
    /// läuft gegen die Antwort einer einzigen. <c>role()</c> ist dort leer, und
    /// das ist die Wahrheit und keine Notlösung.
    /// </summary>
    [Fact]
    public void In_einem_Mapping_ist_role_leer()
    {
        var mapping = new Nipp.Core.Services.Integrations.Mapping.MappingDefinition
        {
            Fields =
            {
                ["direkt"] = new(Path: "$.name"),
                ["ueberRolle"] = new(Expr: "role('name')"),
            },
        };

        Assert.True(Nipp.Core.Services.Integrations.Mapping.MappingEngine.TryCompile(
            mapping, out var uebersetzt, out _));

        var ergebnis = Nipp.Core.Services.Integrations.Mapping.MappingEngine.Map(
            uebersetzt,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "name": "Hans Muster" }"""));

        Assert.Equal("Hans Muster", ergebnis.Fields["direkt"].AsText());
        Assert.True(ergebnis.Fields["ueberRolle"].IsEmpty);
    }

    // --- Die mitgelieferten Karten ---

    /// <summary>
    /// <b>Der Befund, den K1 behebt.</b> Die mitgelieferten Karten nannten
    /// Quellen mit Namen — <c>crm</c>, <c>memory</c>, <c>crm</c>,
    /// <c>erp</c>. Bei einem Kunden mit anderen Kennungen traf keine einzige
    /// Zeile, und die Karte blieb leer.
    /// </summary>
    [Fact]
    public void Die_mitgelieferten_Karten_nennen_keine_Quelle_mit_Namen()
    {
        var verboten = new[] { "crm.", "memory.", "crm.", "erp." };

        foreach (var karte in DefaultCards.All)
        {
            var ausdruecke = karte.Sections
                .SelectMany(static s => s.Rows)
                .SelectMany(static r => r.Columns)
                .SelectMany(static c => c.Elements)
                .SelectMany(static e => new[]
                {
                    e.VisibleWhen,
                    e is CardText t ? t.Value : null,
                    e is CardField f ? f.Value : null,
                    e is CardBadge b ? b.Text : null,
                })
                .Where(static a => !string.IsNullOrEmpty(a));

            foreach (var ausdruck in ausdruecke)
            {
                foreach (var praefix in verboten)
                {
                    Assert.DoesNotContain(praefix, ausdruck!, StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>
    /// Und die Gegenprobe: mit einer Quelle, die beliebig heisst, steht auf
    /// der mitgelieferten Karte trotzdem alles da.
    /// </summary>
    [Fact]
    public void Die_Gespraechskarte_traegt_eine_beliebig_benannte_Quelle()
    {
        var kontext = Kontext(("dings", 10,
        [
            ("contactName", "Hans Muster"),
            ("company", "Muster AG"),
            ("contactType", "Kunde"),
            ("letzteArbeitZeile", "04.09. · Migration"),
            ("letzteZusammenfassung", "Ging um die Anlage"),
        ]));

        Assert.True(CardLayoutEngine.TryCompile(DefaultCards.ActiveCall, out var karte, out _));

        var modell = CardLayoutEngine.Build(karte, kontext);

        var alle = modell.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .Where(static e => e.Visible)
            .ToList();

        Assert.Contains(alle.OfType<CardTextModel>(), t => t.Text == "Hans Muster");
        Assert.Contains(alle.OfType<CardTextModel>(), t => t.Text == "Muster AG");
        Assert.Contains(alle.OfType<CardFieldModel>(), f => f.Value == "Kunde");
        Assert.Contains(alle.OfType<CardFieldModel>(), f => f.Value == "04.09. · Migration");
        Assert.Contains(alle.OfType<CardFieldModel>(), f => f.Value == "Ging um die Anlage");
    }

    /// <summary>
    /// Der Toast und die Karte müssen bei derselben Antwort denselben Namen
    /// zeigen. Am Gerät stand schon einmal im Toast „Dominic Brunner" und in
    /// der Gesprächsansicht zur selben Sekunde „151".
    /// </summary>
    [Fact]
    public void Toast_und_Karte_zeigen_denselben_Namen()
    {
        var kontext = Kontext(("dings", 10, [("contactName", "Hans Muster")]));

        var zeilen = ToastComposer.Compose(kontext, "Rückfall", "+41791234567");

        Assert.StartsWith("Hans Muster", zeilen.Line1, StringComparison.Ordinal);
        Assert.Equal("Hans Muster", Auswerten("role('name')", kontext));
    }
}
