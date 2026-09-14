namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Die Karten, die nipp mitbringt (§21).
///
/// <b>Sie sind kein Sonderfall im Code, sondern gewöhnliche Definitionen</b>
/// — dieselben, die auch ein Administrator schreiben würde. Damit ist von
/// Anfang an geprüft, dass die Beschreibungssprache trägt, was sie tragen
/// soll: wäre die eingebaute Karte im Code fest verdrahtet, fiele erst beim
/// ersten Kunden auf, was sich nicht beschreiben lässt.
///
/// <para><b>Und genau das ist bis zum 07.09.2026 auch passiert.</b> Diese
/// Karten nannten Quellen mit Namen — erst <c>crm.*</c> und <c>erp.*</c> aus
/// dem Beispiel des Auftrags, dann zusätzlich <c>crm.*</c> und
/// <c>memory.*</c>, weil die echten Quellen so heissen. Beim ersten Kunden
/// hätte keine dieser Zeilen getroffen, und die Karte wäre leer geblieben;
/// sichtbar wären die Felder nur über die Rückfallebene mit maschinellen
/// Beschriftungen gewesen („Letzte arbeit zeile").</para>
///
/// <para><b>Deshalb nennen sie jetzt keine Quelle mehr.</b> Sie fragen nach
/// <b>Bedeutung</b>: <c>role('name')</c> heisst „die erste Quelle nach
/// Priorität, die ein Feld mit der Bedeutung Name liefert". Welche das ist,
/// entscheidet die Konfiguration — bei bv2 das CRM und das Gesprächsjournal, beim
/// nächsten Kunden etwas anderes, und die Karte bleibt dieselbe. Die
/// Zuordnung Feldname zu Bedeutung steht im <see cref="FieldCatalog"/> und in
/// <c>docs/integrations/feldnamen.md</c>.</para>
///
/// Sie greifen auf Felder zu, die nur da sind, wenn eine Quelle sie liefert.
/// Fehlt eine, verschwinden die zugehörigen Zeilen — dafür sind die
/// Bedingungen und <c>emptyText: null</c> da.
/// </summary>
public static class DefaultCards
{
    /// <summary>
    /// Der Name oben: was eine Quelle sagt, sonst die eigenen Kontakte, sonst
    /// die formatierte Nummer.
    ///
    /// <b>Die letzte Stufe greift immer</b> — die Karte hat nie eine leere
    /// Überschrift. Und <c>contacts.displayName</c> steht hier trotzdem
    /// ausdrücklich, obwohl <c>role('name')</c> es über die Rolle schon
    /// findet: die eigenen Kontakte haben Priorität 0 und gewinnen ohnehin,
    /// aber wer die Karte liest, soll die Rückfallkette vollständig sehen.
    /// </summary>
    private const string NameExpression =
        "coalesce(role('name'), contacts.displayName, formatPhone(number.e164))";

    private const string CompanyExpression =
        "coalesce(role('company'), contacts.company)";

    /// <summary>
    /// Die Karte im laufenden Gespräch.
    ///
    /// Aufbau: Name und Firma oben, darunter die Art des Kontakts, die letzte
    /// Arbeit samt Datum und Kollegen, das letzte Gespräch mit offenen Punkten
    /// — und am Ende die Zeilen aus dem Beispiel des Auftrags (Kundennummer,
    /// offene Aufträge, Account Manager), die nur erscheinen, wenn eine Quelle
    /// sie liefert.
    /// </summary>
    public static CardDefinition ActiveCall { get; } = new(
        Id: "active-default",
        Name: "Gespräch",
        Kind: CardKind.ActiveExpanded,
        Sections:
        [
            new CardSection(
                Id: "kopf",
                Title: null,
                Rows:
                [
                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardText(NameExpression)
                            {
                                Style = CardTextStyle.Title,
                            },

                            new CardText(CompanyExpression)
                            {
                                Style = CardTextStyle.Subtitle,
                                VisibleWhen = $"!isEmpty({CompanyExpression})",
                            },
                        ]),
                    ]),

                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardField("Art", "role('type')") { EmptyText = null },
                        ]),
                    ]),

                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardField("Letzte Arbeit", "role('work')") { EmptyText = null },
                            new CardField("Wer", "role('colleague')") { EmptyText = null },
                        ]),
                    ]),

                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardField("Letztes Gespräch", "role('summary')") { EmptyText = null },
                            new CardField("Offen", "anyOf('offeneAufgabenZeile')") { EmptyText = null },
                            new CardField("Verlauf", "anyOf('verlauf')") { EmptyText = null },
                        ]),
                    ]),

                    new CardRow([
                        new CardColumn(3, [
                            new CardField("Kundennummer", "anyOf('customerNumber')") { EmptyText = null },
                        ]),
                        new CardColumn(3, [
                            new CardField("Offene Aufträge", "anyOf('openOrders')") { EmptyText = null },
                        ]),
                    ]),
                ]),

            // Der Zustand der Quellen. Welche es gibt, weiss diese Karte
            // nicht — deshalb steht hier kein sourceStatus je Quelle mehr.
            //
            // Bis zum 07.09.2026 waren „crm", „memory", „crm" und „erp"
            // aufgezählt; bei einem Kunden mit anderen Kennungen erschien
            // dadurch zu keiner Quelle ein Zustand, und „CRM antwortet nicht"
            // stand für eine Quelle, die es nie gab. Die Gesprächsansicht
            // zeigt die Zustände über CallerCardViewModel.Sources, das sie aus
            // dem Schnappschuss nimmt und deshalb jede Quelle kennt.
        ]);

    /// <summary>
    /// Die kompakte Karte für den eingehenden Anruf.
    ///
    /// Weniger Zeilen, dieselben Felder: wer klingelt, soll auf einen Blick
    /// sehen, wer das ist und ob es eilt — nicht eine Aufstellung lesen.
    /// </summary>
    public static CardDefinition Incoming { get; } = new(
        Id: "incoming-default",
        Name: "Eingehender Anruf",
        Kind: CardKind.IncomingCompact,
        Sections:
        [
            new CardSection(
                Id: "kopf",
                Title: null,
                Rows:
                [
                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardText(NameExpression)
                            {
                                Style = CardTextStyle.Title,
                            },

                            new CardText(CompanyExpression)
                            {
                                Style = CardTextStyle.Caption,
                                VisibleWhen = $"!isEmpty({CompanyExpression})",
                            },
                        ]),
                    ]),
                ]),
        ]);

    /// <summary>
    /// Die Karte im aufgeklappten Eintrag der Anrufliste (§22.3, ADR-036).
    ///
    /// <para><b>Ohne Namenszeile, und das ist der Unterschied zur
    /// Gesprächskarte.</b> Name, Uhrzeit und Ergebnis stehen im Kopf des
    /// Bereichs — der sagt, <b>welcher</b> Eintrag offen ist, und ist deshalb
    /// Rahmen und nicht Karte. Stünde der Name zusätzlich hier, stünde er auf
    /// 400 Pixeln zweimal untereinander.</para>
    ///
    /// <para>Was sie zeigt, ist der heutige Stand und nicht der von damals
    /// (ADR-027): abgerufen wird beim Aufklappen, gespeichert wird nichts.</para>
    /// </summary>
    public static CardDefinition History { get; } = new(
        Id: "history-default",
        Name: "Anrufliste",
        Kind: CardKind.History,
        Sections:
        [
            new CardSection(
                Id: "kopf",
                Title: null,
                Rows:
                [
                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardText(CompanyExpression)
                            {
                                Style = CardTextStyle.Subtitle,
                                VisibleWhen = $"!isEmpty({CompanyExpression})",
                            },

                            new CardField("Art", "role('type')") { EmptyText = null },
                        ]),
                    ]),

                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardField("Letzte Arbeit", "role('work')") { EmptyText = null },
                            new CardField("Wer", "role('colleague')") { EmptyText = null },
                        ]),
                    ]),

                    new CardRow([
                        new CardColumn(CardLayout.Columns, [
                            new CardField("Letztes Gespräch", "role('summary')")
                            {
                                EmptyText = null,
                            },
                            new CardField("Offen", "anyOf('offeneAufgabenZeile')")
                            {
                                EmptyText = null,
                            },
                        ]),
                    ]),
                ]),
        ]);

    /// <summary>Alle mitgelieferten Karten — für die Auswahl in den Einstellungen.</summary>
    public static IReadOnlyList<CardDefinition> All { get; } = [ActiveCall, Incoming, History];

    /// <summary>Die mitgelieferte Karte zu einer Art, oder <c>null</c>.</summary>
    public static CardDefinition? For(CardKind kind) =>
        All.FirstOrDefault(c => c.Kind == kind);
}
