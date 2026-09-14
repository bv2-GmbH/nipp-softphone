namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Ein Kontakt, wie nipp ihn kennt (§8.4).
///
/// Bewusst flach und unveraenderlich: die Quellen liefern sehr
/// unterschiedliche Datensaetze, und alles, was nipp davon braucht, ist ein
/// Name und waehlbare Nummern. Was Outlook sonst noch weiss, gehoert nach
/// Outlook.
/// </summary>
/// <param name="Id">Innerhalb der Quelle eindeutig.</param>
/// <param name="DisplayName">Anzeigename.</param>
/// <param name="Numbers">Waehlbare Nummern, in der Reihenfolge ihrer Wichtigkeit.</param>
/// <param name="Source">Woher der Kontakt stammt — §8.4 verlangt, beide Quellen getrennt sichtbar zu halten.</param>
/// <param name="SipAddress">SIP-Adresse, falls bekannt. Nur Team-Nebenstellen haben eine.</param>
/// <param name="Company">Firma, falls bekannt — hilft bei gleichen Namen.</param>
/// <param name="SourceId">
/// Die Kennung der Quelle: <c>team</c>, <c>outlook</c> oder die Kennung einer
/// externen Quelle wie <c>crm</c> (§21.3).
///
/// <b>Feiner als <see cref="Source"/></b>, und beide werden gebraucht: das
/// Enum unterscheidet <i>Klassen</i> von Quellen — nur Team hat Präsenz, nur
/// Outlook hängt an COM —, diese Kennung unterscheidet die einzelne Quelle.
/// Bei zwei angebundenen CRM-Systemen ist das der Unterschied zwischen
/// „extern" und „welches".
/// </param>
/// <param name="Email">E-Mail-Adresse, falls bekannt. Merkmal beim Zusammenführen (§21.4).</param>
/// <param name="ExternalId">Die Kennung im Fremdsystem, ohne Präfix — für das Öffnen dort.</param>
/// <param name="OpenUri">
/// Wohin „Kontakt öffnen" führt. Nur <c>http</c> und <c>https</c>, und der
/// Rechnername ist gegen die Quelle geprüft — sonst liesse sich über eine
/// verteilte Konfigurationsdatei ein beliebiger Link unter vertrauter
/// Beschriftung unterbringen (§21.2).
/// </param>
/// <param name="Origins">
/// Nach dem Zusammenführen: aus welchen Quellen dieser Kontakt stammt.
/// <c>null</c> heisst „nur aus <see cref="SourceId"/>".
///
/// §21.4 verlangt, dass die Herkunft nachvollziehbar bleibt: wer eine
/// zusammengeführte Zeile anruft, soll sehen können, woher die Nummer kommt.
/// </param>
/// <param name="Group">
/// Die Gruppe in der Kontaktliste — nur bei Team-Nebenstellen gesetzt
/// (ADR-041).
///
/// <b>Am Kontakt und nicht in der Liste nachgeschlagen.</b> Die Alternative
/// wäre, die Gruppe beim Aufbau der Zeilen über <c>TeamContactSource.IdOf</c>
/// neu zuzuordnen — und das wäre die zweite Stelle mit der Kennungsformel,
/// wovor der Kommentar an <c>IdOf</c> ausdrücklich warnt.
/// </param>
public sealed record Contact(
    string Id,
    string DisplayName,
    IReadOnlyList<ContactNumber> Numbers,
    ContactSourceKind Source,
    string? SipAddress = null,
    string? Company = null,
    string SourceId = "",
    string? Email = null,
    string? ExternalId = null,
    Uri? OpenUri = null,
    IReadOnlyList<ContactOrigin>? Origins = null,
    string? Group = null)
{
    /// <summary>
    /// Alle Herkünfte, auch bei einem Kontakt aus einer einzigen Quelle.
    /// Die Oberfläche muss nicht zwischen „zusammengeführt" und „nicht
    /// zusammengeführt" unterscheiden.
    /// </summary>
    public IReadOnlyList<ContactOrigin> AllOrigins =>
        Origins ?? [new ContactOrigin(EffectiveSourceId, ExternalId ?? Id)];

    /// <summary>
    /// Die Quellenkennung, notfalls aus <see cref="Source"/> abgeleitet.
    ///
    /// Der Rückfall hält die bestehenden Aufrufer am Leben: <c>Contact</c>
    /// wird an einem halben Dutzend Stellen ohne Kennung gebaut, und ein
    /// Pflichtfeld hätte sie alle angefasst, ohne etwas zu verbessern.
    /// </summary>
    public string EffectiveSourceId =>
        SourceId is { Length: > 0 } id
            ? id
            : Source switch
            {
                ContactSourceKind.Team => "team",
                ContactSourceKind.Outlook => "outlook",
                _ => "extern",
            };

    /// <summary>Ob dieser Kontakt aus mehr als einer Quelle stammt (§21.4).</summary>
    public bool IsMerged => Origins is { Count: > 1 };

    /// <summary>Die Nummer, die ein Klick auf den Kontakt waehlt.</summary>
    public string? PrimaryNumber => Numbers.Count > 0 ? Numbers[0].Number : null;

    /// <summary>Initialen fuer das Kreissymbol in der Liste.</summary>
    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant(),
            };
        }
    }
}

/// <summary>Eine Nummer mit ihrer Art (geschaeftlich, mobil, privat).</summary>
/// <param name="Number">Die Nummer, so wie die Quelle sie liefert — normalisiert wird erst beim Waehlen (§8.1).</param>
/// <param name="Kind">Beschriftung fuer die Anzeige.</param>
public sealed record ContactNumber(string Number, ContactNumberKind Kind);

/// <summary>Art einer Kontaktnummer.</summary>
public enum ContactNumberKind
{
    Business,
    Mobile,
    Home,
    Other,
}

/// <summary>
/// Woher ein Kontakt stammt. §8.4: „beide Quellen getrennt sichtbar" — und
/// §14.8: Praesenz wird nur fuer <see cref="Team"/> abonniert, niemals fuer
/// Outlook-Kontakte.
///
/// <b>Die Reihenfolge zaehlt.</b> <c>ContactStore.Sort</c> ordnet danach, und
/// <c>ClipResolver</c> loest in dieser Reihenfolge auf: wer intern anruft,
/// erscheint als Kollege, auch wenn dieselbe Nummer in Outlook oder in einem
/// Fremdsystem steht.
/// </summary>
public enum ContactSourceKind
{
    /// <summary>Team-Nebenstellen aus der Provisionierung oder den Einstellungen.</summary>
    Team,

    /// <summary>Persoenliche Kontakte aus Outlook.</summary>
    Outlook,

    /// <summary>
    /// Aus einem angebundenen Fremdsystem (§21): CRM, ERP, Ticketing.
    ///
    /// <b>Keine Praesenz</b> — die gibt es nur fuer Team-Nebenstellen der
    /// eigenen Anlage (§14.8). Und kein Zwischenspeicher wie bei Outlook:
    /// externe Kontakte entstehen erst bei einer Suche und leben nur so
    /// lange wie deren Ergebnis.
    /// </summary>
    External,
}

/// <summary>
/// Eine Herkunft eines Kontakts (§21.4).
/// </summary>
/// <param name="SourceId">Die Kennung der Quelle, etwa <c>outlook</c> oder <c>crm</c>.</param>
/// <param name="ExternalId">Die Kennung des Kontakts <b>in</b> dieser Quelle.</param>
public sealed record ContactOrigin(string SourceId, string ExternalId);
