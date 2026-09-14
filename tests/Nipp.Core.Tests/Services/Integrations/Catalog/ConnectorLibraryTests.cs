using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Catalog;

/// <summary>
/// Die Anbietervorlagen und das Zusammenführen (K2, ADR-033, ADR-040).
///
/// <para><b>Der Befund, um den es ursprünglich ging.</b> Bis zum 07.09.2026
/// entstand eine neue Quelle nur dadurch, dass jemand eine Datei einlas — und
/// das ersetzte die <b>ganze</b> Konfiguration. Eine zweite Quelle ging nur über
/// Handarbeit im JSON, und genau das verhinderte, was §21 verlangt: weitere
/// APIs anbinden.</para>
///
/// <para><b>Was sich mit ADR-040 geändert hat.</b> Der Katalog ist ein Dienst
/// mit eigenem Ablageort statt eines statischen Singletons, und dieselben
/// Prüfungen laufen jetzt <b>erstmals auch über importierte Vorlagen</b> — die
/// hier eingelesenen sind welche. Das ist ein Gewinn: vorher galten die Zusagen
/// nur für Dateien, die wir selbst geschrieben haben.</para>
/// </summary>
public sealed class ConnectorLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Eine Bibliothek mit eigenem Ordner und den beiden synthetischen
    /// Vorlagen darin.
    ///
    /// <b>Eigener Ordner, nicht %APPDATA%:</b> <c>TestIsolationTests</c>
    /// erzwingt das, und der Grund steht im Projekt fest — ein gewöhnliches
    /// <c>dotnet test</c> hat hier schon einmal ein SIP-Konto gelöscht.
    /// </summary>
    private ConnectorLibrary Bibliothek(bool mitVorlagen = true)
    {
        var ordner = Path.Combine(_directory, "connectors");

        if (mitVorlagen)
        {
            Directory.CreateDirectory(ordner);

            foreach (var datei in TestTemplates.Files)
            {
                File.Copy(datei, Path.Combine(ordner, Path.GetFileName(datei)), overwrite: true);
            }
        }

        var bibliothek = new ConnectorLibrary(NullLogger<ConnectorLibrary>.Instance, ordner);
        bibliothek.Reload();

        return bibliothek;
    }

    private IntegrationConfigStore Speicher()
    {
        var geheimnisse = new IntegrationSecrets(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

        return new IntegrationConfigStore(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));
    }

    private static DataSourceDefinition Quelle(string id) => new()
    {
        Id = id,
        DisplayName = id.ToUpperInvariant(),
        Enabled = false,
        Http = new HttpConnection { BaseUrl = $"https://{id}.example.ch/api" },
        LookupByPhone = new LookupByPhoneCapability
        {
            Request = new RequestDefinition { Path = "/contacts" },
            Mapping = { ["contactName"] = new(Path: "$.name") },
        },
    };

    // --- Die Bibliothek selbst ---

    [Fact]
    public void Sie_ist_nicht_leer_und_jede_Vorlage_ist_vollstaendig()
    {
        var bibliothek = Bibliothek();

        Assert.NotEmpty(bibliothek.Templates);

        foreach (var vorlage in bibliothek.Templates)
        {
            Assert.NotEmpty(vorlage.Id);
            Assert.NotEmpty(vorlage.DisplayName);
            Assert.NotEmpty(vorlage.Summary);
            Assert.NotEmpty(vorlage.Source.Id);
            Assert.NotNull(vorlage.Source.Http);
        }
    }

    /// <summary>
    /// <b>Die mitgelieferte Vorlage muss eingebettet sein.</b> Ohne diesen Test
    /// wäre ein fehlender <c>EmbeddedResource</c>-Eintrag im csproj eine
    /// Vorlage, die stillschweigend fehlt — der Katalog zeigt dann einfach
    /// einen Eintrag weniger, ohne Meldung.
    /// </summary>
    [Fact]
    public void Die_mitgelieferte_Vorlage_ist_da_und_als_solche_gekennzeichnet()
    {
        var vorlage = Bibliothek(mitVorlagen: false).Find("custom-rest");

        Assert.NotNull(vorlage);
        Assert.Equal(ConnectorOrigin.BuiltIn, vorlage.Origin);
        Assert.False(vorlage.IsImported);
    }

    [Fact]
    public void Eine_eingelesene_Vorlage_gilt_als_importiert()
    {
        var vorlage = Bibliothek().Find("musterkontor");

        Assert.NotNull(vorlage);
        Assert.Equal(ConnectorOrigin.Imported, vorlage.Origin);
        Assert.True(vorlage.IsImported);
        Assert.Equal("Muster Systems AG", vorlage.Vendor);
    }

    /// <summary>
    /// <b>Eine Vorlage wird nie eingeschaltet ausgeliefert.</b> Sonst fragt
    /// nipp beim ersten Anruf eine Adresse, die noch niemand gesehen hat. Seit
    /// ADR-040 normalisiert der Leser darauf — die Regel gilt damit auch für
    /// eine fremde Datei, die eingeschaltet hereinkommt.
    /// </summary>
    [Fact]
    public void Jede_Vorlage_ist_abgeschaltet()
    {
        Assert.All(
            Bibliothek().Templates,
            t => Assert.False(
                t.Source.Enabled,
                $"Die Vorlage '{t.Id}' ist eingeschaltet. Eingeschaltet wird nach dem "
                    + "Testabruf, nicht davor."));
    }

    [Fact]
    public void Jede_Vorlage_besteht_die_Pruefung()
    {
        foreach (var vorlage in Bibliothek().Templates)
        {
            var befunde = new IntegrationConfigValidator(
                    new IntegrationSecrets(new SecretStore(
                        NullLogger<SecretStore>.Instance,
                        Path.Combine(_directory, "secrets.dat"))))
                .ValidateSource(vorlage.Source)
                .Where(static b => b.Severity == IssueSeverity.Error)
                .ToList();

            Assert.True(
                befunde.Count == 0,
                $"{vorlage.Id}: {string.Join(" | ", befunde.Select(static b => b.ToString()))}");
        }
    }

    /// <summary>
    /// <b>Jedes Geheimnis braucht Beschriftung und Herkunft.</b> Vorher war das
    /// Eingabefeld freier Text mit einem technischen Platzhalter, und wer ein
    /// Token eintragen wollte, musste vorher im JSON nachlesen, wie der Verweis
    /// heisst.
    /// </summary>
    [Fact]
    public void Jedes_Geheimnis_einer_Vorlage_hat_Beschriftung_und_Herkunft()
    {
        foreach (var vorlage in Bibliothek().Templates)
        {
            var verweise = vorlage.Source.Http?.Auth.SecretRefs ?? [];

            Assert.Equal(verweise.Count, vorlage.Secrets.Count);

            foreach (var verweis in verweise)
            {
                var eintrag = vorlage.Secrets.SingleOrDefault(s => s.Ref == verweis);

                Assert.NotNull(eintrag);
                Assert.NotEmpty(eintrag.Label);
                Assert.False(
                    string.IsNullOrWhiteSpace(eintrag.Hint),
                    $"{vorlage.Id}: '{verweis}' sagt nicht, woher das Token kommt. Genau das "
                        + "ist der Schritt ausserhalb von nipp, bei dem eine Anleitung hilft.");
            }
        }
    }

    /// <summary>
    /// In einer Vorlage steht nie ein Geheimnis, sondern nur sein Verweis.
    /// <b>Seit ADR-040 ist das ein Ablehnungsgrund im Leser</b> und nicht mehr
    /// nur eine Aussage über Dateien, die wir selbst schreiben.
    /// </summary>
    [Fact]
    public void In_keiner_Vorlage_steht_ein_Geheimnis()
    {
        foreach (var vorlage in Bibliothek().Templates)
        {
            var auth = vorlage.Source.Http?.Auth;

            if (auth is null || auth.Type == AuthKind.None)
            {
                continue;
            }

            foreach (var verweis in auth.SecretRefs)
            {
                Assert.True(verweis.Length < 40, $"'{verweis}' sieht wie ein Wert aus, nicht wie ein Verweis.");
                Assert.DoesNotContain(' ', verweis);
            }
        }
    }

    /// <summary>
    /// Die mitgelieferte Vorlage steht oben. Ein Katalog, der mit fremden
    /// Firmennamen beginnt, sieht aus wie ein Produkt für jemand anderen.
    /// </summary>
    [Fact]
    public void Die_mitgelieferte_Vorlage_steht_zuoberst()
    {
        Assert.Equal(ConnectorOrigin.BuiltIn, Bibliothek().Templates[0].Origin);
    }

    /// <summary>
    /// Die Beispielantwort muss durch das Mapping der Vorlage <b>Felder
    /// ergeben</b>. Eine Beispielantwort, die zum eigenen Mapping nicht passt,
    /// zeigt im Designer eine leere Karte — und dann richtet jemand seine Karte
    /// auf Felder aus, die er für kaputt hält.
    /// </summary>
    [Theory]
    [InlineData("musterkontor")]
    [InlineData("gespraechsjournal")]
    public void Die_Beispielantwort_ergibt_Felder(string id)
    {
        var bibliothek = Bibliothek();
        var vorlage = bibliothek.Find(id)!;

        Assert.NotNull(vorlage.SampleResponse);

        var speicher = Speicher();
        speicher.AddOrReplaceSource(vorlage.Source);

        var schnappschuss = new TestSampleStore(bibliothek).BuildSnapshot(
            speicher.Current,
            TestSampleStore.DefaultPreviewNumber);

        var beitrag = schnappschuss.Sources[vorlage.Source.Id];

        Assert.Equal(Nipp.Core.Services.Integrations.Context.SourceState.Success, beitrag.State);
        Assert.NotEmpty(beitrag.Fields);
    }

    /// <summary>
    /// Und die Beispielantworten sind <b>erfunden</b>. Eine echte Antwort ist
    /// die Kundenkarte eines echten Anrufers; §21.2 verbietet einen Cache auf
    /// der Platte, und eine mitgeschnittene Beispielantwort wäre genau das, nur
    /// dauerhaft.
    /// </summary>
    [Fact]
    public void Die_Beispielantworten_sind_als_erfunden_gekennzeichnet()
    {
        foreach (var vorlage in Bibliothek().Templates.Where(static t => t.SampleResponse is not null))
        {
            var hinweis = vorlage.SampleResponse!["_hinweis"]?.GetValue<string>();

            Assert.False(
                string.IsNullOrWhiteSpace(hinweis),
                $"{vorlage.Id}: die Beispielantwort sagt nicht, dass sie erfunden ist.");

            Assert.Contains("ERFUNDEN", hinweis, StringComparison.Ordinal);
        }
    }

    // --- Zusammenführen ---

    /// <summary>
    /// <b>Der Test, der B2 schliesst.</b> Zwei Quellen hinzufügen, und beide
    /// stehen danach da.
    /// </summary>
    [Fact]
    public void Eine_zweite_Quelle_hinzufuegen_verliert_die_erste_nicht()
    {
        var bibliothek = Bibliothek();
        var speicher = Speicher();

        speicher.AddOrReplaceSource(bibliothek.Find("musterkontor")!.Source);
        speicher.AddOrReplaceSource(bibliothek.Find("gespraechsjournal")!.Source);

        var kennungen = Speicher().Load().DataSources.Select(static s => s.Id).ToList();

        Assert.Contains("kontor", kennungen);
        Assert.Contains("journal", kennungen);
        Assert.Equal(2, kennungen.Count);
    }

    [Fact]
    public void Dieselbe_Kennung_ersetzt_und_verdoppelt_nicht()
    {
        var speicher = Speicher();

        speicher.AddOrReplaceSource(Quelle("crm"));
        speicher.AddOrReplaceSource(Quelle("crm") with { DisplayName = "Neuer Name" });

        var quellen = Speicher().Load().DataSources;

        Assert.Single(quellen);
        Assert.Equal("Neuer Name", quellen[0].DisplayName);
    }

    /// <summary>
    /// <b>Die globalen Einstellungen bleiben.</b> Wer eine zweite Quelle
    /// hinzufügt, hat seine Wartezeiten längst eingestellt.
    /// </summary>
    [Fact]
    public void Hinzufuegen_setzt_die_eigenen_Einstellungen_nicht_zurueck()
    {
        var bibliothek = Bibliothek();
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig
        {
            CallerLookup = new CallerLookupSettings { LookupOutgoing = false, CacheSeconds = 60 },
            ContactSearch = new ContactSearchSettings { DebounceMs = 750 },
        });

        speicher.AddOrReplaceSource(bibliothek.Find("musterkontor")!.Source);

        var gelesen = Speicher().Load();

        Assert.False(gelesen.CallerLookup.LookupOutgoing);
        Assert.Equal(60, gelesen.CallerLookup.CacheSeconds);
        Assert.Equal(750, gelesen.ContactSearch.DebounceMs);
    }

    [Fact]
    public void Eine_hinzugefuegte_Quelle_kommt_abgeschaltet_herein()
    {
        var speicher = Speicher();

        speicher.AddOrReplaceSource(Bibliothek().Find("musterkontor")!.Source);

        Assert.False(speicher.Current.DataSources[0].Enabled);
        Assert.Empty(speicher.UsableSources);
    }

    /// <summary>
    /// Eine Karte der Vorlage ersetzt die vorhandene ihrer Art — es gilt eine
    /// je Art, und zwei wären ein Befund statt einer stillen Auswahl.
    /// </summary>
    [Fact]
    public void Eine_Karte_der_Vorlage_ersetzt_die_vorhandene_ihrer_Art()
    {
        var speicher = Speicher();

        var alt = new CardDefinition("alt", "Alt", CardKind.ActiveExpanded,
            [new CardSection("a", null, [new CardRow([new CardColumn(6, [new CardText("'alt'")])])])]);

        var neu = new CardDefinition("neu", "Neu", CardKind.ActiveExpanded,
            [new CardSection("a", null, [new CardRow([new CardColumn(6, [new CardText("'neu'")])])])]);

        var andere = new CardDefinition("kompakt", "Kompakt", CardKind.IncomingCompact,
            [new CardSection("a", null, [new CardRow([new CardColumn(6, [new CardText("'k'")])])])]);

        speicher.Save(new IntegrationConfig { Cards = [alt, andere] });
        speicher.AddOrReplaceSource(Quelle("crm"), [neu]);

        var karten = Speicher().Load().Cards;

        Assert.Equal(2, karten.Count);
        Assert.Contains(karten, k => k.Id == "neu");
        Assert.Contains(karten, k => k.Id == "kompakt");
        Assert.DoesNotContain(karten, k => k.Id == "alt");
    }

    [Fact]
    public void Eine_Quelle_entfernen_laesst_die_anderen_stehen()
    {
        var speicher = Speicher();

        speicher.AddOrReplaceSource(Quelle("crm"));
        speicher.AddOrReplaceSource(Quelle("erp"));

        Assert.True(speicher.RemoveSource("crm"));
        Assert.False(speicher.RemoveSource("gibtEsNicht"));

        Assert.Single(speicher.Current.DataSources);
        Assert.Equal("erp", speicher.Current.DataSources[0].Id);
    }

    /// <summary>
    /// Dieselbe Vorlage zweimal — das passiert bei zwei Mandanten desselben
    /// Systems, und dann sollen beide stehen bleiben.
    /// </summary>
    [Fact]
    public void Eine_freie_Kennung_zaehlt_hoch()
    {
        var speicher = Speicher();

        Assert.Equal("kontor", speicher.FreeSourceId("kontor"));

        speicher.AddOrReplaceSource(Quelle("kontor"));

        Assert.Equal("kontor-2", speicher.FreeSourceId("kontor"));

        speicher.AddOrReplaceSource(Quelle("kontor-2"));

        Assert.Equal("kontor-3", speicher.FreeSourceId("kontor"));
    }

    // --- Beschriftungen für eine eingerichtete Quelle ---

    [Fact]
    public void Die_Beschriftung_eines_Geheimnisses_kommt_aus_der_Vorlage()
    {
        var bibliothek = Bibliothek();

        var geheimnisse = bibliothek.SecretsFor(bibliothek.Find("musterkontor")!.Source);

        var eintrag = Assert.Single(geheimnisse);

        Assert.Equal("kontor", eintrag.Ref);
        Assert.Equal("API-Token", eintrag.Label);
        Assert.Contains("Profil", eintrag.Hint!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Für eine von Hand entstandene Quelle gibt es keine Vorlage. Dann wird
    /// die Beschriftung aus der Anmeldeart gebaut — der Verweis selbst ist eine
    /// technische Kennung und gehört nicht als Feldname in ein Formular.
    /// </summary>
    [Fact]
    public void Ohne_Vorlage_wird_die_Beschriftung_aus_der_Anmeldeart_gebaut()
    {
        var quelle = Quelle("eigenes") with
        {
            Http = new HttpConnection
            {
                BaseUrl = "https://eigenes.example.ch",
                Auth = new AuthDefinition
                {
                    Type = AuthKind.Basic,
                    UsernameSecretRef = "eigenes.user",
                    PasswordSecretRef = "eigenes.pass",
                },
            },
        };

        var geheimnisse = Bibliothek().SecretsFor(quelle);

        Assert.Equal(2, geheimnisse.Count);
        Assert.Equal("Benutzername", geheimnisse.Single(s => s.Ref == "eigenes.user").Label);
        Assert.Equal("Passwort", geheimnisse.Single(s => s.Ref == "eigenes.pass").Label);
    }

    [Fact]
    public void Eine_Quelle_ohne_Anmeldung_braucht_kein_Geheimnis()
    {
        var quelle = Quelle("offen") with
        {
            Http = new HttpConnection { BaseUrl = "https://offen.example.ch" },
        };

        var bibliothek = Bibliothek();

        Assert.Empty(bibliothek.SecretsFor(quelle));
        Assert.Empty(bibliothek.SecretsFor(null));
    }

    // --- Testdaten ---

    /// <summary>
    /// Ein echter Abruf schlägt die Beispielantwort — und die Oberfläche kann
    /// den Unterschied nennen. Ohne das hält jemand eine erfundene Zeile für
    /// seine eigenen Daten.
    /// </summary>
    [Fact]
    public void Ein_echter_Abruf_schlaegt_die_Beispielantwort()
    {
        var proben = new TestSampleStore(Bibliothek());

        Assert.NotNull(proben.For("kontor"));
        Assert.False(proben.IsFromLiveCall("kontor"));

        proben.SetFromLiveCall("kontor", """{ "name": "Echt Gemessen" }""");

        Assert.True(proben.IsFromLiveCall("kontor"));
        Assert.Equal("Echt Gemessen", proben.For("kontor")!["name"]!.GetValue<string>());

        proben.Clear();

        Assert.False(proben.IsFromLiveCall("kontor"));
    }

    [Fact]
    public void Eine_Antwort_die_kein_Json_ist_wirft_die_vorhandene_nicht_weg()
    {
        var proben = new TestSampleStore(Bibliothek());

        proben.SetFromLiveCall("kontor", """{ "name": "Gut" }""");
        proben.SetFromLiveCall("kontor", "<html>Fehlerseite</html>");

        Assert.Equal("Gut", proben.For("kontor")!["name"]!.GetValue<string>());
    }

    /// <summary>
    /// Eine Quelle ohne Vorschaudaten wird als „nichts gefunden" gezeigt und
    /// nicht weggelassen — so sieht der Designer, dass sie da ist und ein
    /// Testabruf fehlt.
    /// </summary>
    [Fact]
    public void Eine_Quelle_ohne_Vorschaudaten_erscheint_mit_Begruendung()
    {
        var speicher = Speicher();
        speicher.AddOrReplaceSource(Quelle("unbekannt"));

        var schnappschuss = new TestSampleStore(Bibliothek()).BuildSnapshot(
            speicher.Current,
            TestSampleStore.DefaultPreviewNumber);

        var beitrag = schnappschuss.Sources["unbekannt"];

        Assert.Equal(Nipp.Core.Services.Integrations.Context.SourceState.Empty, beitrag.State);
        Assert.Contains("Testabruf", beitrag.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Schnappschuss der Vorschau trägt die Priorität der Quellen — sonst
    /// zeigte die Vorschau bei zwei Quellen einen anderen Namen als das echte
    /// Gespräch.
    /// </summary>
    [Fact]
    public void Die_Vorschau_haelt_die_Prioritaet_der_Quellen_ein()
    {
        var speicher = Speicher();

        speicher.AddOrReplaceSource(Quelle("spaet") with { Priority = 90 });
        speicher.AddOrReplaceSource(Quelle("frueh") with { Priority = 10 });

        var proben = new TestSampleStore(Bibliothek());
        proben.SetFromLiveCall("spaet", """{ "name": "Spaet" }""");
        proben.SetFromLiveCall("frueh", """{ "name": "Frueh" }""");

        var schnappschuss = proben.BuildSnapshot(
            speicher.Current,
            TestSampleStore.DefaultPreviewNumber);

        Assert.Equal("Frueh", schnappschuss.FieldAcrossSources("contactName").AsText());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
