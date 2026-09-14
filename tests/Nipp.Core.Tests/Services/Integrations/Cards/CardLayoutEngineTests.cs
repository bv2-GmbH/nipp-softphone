using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Die konfigurierbare Anruferkarte (§21).
///
/// <b>Zwei Dinge stehen hier auf dem Spiel.</b> Erstens soll sich eine Karte
/// beschreiben lassen, ohne dass jemand Code schreibt — die mitgelieferten
/// Karten sind selbst gewöhnliche Beschreibungen und werden hier mitgeprüft.
/// Zweitens darf nichts, was in einer verteilten Konfigurationsdatei steht,
/// mehr auslösen als vorgesehen: keine fremden Schemata in Adressen, keine
/// unbekannten Bausteine, kein Absturz bei fehlenden Daten.
/// </summary>
public sealed class CardLayoutEngineTests
{
    private static ContextSnapshot Kontext(
        params (string Quelle, SourceState Zustand, (string Feld, string Wert)[] Felder)[] quellen)
    {
        var sources = new Dictionary<string, ContextFragment>(StringComparer.Ordinal);
        var prioritaet = 10;

        foreach (var (quelle, zustand, felder) in quellen)
        {
            // Die Reihenfolge der Angabe ist die Priorität: die erste Quelle
            // gewinnt. Sonst entschiede die Reihenfolge eines Dictionary
            // darüber, welchen Namen `role('name')` liefert — und die ist
            // nicht zugesagt.
            sources[quelle] = new ContextFragment(
                quelle,
                quelle.ToUpperInvariant(),
                zustand,
                felder.ToDictionary(
                    f => f.Feld,
                    f => ContextValue.FromText(f.Wert),
                    StringComparer.Ordinal),
                Priority: prioritaet);

            prioritaet += 10;
        }

        return new ContextSnapshot(
            CallHandle.New(),
            PhoneNumberKey.From("0791234567", new NumberNormalizer("+41")),
            sources);
    }

    private static CompiledCard Uebersetzt(CardDefinition definition)
    {
        Assert.True(
            CardLayoutEngine.TryCompile(definition, out var card, out var fehler),
            $"Die Karte liess sich nicht übersetzen: {string.Join(" | ", fehler)}");

        return card;
    }

    private static CardDefinition Karte(params CardElement[] elemente) =>
        new(
            "test",
            "Test",
            CardKind.ActiveExpanded,
            [
                new CardSection(
                    "abschnitt",
                    null,
                    [new CardRow([new CardColumn(CardLayout.Columns, elemente)])]),
            ]);

    private static IReadOnlyList<CardElementModel> Elemente(CardModel modell) =>
        [.. modell.Sections.SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)];

    // --- Die mitgelieferten Karten ---

    /// <summary>
    /// Die eingebauten Karten sind gewöhnliche Beschreibungen, keine
    /// Sonderfälle im Code. Wären sie fest verdrahtet, fiele erst beim ersten
    /// Kunden auf, was sich nicht beschreiben lässt.
    /// </summary>
    [Fact]
    public void Die_mitgelieferten_Karten_lassen_sich_uebersetzen()
    {
        foreach (var definition in DefaultCards.All)
        {
            Assert.True(
                CardLayoutEngine.TryCompile(definition, out _, out var fehler),
                $"'{definition.Name}': {string.Join(" | ", fehler)}");
        }
    }

    /// <summary>
    /// <b>Warum hier <c>contactName</c> steht und nicht <c>customerName</c>.</b>
    /// Bis zum 07.09.2026 nahm dieser Test <c>customerName</c> als Namen der
    /// Person — weil die mitgelieferte Karte das tat. Der
    /// <c>ToastComposer</c> führte dasselbe Feld zur selben Zeit unter
    /// <b>Firma</b>. Zwei Orte, eine Frage, zwei Antworten; der Feldkatalog
    /// entscheidet sie jetzt einmal, und zwar wie der Toast: der Name eines
    /// Kunden kann eine Firma sein, der Name einer Person heisst
    /// <c>contactName</c>. So mappen es auch beide bv2-Vorlagen.
    /// </summary>
    [Fact]
    public void Die_Gespraechskarte_zeigt_Felder_aus_zwei_Systemen_nebeneinander()
    {
        var kontext = Kontext(
            ("crm", SourceState.Success, [("contactName", "Hans Muster"), ("company", "Muster AG")]),
            ("erp", SourceState.Success, [("customerNumber", "4711"), ("openOrders", "3")]));

        var modell = CardLayoutEngine.Build(Uebersetzt(DefaultCards.ActiveCall), kontext);

        var texte = Elemente(modell).OfType<CardTextModel>().Where(static t => t.Visible).ToList();
        var felder = Elemente(modell).OfType<CardFieldModel>().Where(static f => f.Visible).ToList();

        Assert.Contains(texte, t => t.Text == "Hans Muster");
        Assert.Contains(texte, t => t.Text == "Muster AG");
        Assert.Contains(felder, f => f.Label == "Kundennummer" && f.Value == "4711");
        Assert.Contains(felder, f => f.Label == "Offene Aufträge" && f.Value == "3");
    }

    /// <summary>
    /// Der Fall, für den <c>coalesce</c> in der Karte steht: kennt das CRM
    /// niemanden, gilt der eigene Kontakt; kennt auch der niemanden, die
    /// formatierte Nummer. Die Karte hat nie eine leere Überschrift.
    /// </summary>
    [Fact]
    public void Ohne_Crm_Namen_gilt_der_eigene_Kontakt()
    {
        var kontext = Kontext(("contacts", SourceState.Success, [("displayName", "Aus Outlook")]));

        var modell = CardLayoutEngine.Build(Uebersetzt(DefaultCards.ActiveCall), kontext);

        Assert.Contains(
            Elemente(modell).OfType<CardTextModel>(),
            t => t.Visible && t.Text == "Aus Outlook");
    }

    [Fact]
    public void Ganz_ohne_Namen_steht_die_formatierte_Nummer_da()
    {
        var modell = CardLayoutEngine.Build(Uebersetzt(DefaultCards.ActiveCall), Kontext());

        Assert.Contains(
            Elemente(modell).OfType<CardTextModel>(),
            t => t.Visible && t.Text == "+41 79 123 45 67");
    }

    // --- Sichtbarkeit ---

    [Fact]
    public void Ein_Feld_ohne_Wert_verschwindet_wenn_kein_Platzhalter_gesetzt_ist()
    {
        var karte = Karte(
            new CardField("Kundennummer", "erp.customerNumber") { EmptyText = null },
            new CardField("Firma", "crm.company") { EmptyText = "—" });

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());
        var felder = Elemente(modell).OfType<CardFieldModel>().ToList();

        Assert.False(felder[0].Visible);

        // Mit Platzhalter bleibt die Zeile: sie sagt, dass gefragt wurde.
        Assert.True(felder[1].Visible);
        Assert.Equal("—", felder[1].Value);
    }

    [Fact]
    public void Eine_Bedingung_entscheidet_ueber_die_Sichtbarkeit()
    {
        var karte = Karte(
            new CardText("'VIP-Kunde'") { VisibleWhen = "crm.vip == 'ja'" });

        var mitVip = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("crm", SourceState.Success, [("vip", "ja")])));

        var ohneVip = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("crm", SourceState.Success, [("vip", "nein")])));

        Assert.True(Elemente(mitVip)[0].Visible);
        Assert.False(Elemente(ohneVip)[0].Visible);
    }

    /// <summary>
    /// Eine Bedingung, die sich nicht entscheiden lässt, zeigt nichts. Die
    /// Gegenrichtung wäre eine Karte, die bei fehlenden Daten etwas behauptet.
    /// </summary>
    [Fact]
    public void Eine_unentscheidbare_Bedingung_zeigt_nichts()
    {
        var karte = Karte(new CardText("'Gross'") { VisibleWhen = "erp.revenue > 100000" });

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.False(Elemente(modell)[0].Visible);
    }

    [Fact]
    public void Ein_Abschnitt_ohne_sichtbaren_Inhalt_verschwindet()
    {
        var karte = Karte(new CardField("Nichts", "erp.gibtEsNicht") { EmptyText = null });

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.False(modell.Sections[0].Visible);
        Assert.False(modell.HasContent);
    }

    // --- Abzeichen ---

    [Fact]
    public void Der_Ton_eines_Abzeichens_darf_von_den_Daten_abhaengen()
    {
        var karte = Karte(
            new CardBadge("'Offene Aufträge'")
            {
                Tone = "if(erp.openOrders == '5', 'warning', 'neutral')",
            });

        var viele = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("erp", SourceState.Success, [("openOrders", "5")])));

        var wenige = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("erp", SourceState.Success, [("openOrders", "1")])));

        Assert.Equal(CardTone.Warning, Elemente(viele).OfType<CardBadgeModel>().Single().Tone);
        Assert.Equal(CardTone.Neutral, Elemente(wenige).OfType<CardBadgeModel>().Single().Tone);
    }

    [Fact]
    public void Ein_unbekannter_Ton_wird_neutral()
    {
        var karte = Karte(new CardBadge("'Test'") { Tone = "'gibtEsNicht'" });

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.Equal(CardTone.Neutral, Elemente(modell).OfType<CardBadgeModel>().Single().Tone);
    }

    // --- Aktionen und Sicherheit ---

    [Fact]
    public void Eine_Schaltflaeche_setzt_ihre_Adresse_aus_dem_Kontext_zusammen()
    {
        var karte = Karte(
            new CardButton(
                "Im CRM öffnen",
                new OpenUrlAction("https://crm.example.ch/kunden/{{crm.customerId}}")));

        var modell = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("crm", SourceState.Success, [("customerId", "4711")])));

        var button = Elemente(modell).OfType<CardButtonModel>().Single();

        Assert.True(button.Enabled);
        Assert.Equal(
            new Uri("https://crm.example.ch/kunden/4711"),
            Assert.IsType<CardOpenUrl>(button.Action).Target);
    }

    /// <summary>
    /// <b>Die Sicherheitsprüfung</b> (§21.2). Alles ausser http und https
    /// landete sonst in <c>ShellExecute</c>, und das startet, was auch immer
    /// Windows hinter einem Schema vermutet.
    /// </summary>
    [Theory]
    [InlineData("file:///C:/Windows/system32/cmd.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    public void Eine_Adresse_mit_fremdem_Schema_macht_die_Schaltflaeche_unbedienbar(string adresse)
    {
        var karte = Karte(new CardButton("Öffnen", new OpenUrlAction(adresse)));

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());
        var button = Elemente(modell).OfType<CardButtonModel>().Single();

        Assert.Null(button.Action);
        Assert.False(button.Enabled);
    }

    [Fact]
    public void Ein_Verweis_mit_fremdem_Schema_wird_nicht_gezeigt()
    {
        var karte = Karte(new CardLink("Öffnen", "file:///C:/geheim.txt"));

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.False(Elemente(modell).OfType<CardLinkModel>().Single().Visible);
    }

    /// <summary>
    /// Eine Schaltfläche, deren Adresse leer bleibt, verspräche etwas, das
    /// nicht geschieht.
    /// </summary>
    [Fact]
    public void Ohne_aufloesbare_Adresse_ist_die_Schaltflaeche_nicht_bedienbar()
    {
        var karte = Karte(
            new CardButton("Öffnen", new OpenUrlAction("{{crm.fehlt}}")));

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.False(Elemente(modell).OfType<CardButtonModel>().Single().Enabled);
    }

    [Fact]
    public void EnabledWhen_steuert_die_Bedienbarkeit()
    {
        var karte = Karte(
            new CardButton("Öffnen", new OpenUrlAction("https://crm.example.ch/{{crm.customerId}}"))
            {
                EnabledWhen = "!isEmpty(crm.customerId)",
            });

        var ohne = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.False(Elemente(ohne).OfType<CardButtonModel>().Single().Enabled);
    }

    [Fact]
    public void Waehlen_und_Kopieren_werden_aufgeloest()
    {
        var karte = Karte(
            new CardButton("Zurückrufen", new DialAction("{{number.e164}}")),
            new CardButton("Nummer kopieren", new CopyAction("{{number.national}}")));

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());
        var buttons = Elemente(modell).OfType<CardButtonModel>().ToList();

        Assert.Equal("+41791234567", Assert.IsType<CardDial>(buttons[0].Action).Number);
        Assert.Equal("0791234567", Assert.IsType<CardCopy>(buttons[1].Action).Value);
    }

    // --- Quellenzustände ---

    [Fact]
    public void Der_Zustand_einer_Quelle_wird_als_Satz_gezeigt()
    {
        var karte = Karte(new CardSourceStatus("erp"));

        var laedt = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("erp", SourceState.Loading, [])));

        var status = Elemente(laedt).OfType<CardSourceStatusModel>().Single();

        Assert.True(status.Visible);
        Assert.Contains("wird gefragt", status.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eine erfolgreiche Quelle sagt nichts: ihre Felder stehen auf der Karte,
    /// und „ERP: gefunden" wäre Rauschen.
    /// </summary>
    [Fact]
    public void Eine_erfolgreiche_Quelle_wird_nicht_erwaehnt()
    {
        var karte = Karte(new CardSourceStatus("erp"));

        var modell = CardLayoutEngine.Build(
            Uebersetzt(karte),
            Kontext(("erp", SourceState.Success, [("x", "y")])));

        Assert.False(Elemente(modell).OfType<CardSourceStatusModel>().Single().Visible);
    }

    [Fact]
    public void Eine_Quelle_die_es_nicht_gibt_wird_uebergangen()
    {
        var karte = Karte(new CardSourceStatus("gibtEsNicht"));

        var modell = CardLayoutEngine.Build(Uebersetzt(karte), Kontext());

        Assert.False(Elemente(modell).OfType<CardSourceStatusModel>().Single().Visible);
    }

    // --- Prüfung der Beschreibung ---

    [Fact]
    public void Ein_kaputter_Ausdruck_wird_beim_Uebersetzen_gemeldet()
    {
        var karte = Karte(new CardText("concat("));

        Assert.False(CardLayoutEngine.TryCompile(karte, out _, out var fehler));
        Assert.Single(fehler);
    }

    [Fact]
    public void Eine_kaputte_Bedingung_wird_gemeldet()
    {
        var karte = Karte(new CardText("'x'") { VisibleWhen = "a ==" });

        Assert.False(CardLayoutEngine.TryCompile(karte, out _, out var fehler));
        Assert.Contains(fehler, f => f.Contains("visibleWhen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Sechs Einheiten sind die Breite des Rasters — mehr passt in einem
    /// 400 Pixel breiten Fenster nicht nebeneinander.
    /// </summary>
    [Fact]
    public void Zu_viele_Spalten_in_einer_Zeile_werden_gemeldet()
    {
        var karte = new CardDefinition(
            "test",
            "Test",
            CardKind.ActiveExpanded,
            [
                new CardSection(
                    "abschnitt",
                    null,
                    [
                        new CardRow([
                            new CardColumn(4, [new CardText("'a'")]),
                            new CardColumn(4, [new CardText("'b'")]),
                        ]),
                    ]),
            ]);

        Assert.False(CardLayoutEngine.TryCompile(karte, out _, out var fehler));
        Assert.Contains(fehler, f => f.Contains("Einheiten", StringComparison.Ordinal));
    }

    [Fact]
    public void Alle_Fehler_kommen_auf_einmal()
    {
        var karte = Karte(
            new CardText("concat("),
            new CardField("Kaputt", "a =="),
            new CardBadge("'x'") { Tone = "if(" });

        Assert.False(CardLayoutEngine.TryCompile(karte, out _, out var fehler));
        Assert.True(fehler.Count >= 3, $"Erwartet wurden drei Befunde, gefunden: {fehler.Count}");
    }

    // --- Der Schlüssel für den Renderer ---

    /// <summary>
    /// Die Karte wird bei jeder Antwort einer Quelle neu aufgebaut, und das
    /// auf dem Thread, der alle 20 ms das SDK bedient. Ein Renderer, der seine
    /// Steuerelemente über den Schlüssel wiederfindet, setzt nur Text und
    /// Sichtbarkeit, statt den Baum jedes Mal neu zu erzeugen.
    /// </summary>
    [Fact]
    public void Die_Schluessel_der_Bausteine_bleiben_ueber_Aktualisierungen_gleich()
    {
        var karte = Uebersetzt(DefaultCards.ActiveCall);

        var erste = Elemente(CardLayoutEngine.Build(karte, Kontext()));

        var zweite = Elemente(CardLayoutEngine.Build(
            karte,
            Kontext(("crm", SourceState.Success, [("customerName", "Hans")]))));

        Assert.Equal(
            erste.Select(static e => e.Key),
            zweite.Select(static e => e.Key));
    }

    [Fact]
    public void Ohne_Kontext_ist_die_Karte_leer()
    {
        var modell = CardLayoutEngine.Build(Uebersetzt(DefaultCards.ActiveCall), snapshot: null);

        Assert.Same(CardModel.Empty, modell);
        Assert.False(modell.HasContent);
    }
}
