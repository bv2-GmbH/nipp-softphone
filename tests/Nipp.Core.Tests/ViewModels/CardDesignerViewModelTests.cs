using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Der Karten-Designer (K4, I9, ADR-032).
///
/// <para><b>Warum diese Tests den Ausschlag geben.</b> <c>Nipp.App</c> hat
/// kein Testprojekt; das Designer-<b>Fenster</b> ist nur am Gerät prüfbar. Der
/// ganze Zustand liegt deshalb im Kern, und diese Datei ist seine Abnahme.
/// Die drei wichtigsten Gruppen: der <b>Rundlauf</b> (eine geöffnete und ohne
/// Änderung gespeicherte Karte muss dieselbe sein), <b>Rückgängig</b> und die
/// <b>Vorschau</b>.</para>
/// </summary>
public sealed class CardDesignerViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private readonly List<IDisposable> _wegwerfen = [];

    /// <summary>
    /// Die Anbietervorlagen fuer diesen Test: die mitgelieferte und die
    /// synthetischen aus <see cref="TestTemplates"/> (ADR-040).
    /// </summary>
    private ConnectorLibrary Bibliothek()
    {
        var ordner = Path.Combine(_directory, "connectors");

        Directory.CreateDirectory(ordner);

        foreach (var datei in TestTemplates.Files)
        {
            File.Copy(datei, Path.Combine(ordner, Path.GetFileName(datei)), overwrite: true);
        }

        var bibliothek = new ConnectorLibrary(NullLogger<ConnectorLibrary>.Instance, ordner);
        bibliothek.Reload();

        return bibliothek;
    }

    private (CardDesignerViewModel Vm, IntegrationConfigStore Store) Bauen(
        CardKind art = CardKind.ActiveExpanded,
        IntegrationConfig? config = null,
        TestSampleStore? proben = null)
    {
        var geheimnisse = new IntegrationSecrets(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

        var speicher = new IntegrationConfigStore(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));

        if (config is not null)
        {
            speicher.Save(config);
        }
        else
        {
            speicher.Load();
        }

        var karten = new CardResolver(speicher, NullLogger<CardResolver>.Instance);
        _wegwerfen.Add(karten);

        var vm = new CardDesignerViewModel(
            art,
            karten,
            speicher,
            proben ?? new TestSampleStore(Bibliothek()),
            NullLogger<CardDesignerViewModel>.Instance);

        return (vm, speicher);
    }

    private static string Text(CardDefinition definition) =>
        IntegrationConfigStore.SerializeCard(definition);

    // --- Uebernehmen von einer anderen Karte (ADR-036) ---

    [Fact]
    public void Uebernehmen_holt_den_Aufbau_und_laesst_Kennung_und_Art_stehen()
    {
        var (vm, _) = Bauen(CardKind.History);

        var eigeneKennung = vm.Draft.Id;
        var quelle = vm.CopySources.Single(static q => q.Kind == CardKind.ActiveExpanded);

        vm.CopyFromCommand.Execute(quelle);

        // Der Aufbau ist der der Gespraechskarte — Abschnitt fuer Abschnitt.
        Assert.Equal(
            Text(quelle.Definition! with { Id = eigeneKennung, Name = vm.Draft.Name, Kind = CardKind.History }),
            Text(vm.Draft.ToDefinition()),
            StringComparer.Ordinal);

        // … Kennung und Art aber die eigenen. Kaeme die Kennung mit, staenden
        // zwei Karten mit derselben in der Datei, und der Validator meldete
        // einen Befund fuer etwas, das niemand getan hat.
        Assert.Equal(eigeneKennung, vm.Draft.Id);
        Assert.Equal(CardKind.History, vm.Draft.Kind);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Uebernehmen_ist_ein_Schritt_und_laesst_sich_zuruecknehmen()
    {
        var (vm, _) = Bauen(CardKind.History);

        var vorher = Text(vm.Draft.ToDefinition());

        vm.CopyFromCommand.Execute(vm.CopySources.First(static q => q.Kind == CardKind.ActiveExpanded));

        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);

        Assert.Equal(vorher, Text(vm.Draft.ToDefinition()), StringComparer.Ordinal);
    }

    [Fact]
    public void Die_eigene_Art_steht_nicht_in_der_Auswahl()
    {
        var (vm, _) = Bauen(CardKind.History);

        Assert.DoesNotContain(vm.CopySources, static q => q.Kind == CardKind.History);
        Assert.All(vm.CopySources, static q => Assert.NotNull(q.Definition));
    }

    [Fact]
    public void Die_Gespraechskarte_im_Toast_sperrt_das_Speichern_statt_zu_kuerzen()
    {
        var (vm, _) = Bauen(CardKind.Toast);

        vm.CopyFromCommand.Execute(vm.CopySources.First(static q => q.Kind == CardKind.ActiveExpanded));

        // Gekuerzt wird nichts: welche Zeile faellt, entscheidet der Benutzer.
        // Gesagt wird es aber — die Gespraechskarte besteht groesstenteils aus
        // Feldern, und die erscheinen in einer Benachrichtigung nicht —, und
        // gespeichert wird nicht.
        Assert.False(vm.CanSave);
        Assert.Contains(
            vm.Problems,
            static p => p.Contains("Benachrichtigung", StringComparison.Ordinal));

        // Und die Bausteine sind wirklich alle da, keiner ist weggefallen.
        Assert.Equal(
            DefaultCards.ActiveCall.Sections
                .SelectMany(static s => s.Rows)
                .SelectMany(static r => r.Columns)
                .Sum(static c => c.Elements.Count),
            vm.Draft.AllElements.Count());
    }

    // --- Die beiden neuen Bausteine (ADR-037) ---

    [Fact]
    public void Ein_Abstand_laesst_sich_einfuegen_und_behaelt_seine_Groesse()
    {
        var (vm, _) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Spacer);

        var baustein = Assert.IsType<DraftElement>(vm.SelectedElement);
        baustein.SpacerSize = CardSpacerSize.Large;

        vm.Refresh();

        var abstand = vm.Draft.ToDefinition().Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardSpacer>()
            .Single();

        Assert.Equal(CardSpacerSize.Large, abstand.Size);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void Eine_abgeschaltete_Beschriftung_kommt_in_der_Karte_an()
    {
        var (vm, store) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Field);

        vm.SelectedElement!.Label = "Firma";
        vm.SelectedElement.ShowLabel = false;
        vm.SelectedElement.Value.Mode = DraftValueMode.Role;
        vm.SelectedElement.Value.Text = "company";

        vm.Refresh();
        vm.SaveCommand.Execute(null);

        var gespeichert = store.Current.Cards.Single(static k => k.Kind == CardKind.ActiveExpanded);

        var feld = gespeichert.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardField>()
            .Single(static f => f.Label == "Firma");

        Assert.False(feld.ShowLabel);
    }

    // --- Rundlauf: der wichtigste Test dieser Datei ---

    /// <summary>
    /// <b>Öffnen und ohne Änderung speichern muss dieselbe Karte ergeben.</b>
    /// Was der Designer nicht in Formulare zerlegen kann, gibt er unverändert
    /// weiter — er darf nichts wegwerfen, was er nicht anzeigen kann.
    /// </summary>
    [Fact]
    public void Die_mitgelieferten_Karten_ueberleben_den_Rundlauf_zeichengleich()
    {
        foreach (var karte in DefaultCards.All)
        {
            var entwurf = CardDraft.FromDefinition(karte);

            Assert.Equal(Text(karte), Text(entwurf.ToDefinition()));
        }
    }

    /// <summary>
    /// Und dasselbe für eine Karte, die alles enthält — jeden Bausteintyp,
    /// jede Wertform, jede Sichtbarkeitsform, zwei Spalten.
    /// </summary>
    [Fact]
    public void Eine_Karte_mit_allen_Formen_ueberlebt_den_Rundlauf()
    {
        var karte = new CardDefinition(
            "alles",
            "Alles",
            CardKind.ActiveExpanded,
            [
                new CardSection("kopf", "Überschrift",
                    [
                        new CardRow([
                            new CardColumn(6, [
                                // Jede Wertform einmal.
                                new CardText("role('name')") { Style = CardTextStyle.Title },
                                new CardText("crm.company") { Style = CardTextStyle.Subtitle, MaxLines = 2 },
                                new CardText("coalesce(role('name'), contacts.displayName, formatPhone(number.e164))"),
                                new CardText("'Fester Text'") { Style = CardTextStyle.Caption },
                                new CardText("if(erp.openOrders > 5, 'viel', 'wenig')"),
                            ]),
                        ]),
                        new CardRow([
                            new CardColumn(3, [
                                new CardField("Kundennummer", "erp.customerNumber") { EmptyText = null },
                            ]),
                            new CardColumn(3, [
                                new CardField("Offen", "erp.openOrders") { EmptyText = "—" },
                            ]),
                        ]),
                        new CardRow([
                            new CardColumn(6, [
                                new CardBadge("'VIP'") { Tone = "'warning'", VisibleWhen = "crm.vip == true" },
                                new CardDivider(),
                                new CardButton("Öffnen", new OpenUrlAction("https://crm.example.ch/{{crm.id}}")),
                                new CardLink("Website", "https://example.ch"),
                                new CardSourceStatus("crm"),
                            ]),
                        ]),
                    ],
                    VisibleWhen: "!isEmpty(role('name'))"),
            ]);

        Assert.Equal(Text(karte), Text(CardDraft.FromDefinition(karte).ToDefinition()));
    }

    [Theory]
    [InlineData("crm.contactName", DraftValueMode.Field)]
    [InlineData("role('name')", DraftValueMode.Role)]
    [InlineData("'VIP'", DraftValueMode.Literal)]
    [InlineData("coalesce(a.b, c.d)", DraftValueMode.FirstOf)]
    [InlineData("coalesce(role('name'), formatPhone(number.e164))", DraftValueMode.FirstOf)]
    [InlineData("if(x > 5, 'a', 'b')", DraftValueMode.Expression)]
    [InlineData("concat(a, ' ', b)", DraftValueMode.Expression)]
    public void Ein_Ausdruck_wird_in_die_richtige_Form_gelesen(string ausdruck, DraftValueMode erwartet)
    {
        var wert = DraftValue.FromExpression(ausdruck);

        Assert.Equal(erwartet, wert.Mode);
        Assert.Equal(ausdruck, wert.ToExpression());
    }

    /// <summary>
    /// <b>Die Sicherung des Rückwegs.</b> <c>coalesce(a,b)</c> ohne Leerzeichen
    /// liesse sich nicht zeichengleich erzeugen — der Designer nimmt es
    /// deshalb als Ausdruck und schreibt es unverändert zurück, statt es still
    /// umzuformatieren.
    /// </summary>
    [Theory]
    [InlineData("coalesce(a,b)")]
    [InlineData("role( 'name' )")]
    [InlineData("coalesce(a, b,)")]
    [InlineData("coalesce(a, (b)")]
    public void Was_sich_nicht_zeichengleich_erzeugen_laesst_bleibt_ein_Ausdruck(string ausdruck)
    {
        var wert = DraftValue.FromExpression(ausdruck);

        Assert.Equal(DraftValueMode.Expression, wert.Mode);
        Assert.Equal(ausdruck, wert.ToExpression());
    }

    /// <summary>
    /// <c>!isEmpty(&lt;eigener Wert&gt;)</c> wird als „nur wenn ein Wert da
    /// ist" erkannt — aber nur, wenn es sich wirklich auf den eigenen Wert
    /// bezieht.
    /// </summary>
    [Fact]
    public void Die_haeufige_Sichtbarkeitsregel_wird_erkannt()
    {
        var eigen = DraftElement.FromElement(
            new CardText("crm.company") { VisibleWhen = "!isEmpty(crm.company)" });

        Assert.Equal(DraftVisibility.WhenValuePresent, eigen.Visibility);

        var fremd = DraftElement.FromElement(
            new CardText("crm.company") { VisibleWhen = "!isEmpty(crm.vip)" });

        Assert.Equal(DraftVisibility.Expression, fremd.Visibility);
        Assert.Equal("!isEmpty(crm.vip)", fremd.VisibleWhen);

        var immer = DraftElement.FromElement(new CardText("x"));

        Assert.Equal(DraftVisibility.Always, immer.Visibility);
    }

    // --- Palette ---

    [Fact]
    public void Die_Palette_hat_auch_ohne_Quelle_Inhalt()
    {
        var (vm, _) = Bauen();

        Assert.NotEmpty(vm.Palette);

        var alle = vm.Palette.SelectMany(static g => g.Entries).ToList();

        Assert.Contains(alle, e => e.Path == "number.e164");
        Assert.Contains(alle, e => e.Path == "contacts.displayName");
        Assert.Contains(alle, e => e.Path == "role('name')");
    }

    /// <summary>
    /// Und die Felder einer eingerichteten Quelle stehen darin — mit der
    /// Beschriftung aus dem Katalog, wo es eine gibt.
    /// </summary>
    [Fact]
    public void Die_Palette_zeigt_die_Felder_der_eingerichteten_Quellen()
    {
        var (vm, _) = Bauen(config: new IntegrationConfig
        {
            DataSources = [Bibliothek().Find("musterkontor")!.Source],
        });

        var crm = vm.Palette.SingleOrDefault(g => g.Name == "Musterkontor");

        Assert.NotNull(crm);
        Assert.Contains(crm.Entries, e => e.Path == "kontor.contactName" && e.Label == "Name");

        // Ein eigenes Feld wird angeboten und als solches gekennzeichnet.
        var eigenes = crm.Entries.FirstOrDefault(e => !e.IsKnown);

        if (eigenes is not null)
        {
            Assert.Contains("eigenes Feld", eigenes.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ein_Paletteneintrag_nennt_der_Sprachausgabe_keinen_Datensatz()
    {
        var (vm, _) = Bauen();

        var eintrag = vm.Palette.SelectMany(static g => g.Entries).First();

        Assert.DoesNotContain("PaletteEntry", eintrag.AutomationName, StringComparison.Ordinal);
        Assert.Contains(eintrag.Label, eintrag.AutomationName, StringComparison.Ordinal);
    }

    // --- Bearbeiten ---

    [Fact]
    public void Ein_Feld_aus_der_Palette_landet_auf_der_Karte()
    {
        var (vm, _) = Bauen();

        var vorher = vm.Draft.AllElements.Count();
        var eintrag = vm.Palette.SelectMany(static g => g.Entries)
            .Single(e => e.Path == "number.national");

        vm.AddFieldCommand.Execute(eintrag);

        Assert.Equal(vorher + 1, vm.Draft.AllElements.Count());

        var neu = vm.SelectedElement;

        Assert.NotNull(neu);
        Assert.Equal(DraftElementKind.Field, neu.Kind);
        Assert.Equal("Nummer national", neu.Label);
        Assert.Equal("number.national", neu.Value.ToExpression());

        // Neue Felder verschwinden bei leerem Wert, statt einen Gedankenstrich
        // zu zeigen — das ist auf einer Karte mit wechselnden Quellen die
        // häufigere Wahl.
        Assert.Null(neu.EmptyText);
    }

    [Fact]
    public void Jede_Bausteinart_laesst_sich_einfuegen()
    {
        var (vm, _) = Bauen();

        foreach (var art in Enum.GetValues<DraftElementKind>())
        {
            vm.AddElementCommand.Execute(art);

            Assert.NotNull(vm.SelectedElement);
            Assert.Equal(art, vm.SelectedElement!.Kind);
        }
    }

    [Fact]
    public void Ein_Baustein_laesst_sich_entfernen()
    {
        var (vm, _) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Divider);

        var vorher = vm.Draft.AllElements.Count();

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Equal(vorher - 1, vm.Draft.AllElements.Count());
        Assert.Null(vm.SelectedElement);
    }

    [Fact]
    public void Ein_Baustein_laesst_sich_nach_oben_und_unten_verschieben()
    {
        var (vm, _) = Bauen(CardKind.IncomingCompact);

        var alle = vm.Draft.AllElements.ToList();

        Assert.True(alle.Count >= 2, "Die kompakte Karte hat zwei Bausteine.");

        vm.SelectedElement = alle[1];
        vm.MoveSelectedCommand.Execute(-1);

        Assert.Equal(alle[1], vm.Draft.AllElements.First());

        vm.MoveSelectedCommand.Execute(1);

        Assert.Equal(alle[0], vm.Draft.AllElements.First());
    }

    /// <summary>
    /// Verschieben geht <b>über Spaltengrenzen</b>: die Karte ist eine Folge
    /// von Plätzen, und ob der nächste in derselben Spalte liegt, soll
    /// niemanden beschäftigen.
    /// </summary>
    [Fact]
    public void Verschieben_geht_ueber_die_Spaltengrenze()
    {
        var (vm, _) = Bauen();

        // Erst eine eigene Zeile: eingefuegt wird in die LETZTE Zeile, und die
        // mitgelieferte Gespraechskarte hat dort schon zwei Spalten
        // (Kundennummer neben offenen Auftraegen). Ohne diesen Schritt
        // pruefte der Test das Zusammenfuehren statt das Teilen.
        vm.AddRowCommand.Execute(null);
        vm.AddElementCommand.Execute(DraftElementKind.Field);
        vm.AddElementCommand.Execute(DraftElementKind.Divider);

        var trenner = vm.SelectedElement!;

        vm.TogglePairedCommand.Execute(null);

        var zeile = vm.Draft.Sections.SelectMany(static s => s.Rows)
            .Single(r => r.Columns.Any(c => c.Elements.Contains(trenner)));

        Assert.Equal(2, zeile.Columns.Count);
        Assert.Contains(trenner, zeile.Columns[1].Elements);

        // Und zurück in die erste Spalte.
        vm.MoveSelectedCommand.Execute(-1);

        Assert.Contains(trenner, zeile.Columns[0].Elements);
    }

    [Fact]
    public void Zwei_nebeneinander_und_wieder_zusammen()
    {
        var (vm, _) = Bauen();

        vm.AddRowCommand.Execute(null);
        vm.AddElementCommand.Execute(DraftElementKind.Field);
        vm.AddElementCommand.Execute(DraftElementKind.Field);

        vm.TogglePairedCommand.Execute(null);

        var zeile = vm.Draft.Sections.SelectMany(static s => s.Rows)
            .Single(r => r.Columns.Any(c => c.Elements.Contains(vm.SelectedElement!)));

        Assert.Equal(2, zeile.Columns.Count);
        Assert.Equal(3, zeile.Columns[0].Span);
        Assert.Equal(3, zeile.Columns[1].Span);

        vm.TogglePairedCommand.Execute(null);

        Assert.Single(zeile.Columns);
        Assert.Equal(6, zeile.Columns[0].Span);
    }

    /// <summary>
    /// Und die Spaltenbreiten bleiben im Raster: mehr als sechs Einheiten in
    /// einer Zeile ist ein Befund, den die Engine schon meldet.
    /// </summary>
    [Fact]
    public void Nach_dem_Teilen_bleibt_die_Karte_gueltig()
    {
        var (vm, _) = Bauen();

        vm.AddRowCommand.Execute(null);
        vm.AddElementCommand.Execute(DraftElementKind.Field);
        vm.AddElementCommand.Execute(DraftElementKind.Field);
        vm.TogglePairedCommand.Execute(null);

        Assert.Empty(vm.Problems);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void Eine_neue_Zeile_laesst_sich_anhaengen()
    {
        var (vm, _) = Bauen();

        var vorher = vm.Draft.Sections.Sum(static s => s.Rows.Count);

        vm.AddRowCommand.Execute(null);

        Assert.Equal(vorher + 1, vm.Draft.Sections.Sum(static s => s.Rows.Count));
        Assert.NotNull(vm.SelectedRow);
    }

    [Fact]
    public void Ziehen_setzt_den_Baustein_an_die_gewaehlte_Stelle()
    {
        var (vm, _) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Field);
        var erster = vm.SelectedElement!;

        vm.AddRowCommand.Execute(null);
        vm.AddElementCommand.Execute(DraftElementKind.Divider);

        var zweiteZeile = vm.SelectedRow!;

        vm.MoveTo(erster, zweiteZeile.Columns[0], 0);

        Assert.Equal(erster, zweiteZeile.Columns[0].Elements[0]);
        Assert.Equal(erster, vm.SelectedElement);
    }

    // --- Rückgängig ---

    [Fact]
    public void Jeder_Schritt_laesst_sich_zuruecknehmen()
    {
        var (vm, _) = Bauen();

        var anfang = Text(vm.Draft.ToDefinition());

        Assert.False(vm.CanUndo);

        vm.AddElementCommand.Execute(DraftElementKind.Divider);
        vm.AddRowCommand.Execute(null);
        vm.AddElementCommand.Execute(DraftElementKind.Field);

        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.Equal(anfang, Text(vm.Draft.ToDefinition()));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Rueckgaengig_und_Wiederholen_ergeben_dieselbe_Karte()
    {
        var (vm, _) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Field);
        vm.SelectedElement!.Label = "Etwas";
        vm.Refresh();

        var nachher = Text(vm.Draft.ToDefinition());

        vm.UndoCommand.Execute(null);

        Assert.NotEqual(nachher, Text(vm.Draft.ToDefinition()));
        Assert.True(vm.CanRedo);

        vm.RedoCommand.Execute(null);

        Assert.Equal(nachher, Text(vm.Draft.ToDefinition()));
    }

    /// <summary>
    /// Nach dreissig Schritten ist alles noch zurücknehmbar — genau der Fall,
    /// den die Testmatrix am Gerät prüft.
    /// </summary>
    [Fact]
    public void Auch_nach_dreissig_Schritten_fuehrt_Rueckgaengig_zum_Anfang()
    {
        var (vm, _) = Bauen();

        var anfang = Text(vm.Draft.ToDefinition());

        for (var i = 0; i < 30; i++)
        {
            vm.AddElementCommand.Execute(DraftElementKind.Divider);
        }

        for (var i = 0; i < 30; i++)
        {
            vm.UndoCommand.Execute(null);
        }

        Assert.Equal(anfang, Text(vm.Draft.ToDefinition()));
    }

    /// <summary>
    /// Der Stapel ist begrenzt. Über die Grenze hinaus geht der älteste Stand
    /// verloren — nicht der neueste, und nichts stürzt ab.
    /// </summary>
    [Fact]
    public void Der_Rueckgaengig_Stapel_bleibt_begrenzt()
    {
        var (vm, _) = Bauen();

        for (var i = 0; i < CardDesignerViewModel.MaxUndoSteps + 20; i++)
        {
            vm.AddElementCommand.Execute(DraftElementKind.Divider);
        }

        var zuruecknehmbar = 0;

        while (vm.CanUndo)
        {
            vm.UndoCommand.Execute(null);
            zuruecknehmbar++;
        }

        Assert.Equal(CardDesignerViewModel.MaxUndoSteps, zuruecknehmbar);
    }

    [Fact]
    public void Ein_neuer_Schritt_verwirft_das_Wiederholen()
    {
        var (vm, _) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Divider);
        vm.UndoCommand.Execute(null);

        Assert.True(vm.CanRedo);

        vm.AddElementCommand.Execute(DraftElementKind.Field);

        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Verwerfen_stellt_den_Stand_beim_Oeffnen_her()
    {
        var (vm, _) = Bauen();

        var anfang = Text(vm.Draft.ToDefinition());

        vm.AddElementCommand.Execute(DraftElementKind.Divider);
        vm.AddElementCommand.Execute(DraftElementKind.Field);

        Assert.True(vm.HasUnsavedChanges);

        vm.DiscardCommand.Execute(null);

        Assert.Equal(anfang, Text(vm.Draft.ToDefinition()));
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.CanUndo);
    }

    // --- Prüfung ---

    /// <summary>
    /// <b>Ein Befund sperrt das Speichern.</b> Eine Karte mit kaputtem
    /// Ausdruck würde beim Laden auf die mitgelieferte zurückfallen — der
    /// Benutzer sähe seine Arbeit nicht und wüsste nicht, warum.
    /// </summary>
    [Fact]
    public void Ein_kaputter_Ausdruck_sperrt_das_Speichern()
    {
        var (vm, store) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Text);
        vm.SelectedElement!.Value.Mode = DraftValueMode.Expression;
        vm.SelectedElement.Value.Text = "coalesce(( kaputt";
        vm.Refresh();

        Assert.False(vm.CanSave);
        Assert.NotEmpty(vm.Problems);

        vm.SaveCommand.Execute(null);

        Assert.Empty(store.Current.Cards);
    }

    [Fact]
    public void Eine_leere_Kennung_sperrt_das_Speichern_und_sagt_warum()
    {
        var (vm, _) = Bauen();

        vm.Draft.Id = "  ";
        vm.Refresh();

        Assert.False(vm.CanSave);
        Assert.Contains(vm.Problems, p => p.Contains("Kennung", StringComparison.Ordinal));

        vm.Draft.Id = "meine karte";
        vm.Refresh();

        Assert.False(vm.CanSave);

        vm.Draft.Id = "meine-karte";
        vm.Refresh();

        Assert.True(vm.CanSave);
    }

    /// <summary>
    /// Der Deckel des Toasts ist im Designer sichtbar — nicht erst beim
    /// Speichern. Eine vierte Zeile wäre nicht abgeschnitten, sondern weg.
    /// </summary>
    [Fact]
    public void Beim_Toast_wird_die_vierte_Textzeile_gemeldet()
    {
        var (vm, _) = Bauen(CardKind.Toast);

        // Der Anfangsentwurf bringt schon drei Zeilen mit (K5) — die vierte
        // ist die, die Windows nicht mehr zeigt.
        Assert.Equal(3, vm.TextRowCount);
        Assert.True(vm.CanSave);

        vm.AddElementCommand.Execute(DraftElementKind.Text);

        Assert.Equal(4, vm.TextRowCount);
        Assert.False(vm.CanSave);
        Assert.Contains(vm.Problems, p => p.Contains("Textzeilen", StringComparison.Ordinal));
        Assert.Contains("drei Textzeilen", vm.KindNote, StringComparison.Ordinal);
    }

    // --- Speichern ---

    [Fact]
    public void Eine_gespeicherte_Karte_gilt_danach()
    {
        var (vm, store) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Divider);
        vm.SaveCommand.Execute(null);

        var karte = Assert.Single(store.Current.Cards);

        Assert.Equal(CardKind.ActiveExpanded, karte.Kind);
        Assert.False(vm.HasUnsavedChanges);

        using var aufloeser = new CardResolver(store, NullLogger<CardResolver>.Instance);

        Assert.True(aufloeser.IsCustom(CardKind.ActiveExpanded));
    }

    [Fact]
    public void Eine_gespeicherte_Karte_ersetzt_nur_ihre_eigene_Art()
    {
        var (vm, store) = Bauen(CardKind.IncomingCompact, config: new IntegrationConfig
        {
            Cards =
            [
                new CardDefinition("bestehend", "Bestehend", CardKind.ActiveExpanded,
                    [new CardSection("a", null, [new CardRow([new CardColumn(6, [new CardText("'x'")])])])]),
            ],
        });

        vm.AddElementCommand.Execute(DraftElementKind.Divider);
        vm.SaveCommand.Execute(null);

        Assert.Equal(2, store.Current.Cards.Count);
        Assert.Contains(store.Current.Cards, k => k.Id == "bestehend");
        Assert.Contains(store.Current.Cards, k => k.Kind == CardKind.IncomingCompact);
    }

    /// <summary>
    /// Zurücksetzen <b>entfernt</b> die eigene Karte, statt eine Kopie der
    /// mitgelieferten zu speichern: sonst kommt eine Verbesserung in nipp
    /// nicht mit.
    /// </summary>
    [Fact]
    public void Zuruecksetzen_entfernt_die_eigene_Karte()
    {
        var (vm, store) = Bauen();

        vm.AddElementCommand.Execute(DraftElementKind.Divider);
        vm.SaveCommand.Execute(null);

        Assert.Single(store.Current.Cards);

        vm.ResetToDefaultCommand.Execute(null);

        Assert.Empty(store.Current.Cards);
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.CanUndo);
        Assert.Equal(
            Text(DefaultCards.ActiveCall),
            Text(vm.Draft.ToDefinition() with { Id = DefaultCards.ActiveCall.Id }));
    }

    // --- Vorschau ---

    /// <summary>
    /// <b>Die Vorschau zeigt Werte, nicht Ausdrücke.</b> Und sie kommen durch
    /// dasselbe Mapping wie im Betrieb — eine Vorschau, die ihre Werte anders
    /// gewinnt als der Anruf, ist keine.
    /// </summary>
    [Fact]
    public void Die_Vorschau_zeigt_die_Beispieldaten_der_Vorlage()
    {
        var (vm, _) = Bauen(config: new IntegrationConfig
        {
            DataSources = [Bibliothek().Find("musterkontor")!.Source with { Enabled = true }],
        });

        var texte = vm.Preview.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Where(static t => t.Visible)
            .Select(static t => t.Text)
            .ToList();

        Assert.Contains("Hans Muster", texte);
        Assert.Contains("Beispieldaten", vm.PreviewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_echter_Testabruf_steht_in_der_Vorschau_und_wird_benannt()
    {
        var proben = new TestSampleStore(Bibliothek());
        proben.SetFromLiveCall("kontor", """{ "name": "Echt Gemessen" }""");

        var (vm, _) = Bauen(
            config: new IntegrationConfig
            {
                DataSources = [Bibliothek().Find("musterkontor")!.Source with { Enabled = true }],
            },
            proben: proben);

        var texte = vm.Preview.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Select(static t => t.Text)
            .ToList();

        Assert.Contains("Echt Gemessen", texte);
        Assert.Contains("Testabruf", vm.PreviewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Quelle_sagt_die_Vorschau_warum_sie_leer_ist()
    {
        var (vm, _) = Bauen();

        Assert.Contains("Keine Quelle eingerichtet", vm.PreviewSource, StringComparison.Ordinal);

        // Und trotzdem steht die Nummer da — die Rückfallebene der
        // mitgelieferten Karte greift immer.
        var texte = vm.Preview.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Where(static t => t.Visible)
            .Select(static t => t.Text)
            .ToList();

        Assert.Contains("+41 79 123 45 67", texte);
    }

    [Fact]
    public void Die_Vorschau_folgt_jeder_Aenderung()
    {
        var (vm, _) = Bauen(CardKind.IncomingCompact);

        vm.AddElementCommand.Execute(DraftElementKind.Text);
        vm.SelectedElement!.Value.Mode = DraftValueMode.Literal;
        vm.SelectedElement.Value.Text = "Neu dazu";
        vm.Refresh();

        var texte = vm.Preview.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Select(static t => t.Text)
            .ToList();

        Assert.Contains("Neu dazu", texte);
    }

    // --- Die Nummer, mit der die Vorschau rechnet ---

    /// <summary>
    /// <b>Die Vorschau folgt der eingegebenen Nummer.</b> Sie hing fest an der
    /// Beispielnummer, obwohl <c>BuildSnapshot</c> sie seit jeher als Parameter
    /// nimmt — wer sehen wollte, wie seine Karte bei einer bestimmten Nummer
    /// aussieht, konnte es nicht.
    /// </summary>
    [Fact]
    public void Die_Vorschau_rechnet_mit_der_eingegebenen_Nummer()
    {
        var (vm, _) = Bauen();

        vm.PreviewNumber = "0447654321";

        var texte = vm.Preview.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Where(static t => t.Visible)
            .Select(static t => t.Text)
            .ToList();

        Assert.Contains("+41 44 765 43 21", texte);
        Assert.DoesNotContain("+41 79 123 45 67", texte);
    }

    /// <summary>
    /// Ein leeres Feld ist keine Nummer, sondern „keine Angabe" — dann gilt
    /// wieder die Beispielnummer. Eine leere Vorschau wäre die schlechtere
    /// Auskunft.
    /// </summary>
    [Fact]
    public void Ohne_Eingabe_gilt_wieder_die_Beispielnummer()
    {
        var (vm, _) = Bauen();

        vm.PreviewNumber = "0447654321";
        vm.PreviewNumber = string.Empty;

        var texte = vm.Preview.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Where(static t => t.Visible)
            .Select(static t => t.Text)
            .ToList();

        Assert.Contains("+41 79 123 45 67", texte);
    }

    /// <summary>
    /// <b>Ohne Abrufdienst ist der Knopf aus, und der Befehl tut nichts.</b>
    /// So laufen diese Tests ohne Netz — und so verhält sich der Designer,
    /// solange keine Quelle mit Anruferkontext eingerichtet ist.
    /// </summary>
    [Fact]
    public async Task Ohne_Dienst_ist_kein_Abruf_moeglich_und_der_Entwurf_bleibt()
    {
        var (vm, _) = Bauen();

        var vorher = Text(vm.Draft.ToDefinition());
        var vorschau = vm.Preview;

        Assert.False(vm.CanLookup);

        await vm.RunLookupCommand.ExecuteAsync(null);

        Assert.Equal(vorher, Text(vm.Draft.ToDefinition()));
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.IsLookingUp);
        Assert.Same(vorschau, vm.Preview);
    }

    // --- Eine leere Karte ---

    /// <summary>
    /// Eine leere Karte hat eine Zeile, in die sich etwas legen lässt — eine
    /// Karte ohne Zeile hätte keine Stelle dafür, und der Designer wäre eine
    /// Fläche, auf der nichts geht.
    ///
    /// <b>Der Toast ist die Ausnahme</b> und bringt drei Zeilen mit (K5): er
    /// hat genau drei Plätze, und die leer vorzufinden wäre eine Aufgabe statt
    /// eines Anfangs.
    /// </summary>
    [Fact]
    public void Eine_leere_Karte_hat_eine_Zeile_zum_Hineinlegen()
    {
        foreach (var art in new[] { CardKind.ActiveExpanded, CardKind.IncomingCompact })
        {
            var entwurf = CardDraft.Empty(art);

            Assert.Single(entwurf.Sections);
            Assert.Single(entwurf.Sections[0].Rows);
            Assert.Single(entwurf.Sections[0].Rows[0].Columns);
            Assert.Equal(art, entwurf.Kind);
            Assert.True(entwurf.HasUsableId);
        }

        var toast = CardDraft.Empty(CardKind.Toast);

        Assert.Equal(3, toast.Sections[0].Rows.Count);
        Assert.True(toast.HasUsableId);
    }

    public void Dispose()
    {
        foreach (var einer in _wegwerfen)
        {
            einer.Dispose();
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
