using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Karten in der Konfiguration (ADR-032, <c>docs/plans/INTEGRATION-PLAN.md</c> D.2).
///
/// <para><b>Was hier auf dem Spiel steht.</b> Bis zum 07.09.2026 hatte
/// <c>IntegrationConfig</c> kein <c>cards</c>, obwohl der Plan die Datei genau
/// so beschreibt und <c>CardLayoutEngine</c> jede Karte übersetzen kann. Eine
/// eigene Karte war damit nicht einzurichten — nicht schwer, sondern
/// unmöglich. Diese Tests halten die Naht fest: die Karte kommt aus der Datei,
/// eine kaputte kostet nichts ausser sich selbst, und die Schreibweise in der
/// Datei ist die, die in jedem Beispiel steht.</para>
/// </summary>
public sealed class CardConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

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

    private static CardDefinition Karte(
        string id = "eigene",
        CardKind art = CardKind.ActiveExpanded,
        string wert = "crm.contactName") =>
        new(
            id,
            "Eigene Karte",
            art,
            [
                new CardSection(
                    "kopf",
                    null,
                    [
                        new CardRow([
                            new CardColumn(CardLayout.Columns, [
                                new CardText(wert) { Style = CardTextStyle.Title },
                            ]),
                        ]),
                    ]),
            ]);

    // --- Schreibweise in der Datei ---

    /// <summary>
    /// <b>Der Test, der in K0 zuerst dran war.</b>
    ///
    /// <c>CardDefinition.Kind</c> trug ein eigenes
    /// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> <b>ohne</b>
    /// Benennungsregel, während der Store seine Optionen mit
    /// <c>JsonNamingPolicy.CamelCase</c> aufsetzte. Ein Attribut an der
    /// Eigenschaft schlägt die Optionen — nipp hätte also
    /// <c>"ActiveExpanded"</c> geschrieben, wo D.2 und jede Vorlage
    /// <c>"activeExpanded"</c> zeigen.
    ///
    /// Aufgefallen wäre das nie von selbst: Enums werden unabhängig von der
    /// Schreibweise gelesen. Erst wer eine ausgegebene Datei neben eine
    /// Vorlage legt, sieht den Unterschied.
    /// </summary>
    [Fact]
    public void Die_Kartenart_steht_kleingeschrieben_in_der_Datei()
    {
        var json = IntegrationConfigStore.Serialize(
            new IntegrationConfig { Cards = [Karte()] });

        Assert.Contains("\"activeExpanded\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ActiveExpanded\"", json, StringComparison.Ordinal);
    }

    /// <summary>Dasselbe für die übrigen Enums der Datei.</summary>
    [Fact]
    public void Auch_Anmeldeart_Textstil_und_Werteart_stehen_kleingeschrieben()
    {
        var json = IntegrationConfigStore.Serialize(new IntegrationConfig
        {
            Cards = [Karte()],
            DataSources =
            [
                new DataSourceDefinition
                {
                    Id = "crm",
                    DisplayName = "CRM",
                    Http = new HttpConnection
                    {
                        BaseUrl = "https://crm.example.ch/api",
                        Auth = new AuthDefinition
                        {
                            Type = AuthKind.Bearer,
                            In = ApiKeyLocation.Header,
                            SecretRef = "crm.token",
                        },
                    },
                },
            ],
        });

        Assert.Contains("\"bearer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"header\"", json, StringComparison.Ordinal);
        Assert.Contains("\"title\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eine von Hand geschriebene Vorlage benutzt Kleinschreibung, eine
    /// ausgegebene Datei ebenfalls — beide müssen gelesen werden.
    /// </summary>
    [Theory]
    [InlineData("activeExpanded")]
    [InlineData("ActiveExpanded")]
    public void Beide_Schreibweisen_werden_gelesen(string art)
    {
        var json = $$"""
            {
              "cards": [
                {
                  "id": "eigene",
                  "name": "Eigene Karte",
                  "kind": "{{art}}",
                  "sections": [
                    { "id": "kopf", "rows": [ { "columns": [ { "span": 6, "elements": [
                      { "type": "text", "value": "crm.contactName", "style": "title" }
                    ] } ] } ] }
                  ]
                }
              ]
            }
            """;

        var gelesen = JsonSerializer.Deserialize<IntegrationConfig>(json, IntegrationJson.Options);

        Assert.NotNull(gelesen);
        Assert.Single(gelesen.Cards);
        Assert.Equal(CardKind.ActiveExpanded, gelesen.Cards[0].Kind);
    }

    /// <summary>
    /// Die abgeleitete Fähigkeitenliste gehört nicht in die Datei: sie sieht
    /// änderbar aus und ist es nicht.
    /// </summary>
    [Fact]
    public void Die_Faehigkeiten_stehen_nicht_in_der_Datei()
    {
        var json = IntegrationConfigStore.Serialize(new IntegrationConfig
        {
            DataSources =
            [
                new DataSourceDefinition
                {
                    Id = "crm",
                    DisplayName = "CRM",
                    Http = new HttpConnection { BaseUrl = "https://crm.example.ch/api" },
                    SearchContacts = new SearchContactsCapability
                    {
                        Request = new RequestDefinition { Path = "/search" },
                    },
                },
            ],
        });

        Assert.DoesNotContain("\"capabilities\"", json, StringComparison.Ordinal);
    }

    // --- Rundlauf ---

    [Fact]
    public void Eine_gespeicherte_Karte_kommt_unveraendert_zurueck()
    {
        Speicher().Save(new IntegrationConfig { Cards = [Karte()] });

        var gelesen = Speicher().Load();

        Assert.Single(gelesen.Cards);
        Assert.Equal("eigene", gelesen.Cards[0].Id);
        Assert.Equal(CardKind.ActiveExpanded, gelesen.Cards[0].Kind);

        var element = Assert.IsType<CardText>(
            gelesen.Cards[0].Sections[0].Rows[0].Columns[0].Elements[0]);

        Assert.Equal("crm.contactName", element.Value);
        Assert.Equal(CardTextStyle.Title, element.Style);
    }

    /// <summary>
    /// Die mitgelieferten Karten sind gewöhnliche Beschreibungen — also müssen
    /// sie den Weg durch die Datei überleben. Wären sie es nicht, fiele erst
    /// beim ersten Kunden auf, was sich nicht beschreiben lässt.
    /// </summary>
    [Fact]
    public void Die_mitgelieferten_Karten_ueberleben_den_Weg_durch_die_Datei()
    {
        Speicher().Save(new IntegrationConfig
        {
            Cards = [DefaultCards.ActiveCall with { Id = "a" }, DefaultCards.Incoming with { Id = "b" }],
        });

        var gelesen = Speicher().Load();

        Assert.Equal(2, gelesen.Cards.Count);

        foreach (var karte in gelesen.Cards)
        {
            Assert.True(
                CardLayoutEngine.TryCompile(karte, out _, out var fehler),
                string.Join(" | ", fehler));
        }
    }

    /// <summary>
    /// Jeder Bausteintyp einmal durch die Datei. Ein Typ, der beim
    /// Deserialisieren verlorengeht, fällt sonst erst auf, wenn ihn jemand im
    /// Designer benutzt und die Karte danach anders aussieht.
    /// </summary>
    [Fact]
    public void Jeder_Bausteintyp_ueberlebt_den_Rundlauf()
    {
        var karte = new CardDefinition(
            "alle",
            "Alle Bausteine",
            CardKind.ActiveExpanded,
            [
                new CardSection(
                    "alles",
                    "Überschrift",
                    [
                        new CardRow([
                            new CardColumn(CardLayout.Columns, [
                                new CardText("crm.contactName") { Style = CardTextStyle.Subtitle, MaxLines = 2 },
                                new CardField("Art", "crm.contactType") { EmptyText = null },
                                new CardBadge("'VIP'") { Tone = "'warning'", VisibleWhen = "crm.vip == true" },
                                new CardDivider(),
                                new CardButton("Öffnen", new OpenUrlAction("https://crm.example.ch/{{crm.id}}")),
                                new CardLink("Website", "https://example.ch"),
                                new CardSourceStatus("crm"),
                            ]),
                        ]),
                    ]),
            ]);

        Speicher().Save(new IntegrationConfig { Cards = [karte] });

        var gelesen = Speicher().Load().Cards[0];
        var bausteine = gelesen.Sections[0].Rows[0].Columns[0].Elements;

        Assert.Equal(7, bausteine.Count);
        Assert.IsType<CardText>(bausteine[0]);
        Assert.IsType<CardField>(bausteine[1]);
        Assert.IsType<CardBadge>(bausteine[2]);
        Assert.IsType<CardDivider>(bausteine[3]);
        Assert.IsType<CardButton>(bausteine[4]);
        Assert.IsType<CardLink>(bausteine[5]);
        Assert.IsType<CardSourceStatus>(bausteine[6]);

        // Die Sonderfälle, an denen ein Rundlauf gern scheitert: null als
        // bedeutungsvoller Wert und eine Aktion mit ihrem Typ.
        Assert.Null(((CardField)bausteine[1]).EmptyText);
        Assert.Equal("crm.vip == true", bausteine[2].VisibleWhen);
        Assert.IsType<OpenUrlAction>(((CardButton)bausteine[4]).Action);
        Assert.Equal(2, ((CardText)bausteine[0]).MaxLines);
    }

    /// <summary>
    /// <b>Die Falle, die dieser Testblock gefunden hat.</b>
    ///
    /// <c>EmptyText = null</c> heisst „die Zeile verschwindet, wenn der Wert
    /// leer ist". Die Datei ignoriert beim Schreiben <c>null</c>, damit sie
    /// kurz bleibt — und die Vorgabe von <c>EmptyText</c> ist nicht
    /// <c>null</c>, sondern <c>"—"</c>. Ohne Gegenmassnahme ändert eine Karte
    /// also beim Speichern ihr Verhalten: aus verschwindenden Zeilen werden
    /// Zeilen mit einem Gedankenstrich. Die mitgelieferten Karten setzen
    /// <c>null</c> sechsmal, es hätte also jede getroffen.
    /// </summary>
    [Fact]
    public void Eine_Zeile_die_verschwinden_soll_ueberlebt_das_Speichern()
    {
        var karte = new CardDefinition(
            "verschwindend",
            "Verschwindend",
            CardKind.ActiveExpanded,
            [
                new CardSection("kopf", null, [
                    new CardRow([
                        new CardColumn(6, [
                            new CardField("Art", "crm.contactType") { EmptyText = null },
                            new CardField("Offen", "erp.openOrders") { EmptyText = "—" },
                        ]),
                    ]),
                ]),
            ]);

        Speicher().Save(new IntegrationConfig { Cards = [karte] });

        var bausteine = Speicher().Load().Cards[0].Sections[0].Rows[0].Columns[0].Elements;

        Assert.Null(((CardField)bausteine[0]).EmptyText);
        Assert.Equal("—", ((CardField)bausteine[1]).EmptyText);
    }

    /// <summary>
    /// Dieselbe Falle an der zweiten Stelle, an der sie steckte: <c>null</c>
    /// heisst „es gilt die Zeitgrenze der Verbindung", die Vorgabe ist 3000.
    /// </summary>
    [Fact]
    public void Eine_Suche_ohne_eigene_Zeitgrenze_behaelt_sie_nicht()
    {
        Speicher().Save(new IntegrationConfig
        {
            DataSources =
            [
                new DataSourceDefinition
                {
                    Id = "crm",
                    DisplayName = "CRM",
                    Http = new HttpConnection { BaseUrl = "https://crm.example.ch/api" },
                    SearchContacts = new SearchContactsCapability
                    {
                        Request = new RequestDefinition { Path = "/search" },
                        Mapping = { ["displayName"] = new(Path: "$.name") },
                        TimeoutMs = null,
                    },
                },
            ],
        });

        Assert.Null(Speicher().Load().DataSources[0].SearchContacts!.TimeoutMs);
    }

    // --- Der Resolver ---

    private static CardResolver Aufloeser(IntegrationConfigStore speicher) =>
        new(speicher, NullLogger<CardResolver>.Instance);

    [Fact]
    public void Ohne_eigene_Karte_gilt_die_mitgelieferte()
    {
        var speicher = Speicher();
        speicher.Load();

        using var aufloeser = Aufloeser(speicher);

        Assert.False(aufloeser.IsCustom(CardKind.ActiveExpanded));
        Assert.Equal(DefaultCards.ActiveCall.Id, aufloeser.DefinitionFor(CardKind.ActiveExpanded)!.Id);
        Assert.Equal(DefaultCards.ActiveCall.Id, aufloeser.For(CardKind.ActiveExpanded).Id);
    }

    [Fact]
    public void Eine_eigene_Karte_schlaegt_die_mitgelieferte()
    {
        var speicher = Speicher();
        speicher.Save(new IntegrationConfig { Cards = [Karte()] });

        using var aufloeser = Aufloeser(speicher);

        Assert.True(aufloeser.IsCustom(CardKind.ActiveExpanded));
        Assert.Equal("eigene", aufloeser.For(CardKind.ActiveExpanded).Id);

        // Und die andere Art bleibt unberührt.
        Assert.False(aufloeser.IsCustom(CardKind.IncomingCompact));
        Assert.Equal(DefaultCards.Incoming.Id, aufloeser.For(CardKind.IncomingCompact).Id);
    }

    /// <summary>
    /// I4 verlangt es wörtlich: „eine Karte mit Fehler in der Definition zeigt
    /// einen Hinweis statt abzustürzen". Und der Rückfall wird <b>benannt</b> —
    /// sonst sieht der Benutzer die mitgelieferte Karte und hält seine eigene
    /// für gespeichert.
    /// </summary>
    [Fact]
    public void Eine_kaputte_Karte_faellt_auf_die_mitgelieferte_zurueck_und_meldet_das()
    {
        var speicher = Speicher();
        speicher.Save(new IntegrationConfig { Cards = [Karte(wert: "crm.name ((")] });

        using var aufloeser = Aufloeser(speicher);

        Assert.Equal(DefaultCards.ActiveCall.Id, aufloeser.For(CardKind.ActiveExpanded).Id);
        Assert.NotEmpty(aufloeser.Issues);
        Assert.All(aufloeser.Issues, i => Assert.Equal(IssueSeverity.Error, i.Severity));
        Assert.Contains(
            aufloeser.Issues,
            i => i.Message.Contains("mitgelieferte Karte", StringComparison.Ordinal));
    }

    [Fact]
    public void Nach_einer_Aenderung_meldet_der_Aufloeser_das()
    {
        var speicher = Speicher();
        speicher.Load();

        using var aufloeser = Aufloeser(speicher);

        var gemeldet = 0;
        aufloeser.Changed += (_, _) => gemeldet++;

        speicher.Save(new IntegrationConfig { Cards = [Karte()] });

        Assert.Equal(1, gemeldet);
        Assert.Equal("eigene", aufloeser.For(CardKind.ActiveExpanded).Id);
    }

    // --- Der Validator ---

    [Fact]
    public void Zwei_Karten_derselben_Art_sind_ein_Fehler()
    {
        var befunde = CardDefinitionValidator.Validate(
            [Karte("erste"), Karte("zweite")]);

        Assert.Contains(
            befunde,
            b => b.Severity == IssueSeverity.Error
                && b.Message.Contains("eine Karte je Art", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_doppelte_Kennung_ist_ein_Fehler()
    {
        var befunde = CardDefinitionValidator.Validate(
            [Karte("gleich"), Karte("gleich", CardKind.IncomingCompact)]);

        Assert.Contains(
            befunde,
            b => b.Severity == IssueSeverity.Error
                && b.Message.Contains("mehrfach", StringComparison.Ordinal));
    }

    [Fact]
    public void Zu_breite_Spalten_werden_gemeldet()
    {
        var karte = new CardDefinition(
            "breit",
            "Zu breit",
            CardKind.ActiveExpanded,
            [
                new CardSection("kopf", null, [
                    new CardRow([
                        new CardColumn(4, [new CardText("a")]),
                        new CardColumn(4, [new CardText("b")]),
                    ]),
                ]),
            ]);

        Assert.Contains(
            CardDefinitionValidator.ValidateCard(karte),
            b => b.Severity == IssueSeverity.Error
                && b.Message.Contains("nebeneinander", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_Breite_ausserhalb_des_Rasters_wird_gemeldet()
    {
        var karte = new CardDefinition(
            "null",
            "Breite null",
            CardKind.ActiveExpanded,
            [
                new CardSection("kopf", null, [
                    new CardRow([new CardColumn(0, [new CardText("a")])]),
                ]),
            ]);

        Assert.Contains(
            CardDefinitionValidator.ValidateCard(karte),
            b => b.Message.Contains("ausserhalb von 1 bis 6", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_Karte_ohne_Bausteine_wird_gemeldet()
    {
        var karte = new CardDefinition("leer", "Leer", CardKind.ActiveExpanded, []);

        Assert.Contains(
            CardDefinitionValidator.ValidateCard(karte),
            b => b.Severity == IssueSeverity.Warning
                && b.Message.Contains("bleibt leer", StringComparison.Ordinal));
    }

    [Fact]
    public void Zu_viele_Abschnitte_werden_gemeldet()
    {
        var abschnitte = Enumerable.Range(0, CardLayout.MaxSections + 1)
            .Select(i => new CardSection(
                $"a{i}",
                null,
                [new CardRow([new CardColumn(6, [new CardText("'x'")])])]))
            .ToList();

        Assert.Contains(
            CardDefinitionValidator.ValidateCard(
                new CardDefinition("viele", "Viele", CardKind.ActiveExpanded, abschnitte)),
            b => b.Message.Contains("Abschnitte, erlaubt sind", StringComparison.Ordinal));
    }

    /// <summary>
    /// Der Deckel des Toasts (§8.6, ADR-030). Eine vierte Zeile wäre nicht
    /// falsch aussehend, sondern unsichtbar — Windows nimmt drei.
    /// </summary>
    [Fact]
    public void Mehr_als_drei_Textzeilen_im_Toast_sind_ein_Fehler()
    {
        var karte = new CardDefinition(
            "toast",
            "Toast",
            CardKind.Toast,
            [
                new CardSection("zeilen", null, [
                    new CardRow([
                        new CardColumn(6, [
                            new CardText("'a'"),
                            new CardText("'b'"),
                            new CardText("'c'"),
                            new CardText("'d'"),
                        ]),
                    ]),
                ]),
            ]);

        Assert.Contains(
            CardDefinitionValidator.ValidateCard(karte),
            b => b.Severity == IssueSeverity.Error
                && b.Message.Contains("Textzeilen", StringComparison.Ordinal));
    }

    [Fact]
    public void Genau_drei_Textzeilen_im_Toast_sind_in_Ordnung()
    {
        var karte = new CardDefinition(
            "toast",
            "Toast",
            CardKind.Toast,
            [
                new CardSection("zeilen", null, [
                    new CardRow([
                        new CardColumn(6, [
                            new CardText("'a'"),
                            new CardText("'b'"),
                            new CardText("'c'"),
                        ]),
                    ]),
                ]),
            ]);

        Assert.Empty(CardDefinitionValidator.ValidateCard(karte));
    }

    /// <summary>
    /// Ein Feld oder eine Schaltfläche hat in einer Benachrichtigung keine
    /// Entsprechung. Das ist eine Warnung und kein Fehler: die Karte
    /// funktioniert, der Baustein erscheint bloss nicht.
    /// </summary>
    [Fact]
    public void Ein_Feld_im_Toast_wird_als_wirkungslos_gemeldet()
    {
        var karte = new CardDefinition(
            "toast",
            "Toast",
            CardKind.Toast,
            [
                new CardSection("zeilen", null, [
                    new CardRow([
                        new CardColumn(6, [
                            new CardText("'a'"),
                            new CardField("Art", "crm.contactType"),
                        ]),
                    ]),
                ]),
            ]);

        var befunde = CardDefinitionValidator.ValidateCard(karte);

        Assert.Contains(
            befunde,
            b => b.Severity == IssueSeverity.Warning
                && b.Message.Contains("nur Text", StringComparison.Ordinal));
    }

    // --- Zusammenspiel mit der Quellenprüfung ---

    /// <summary>
    /// <b>Ein Befund an einer Karte darf keine Quelle abschalten.</b>
    /// <c>UsableSources</c> filtert über den Pfadanfang
    /// <c>dataSources[...]</c>; ein Kartenbefund trägt <c>cards[...]</c>. Wäre
    /// das vertauscht, hätte ein Tippfehler in einer Karte die Telefonie um
    /// ihren Anruferkontext gebracht.
    /// </summary>
    [Fact]
    public void Eine_kaputte_Karte_schaltet_keine_Quelle_ab()
    {
        var speicher = Speicher();

        speicher.Save(new IntegrationConfig
        {
            Cards = [Karte(wert: "crm.name ((")],
            DataSources =
            [
                new DataSourceDefinition
                {
                    Id = "crm",
                    DisplayName = "CRM",
                    Http = new HttpConnection { BaseUrl = "https://crm.example.ch/api" },
                    LookupByPhone = new LookupByPhoneCapability
                    {
                        Request = new RequestDefinition { Path = "/contacts" },
                        Mapping = { ["contactName"] = new(Path: "$.name") },
                    },
                },
            ],
        });

        Assert.Single(speicher.UsableSources);
        Assert.Contains(speicher.Issues, i => i.Path.StartsWith("cards[", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
