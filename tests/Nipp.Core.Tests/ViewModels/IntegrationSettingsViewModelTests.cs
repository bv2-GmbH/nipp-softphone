using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Http;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Die Verwaltung der Integrationen in den Einstellungen (K3, §21.4).
///
/// <para><b>Was hier auf dem Spiel steht.</b> Vorher standen in dieser Seite
/// drei Geschwister-Aufklapper, die sich alle auf „die ausgewählte Quelle"
/// bezogen — und nur einer benutzte die Auswahl. Der Zugangsdaten-Block hatte
/// ein freies Textfeld für den Verweis, und der Testabruf zeigte ein Feld
/// „Suchbegriff", das für jede Quelle mit <c>lookupByPhone</c> wirkungslos
/// war. Diese Tests halten fest, dass das Detail wirklich an der Auswahl
/// hängt.</para>
/// </summary>
public sealed class IntegrationSettingsViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private readonly List<IDisposable> _wegwerfen = [];

    private SecretStore Ablage => new(
        NullLogger<SecretStore>.Instance,
        Path.Combine(_directory, "secrets.dat"));

    private IntegrationConfigStore Speicher(IntegrationSecrets geheimnisse) =>
        new(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));

    private SettingsService Einstellungen() =>
        new(
            Ablage,
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

    /// <summary>
    /// Baut das ViewModel mit einem gemeinsamen Speicher — so, wie es im
    /// Container entsteht.
    /// </summary>
    private (IntegrationSettingsViewModel Vm, IntegrationConfigStore Store, IntegrationSecrets Secrets) Bauen()
    {
        var geheimnisse = new IntegrationSecrets(Ablage);
        var speicher = Speicher(geheimnisse);

        speicher.Load();

        var karten = new CardResolver(speicher, NullLogger<CardResolver>.Instance);

        var http = new IntegrationHttpClient(
            geheimnisse,
            NullLogger<IntegrationHttpClient>.Instance,
            new StubHandler());

        // Die Anbietervorlagen: die mitgelieferte und die synthetischen aus
        // TestTemplates (ADR-040). Damit laeuft dieser Test erstmals ueber
        // IMPORTIERTE Vorlagen — vorher galten die Zusagen nur fuer Dateien,
        // die wir selbst geschrieben haben.
        var ordner = Path.Combine(_directory, "connectors");

        Directory.CreateDirectory(ordner);

        foreach (var datei in TestTemplates.Files)
        {
            File.Copy(datei, Path.Combine(ordner, Path.GetFileName(datei)), overwrite: true);
        }

        var bibliothek = new ConnectorLibrary(NullLogger<ConnectorLibrary>.Instance, ordner);
        bibliothek.Reload();

        var vm = new IntegrationSettingsViewModel(
            speicher,
            geheimnisse,
            new IntegrationTester(http),
            Einstellungen(),
            new TestSampleStore(bibliothek),
            karten,
            bibliothek);

        _wegwerfen.Add(vm);
        _wegwerfen.Add(karten);
        _wegwerfen.Add(http);

        return (vm, speicher, geheimnisse);
    }

    /// <summary>Ein Handler, der nie gefragt wird — hier läuft kein Testabruf.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("In diesem Test wird nichts abgerufen.");
    }

    // --- Katalog und Hinzufügen ---

    [Fact]
    public void Der_Katalog_steht_im_ViewModel()
    {
        var (vm, _, _) = Bauen();

        Assert.NotEmpty(vm.Catalog);
        Assert.Contains(vm.Catalog, r => r.Id == "musterkontor" && r.IsImported);
        Assert.Contains(vm.Catalog, r => r.Id == "custom-rest" && !r.IsImported);

        // Die Herkunft steht an der Zeile, und der Hersteller dahinter — die
        // Datei selbst darf nicht behaupten, mitgeliefert zu sein (ADR-040).
        Assert.Equal(
            "importiert · Muster Systems AG",
            vm.Catalog.Single(r => r.Id == "gespraechsjournal").VendorNote);
        Assert.Empty(vm.Catalog.Single(r => r.Id == "custom-rest").VendorNote);
    }

    /// <summary>
    /// <b>Der Test, der B2 und B4 zusammen schliesst:</b> zwei Quellen über
    /// den Katalog hinzufügen, und beide stehen da — ohne dass jemand eine
    /// Datei anfassen musste.
    /// </summary>
    [Fact]
    public void Zwei_Quellen_aus_dem_Katalog_stehen_beide_da()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        Assert.Equal(2, vm.Sources.Count);
        Assert.Contains(vm.Sources, s => s.Id == "kontor");
        Assert.Contains(vm.Sources, s => s.Id == "journal");
    }

    [Fact]
    public void Eine_hinzugefuegte_Quelle_ist_ausgewaehlt_und_noch_aus()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        Assert.NotNull(vm.Selected);
        Assert.Equal("kontor", vm.Selected!.Id);
        Assert.False(vm.Selected.IsEnabled);
        Assert.Contains("noch aus", vm.DetailFeedback, StringComparison.Ordinal);
    }

    /// <summary>
    /// Die Meldung nach dem Hinzufügen sagt den <b>nächsten Schritt</b> und
    /// nennt ihn beim Namen — §15. „Angelegt." allein liesse jemanden im
    /// Zweifel, warum nichts passiert.
    /// </summary>
    [Fact]
    public void Nach_dem_Hinzufuegen_steht_der_naechste_Schritt_da()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        Assert.Contains("API-Token", vm.DetailFeedback, StringComparison.Ordinal);
        Assert.Contains("testen", vm.DetailFeedback, StringComparison.Ordinal);
    }

    [Fact]
    public void Dieselbe_Vorlage_zweimal_ergibt_zwei_Quellen()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("musterkontor");

        Assert.Equal(2, vm.Sources.Count);
        Assert.Contains(vm.Sources, s => s.Id == "kontor");
        Assert.Contains(vm.Sources, s => s.Id == "kontor-2");
    }

    [Fact]
    public void Eine_unbekannte_Vorlage_tut_nichts()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("gibtEsNicht");
        vm.AddFromCatalogCommand.Execute(null);

        Assert.Empty(vm.Sources);
    }

    // --- Das Detail hängt an der Auswahl ---

    /// <summary>
    /// <b>B3.</b> Vorher war der Verweis ein freies Textfeld mit dem
    /// Platzhalter <c>crm.apiKey</c>, und die Auswahl änderte daran nichts.
    /// </summary>
    [Fact]
    public void Die_Zugangsdaten_gehoeren_zur_ausgewaehlten_Quelle()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        vm.Selected = vm.Sources.Single(s => s.Id == "kontor");

        var zeile = Assert.Single(vm.Secrets);

        Assert.Equal("kontor", zeile.Reference);
        Assert.Equal("API-Token", zeile.Label);
        Assert.False(zeile.IsConfigured);
        Assert.True(zeile.HasHint);

        vm.Selected = vm.Sources.Single(s => s.Id == "journal");

        Assert.Equal("journal", Assert.Single(vm.Secrets).Reference);
    }

    [Fact]
    public void Das_Detail_zeigt_die_Verbindung_der_ausgewaehlten_Quelle()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        Assert.Equal("Musterkontor", vm.DetailDisplayName);
        Assert.Equal("https://api.musterkontor.example/v1", vm.DetailBaseUrl);
        Assert.Equal("1500", vm.DetailTimeoutMs);

        // Die Vorlage verlangt „Token" — genau der Punkt, an dem beim Anbinden
        // Zeit verlorenging, weil der Server bei 401 „Bearer" meldet.
        Assert.Equal("Token", vm.DetailScheme);
    }

    /// <summary>
    /// <b>B5.</b> Das Feld „Suchbegriff" stand immer da, benutzt wurde es
    /// nie, sobald eine Quelle <c>lookupByPhone</c> hatte.
    /// </summary>
    [Fact]
    public void Die_Testfelder_richten_sich_nach_den_Faehigkeiten()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        // Das Journal kann nur Anruferkontext.
        Assert.True(vm.SelectedHasLookup);
        Assert.False(vm.SelectedHasSearch);

        vm.AddFromCatalogCommand.Execute("musterkontor");

        // Musterkontor kann beides.
        Assert.True(vm.SelectedHasLookup);
        Assert.True(vm.SelectedHasSearch);
    }

    [Fact]
    public void Ohne_Auswahl_ist_das_Detail_leer()
    {
        var (vm, _, _) = Bauen();

        Assert.False(vm.HasSelection);
        Assert.Empty(vm.DetailBaseUrl);
        Assert.Empty(vm.DetailJson);
        Assert.Empty(vm.Secrets);
    }

    // --- Übernehmen ---

    [Fact]
    public void Die_Verbindung_laesst_sich_uebernehmen()
    {
        var (vm, store, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("custom-rest");

        vm.DetailDisplayName = "Mein ERP";
        vm.DetailBaseUrl = "https://erp.example.ch/api/v2";
        vm.DetailTimeoutMs = "2500";
        vm.DetailPriority = "30";
        vm.DetailScheme = "Token";

        vm.ApplyDetailCommand.Execute(null);

        var quelle = store.Current.DataSources.Single();

        Assert.Equal("Mein ERP", quelle.DisplayName);
        Assert.Equal("https://erp.example.ch/api/v2", quelle.Http!.BaseUrl);
        Assert.Equal(2500, quelle.Http.TimeoutMs);
        Assert.Equal(30, quelle.Priority);
        Assert.Equal("Token", quelle.Http.Auth.Scheme);
        Assert.Equal("Übernommen.", vm.DetailFeedback);
    }

    /// <summary>
    /// Und die Endpunkte bleiben dabei stehen. Sie stehen nicht im Formular,
    /// also darf ein Übernehmen sie nicht anfassen.
    /// </summary>
    [Fact]
    public void Uebernehmen_laesst_Endpunkte_und_Mapping_unberuehrt()
    {
        var (vm, store, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        var vorher = store.Current.DataSources.Single().LookupByPhone!.Mapping.Count;

        vm.DetailDisplayName = "Anders";
        vm.ApplyDetailCommand.Execute(null);

        var nachher = store.Current.DataSources.Single().LookupByPhone!;

        Assert.Equal(vorher, nachher.Mapping.Count);
        Assert.Equal("/contacts/by-phone/", nachher.Request.Path);
    }

    [Fact]
    public void Eine_Zeitgrenze_die_keine_Zahl_ist_wird_gemeldet_und_nichts_gespeichert()
    {
        var (vm, store, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("custom-rest");

        vm.DetailTimeoutMs = "zwei Sekunden";
        vm.ApplyDetailCommand.Execute(null);

        Assert.Contains("keine Zahl", vm.DetailFeedback, StringComparison.Ordinal);
        Assert.Equal(1500, store.Current.DataSources.Single().Http!.TimeoutMs);
    }

    /// <summary>
    /// Eine leere Adresse ist ein Befund der Prüfung — und der steht dann in
    /// der Rückmeldung, nicht ein stilles „Übernommen" (§15).
    /// </summary>
    [Fact]
    public void Eine_unbrauchbare_Adresse_wird_nach_dem_Uebernehmen_gemeldet()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("custom-rest");

        vm.DetailBaseUrl = "ftp://irgendwo.example.ch";
        vm.ApplyDetailCommand.Execute(null);

        Assert.NotEqual("Übernommen.", vm.DetailFeedback);
        Assert.NotEmpty(vm.DetailFeedback);
    }

    // --- JSON je Quelle ---

    [Fact]
    public void Das_Json_zeigt_nur_diese_Quelle()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        vm.Selected = vm.Sources.Single(s => s.Id == "kontor");

        Assert.Contains("\"kontor\"", vm.DetailJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"journal\"", vm.DetailJson, StringComparison.Ordinal);

        // Und keine globalen Einstellungen — das ist die Quelle, nicht die Datei.
        Assert.DoesNotContain("callerLookup", vm.DetailJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Eine kaputte Bearbeitung geht nur auf Kosten dieser Quelle.</b>
    /// Vorher musste die ganze Datei durch den Editor, und das Einlesen
    /// ersetzte alles — auf diesem Weg ist am 07.09.2026 eine eingerichtete Quelle verschwunden.
    /// </summary>
    [Fact]
    public void Ein_kaputtes_Json_aendert_nichts_und_meldet_die_Stelle()
    {
        var (vm, store, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        vm.Selected = vm.Sources.Single(s => s.Id == "kontor");
        vm.DetailJson = "{ das ist kein JSON";
        vm.ApplyDetailJsonCommand.Execute(null);

        Assert.Contains("kein gültiges JSON", vm.DetailFeedback, StringComparison.Ordinal);
        Assert.Equal(2, store.Current.DataSources.Count);
    }

    [Fact]
    public void Ein_bearbeitetes_Json_wird_uebernommen()
    {
        var (vm, store, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("custom-rest");

        vm.DetailJson = vm.DetailJson.Replace(
            "\"path\": \"/contacts/by-phone\"",
            "\"path\": \"/v2/lookup\"",
            StringComparison.Ordinal);

        vm.ApplyDetailJsonCommand.Execute(null);

        Assert.Equal(
            "/v2/lookup",
            store.Current.DataSources.Single().LookupByPhone!.Request.Path);
    }

    [Fact]
    public void Ein_Json_ohne_Kennung_wird_gemeldet()
    {
        var (vm, store, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("custom-rest");

        vm.DetailJson = """{ "displayName": "Ohne Kennung" }""";
        vm.ApplyDetailJsonCommand.Execute(null);

        Assert.NotEmpty(vm.DetailFeedback);
        Assert.Single(store.Current.DataSources);
    }

    // --- Zugangsdaten ---

    [Fact]
    public void Ein_abgelegtes_Geheimnis_verschwindet_aus_dem_ViewModel()
    {
        var (vm, _, geheimnisse) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        var zeile = vm.Secrets.Single();
        zeile.Value = "geheim-12345";

        vm.SaveSecretCommand.Execute(zeile);

        Assert.Empty(zeile.Value);
        Assert.True(geheimnisse.IsConfigured("kontor"));
    }

    /// <summary>
    /// Und die Liste zeigt danach nicht mehr „Zugangsdaten fehlen" — genau
    /// diese Zeile ist der Hinweis, den jemand sucht, wenn eine Quelle
    /// schweigt.
    /// </summary>
    [Fact]
    public void Nach_dem_Ablegen_meldet_die_Liste_keine_fehlenden_Zugangsdaten()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        Assert.True(vm.Sources.Single().NeedsAttention);

        var zeile = vm.Secrets.Single();
        zeile.Value = "geheim-12345";
        vm.SaveSecretCommand.Execute(zeile);

        Assert.False(vm.Sources.Single().NeedsAttention);
        Assert.Contains("hinterlegt", vm.DetailFeedback, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_leeres_Geheimnis_wird_nicht_abgelegt()
    {
        var (vm, _, geheimnisse) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.SaveSecretCommand.Execute(vm.Secrets.Single());

        Assert.False(geheimnisse.IsConfigured("kontor"));
    }

    // --- Entfernen ---

    /// <summary>
    /// Die Zugangsdaten bleiben liegen. Ein Token wird nur einmal ausgegeben;
    /// wer es wiederbeschaffen muss, braucht einen Administrator im
    /// Zielsystem.
    /// </summary>
    [Fact]
    public void Entfernen_laesst_die_Zugangsdaten_liegen()
    {
        var (vm, _, geheimnisse) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");

        var zeile = vm.Secrets.Single();
        zeile.Value = "geheim-12345";
        vm.SaveSecretCommand.Execute(zeile);

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.Sources);
        Assert.True(geheimnisse.IsConfigured("kontor"));
        Assert.Contains("bleiben", vm.DetailFeedback, StringComparison.Ordinal);
    }

    [Fact]
    public void Entfernen_ohne_Auswahl_tut_nichts()
    {
        var (vm, _, _) = Bauen();

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.Sources);
    }

    // --- Nachschlage-Einstellungen ---

    /// <summary>
    /// Diese vier standen bisher nur in der Datei. Wer sie ändern wollte,
    /// musste die Konfiguration ausgeben, von Hand bearbeiten und einlesen —
    /// für vier Schalter.
    /// </summary>
    [Fact]
    public void Die_Nachschlage_Einstellungen_lassen_sich_schalten()
    {
        var (vm, store, _) = Bauen();

        vm.LookupOutgoing = false;
        vm.LookupInternalNumbers = true;
        vm.ApplyLookupSettingsCommand.Execute(null);

        Assert.False(store.Current.CallerLookup.LookupOutgoing);
        Assert.True(store.Current.CallerLookup.LookupInternalNumbers);
        Assert.True(store.Current.CallerLookup.LookupIncoming);
    }

    /// <summary>
    /// Und sie kommen beim Neuaufbau zurück, ohne dabei ein Speichern
    /// auszulösen — sonst schriebe jedes Laden die Datei neu.
    /// </summary>
    [Fact]
    public void Die_Einstellungen_kommen_aus_der_Datei_zurueck()
    {
        var (vm, store, _) = Bauen();

        store.Save(store.Current with
        {
            CallerLookup = store.Current.CallerLookup with
            {
                LookupIncoming = false,
                LookupInternalNumbers = true,
            },
        });

        Assert.False(vm.LookupIncoming);
        Assert.True(vm.LookupInternalNumbers);
    }

    // --- Karten in der Übersicht ---

    /// <summary>
    /// Alle vier Arten stehen in der Übersicht, auch die ohne Karte.
    ///
    /// <para>Vier seit dem 07.09.2026: die Anrufliste ist dazugekommen
    /// (ADR-036). Sie stand als <c>CardKind.History</c> seit I4 im Modell und
    /// war nirgends angeschlossen — dieser Test war die Stelle, an der das
    /// hätte auffallen können, und zählte stattdessen mit.</para>
    /// </summary>
    [Fact]
    public void Die_Kartenuebersicht_nennt_alle_vier_Arten()
    {
        var (vm, _, _) = Bauen();

        Assert.Equal(4, vm.Cards.Count);
        Assert.Contains(vm.Cards, c => c.Kind == CardKind.ActiveExpanded);
        Assert.Contains(vm.Cards, c => c.Kind == CardKind.IncomingCompact);

        // Die Anrufliste hat eine mitgelieferte Karte — anders als der Toast.
        var liste = vm.Cards.Single(c => c.Kind == CardKind.History);

        Assert.NotNull(liste.Definition);
        Assert.False(liste.IsCustom);
        Assert.Equal("Anrufliste", liste.KindName);

        // Der Toast hat ohne eigene Karte keine — dort gilt die
        // Zusammensetzung im Code (K5, ADR-034). Die Zeile muss trotzdem da
        // sein, sonst gibt es keinen Weg zu seinem Designer; und ihre
        // Beschreibung sagt, was gilt, statt „0 Bausteine" zu behaupten.
        var toast = vm.Cards.Single(c => c.Kind == CardKind.Toast);

        Assert.Null(toast.Definition);
        Assert.False(toast.IsCustom);
        Assert.Equal("Benachrichtigung", toast.KindName);
        Assert.Contains("mitgelieferte Zusammensetzung", toast.Summary, StringComparison.Ordinal);

        var gespraech = vm.Cards.Single(c => c.Kind == CardKind.ActiveExpanded);

        Assert.Equal("Gespräch", gespraech.KindName);
        Assert.False(gespraech.IsCustom);
        Assert.Contains("mitgeliefert", gespraech.Summary, StringComparison.Ordinal);
        Assert.True(gespraech.ElementCount > 0);
    }

    [Fact]
    public void Eine_eigene_Karte_wird_als_solche_gezeigt()
    {
        var (vm, store, _) = Bauen();

        store.Save(store.Current with
        {
            Cards =
            [
                new CardDefinition("eigene", "Eigene", CardKind.ActiveExpanded,
                    [new CardSection("a", null, [new CardRow([new CardColumn(6, [new CardText("'x'")])])])]),
            ],
        });

        var gespraech = vm.Cards.Single(c => c.Kind == CardKind.ActiveExpanded);

        Assert.True(gespraech.IsCustom);
        Assert.Contains("eigene Karte", gespraech.Summary, StringComparison.Ordinal);
        Assert.Equal(1, gespraech.ElementCount);
    }

    // --- Ganze Datei ---

    /// <summary>
    /// Vor dem Einlesen soll dastehen, was verlorengeht — genau darüber ist
    /// am 07.09.2026 eine eingerichtete Quelle verschwunden.
    /// </summary>
    [Fact]
    public void Vor_dem_Einlesen_ist_bekannt_wie_viele_Quellen_ersetzt_werden()
    {
        var (vm, _, _) = Bauen();

        Assert.Equal(0, vm.SourcesAtRisk);

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        Assert.Equal(2, vm.SourcesAtRisk);
    }

    [Fact]
    public void Eine_ausgegebene_Datei_laesst_sich_wieder_einlesen()
    {
        var (vm, _, _) = Bauen();

        vm.AddFromCatalogCommand.Execute("musterkontor");
        vm.AddFromCatalogCommand.Execute("gespraechsjournal");

        var text = vm.Export();

        Assert.True(vm.TryImport(text, out var probleme), string.Join(" | ", probleme));
        Assert.Equal(2, vm.Sources.Count);
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
