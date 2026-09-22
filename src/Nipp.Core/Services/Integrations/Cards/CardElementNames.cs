namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Wie ein Baustein in einer Meldung heisst — und das entscheidet genau diese
/// Stelle (Befund A1-14).
///
/// <para><b>Woher der Befund kam.</b> Beim Übernehmen der Gesprächskarte in
/// die Benachrichtigung stand achtmal wortgleich «Ein Baustein der Art
/// 'CardField' erscheint in einer Benachrichtigung nicht»: der Satz nannte
/// den .NET-Typnamen — ausdrücklich ausgeschlossen — und sagte nicht, welcher
/// der acht Bausteine gemeint war. Der Name entstand aus
/// <c>element.GetType().Name</c>, und <b>genau deshalb konnte
/// <c>UserTextTests</c> ihn nie sehen</b>: im Literal steht kein Typname, er
/// kommt erst zur Laufzeit hinein.</para>
///
/// <para><b>Was hier nicht steht.</b> Der Karten-Designer beschriftet seine
/// Knöpfe heute noch selbst («Text», «Abzeichen», «Linie», «Abstand», in
/// <c>CardDesignerWindow.xaml</c>). Das ist die zweite Stelle, und sie gehört
/// hierher gezogen — solange sie es nicht ist, <b>müssen die Wörter hier zu
/// denen dort passen</b>.</para>
/// </summary>
public static class CardElementNames
{
    /// <summary>
    /// Die Art des Bausteins, auf Deutsch. Für Meldungen, in denen kein
    /// einzelner Baustein gemeint ist.
    /// </summary>
    public static string ArtVon(CardElement element) => element switch
    {
        CardText => "Text",
        CardField => "Feld",
        CardBadge => "Abzeichen",
        CardDivider => "Linie",
        CardSpacer => "Abstand",
        CardButton => "Schaltfläche",
        CardLink => "Verweis",
        CardSourceStatus => "Zustand einer Quelle",

        // Bewusst kein Wurf: eine Meldung ist kein Ort, an dem ein neuer
        // Bausteintyp das Programm anhalten darf. Wer einen ergänzt und diese
        // Stelle vergisst, liest «Baustein» — unschön, aber harmlos.
        _ => "Baustein",
    };

    /// <summary>
    /// Wie der Baustein in einer Meldung genannt wird: seine Beschriftung,
    /// wenn er eine hat, sonst seine Art.
    ///
    /// <para><b>Die Beschriftung zuerst</b>, weil sie das ist, was im
    /// Designer dasteht — «Art» findet der Benutzer wieder, «Feld» nicht.</para>
    /// </summary>
    public static string Beschreibe(CardElement element)
    {
        var beschriftung = element switch
        {
            CardField feld => feld.Label,
            CardButton knopf => knopf.Label,
            CardLink verweis => verweis.Label,
            CardBadge abzeichen => abzeichen.Text,
            CardSourceStatus zustand => zustand.Source,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(beschriftung)
            ? ArtVon(element)
            : $"«{beschriftung}»";
    }
}
