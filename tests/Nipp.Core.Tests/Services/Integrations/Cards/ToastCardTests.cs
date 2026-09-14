using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Der Toast als Kartenart (K5, ADR-034).
///
/// <para><b>Die Abnahme von K5 steht in der Nachbardatei</b>, nicht hier:
/// <c>ToastComposerTests</c> muss <b>unverändert</b> durchlaufen. Was am Gerät
/// belegt ist (ADR-030), darf durch die Konfigurierbarkeit nicht anders
/// werden. Diese Datei prüft den zweiten Weg — was passiert, wenn jemand
/// wirklich eine Toast-Karte einrichtet.</para>
///
/// <para><b>Und die eine Entscheidung, die hier festgehalten wird:</b> die
/// mitgelieferte Zusammensetzung bleibt Code. Eine Karte besteht aus
/// unabhängigen Zeilen, der Toast setzt seine erste aus drei Werten zusammen
/// und lässt Teile weg — als Ausdruck wären das drei unlesbare Ungetüme. Wer
/// den Toast selbst zusammenstellt, gibt die Feinheiten auf; das ist seine
/// Wahl und in der Vorschau zu sehen.</para>
/// </summary>
public sealed class ToastCardTests
{
    private const string Nummer = "+41441234567";

    private static ContextSnapshot Snapshot(
        params (string Feld, string Wert)[] felder)
    {
        var fragment = new ContextFragment(
            "irgendeine",
            "Irgendeine Quelle",
            SourceState.Success,
            felder.ToDictionary(
                f => f.Feld,
                f => ContextValue.FromText(f.Wert),
                StringComparer.Ordinal),
            Priority: 10);

        return new ContextSnapshot(
            CallHandle.New(),
            PhoneNumberKey.From(Nummer, new NumberNormalizer("+41")),
            new Dictionary<string, ContextFragment>(StringComparer.Ordinal)
            {
                ["irgendeine"] = fragment,
            });
    }

    private static CompiledCard Karte(params string[] ausdruecke)
    {
        var definition = new CardDefinition(
            "toast",
            "Benachrichtigung",
            CardKind.Toast,
            [
                new CardSection(
                    "zeilen",
                    null,
                    [
                        .. ausdruecke.Select(static a => new CardRow(
                        [
                            new CardColumn(CardLayout.Columns, [new CardText(a)]),
                        ])),
                    ]),
            ]);

        Assert.True(
            CardLayoutEngine.TryCompile(definition, out var uebersetzt, out var fehler),
            string.Join(" | ", fehler));

        return uebersetzt;
    }

    // --- Ohne Karte bleibt alles, wie es war ---

    [Fact]
    public void Ohne_Karte_gilt_die_mitgelieferte_Zusammensetzung()
    {
        var kontext = Snapshot(
            ("contactName", "Hans Muster"),
            ("company", "Muster AG"),
            ("contactType", "Kunde"));

        var ohne = ToastComposer.Compose(kontext, "Rückfall", Nummer);
        var mitLeerer = ToastComposer.Compose(kontext, "Rückfall", Nummer, new CompiledCard());

        Assert.Equal("Hans Muster · Muster AG (Kunde)", ohne.Line1);
        Assert.Equal(ohne, mitLeerer);
    }

    /// <summary>
    /// Eine Karte einer <b>anderen</b> Art wird nicht als Toast-Karte genommen
    /// — sonst zeigte die Benachrichtigung die Gesprächskarte, und niemand
    /// wüsste, woher das kommt.
    /// </summary>
    [Fact]
    public void Eine_Karte_der_falschen_Art_wird_nicht_benutzt()
    {
        Assert.True(CardLayoutEngine.TryCompile(DefaultCards.ActiveCall, out var gespraech, out _));

        var kontext = Snapshot(("contactName", "Hans Muster"), ("company", "Muster AG"));

        var zeilen = ToastComposer.Compose(kontext, "Rückfall", Nummer, gespraech);

        Assert.Equal("Hans Muster · Muster AG", zeilen.Line1);
    }

    // --- Mit Karte gilt die Karte ---

    [Fact]
    public void Eine_eingerichtete_Karte_bestimmt_die_drei_Zeilen()
    {
        var kontext = Snapshot(
            ("contactName", "Hans Muster"),
            ("company", "Muster AG"),
            ("letzteZusammenfassung", "Rückruf vereinbart."));

        var zeilen = ToastComposer.Compose(
            kontext,
            "Rückfall",
            Nummer,
            Karte("role('company')", "role('name')", "role('summary')"));

        // Die Reihenfolge ist die der Karte, nicht die der mitgelieferten
        // Fassung: die Firma steht oben, weil sie oben eingetragen ist.
        Assert.Equal("Muster AG", zeilen.Line1);
        Assert.Equal("Hans Muster", zeilen.Line2);
        Assert.Equal("Rückruf vereinbart.", zeilen.Line3);
        Assert.Equal("+41 44 123 45 67", zeilen.Attribution);
    }

    /// <summary>
    /// Eine vierte Zeile wird nicht gezeigt. Der Validator meldet sie beim
    /// Einrichten; hier wird sie stillschweigend weggelassen, statt mitten im
    /// Wort abgeschnitten zu erscheinen.
    /// </summary>
    [Fact]
    public void Eine_vierte_Zeile_erscheint_nicht()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("contactName", "Wer")),
            "Rückfall",
            Nummer,
            Karte("'eins'", "'zwei'", "'drei'", "'vier'"));

        Assert.Equal("eins", zeilen.Line1);
        Assert.Equal("zwei", zeilen.Line2);
        Assert.Equal("drei", zeilen.Line3);
    }

    /// <summary>
    /// Eine Zeile, deren Wert leer bleibt, rückt nicht nach — sie fällt weg.
    /// Sonst stünde die Zusammenfassung an der Stelle des Namens, und der
    /// Toast läse sich bei jeder Quelle anders.
    /// </summary>
    [Fact]
    public void Eine_leere_Zeile_faellt_weg()
    {
        var zeilen = ToastComposer.Compose(
            Snapshot(("contactName", "Wer"), ("letzteZusammenfassung", "Etwas")),
            "Rückfall",
            Nummer,
            Karte("role('name')", "role('work')", "role('summary')"));

        Assert.Equal("Wer", zeilen.Line1);
        Assert.Equal("Etwas", zeilen.Line2);
        Assert.Null(zeilen.Line3);
    }

    /// <summary>
    /// Liefert die Karte gar nichts, gilt wieder die mitgelieferte Fassung.
    /// Ein Toast mit drei leeren Zeilen wäre schlimmer als einer mit dem
    /// Namen — und §21 verlangt, dass eine Integration nichts kostet.
    /// </summary>
    [Fact]
    public void Eine_Karte_ohne_Ergebnis_faellt_auf_die_mitgelieferte_zurueck()
    {
        var kontext = Snapshot(("contactName", "Hans Muster"));

        var zeilen = ToastComposer.Compose(
            kontext,
            "Rückfall",
            Nummer,
            Karte("anyOf('gibtEsNicht')"));

        Assert.Equal("Hans Muster", zeilen.Line1);
    }

    /// <summary>
    /// Was nicht Text ist, hat im Toast keine Entsprechung und wird
    /// übergangen. Der Validator sagt das beim Einrichten.
    /// </summary>
    [Fact]
    public void Felder_und_Trennlinien_werden_uebergangen()
    {
        var definition = new CardDefinition(
            "toast",
            "Benachrichtigung",
            CardKind.Toast,
            [
                new CardSection("zeilen", null, [
                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardField("Art", "role('type')"),
                            new CardDivider(),
                            new CardText("role('name')"),
                        ]),
                    ]),
                ]),
            ]);

        Assert.True(CardLayoutEngine.TryCompile(definition, out var karte, out _));

        var zeilen = ToastComposer.Compose(
            Snapshot(("contactName", "Wer"), ("contactType", "Kunde")),
            "Rückfall",
            Nummer,
            karte);

        Assert.Equal("Wer", zeilen.Line1);
        Assert.Null(zeilen.Line2);
    }

    /// <summary>
    /// Eine Bedingung auf einer Toast-Zeile wirkt — dafür sind Karten da.
    /// </summary>
    [Fact]
    public void Eine_unsichtbare_Zeile_erscheint_nicht()
    {
        var definition = new CardDefinition(
            "toast",
            "Benachrichtigung",
            CardKind.Toast,
            [
                new CardSection("zeilen", null, [
                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardText("role('name')"),
                            // `!isEmpty(...)` und nicht `== true`: der Wert kommt als
                            // Text aus dem Mapping, und die Ausdruckssprache wandelt
                            // nichts stillschweigend um — dieselbe Regel, die eine
                            // Rufnummer davor bewahrt, eine Zahl zu werden.
                            new CardText("'nur bei VIP'") { VisibleWhen = "!isEmpty(irgendeine.vip)" },
                        ]),
                    ]),
                ]),
            ]);

        Assert.True(CardLayoutEngine.TryCompile(definition, out var karte, out _));

        var ohneVip = ToastComposer.Compose(
            Snapshot(("contactName", "Wer")), "Rückfall", Nummer, karte);

        Assert.Equal("Wer", ohneVip.Line1);
        Assert.Null(ohneVip.Line2);

        var mitVip = ToastComposer.Compose(
            Snapshot(("contactName", "Wer"), ("vip", "ja")), "Rückfall", Nummer, karte);

        Assert.Equal("nur bei VIP", mitVip.Line2);
    }

    /// <summary>
    /// Gekürzt wird auch auf dem Kartenweg — Windows schneidet sonst mitten im
    /// Wort ab.
    /// </summary>
    [Fact]
    public void Auch_eine_Kartenzeile_wird_gekuerzt()
    {
        var lang = string.Join(" ", Enumerable.Repeat("Wort", 60));

        var zeilen = ToastComposer.Compose(
            Snapshot(("letzteZusammenfassung", lang)),
            "Rückfall",
            Nummer,
            Karte("role('summary')"));

        Assert.True(zeilen.Line1.Length <= ToastComposer.MaxLineLength + 1);
        Assert.EndsWith("…", zeilen.Line1, StringComparison.Ordinal);
    }

    /// <summary>
    /// Und ohne Kontext gilt weiter der Rückfall, Karte hin oder her: es gibt
    /// nichts aufzulösen.
    /// </summary>
    [Fact]
    public void Ohne_Kontext_bleibt_es_beim_Rueckfall()
    {
        var zeilen = ToastComposer.Compose(
            snapshot: null,
            "Hans Muster",
            Nummer,
            Karte("role('summary')"));

        Assert.Equal("Hans Muster", zeilen.Line1);
        Assert.Null(zeilen.Line2);
    }

    // --- Der Anfangsentwurf im Designer ---

    /// <summary>
    /// Wer den Toast zum ersten Mal öffnet, findet drei Zeilen vor — nicht
    /// eine leere Fläche. Und es sind einfache Zeilen, keine Kopie der
    /// mitgelieferten Zusammensetzung: die wäre im Designer nur als
    /// „Ausdruck" bearbeitbar.
    /// </summary>
    [Fact]
    public void Der_Anfangsentwurf_hat_drei_lesbare_Zeilen()
    {
        var entwurf = CardDraft.Empty(CardKind.Toast);

        var bausteine = entwurf.AllElements.ToList();

        Assert.Equal(3, bausteine.Count);
        Assert.All(bausteine, b => Assert.Equal(DraftElementKind.Text, b.Kind));
        Assert.All(bausteine, b => Assert.Equal(DraftValueMode.Role, b.Value.Mode));

        Assert.Equal(
            ["role('name')", "role('work')", "role('summary')"],
            bausteine.Select(static b => b.Value.ToExpression()));

        // Und er ist speicherbar, ohne dass jemand etwas korrigieren muss.
        Assert.Empty(CardDefinitionValidator.ValidateCard(entwurf.ToDefinition()));
    }

    /// <summary>
    /// Der Anfangsentwurf ergibt einen brauchbaren Toast — sonst wäre er kein
    /// Anfang, sondern eine Aufgabe.
    /// </summary>
    [Fact]
    public void Der_Anfangsentwurf_ergibt_einen_brauchbaren_Toast()
    {
        Assert.True(CardLayoutEngine.TryCompile(
            CardDraft.Empty(CardKind.Toast).ToDefinition(),
            out var karte,
            out _));

        var zeilen = ToastComposer.Compose(
            Snapshot(
                ("contactName", "Hans Muster"),
                ("letzteArbeitZeile", "04.09. · Migration"),
                ("letzteZusammenfassung", "Rückruf vereinbart.")),
            "Rückfall",
            Nummer,
            karte);

        Assert.Equal("Hans Muster", zeilen.Line1);
        Assert.Equal("04.09. · Migration", zeilen.Line2);
        Assert.Equal("Rückruf vereinbart.", zeilen.Line3);
        Assert.True(zeilen.HasContext);
    }
}
