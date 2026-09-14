using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Die Karte in der Anrufliste (ADR-036) und die beiden neuen Bausteine
/// (ADR-037).
///
/// <para><b>Warum diese Datei überhaupt entsteht:</b> <c>CardKind.History</c>
/// stand seit I4 im Modell und war nirgends angeschlossen — gebaut, aber nicht
/// verdrahtet. Ein Test, der die Kartenart über den <c>CardResolver</c> holt,
/// hätte das gefunden.</para>
/// </summary>
public sealed class HistoryCardTests
{
    private static ContextSnapshot Kontext(CallFacts? fakten = null)
    {
        var quellen = new Dictionary<string, ContextFragment>(StringComparer.Ordinal)
        {
            ["crm"] = new(
                "crm",
                "CRM",
                SourceState.Success,
                new Dictionary<string, ContextValue>(StringComparer.Ordinal)
                {
                    ["organization"] = ContextValue.FromText("Muster AG"),
                    ["contactType"] = ContextValue.FromText("Kunde"),
                },
                Priority: 10),
        };

        return new ContextSnapshot(
            CallHandle.New(),
            PhoneNumberKey.From("0791234567", new NumberNormalizer("+41")),
            quellen,
            fakten);
    }

    private static CallFacts Fakten() => new(
        new DateTimeOffset(2026, 9, 7, 14, 10, 0, TimeSpan.FromHours(2)),
        TimeSpan.FromSeconds(95),
        "verpasst",
        "eingehend");

    [Fact]
    public void Die_mitgelieferte_Karte_der_Anrufliste_uebersetzt_fehlerfrei()
    {
        Assert.True(CardLayoutEngine.TryCompile(DefaultCards.History, out _, out var fehler));
        Assert.Empty(fehler);
    }

    [Fact]
    public void Sie_nennt_keine_Quelle_beim_Namen()
    {
        // Der Fehler vom 07.09.2026: die mitgelieferten Karten hiessen
        // `crm.*` und `memory.*`, und beim ersten Kunden mit anderen
        // Kennungen traf keine Zeile.
        var text = System.Text.Json.JsonSerializer.Serialize(DefaultCards.History);

        foreach (var kennung in new[] { "crm.", "memory.", "crm.", "erp." })
        {
            Assert.DoesNotContain(kennung, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Der_Resolver_liefert_fuer_die_Anrufliste_eine_Karte()
    {
        var store = Konfiguration();
        using var resolver = new CardResolver(store, NullLogger<CardResolver>.Instance);

        var karte = resolver.For(CardKind.History);
        var modell = CardLayoutEngine.Build(karte, Kontext(Fakten()));

        Assert.True(modell.HasContent);
        Assert.False(resolver.IsCustom(CardKind.History));
        Assert.NotNull(resolver.DefinitionFor(CardKind.History));
    }

    [Fact]
    public void Der_Namensraum_call_loest_auf()
    {
        var schnappschuss = Kontext(Fakten());

        Assert.False(schnappschuss.Resolve("call.startedAt").IsEmpty);
        Assert.Equal("verpasst", schnappschuss.Resolve("call.outcome").AsDisplayText());
        Assert.Equal("eingehend", schnappschuss.Resolve("call.direction").AsDisplayText());
        Assert.Equal("1:35", schnappschuss.Resolve("call.duration").AsDisplayText());

        // Ein unbekanntes Feld ist leer und kein Fehler — sonst brächte eine
        // Karte mit einem Tippfehler die Anzeige zum Stehen.
        Assert.True(schnappschuss.Resolve("call.gibtsNicht").IsEmpty);
    }

    [Fact]
    public void Ohne_Anrufangaben_bleibt_call_leer_statt_zu_werfen()
    {
        var schnappschuss = Kontext();

        Assert.True(schnappschuss.Resolve("call.outcome").IsEmpty);
        Assert.True(schnappschuss.Resolve("call.duration").IsEmpty);
    }

    [Fact]
    public void Eine_Dauer_von_null_ist_leer_und_nicht_null_doppelpunkt_null()
    {
        // Ein nie verbundener Anruf soll die Zeile verschwinden lassen, nicht
        // „0:00" zeigen — das behauptete ein Gespräch von null Sekunden.
        var fakten = new CallFacts(DateTimeOffset.UtcNow, null, "verpasst", "eingehend");

        Assert.Equal(string.Empty, fakten.DurationText);
        Assert.True(Kontext(fakten).Resolve("call.duration").IsEmpty);
    }

    [Fact]
    public void Ein_Abstand_ueberlebt_den_Rundlauf_durch_die_Datei()
    {
        var karte = MitBausteinen(new CardSpacer { Size = CardSpacerSize.Large });

        var text = IntegrationConfigStore.SerializeCard(karte);
        var zurueck = System.Text.Json.JsonSerializer.Deserialize<CardDefinition>(
            text, IntegrationJson.Options);

        var baustein = Assert.IsType<CardSpacer>(
            zurueck!.Sections[0].Rows[0].Columns[0].Elements[0]);

        Assert.Equal(CardSpacerSize.Large, baustein.Size);
        Assert.Contains("\"spacer\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_abgeschaltete_Beschriftung_ueberlebt_den_Rundlauf()
    {
        // Dieselbe Falle wie bei `emptyText`: ein Feld, dessen Vorgabe nicht
        // dem gewünschten Wert entspricht, verlor sein `null` beim Speichern.
        // Ein `bool` trifft das nicht — geprüft ist es trotzdem.
        var karte = MitBausteinen(new CardField("Name", "role('name')") { ShowLabel = false });

        var text = IntegrationConfigStore.SerializeCard(karte);
        var zurueck = System.Text.Json.JsonSerializer.Deserialize<CardDefinition>(
            text, IntegrationJson.Options);

        var feld = Assert.IsType<CardField>(zurueck!.Sections[0].Rows[0].Columns[0].Elements[0]);

        Assert.False(feld.ShowLabel);
    }

    [Fact]
    public void Der_Abstand_kommt_als_eigenes_Modell_beim_Renderer_an()
    {
        var karte = MitBausteinen(new CardSpacer { Size = CardSpacerSize.Small });

        Assert.True(CardLayoutEngine.TryCompile(karte, out var uebersetzt, out _));

        var modell = CardLayoutEngine.Build(uebersetzt, Kontext(Fakten()));
        var baustein = modell.Sections[0].Rows[0].Columns[0].Elements[0];

        var abstand = Assert.IsType<CardSpacerModel>(baustein);

        Assert.Equal(CardSpacerSize.Small, abstand.Size);
        Assert.True(abstand.Visible);
    }

    [Fact]
    public void Der_Entwurf_gibt_Abstand_und_Beschriftung_unveraendert_zurueck()
    {
        // Der Rundlauf des Designers: FromDefinition(ToDefinition(x)) == x.
        // Was er nicht versteht, darf er nicht wegwerfen.
        var karte = MitBausteinen(
            new CardSpacer { Size = CardSpacerSize.Large },
            new CardField("Firma", "role('company')") { ShowLabel = false, EmptyText = null });

        var zurueck = CardDraft.FromDefinition(karte).ToDefinition();

        Assert.Equal(
            IntegrationConfigStore.SerializeCard(karte),
            IntegrationConfigStore.SerializeCard(zurueck),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Ein_Abstand_in_einer_Toastkarte_wird_uebergangen_und_nicht_gemeldet()
    {
        var karte = MitBausteinen(
            new CardText("role('name')"),
            new CardSpacer(),
            new CardDivider()) with
        {
            Kind = CardKind.Toast,
        };

        Assert.Empty(CardDefinitionValidator.ValidateCard(karte));
    }

    private static CardDefinition MitBausteinen(params CardElement[] elemente) => new(
        Id: "test",
        Name: "Test",
        Kind: CardKind.History,
        Sections:
        [
            new CardSection("kopf", null, [new CardRow([new CardColumn(CardLayout.Columns, elemente)])]),
        ]);

    private static IntegrationConfigStore Konfiguration()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "nipp-tests", Guid.NewGuid().ToString("N"));

        var geheimnisse = new IntegrationSecrets(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(verzeichnis, "secrets.dat")));

        return new IntegrationConfigStore(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(verzeichnis, "integrations.json"));
    }
}
