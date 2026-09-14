using Nipp.Core.Services.Contacts;

namespace Nipp.Core.Services.Integrations.Search;

/// <summary>
/// Eine Suchanfrage.
/// </summary>
/// <param name="Text">Der eingegebene Text, bereits beschnitten.</param>
/// <param name="Limit">Höchstzahl Treffer, die diese Quelle liefern soll.</param>
public sealed record ContactQuery(string Text, int Limit);

/// <summary>
/// Was eine Suchquelle über sich weiss.
/// </summary>
/// <param name="IsLocal">
/// Ob sie ohne Netz antwortet. Lokale Quellen werden <b>vor</b> dem Debounce
/// gefragt: der Zwischenspeicher der Kontakte liegt im Arbeitsspeicher, und
/// dort auf 300 ms zu warten wäre Verzögerung ohne Gegenwert.
/// </param>
/// <param name="MinQueryLength">Ab wie vielen Zeichen sie überhaupt gefragt wird.</param>
/// <param name="Timeout">Wie lange auf sie gewartet wird.</param>
public sealed record ContactSearchTraits(
    bool IsLocal,
    int MinQueryLength = 1,
    TimeSpan? Timeout = null);

/// <summary>
/// Was eine Quelle geantwortet hat.
/// </summary>
/// <param name="SourceId">Welche Quelle.</param>
/// <param name="State">Wie es ausging.</param>
/// <param name="Contacts">Die Treffer. Nie <c>null</c>, notfalls leer.</param>
/// <param name="Message">
/// Ein fertiger Satz für die Oberfläche nach §15, wenn etwas zu sagen ist.
/// Ohne Suchtext, ohne Adresse, ohne Namen.
/// </param>
public sealed record ContactSearchPage(
    string SourceId,
    SearchState State,
    IReadOnlyList<Contact> Contacts,
    string? Message = null)
{
    public static ContactSearchPage Empty(string sourceId) =>
        new(sourceId, SearchState.Empty, []);
}

/// <summary>
/// Der Zustand einer Quelle während einer Suche (§21.1: die Oberfläche soll
/// darauf reagieren können).
/// </summary>
public enum SearchState
{
    /// <summary>Wird gerade gefragt.</summary>
    Loading,

    /// <summary>Hat Treffer geliefert.</summary>
    Success,

    /// <summary>Hat geantwortet, kennt aber niemanden.</summary>
    Empty,

    /// <summary>Netz, Anmeldung oder Antwort waren unbrauchbar.</summary>
    Error,

    /// <summary>Hat nicht rechtzeitig geantwortet.</summary>
    Timeout,

    /// <summary>Wurde nicht gefragt — zu kurze Eingabe, fehlendes Geheimnis, abgeschaltet.</summary>
    Skipped,
}

/// <summary>
/// Eine Quelle, in der sich Kontakte suchen lassen (§21.1).
///
/// <b>Dieselbe Schnittstelle für Outlook und für ein CRM</b> — das ist der
/// Zweck. Die Kontaktliste soll nicht wissen, woher ein Kontakt stammt; sie
/// bekommt von jeder Quelle dasselbe <see cref="Contact"/>-Modell.
///
/// Dass die Quellen technisch völlig verschieden arbeiten, bleibt hinter der
/// Schnittstelle: Outlook liefert aus einem Zwischenspeicher, der über COM
/// auf einem eigenen STA-Thread gefüllt wurde; ein CRM antwortet über HTTP.
///
/// <b>Die Zusage an alle Implementierungen</b> ist dieselbe wie bei
/// <see cref="IContactSource"/>: <see cref="SearchAsync"/> wirft nicht. Eine
/// Quelle, die nicht kann, liefert eine Seite mit ihrem Zustand und einer
/// Meldung. Eine Suche darf nichts kosten ausser sich selbst.
/// </summary>
public interface IContactSearchProvider
{
    /// <summary>Die Kennung der Quelle — <c>team</c>, <c>outlook</c>, <c>crm</c>.</summary>
    string SourceId { get; }

    /// <summary>Wie sie in der Oberfläche heisst.</summary>
    string DisplayName { get; }

    ContactSearchTraits Traits { get; }

    Task<ContactSearchPage> SearchAsync(ContactQuery query, CancellationToken cancellationToken);
}
