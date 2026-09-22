using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Prüft Kartenbeschreibungen (§21.3, ADR-032).
///
/// <para><b>Was hier geprüft wird und was die Engine prüft.</b>
/// <see cref="CardLayoutEngine.TryCompile"/> findet alles, was am einzelnen
/// Baustein hängt: kaputte Ausdrücke, überbelegte Zeilen, unbekannte Typen.
/// Das wird hier nicht wiederholt, sondern <b>aufgerufen</b> — eine zweite
/// Kopie derselben Prüfung wäre eine zweite Gelegenheit, sie falsch zu
/// haben.</para>
///
/// <para>Dieser Validator ergänzt, was die Engine <b>nicht</b> sehen kann,
/// weil es über eine einzelne Karte hinausgeht oder weil die Engine
/// stillschweigend kürzt statt zu meckern:</para>
/// <list type="bullet">
///   <item>je Kartenart höchstens eine Karte,</item>
///   <item>Kennungen kommen nur einmal vor,</item>
///   <item>die Grenzen aus <see cref="CardLayout"/> — die Engine schneidet
///   mit <c>Take(...)</c> ab, und ein stillschweigend fehlender Abschnitt ist
///   schlimmer als eine Meldung,</item>
///   <item>eine Karte ohne einen einzigen sichtbaren Baustein,</item>
///   <item>der Deckel der Toast-Karte (§8.6, ADR-030): Windows nimmt drei
///   Textzeilen, und was nicht passt, schneidet es mitten im Wort ab.</item>
/// </list>
/// </summary>
public static class CardDefinitionValidator
{
    /// <summary>
    /// Wie viele Textzeilen eine Toast-Karte tragen darf.
    ///
    /// <b>Keine Auslegungssache, sondern eine Grenze von Windows</b>: eine
    /// Benachrichtigung nimmt drei Textelemente plus die Attributionszeile,
    /// die klein und grau darunter steht.
    /// </summary>
    public const int MaxToastTextRows = 3;

    /// <summary>Prüft alle Karten einer Konfiguration.</summary>
    public static IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<CardDefinition>? cards)
    {
        var issues = new List<ValidationIssue>();

        if (cards is null || cards.Count == 0)
        {
            return issues;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kinds = new Dictionary<CardKind, string>();

        foreach (var card in cards)
        {
            if (card is null)
            {
                continue;
            }

            var path = $"cards[{card.Id}]";

            if (string.IsNullOrWhiteSpace(card.Id))
            {
                issues.Add(new ValidationIssue(
                    "cards[]",
                    IssueSeverity.Error,
                    "Der Karte fehlt die Kennung. Sie steht in Befunden und im Protokoll; "
                        + "ohne sie ist nicht zu sagen, welche Karte gemeint ist."));
            }
            else if (!ids.Add(card.Id))
            {
                issues.Add(new ValidationIssue(
                    path,
                    IssueSeverity.Error,
                    $"Die Kennung '{card.Id}' kommt mehrfach vor. Jede Karte braucht eine eigene."));
            }

            if (kinds.TryGetValue(card.Kind, out var erste))
            {
                // Nicht „die erste gewinnt": eine stille Auswahl zwischen zwei
                // Karten ist genau der Fehler, bei dem jemand eine Änderung
                // speichert und nichts passiert.
                issues.Add(new ValidationIssue(
                    path,
                    IssueSeverity.Error,
                    $"Für die Art '{card.Kind}' ist schon '{erste}' eingetragen. "
                        + "Es gilt eine Karte je Art — die zweite entfernen."));
            }
            else
            {
                kinds[card.Kind] = card.Id;
            }

            ValidateCard(card, path, issues);
        }

        return issues;
    }

    /// <summary>Prüft eine einzelne Karte — für den Designer, der beim Tippen prüft.</summary>
    public static IReadOnlyList<ValidationIssue> ValidateCard(CardDefinition? card)
    {
        var issues = new List<ValidationIssue>();

        if (card is not null)
        {
            ValidateCard(card, $"cards[{card.Id}]", issues);
        }

        return issues;
    }

    private static void ValidateCard(CardDefinition card, string path, List<ValidationIssue> issues)
    {
        // Erst die Engine: sie kennt die Ausdrücke und die Spaltenbreiten.
        if (!CardLayoutEngine.TryCompile(card, out _, out var fehler))
        {
            foreach (var eintrag in fehler)
            {
                issues.Add(new ValidationIssue(path, IssueSeverity.Error, eintrag));
            }
        }

        if (card.Sections.Count > CardLayout.MaxSections)
        {
            issues.Add(new ValidationIssue(
                $"{path}.sections",
                IssueSeverity.Error,
                $"{card.Sections.Count} Abschnitte, erlaubt sind {CardLayout.MaxSections}. "
                    + "Die überzähligen werden nicht gezeigt."));
        }

        var bausteine = 0;

        foreach (var section in card.Sections)
        {
            var abschnitt = $"{path}.sections[{section.Id}]";

            if (section.Rows.Count > CardLayout.MaxRowsPerSection)
            {
                issues.Add(new ValidationIssue(
                    $"{abschnitt}.rows",
                    IssueSeverity.Error,
                    $"{section.Rows.Count} Zeilen, erlaubt sind {CardLayout.MaxRowsPerSection}. "
                        + "Die überzähligen werden nicht gezeigt."));
            }

            foreach (var (row, rowIndex) in section.Rows.Select(static (r, i) => (r, i)))
            {
                if (row.Columns.Count == 0)
                {
                    issues.Add(new ValidationIssue(
                        $"{abschnitt}.rows[{rowIndex}]",
                        IssueSeverity.Warning,
                        "Die Zeile hat keine Spalte und bleibt leer."));
                }

                foreach (var column in row.Columns)
                {
                    if (column.Span < 1 || column.Span > CardLayout.Columns)
                    {
                        issues.Add(new ValidationIssue(
                            $"{abschnitt}.rows[{rowIndex}].span",
                            IssueSeverity.Error,
                            $"Die Breite {column.Span} liegt ausserhalb von 1 bis "
                                + $"{CardLayout.Columns}."));
                    }

                    if (column.Elements.Count > CardLayout.MaxElementsPerColumn)
                    {
                        issues.Add(new ValidationIssue(
                            $"{abschnitt}.rows[{rowIndex}]",
                            IssueSeverity.Error,
                            $"{column.Elements.Count} Bausteine in einer Spalte, erlaubt sind "
                                + $"{CardLayout.MaxElementsPerColumn}."));
                    }

                    bausteine += column.Elements.Count;
                }
            }
        }

        if (bausteine == 0)
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Warning,
                "Die Karte hat keinen einzigen Baustein und bleibt leer."));
        }

        ValidateToast(card, path, issues);
    }

    /// <summary>
    /// Der Deckel für die Toast-Karte (§8.6, ADR-030, ADR-034).
    ///
    /// Gezählt werden die <see cref="CardText"/>-Bausteine, weil genau die im
    /// Toast zu Textzeilen werden. Ein <see cref="CardField"/> oder eine
    /// Schaltfläche hat dort keine Entsprechung — sie werden nicht gezählt,
    /// sondern gemeldet.
    /// </summary>
    private static void ValidateToast(CardDefinition card, string path, List<ValidationIssue> issues)
    {
        if (card.Kind != CardKind.Toast)
        {
            return;
        }

        var texte = 0;
        var ohneEntsprechung = new List<string>();

        foreach (var element in card.Sections
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements))
        {
            switch (element)
            {
                case CardText:
                    texte++;
                    break;

                case CardDivider:
                case CardSpacer:
                case CardSourceStatus:
                    // Alles drei ist im Toast bedeutungslos, aber harmlos: es
                    // wird beim Bauen der Zeilen übergangen.
                    break;

                default:
                    ohneEntsprechung.Add(CardElementNames.Beschreibe(element));
                    break;
            }
        }

        // EINE Meldung mit allen Namen, nicht eine je Baustein (Befund A1-14).
        // Beim Übernehmen einer Gesprächskarte kamen hier acht wortgleiche
        // Sätze heraus, von denen fünf ins Feld passten — sie sagten dem
        // Benutzer nur, dass es acht sind, und nicht welche.
        if (ohneEntsprechung.Count > 0)
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Warning,
                $"In einer Benachrichtigung {(ohneEntsprechung.Count == 1 ? "erscheint" : "erscheinen")} "
                    + $"{string.Join(", ", ohneEntsprechung)} nicht — Windows nimmt dort nur Text. "
                    + "Entfernen oder durch einen Textbaustein ersetzen."));
        }

        if (texte > MaxToastTextRows)
        {
            issues.Add(new ValidationIssue(
                path,
                IssueSeverity.Error,
                $"{texte} Textzeilen. Eine Benachrichtigung nimmt {MaxToastTextRows} "
                    + "plus die Attributionszeile; was nicht passt, schneidet Windows mitten "
                    + "im Wort ab."));
        }
    }
}
