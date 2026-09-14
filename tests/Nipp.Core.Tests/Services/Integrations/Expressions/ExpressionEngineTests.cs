using System.Globalization;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Expressions;

namespace Nipp.Core.Tests.Services.Integrations.Expressions;

/// <summary>
/// Die eingeschränkte Ausdruckssprache (ADR-016, §21.2).
///
/// <b>Warum das so ausführlich geprüft wird.</b> Diese Sprache ist die eine
/// Stelle, an der eine Konfigurationsdatei — die über das Netz verteilt werden
/// kann — Verhalten im Programm auslöst. Zwei Dinge müssen darum stimmen: sie
/// darf nicht mehr können als vorgesehen, und sie darf unter keinen Umständen
/// werfen. Eine Ausnahme von hier stiege durch die Kartenlogik in einen
/// SDK-Ereignishandler und nähme die Anwendung mit.
/// </summary>
public sealed class ExpressionEngineTests
{
    /// <summary>Ein Kontext aus festen Werten — mehr braucht die Sprache nicht.</summary>
    private sealed class Kontext : IExpressionScope
    {
        private readonly Dictionary<string, ContextValue> _felder = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ContextValue> _pfade = new(StringComparer.Ordinal);

        public Kontext Mit(string pfad, ContextValue wert)
        {
            _felder[pfad] = wert;
            return this;
        }

        public Kontext MitPfad(string pfad, ContextValue wert)
        {
            _pfade[pfad] = wert;
            return this;
        }

        /// <summary>Zählt, wie oft aufgelöst wurde — für den Kurzschluss-Test.</summary>
        public int Zugriffe { get; private set; }

        public ContextValue Resolve(string path)
        {
            Zugriffe++;
            return _felder.TryGetValue(path, out var wert) ? wert : ContextValue.Null;
        }

        public ContextValue ResolveJsonPath(string path)
        {
            Zugriffe++;
            return _pfade.TryGetValue(path, out var wert) ? wert : ContextValue.Null;
        }
    }

    private static ContextValue Werte(string ausdruck, IExpressionScope kontext) =>
        ExpressionEvaluator.Evaluate(ExpressionParser.Parse(ausdruck), kontext);

    private static string Text(string ausdruck, IExpressionScope kontext) =>
        Werte(ausdruck, kontext).AsText();

    // --- Literale und Verweise ---

    [Theory]
    [InlineData("'Hans'", "Hans")]
    [InlineData("'Hans''s Firma'", "Hans's Firma")]
    [InlineData("42", "42")]
    [InlineData("1.5", "1.5")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "")]
    [InlineData("''", "")]
    public void Literale_werden_gelesen(string ausdruck, string erwartet) =>
        Assert.Equal(erwartet, Text(ausdruck, new Kontext()));

    [Fact]
    public void Ein_Feld_wird_ueber_seinen_Pfad_aufgeloest()
    {
        var kontext = new Kontext().Mit("crm.customerName", ContextValue.FromText("Hans Muster"));

        Assert.Equal("Hans Muster", Text("crm.customerName", kontext));
    }

    [Fact]
    public void Ein_unbekanntes_Feld_ist_leer_und_kein_Fehler() =>
        Assert.Equal(string.Empty, Text("crm.gibtEsNicht", new Kontext()));

    [Fact]
    public void Ein_einfacher_Jsonpfad_wird_aufgeloest()
    {
        var kontext = new Kontext().MitPfad("$.contact.name", ContextValue.FromText("Muster AG"));

        Assert.Equal("Muster AG", Text("$.contact.name", kontext));
    }

    // --- Rangfolge ---

    [Theory]
    [InlineData("1 + 2 == 3", true)]
    [InlineData("2 > 1 && 3 > 2", true)]
    [InlineData("2 > 1 || 1 > 2", true)]
    [InlineData("1 > 2 || 2 > 3", false)]
    [InlineData("(1 > 2 || true) && true", true)]
    [InlineData("!(1 > 2)", true)]
    [InlineData("10 - 3 - 2 == 5", true)]
    public void Die_Rangfolge_der_Operatoren_stimmt(string ausdruck, bool erwartet) =>
        Assert.Equal(erwartet, ExpressionEvaluator.IsTrue(Werte(ausdruck, new Kontext())));

    // --- Gleichheit ohne stillschweigende Umwandlung ---

    /// <summary>
    /// Der Punkt aus ADR-016: gemischte Typen sind nicht gleich. Eine
    /// stillschweigende Umwandlung ist die häufigste Ursache dafür, dass eine
    /// Karte beim Kunden anders aussieht als im Test.
    /// </summary>
    [Fact]
    public void Text_und_Zahl_sind_nie_gleich()
    {
        var kontext = new Kontext()
            .Mit("a", ContextValue.FromText("3"))
            .Mit("b", ContextValue.FromNumber(3));

        Assert.False(ExpressionEvaluator.IsTrue(Werte("a == b", kontext)));
        Assert.True(ExpressionEvaluator.IsTrue(Werte("a != b", kontext)));
    }

    /// <summary>
    /// Die eine bewusste Ausnahme: Text vergleicht sich ohne Rücksicht auf
    /// Gross- und Kleinschreibung. Fremde Systeme liefern „Active" und
    /// „active" für dasselbe.
    /// </summary>
    [Fact]
    public void Text_vergleicht_sich_ohne_Ruecksicht_auf_Gross_und_Klein()
    {
        var kontext = new Kontext().Mit("status", ContextValue.FromText("Active"));

        Assert.True(ExpressionEvaluator.IsTrue(Werte("status == 'active'", kontext)));
    }

    [Fact]
    public void Fehlende_Werte_vergleichen_sich_mit_null()
    {
        var kontext = new Kontext().Mit("da", ContextValue.FromText("x"));

        Assert.True(ExpressionEvaluator.IsTrue(Werte("fehlt == null", kontext)));
        Assert.True(ExpressionEvaluator.IsTrue(Werte("da != null", kontext)));
        Assert.False(ExpressionEvaluator.IsTrue(Werte("fehlt == 'x'", kontext)));
    }

    // --- Unbekanntes bleibt unbekannt ---

    /// <summary>
    /// Ein Vergleich, der sich nicht entscheiden lässt, ist weder wahr noch
    /// falsch. Ein Feld mit einer solchen Bedingung wird <b>nicht</b>
    /// angezeigt — die Gegenrichtung wäre eine Karte, die bei fehlenden Daten
    /// etwas behauptet.
    /// </summary>
    [Fact]
    public void Ein_unentscheidbarer_Vergleich_ist_weder_wahr_noch_falsch()
    {
        var kontext = new Kontext().Mit("text", ContextValue.FromText("Hans"));

        var ergebnis = Werte("text > 5", kontext);

        Assert.Equal(ContextValue.Null, ergebnis);
        Assert.False(ExpressionEvaluator.IsTrue(ergebnis));

        // Und die Verneinung macht daraus kein „wahr".
        Assert.False(ExpressionEvaluator.IsTrue(Werte("!(text > 5)", kontext)));
    }

    [Fact]
    public void Eine_Verknuepfung_kuerzt_ab_und_wertet_die_andere_Seite_nicht_aus()
    {
        var kontext = new Kontext().Mit("teuer", ContextValue.FromText("x"));

        Assert.False(ExpressionEvaluator.IsTrue(Werte("false && teuer == 'x'", kontext)));
        Assert.Equal(0, kontext.Zugriffe);

        Assert.True(ExpressionEvaluator.IsTrue(Werte("true || teuer == 'x'", kontext)));
        Assert.Equal(0, kontext.Zugriffe);
    }

    /// <summary>
    /// <b>Der Befund, der diesen Test hervorgebracht hat.</b> Der erste
    /// Entwurf las Text mit <c>decimal.TryParse</c> — und <c>+41791234567</c>
    /// ist für <c>TryParse</c> eine gültige Zahl mit Vorzeichen. Eine
    /// Rufnummer wurde damit zu 41'791'234'567: <c>nummer + 1</c> ergab ein
    /// Ergebnis, <c>nummer &gt; 5</c> war wahr, und <c>formatNumber</c> hätte
    /// aus der Nummer eine gruppierte Zahl gemacht.
    ///
    /// Auf einer Karte, die Rufnummern anzeigt, ist das die schlimmste Art von
    /// Fehler: das Ergebnis sieht plausibel aus.
    /// </summary>
    [Fact]
    public void Eine_Rufnummer_ist_keine_Zahl()
    {
        var kontext = new Kontext().Mit("nummer", ContextValue.FromText("+41791234567"));

        Assert.Equal(ContextValue.Null, Werte("nummer + 1", kontext));
        Assert.False(ExpressionEvaluator.IsTrue(Werte("nummer > 5", kontext)));
        Assert.Equal(string.Empty, Text("formatNumber(nummer, 'N0')", kontext));

        // Und der Weg, den es stattdessen gibt: im Mapping "as: number".
        var alsZahl = new Kontext().Mit("anzahl", ContextValue.FromNumber(3));
        Assert.Equal("4", Text("anzahl + 1", alsZahl));
    }

    [Fact]
    public void Plus_rechnet_und_verkettet_nicht()
    {
        var kontext = new Kontext()
            .Mit("a", ContextValue.FromText("Hans"))
            .Mit("b", ContextValue.FromText("Muster"));

        Assert.Equal(ContextValue.Null, Werte("a + b", kontext));
        Assert.Equal("7", Text("3 + 4", kontext));
    }

    // --- Funktionen ---

    [Fact]
    public void Concat_ueberspringt_fehlende_Glieder()
    {
        var kontext = new Kontext().Mit("nachname", ContextValue.FromText("Muster"));

        Assert.Equal("Muster", Text("concat(vorname, nachname)", kontext));
        Assert.Equal("Herr Muster", Text("concat('Herr ', nachname)", kontext));
    }

    [Fact]
    public void Coalesce_nimmt_den_ersten_gefuellten_Wert()
    {
        var kontext = new Kontext()
            .Mit("zweite", ContextValue.FromText("aus dem CRM"))
            .Mit("dritte", ContextValue.FromText("aus Outlook"));

        Assert.Equal("aus dem CRM", Text("coalesce(erste, zweite, dritte)", kontext));
    }

    /// <summary>
    /// <c>coalesce</c> darf die späteren Angaben nicht auswerten — auf einer
    /// Karte steht dort oft eine Formatierung, die nicht laufen soll, wenn der
    /// erste Wert schon da ist.
    /// </summary>
    [Fact]
    public void Coalesce_wertet_nur_bis_zum_ersten_Treffer_aus()
    {
        var kontext = new Kontext().Mit("erste", ContextValue.FromText("da"));

        Assert.Equal("da", Text("coalesce(erste, zweite, dritte)", kontext));
        Assert.Equal(1, kontext.Zugriffe);
    }

    [Fact]
    public void If_wertet_nur_den_gewaehlten_Zweig_aus()
    {
        var kontext = new Kontext().Mit("ja", ContextValue.FromText("ja"));

        Assert.Equal("ja", Text("if(true, ja, nein)", kontext));
        Assert.Equal(1, kontext.Zugriffe);
    }

    [Theory]
    [InlineData("isEmpty(fehlt)", "true")]
    [InlineData("isEmpty('x')", "false")]
    [InlineData("isEmpty('')", "true")]
    [InlineData("upper('hans')", "HANS")]
    [InlineData("lower('HANS')", "hans")]
    [InlineData("trim('  hans  ')", "hans")]
    [InlineData("substring('4711-01', 0, 4)", "4711")]
    [InlineData("substring('4711', 0, 99)", "4711")]
    [InlineData("substring('4711', 99)", "")]
    [InlineData("contains('Muster AG', 'muster')", "true")]
    [InlineData("startsWith('Muster AG', 'Mu')", "true")]
    [InlineData("count(fehlt)", "0")]
    [InlineData("count('einer')", "1")]
    public void Die_erlaubten_Funktionen_tun_was_sie_sollen(string ausdruck, string erwartet) =>
        Assert.Equal(erwartet, Text(ausdruck, new Kontext()));

    /// <summary>
    /// Eine Zahl ist nie leer — auch nicht die Null. „Null offene Aufträge"
    /// ist eine Antwort, kein fehlender Wert, und die Karte soll sie zeigen.
    /// </summary>
    [Fact]
    public void Die_Zahl_null_gilt_nicht_als_leer()
    {
        var kontext = new Kontext().Mit("offen", ContextValue.FromNumber(0));

        Assert.False(ExpressionEvaluator.IsTrue(Werte("isEmpty(offen)", kontext)));
        Assert.Equal("0", Text("offen", kontext));
    }

    [Fact]
    public void Listen_lassen_sich_zaehlen_und_verbinden()
    {
        var kontext = new Kontext().Mit(
            "tags",
            ContextValue.FromList([ContextValue.FromText("VIP"), ContextValue.FromText("Neu")]));

        Assert.Equal("2", Text("count(tags)", kontext));
        Assert.Equal("VIP / Neu", Text("join(tags, ' / ')", kontext));
    }

    [Fact]
    public void FormatPhone_benutzt_dieselbe_Darstellung_wie_das_uebrige_Programm()
    {
        var kontext = new Kontext().Mit("nummer", ContextValue.FromText("+41791234567"));

        Assert.Equal("+41 79 123 45 67", Text("formatPhone(nummer)", kontext));
    }

    [Fact]
    public void FormatDate_liest_Iso_und_formatiert()
    {
        var kontext = new Kontext().Mit("wann", ContextValue.FromText("2026-09-06T10:15:00Z"));

        Assert.Equal("2026-09-06", Text("formatDate(wann, 'yyyy-MM-dd')", kontext));
    }

    /// <summary>
    /// Eine Angabe wie <c>06.09.2026</c> könnte auch der 9. Juni sein. Wo das
    /// Datum falsch stehen könnte, bleibt es lieber leer.
    /// </summary>
    [Fact]
    public void FormatDate_raet_nicht_bei_mehrdeutigen_Angaben()
    {
        var kontext = new Kontext().Mit("wann", ContextValue.FromText("06.09.2026"));

        Assert.Equal(string.Empty, Text("formatDate(wann, 'yyyy-MM-dd')", kontext));
    }

    [Fact]
    public void FormatNumber_formatiert_in_der_Kultur_des_Benutzers()
    {
        var kontext = new Kontext().Mit("umsatz", ContextValue.FromNumber(125000));

        var erwartet = 125000m.ToString("N0", CultureInfo.CurrentCulture);

        Assert.Equal(erwartet, Text("formatNumber(umsatz, 'N0')", kontext));
    }

    /// <summary>
    /// Der Rohtext eines Werts ist dagegen <b>immer</b> invariant: er geht
    /// auch in URLs und Anfrageparameter, und ein Dezimalkomma wäre dort eine
    /// andere Zahl.
    /// </summary>
    [Fact]
    public void Der_Rohtext_einer_Zahl_ist_kulturunabhaengig() =>
        Assert.Equal("1234.5", ContextValue.FromNumber(1234.5m).AsText());

    // --- Was die Sprache nicht kann ---

    [Theory]
    [InlineData("System.IO.File.Delete('x')")]
    [InlineData("GetType()")]
    [InlineData("eval('1')")]
    [InlineData("exec('cmd')")]
    public void Unbekannte_Funktionen_werden_beim_Uebersetzen_abgelehnt(string ausdruck)
    {
        var ex = Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse(ausdruck));

        Assert.Contains("gibt es nicht", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Zuweisung_gibt_es_nicht()
    {
        var ex = Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse("a = 1"));

        Assert.Contains("==", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("substring('x')", "mindestens")]
    [InlineData("isEmpty('a', 'b')", "höchstens")]
    public void Die_Anzahl_der_Angaben_wird_beim_Uebersetzen_geprueft(string ausdruck, string erwartet)
    {
        var ex = Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse(ausdruck));

        Assert.Contains(erwartet, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("concat('a'", "schliessende Klammer")]
    [InlineData("'unfertig", "nicht geschlossen")]
    [InlineData("1 +", "bricht ab")]
    [InlineData("crm.", "fehlt der Feldname")]
    [InlineData("1 2", "steht noch")]
    [InlineData("$", "fehlt der Pfad")]
    [InlineData("a ? b : c", "gehört nicht in einen Ausdruck")]
    public void Ein_kaputter_Ausdruck_meldet_Ursache_und_Stelle(string ausdruck, string erwartet)
    {
        var ex = Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse(ausdruck));

        Assert.Contains(erwartet, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Stelle", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Die Tiefe ist begrenzt, weil der Auswerter rekursiv läuft. Ein
    /// <c>StackOverflowException</c> lässt sich in .NET nicht abfangen — er
    /// beendet den Prozess, und bei einem Softphone hiesse das: die
    /// Konfiguration legt das Telefon still.
    /// </summary>
    [Fact]
    public void Ein_zu_tief_verschachtelter_Ausdruck_wird_abgelehnt()
    {
        var ausdruck = new string('!', ExpressionParser.MaxDepth + 5) + "true";

        var ex = Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse(ausdruck));

        Assert.Contains("verschachtelt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_zu_langer_Ausdruck_wird_abgelehnt()
    {
        var ausdruck = string.Join(" + ", Enumerable.Repeat("1", 400));

        Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse(ausdruck));
    }

    [Fact]
    public void TryParse_meldet_den_Fehler_als_Text_statt_als_Ausnahme()
    {
        Assert.False(ExpressionParser.TryParse("concat(", out var node, out var fehler));
        Assert.Null(node);
        Assert.NotNull(fehler);

        Assert.True(ExpressionParser.TryParse("'ok'", out var gut, out var keinFehler));
        Assert.NotNull(gut);
        Assert.Null(keinFehler);
    }

    // --- Diagnose ---

    [Fact]
    public void Was_nicht_geht_landet_in_der_Diagnose_ohne_die_Werte_zu_nennen()
    {
        var kontext = new Kontext().Mit("nummer", ContextValue.FromText("+41791234567"));
        var diagnose = new List<string>();

        var ergebnis = ExpressionEvaluator.Evaluate(
            ExpressionParser.Parse("nummer + 1"),
            kontext,
            diagnose);

        Assert.Equal(ContextValue.Null, ergebnis);
        Assert.Single(diagnose);
        Assert.DoesNotContain("791234567", diagnose[0], StringComparison.Ordinal);
    }

    // --- Vorlagen ---

    [Fact]
    public void Eine_Vorlage_setzt_Werte_ein()
    {
        var kontext = new Kontext().Mit("number.e164", ContextValue.FromText("+41791234567"));
        var vorlage = TemplateRenderer.Compile("/kunden/{{number.e164}}/details");

        Assert.Equal("/kunden/+41791234567/details", vorlage.Render(kontext));
    }

    /// <summary>
    /// In einer URL <b>muss</b> kodiert werden: ein <c>+</c> wird sonst als
    /// Leerzeichen gelesen, und ein <c>&amp;</c> im Wert hängt einen
    /// zusätzlichen Parameter an die Anfrage.
    /// </summary>
    [Fact]
    public void In_einer_Url_wird_der_Wert_kodiert()
    {
        var kontext = new Kontext().Mit("number.e164", ContextValue.FromText("+41791234567"));
        var vorlage = TemplateRenderer.Compile("{{number.e164}}");

        Assert.Equal("%2B41791234567", vorlage.Render(kontext, TemplateEscaping.UrlComponent));
    }

    [Fact]
    public void Eine_Vorlage_darf_rechnen_wie_ein_berechnetes_Feld()
    {
        var kontext = new Kontext()
            .Mit("query.text", ContextValue.FromText("Muster"))
            .Mit("query.limit", ContextValue.FromNumber(25));

        var vorlage = TemplateRenderer.Compile("{{concat(query.text, '*')}}|{{query.limit}}");

        Assert.Equal("Muster*|25", vorlage.Render(kontext));
    }

    [Fact]
    public void Eine_Vorlage_ohne_Ausdruck_bleibt_wie_sie_ist()
    {
        var vorlage = TemplateRenderer.Compile("/kunden");

        Assert.False(vorlage.HasExpressions);
        Assert.Equal("/kunden", vorlage.Render(new Kontext()));
    }

    [Theory]
    [InlineData("{{number.e164", "schliesst nie")]
    [InlineData("{{ }}", "kein Ausdruck")]
    [InlineData("{{concat(}}", "bricht ab")]
    public void Eine_kaputte_Vorlage_meldet_die_Ursache(string vorlage, string erwartet)
    {
        var ex = Assert.Throws<ExpressionParseException>(() => TemplateRenderer.Compile(vorlage));

        Assert.Contains(erwartet, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Vorlage_mit_leerem_Ergebnis_hinterlaesst_eine_leere_Stelle()
    {
        var vorlage = TemplateRenderer.Compile("a{{fehlt}}b");

        Assert.Equal("ab", vorlage.Render(new Kontext()));
    }
}
