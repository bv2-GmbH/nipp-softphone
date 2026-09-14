using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Die Zeilen eines Toasts für einen eingehenden Anruf (§8.6, ADR-030).
///
/// <para><b>Warum genau drei plus eine.</b> Windows nimmt in einer
/// Benachrichtigung höchstens drei Textelemente plus die Attributionszeile,
/// die klein und grau unter den anderen steht. Mehr geht nicht, und was in
/// eine Zeile nicht passt, schneidet Windows ab. Alles Weitere steht in der
/// Anruferkarte im Fenster, die beim Klingeln ohnehin erscheint.</para>
/// </summary>
/// <param name="Line1">Wer anruft — Name, Firma, Art des Kontakts.</param>
/// <param name="Line2">Woran zuletzt gearbeitet wurde, mit Datum und Kollegen.</param>
/// <param name="Line3">Worum es im letzten Gespräch ging.</param>
/// <param name="Attribution">Die Rufnummer, klein unter allem.</param>
public sealed record ToastLines(
    string Line1,
    string? Line2 = null,
    string? Line3 = null,
    string? Attribution = null)
{
    /// <summary>Ob mehr dasteht als der Name allein — dann lohnt ein Ersetzen des Toasts.</summary>
    public bool HasContext =>
        !string.IsNullOrEmpty(Line2) || !string.IsNullOrEmpty(Line3);
}

/// <summary>
/// Baut die Textzeilen des Toasts aus dem Anruferkontext (§8.6, §21).
///
/// <para><b>Warum das hier steht und nicht im ToastService.</b>
/// <c>Nipp.App</c> hat kein Testprojekt — was dort liegt, ist nur am Gerät
/// prüfbar. Beim Toast ist das schon einmal teuer geworden: die Regel „beginnt
/// hier ein Anruf zu klingeln?" stand wörtlich gleich in <c>MainWindow</c> und
/// im <c>ToastService</c> und war an beiden Stellen falsch, wodurch ein
/// eingehender Anruf völlig unsichtbar blieb.</para>
///
/// <para><b>Warum nach Feldnamen und nicht nach Quelle.</b> Die Plattform ist
/// generisch (§21): welche Quellen es gibt, entscheidet die Konfiguration, und
/// sie heissen beim einen Kunden <c>crm</c> und beim nächsten anders. Der
/// Toast fragt deshalb nicht „<c>crm.contactName</c>", sondern „hat
/// irgendeine Quelle ein Feld <c>contactName</c>?" — in der Reihenfolge der
/// Quellenpriorität. Was ein Mapping wie nennen soll, steht in
/// <c>docs/integrations/feldnamen.md</c> — bis zum 07.09.2026 verwies dieser
/// Kommentar auf <c>einrichten.md</c>, <b>wo keine Tabelle stand</b>. Die
/// mitgelieferten Vorlagen halten sich daran.</para>
///
/// <para><b>Was nicht hineingehört.</b> Kein Feldinhalt ins Protokoll (§21.2),
/// und nichts, was nicht in eine Benachrichtigung gehört: der Toast bleibt im
/// Windows-Benachrichtigungscenter stehen, bis ihn jemand entfernt. Deshalb
/// die kurze Zusammenfassung und nicht die lange, und deshalb entfernt der
/// <c>ToastService</c> den Toast, wenn der Anruf vorbei ist (ADR-027 zieht
/// dieselbe Grenze für das Journal).</para>
/// </summary>
public static class ToastComposer
{
    /// <summary>
    /// <b>Hier standen fünf fest verdrahtete Feldlisten.</b> Sie sagten,
    /// welcher Feldname „wer ruft an" beantwortet, welcher „woran wurde
    /// zuletzt gearbeitet", und in welcher Reihenfolge — dasselbe Wissen, das
    /// die Karte in ihren <c>coalesce</c>-Ketten trug, und dasselbe, auf das
    /// <c>docs/integrations/einrichten.md</c> verwies, ohne es zu enthalten.
    ///
    /// <para>Drei Orte für eine Entscheidung, und keiner vollständig. Genau
    /// diese Bauart hat schon einmal einen eingehenden Anruf unsichtbar
    /// gemacht: die Frage „beginnt hier ein Anruf zu klingeln?" stand wörtlich
    /// gleich in <c>MainWindow</c> und im <c>ToastService</c> und war an beiden
    /// Stellen falsch. Jetzt steht die Zuordnung einmal, im
    /// <see cref="FieldCatalog"/>, und ein Test hält fest, dass keiner der
    /// alten Namen dabei verlorenging.</para>
    /// </summary>
    private static IReadOnlyList<string> Fields(FieldRole role) =>
        FieldCatalog.NamesFor(role);

    /// <summary>
    /// Wie lang eine Zeile werden darf, bevor gekürzt wird.
    ///
    /// Windows schneidet selbst ab, aber mitten im Wort und ohne Zeichen
    /// dafür. Genau diese Kerbe steckt schon in <c>summary_short</c> von
    /// das Gesprächsjournal, das nicht kurz geschrieben, sondern bei 120 Zeichen
    /// abgeschnitten ist.
    /// </summary>
    public const int MaxLineLength = 110;

    /// <summary>
    /// Baut die Zeilen. <paramref name="snapshot"/> darf <c>null</c> sein —
    /// dann steht dort dasselbe wie vor der Anreicherung.
    /// </summary>
    /// <param name="snapshot">Was die Quellen bisher geliefert haben.</param>
    /// <param name="fallbackLabel">
    /// Was ohne Kontext gilt: der Anzeigename aus dem SIP-Signal, sonst die
    /// Nummer (<c>CallInfo.DisplayLabel</c>).
    /// </param>
    /// <param name="number">Die Rufnummer für die Attributionszeile.</param>
    /// <param name="card">
    /// Eine eingerichtete Karte der Art <see cref="CardKind.Toast"/>, oder
    /// <c>null</c> für die mitgelieferte Zusammensetzung.
    ///
    /// <para><b>Warum die mitgelieferte Fassung Code bleibt und nicht selbst
    /// eine Karte ist.</b> Sonst sind die mitgelieferten Karten gewöhnliche
    /// Beschreibungen (§I4) — hier geht das nicht, und der Grund ist eine
    /// Modellierungsgrenze: eine Karte besteht aus <b>unabhängigen</b>
    /// Textzeilen, der Toast setzt seine erste Zeile aber aus drei Werten
    /// <b>zusammen</b> — „Hans Muster · Muster AG (Kunde)", jeder Teil einzeln
    /// weglassbar, und die Firma unterdrückt, wenn sie dasselbe sagt wie der
    /// Name. Dasselbe bei der Arbeitszeile, an die der Kollege nur angehängt
    /// wird, wenn er nicht schon darin steht.</para>
    ///
    /// <para>Als Karte ausgedrückt wären das drei
    /// <c>concat(if(isEmpty(...)))</c>-Ungetüme: technisch richtig, im
    /// Designer nur als „Ausdruck" bearbeitbar, und für niemanden lesbar. Die
    /// bewährte Zusammensetzung bleibt deshalb hier — sie ist getestet und am
    /// Gerät belegt (ADR-030) —, und eine eingerichtete Karte <b>ersetzt</b>
    /// sie. Wer den Toast selbst zusammenstellt, bekommt drei einfache Zeilen
    /// statt der Feinheiten; was er wählt, sieht er in der Vorschau.</para>
    /// </param>
    public static ToastLines Compose(
        ContextSnapshot? snapshot,
        string fallbackLabel,
        string? number,
        CompiledCard? card = null)
    {
        var nummer = string.IsNullOrWhiteSpace(number)
            ? null
            : PhoneNumberFormat.ForDisplay(number);

        if (snapshot is null)
        {
            return new ToastLines(fallbackLabel, Attribution: nummer);
        }

        if (FromCard(card, snapshot, nummer) is { } ausKarte)
        {
            return ausKarte;
        }

        var name = First(snapshot, Fields(FieldRole.Name)) ?? fallbackLabel;
        var firma = First(snapshot, Fields(FieldRole.Company));
        var art = First(snapshot, Fields(FieldRole.Type));

        // „Hans Muster · Muster AG (Kunde)" — jeder Teil einzeln
        // weglassbar. Ist die Firma derselbe Text wie der Name, steht sie
        // nicht zweimal da: bei einer Firma als Kontakt liefern beide Felder
        // dasselbe.
        var kopf = name;

        if (!string.IsNullOrEmpty(firma)
            && !string.Equals(firma, name, StringComparison.OrdinalIgnoreCase))
        {
            kopf = $"{kopf} · {firma}";
        }

        if (!string.IsNullOrEmpty(art))
        {
            kopf = $"{kopf} ({art})";
        }

        return new ToastLines(
            Cut(kopf)!,
            Cut(WorkLine(snapshot)),
            Cut(SummaryLine(snapshot)),
            nummer);
    }

    /// <summary>
    /// Die Zeilen aus einer eingerichteten Karte, oder <c>null</c>, wenn keine
    /// gilt.
    ///
    /// <para><b>Die drei ersten sichtbaren Textbausteine</b>, in der
    /// Reihenfolge, in der sie auf der Karte stehen. Windows nimmt drei; der
    /// Validator meldet eine vierte, und hier wird sie schlicht nicht
    /// gezeigt — was nicht passt, soll nicht mitten im Wort abgeschnitten
    /// erscheinen.</para>
    ///
    /// <para>Ein Feld, ein Knopf oder eine Trennlinie auf einer Toast-Karte
    /// hat keine Entsprechung und wird übergangen. Der Validator sagt das beim
    /// Einrichten; hier wäre eine Meldung zu spät.</para>
    /// </summary>
    private static ToastLines? FromCard(
        CompiledCard? card,
        ContextSnapshot snapshot,
        string? nummer)
    {
        if (card is null || card.Kind != CardKind.Toast)
        {
            return null;
        }

        var modell = CardLayoutEngine.Build(card, snapshot);

        if (!modell.HasContent)
        {
            return null;
        }

        var zeilen = modell.Sections
            .Where(static s => s.Visible)
            .SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements)
            .OfType<CardTextModel>()
            .Where(static t => t.Visible && t.Text.Length > 0)
            .Select(static t => t.Text.Trim())
            .Take(CardDefinitionValidator.MaxToastTextRows)
            .ToList();

        if (zeilen.Count == 0)
        {
            return null;
        }

        return new ToastLines(
            Cut(zeilen[0])!,
            zeilen.Count > 1 ? Cut(zeilen[1]) : null,
            zeilen.Count > 2 ? Cut(zeilen[2]) : null,
            nummer);
    }

    /// <summary>
    /// Die Zeile zur letzten Arbeit. Fehlt die fertige Zeile aus dem Mapping,
    /// wird sie aus Beschreibung und Kollegen zusammengesetzt.
    /// </summary>
    private static string? WorkLine(ContextSnapshot snapshot)
    {
        if (First(snapshot, Fields(FieldRole.Work)) is not { } arbeit)
        {
            return null;
        }

        var wer = First(snapshot, Fields(FieldRole.Colleague));

        return string.IsNullOrEmpty(wer) || arbeit.Contains(wer, StringComparison.OrdinalIgnoreCase)
            ? arbeit
            : $"{arbeit} · {wer}";
    }

    private static string? SummaryLine(ContextSnapshot snapshot) =>
        First(snapshot, Fields(FieldRole.Summary)) is { } zusammenfassung
            ? $"Zuletzt: {zusammenfassung}"
            : null;

    /// <summary>
    /// Der erste nicht leere Wert zu einem dieser Feldnamen, über alle Quellen
    /// in ihrer Reihenfolge.
    ///
    /// <b>Feld vor Quelle:</b> geprüft wird Feldname für Feldname, und
    /// innerhalb eines Feldnamens Quelle für Quelle. Sonst gewönne ein
    /// unscharfer Treffer der ersten Quelle über den genauen der zweiten.
    /// </summary>
    private static string? First(ContextSnapshot snapshot, IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
        {
            foreach (var fragment in snapshot.SourcesByPriority)
            {
                if (!fragment.HasData)
                {
                    continue;
                }

                if (fragment.Fields.TryGetValue(field, out var value)
                    && !value.IsEmpty
                    && value.AsDisplayText() is { Length: > 0 } text)
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Dasselbe zu einer <b>Bedeutung</b> — der Weg, den seit ADR-043 auch die
    /// Gesprächsansicht geht. Steht in <see cref="ContextRoles"/>, damit die
    /// Regel nicht zweimal existiert.
    /// </summary>
    private static string? First(ContextSnapshot snapshot, FieldRole role) =>
        ContextRoles.Text(snapshot, role);

    /// <summary>
    /// Kürzt an der Wortgrenze, mit Auslassungszeichen.
    /// </summary>
    private static string? Cut(string? text)
    {
        if (text is null || text.Length <= MaxLineLength)
        {
            return text;
        }

        var schnitt = text.LastIndexOf(' ', MaxLineLength - 1);

        // Ein Wort, das allein schon zu lang ist, wird hart geschnitten —
        // sonst bliebe die Zeile leer.
        if (schnitt < MaxLineLength / 2)
        {
            schnitt = MaxLineLength - 1;
        }

        return string.Concat(text.AsSpan(0, schnitt).TrimEnd(), "…");
    }
}
