using System.Reflection;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Tests.Services.Integrations.Cards;

/// <summary>
/// Wie ein Baustein in einer Meldung heisst (Befund A1-14).
///
/// <para><b>Warum es diese Datei gibt.</b> Der Validator setzte den Namen des
/// Bausteins aus <c>element.GetType().Name</c> zusammen, und beim Übernehmen
/// einer Gesprächskarte in die Benachrichtigung stand achtmal wortgleich «Ein
/// Baustein der Art 'CardField' erscheint in einer Benachrichtigung nicht».
/// Der bestehende Test prüfte auf «nur Text» und blieb dabei grün.</para>
///
/// <para><b>Der Typname ist das eigentliche Loch.</b> <c>UserTextTests</c>
/// liest die Zeichenkettenliterale im Code — dort stand kein Typname, er kam
/// erst zur Laufzeit hinein. Ein Test, der den fertigen Satz ansieht, ist die
/// einzige Stelle, an der das auffallen kann; deshalb prüft
/// <see cref="Keine_Meldung_nennt_einen_Typnamen"/> jeden Bausteintyp
/// einzeln.</para>
/// </summary>
public sealed class CardElementNameTests
{
    private static CardDefinition Toast(params CardElement[] bausteine) =>
        new("toast", "Toast", CardKind.Toast, [
            new CardSection("zeilen", null, [
                new CardRow([new CardColumn(6, bausteine)]),
            ]),
        ]);

    private static string ToastMeldung(params CardElement[] bausteine) =>
        CardDefinitionValidator.ValidateCard(Toast(bausteine))
            .Single(b => b.Message.Contains("nur Text", StringComparison.Ordinal))
            .Message;

    [Fact]
    public void Die_Meldung_nennt_die_Beschriftung_des_Bausteins()
    {
        var meldung = ToastMeldung(new CardText("'a'"), new CardField("Art", "crm.contactType"));

        // «Art» ist das Wort, das im Designer an dem Baustein steht — danach
        // sucht der Benutzer, nicht nach seiner Art.
        Assert.Contains("«Art»", meldung, StringComparison.Ordinal);
    }

    [Fact]
    public void Mehrere_Bausteine_ergeben_eine_Meldung_mit_allen_Namen()
    {
        var befunde = CardDefinitionValidator.ValidateCard(Toast(
            new CardText("'a'"),
            new CardField("Art", "crm.contactType"),
            new CardField("Wer", "crm.owner"),
            new CardButton("Öffnen", new OpenUrlAction("https://example.invalid"))));

        var betroffen = befunde.Where(b => b.Message.Contains("nur Text", StringComparison.Ordinal)).ToList();

        // EINE Meldung, nicht drei. Acht wortgleiche Sätze sagten dem
        // Benutzer nur, dass es acht sind.
        Assert.Single(betroffen);
        Assert.Contains("«Art»", betroffen[0].Message, StringComparison.Ordinal);
        Assert.Contains("«Wer»", betroffen[0].Message, StringComparison.Ordinal);
        Assert.Contains("«Öffnen»", betroffen[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_Meldung_sagt_was_zu_tun_ist()
    {
        var meldung = ToastMeldung(new CardText("'a'"), new CardField("Art", "crm.contactType"));

        Assert.Contains("Entfernen oder durch einen Textbaustein ersetzen", meldung, StringComparison.Ordinal);
    }

    /// <summary>
    /// Kein Bausteintyp darf mit seinem .NET-Namen in einer Meldung landen —
    /// auch keiner, den es heute noch nicht gibt. Deshalb über Reflexion und
    /// nicht über eine Aufzählung von Hand.
    /// </summary>
    [Fact]
    public void Keine_Meldung_nennt_einen_Typnamen()
    {
        var typnamen = typeof(CardElement).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(CardElement)) && !t.IsAbstract)
            .Select(t => t.Name)
            .ToList();

        Assert.NotEmpty(typnamen);

        foreach (var baustein in Bausteine())
        {
            var befunde = CardDefinitionValidator.ValidateCard(Toast(new CardText("'a'"), baustein));

            foreach (var befund in befunde)
            {
                foreach (var name in typnamen)
                {
                    Assert.DoesNotContain(name, befund.Message, StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>
    /// Ein Baustein je Art. <b>Schlägt die Zählung fehl, ist ein Typ
    /// dazugekommen</b> und gehört hier und in
    /// <see cref="CardElementNames.ArtVon"/> ergänzt — sonst prüft der Test
    /// darüber einen Typ weniger, als es gibt, und merkt es nicht.
    /// </summary>
    [Fact]
    public void Die_Liste_der_Bausteine_ist_vollstaendig()
    {
        var arten = typeof(CardElement).Assembly
            .GetTypes()
            .Count(t => t.IsSubclassOf(typeof(CardElement)) && !t.IsAbstract);

        Assert.Equal(arten, Bausteine().Select(b => b.GetType()).Distinct().Count());
    }

    [Fact]
    public void Ein_Baustein_ohne_Beschriftung_wird_mit_seiner_Art_genannt()
    {
        // CardBadge hat einen Text, CardDivider nicht — und ein Baustein ohne
        // Beschriftung darf nicht namenlos in der Meldung stehen.
        Assert.Equal("Linie", CardElementNames.Beschreibe(new CardDivider()));
        Assert.Equal("Abstand", CardElementNames.Beschreibe(new CardSpacer()));

        // Leere Beschriftung faellt auf die Art zurueck, nicht auf «».
        Assert.Equal("Feld", CardElementNames.Beschreibe(new CardField("  ", "x")));
    }

    private static IEnumerable<CardElement> Bausteine() =>
    [
        new CardText("'a'"),
        new CardField("Art", "crm.contactType"),
        new CardBadge("VIP"),
        new CardDivider(),
        new CardSpacer(),
        new CardButton("Öffnen", new OpenUrlAction("https://example.invalid")),
        new CardLink("Verweis", "https://example.invalid"),
        new CardSourceStatus("crm"),
    ];
}
