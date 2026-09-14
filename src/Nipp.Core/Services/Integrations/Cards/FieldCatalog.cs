namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Was ein Feld auf einer Karte <b>bedeutet</b> — unabhängig davon, welche
/// Quelle es liefert.
///
/// <para>Eine Rolle ist die Antwort auf eine Frage, die jede Karte und jeder
/// Toast stellt: „wer ruft an?", „aus welcher Firma?", „woran wurde zuletzt
/// gearbeitet?". Welches Feld welcher Quelle das beantwortet, entscheidet die
/// Konfiguration; <b>dass</b> die Frage gestellt wird, gehört ins Produkt.</para>
/// </summary>
public enum FieldRole
{
    /// <summary>Keine besondere Bedeutung — das ist der Normalfall.</summary>
    None,

    /// <summary>Wer anruft.</summary>
    Name,

    /// <summary>Aus welcher Firma oder für welchen Kunden.</summary>
    Company,

    /// <summary>Was für ein Kontakt das ist — Kunde, Lieferant, Interessent.</summary>
    Type,

    /// <summary>Woran zuletzt gearbeitet wurde.</summary>
    Work,

    /// <summary>Worum es im letzten Gespräch ging.</summary>
    Summary,

    /// <summary>Wer intern zuletzt mit dieser Nummer zu tun hatte.</summary>
    Colleague,
}

/// <summary>Welche Art Wert hinter einem Feld steckt — für die Anzeige in der Palette.</summary>
public enum FieldShape
{
    Text,
    Number,
    Date,
    List,
    Boolean,
}

/// <summary>
/// Ein Eintrag der Palette.
/// </summary>
/// <param name="Path">
/// Wie es in einem Ausdruck heisst. Bei einem Feld einer Quelle
/// <c>quelle.feld</c>, bei einem eingebauten <c>number.e164</c>.
/// </param>
/// <param name="Label">Die Beschriftung, die vorgeschlagen wird.</param>
/// <param name="Group">
/// Woraus es kommt — der Anzeigename der Quelle, oder „Anruf" für die
/// eingebauten. Die Palette gruppiert danach.
/// </param>
/// <param name="Role">Die Bedeutung, wenn eine bekannt ist.</param>
/// <param name="Shape">Was für ein Wert.</param>
/// <param name="IsKnown">
/// Ob der Name im Katalog steht. <c>false</c> heisst: ein eigenes Feld dieser
/// Quelle — es wird angeboten, aber die Beschriftung ist geraten.
/// </param>
/// <param name="Hint">Was in der Palette darunter steht, oder <c>null</c>.</param>
public sealed record FieldEntry(
    string Path,
    string Label,
    string Group,
    FieldRole Role = FieldRole.None,
    FieldShape Shape = FieldShape.Text,
    bool IsKnown = true,
    string? Hint = null);

/// <summary>
/// Die kanonischen Feldnamen (§21, ADR-032).
///
/// <para><b>Warum es diesen Katalog gibt.</b> Bis zum 07.09.2026 stand das
/// Wissen „welches Feld heisst wie und bedeutet was" an drei Stellen und in
/// keiner vollständig: als fünf fest verdrahtete Arrays im
/// <c>ToastComposer</c>, als <c>coalesce</c>-Ketten mit bv2-Quellennamen in
/// <c>DefaultCards</c>, und als Verweis in <c>docs/integrations/einrichten.md</c>
/// — <b>wo keine Tabelle stand</b>. Der Verweis war gebrochen, und die
/// mitgelieferten Karten waren bei jedem anderen Kunden leer.</para>
///
/// <para><b>Was der Katalog nicht ist:</b> eine Pflicht. Eine Quelle darf
/// jedes Feld beliebig nennen — dann steht es in der Palette als eigenes Feld
/// und lässt sich genauso auf eine Karte ziehen. Der Katalog entscheidet nur,
/// was eine <b>Beschriftung</b> und eine <b>Rolle</b> bekommt, und damit, was
/// <c>role(...)</c> findet.</para>
///
/// <para>Die Tabelle für Menschen steht in
/// <c>docs/integrations/feldnamen.md</c> und wird von einem Test gegen diese
/// Datei gehalten.</para>
/// </summary>
public static class FieldCatalog
{
    /// <summary>Ein Eintrag des Katalogs.</summary>
    /// <param name="Name">Der Feldname ohne Quelle, etwa <c>contactName</c>.</param>
    /// <param name="Label">Die deutsche Beschriftung.</param>
    /// <param name="Role">Die Bedeutung.</param>
    /// <param name="Shape">Was für ein Wert.</param>
    /// <param name="RolePreference">
    /// Rang innerhalb der Rolle, kleiner gewinnt. Zwei Quellen mit derselben
    /// Priorität, aber verschiedenen Feldnamen — dann entscheidet dieser Rang,
    /// und die Entscheidung ist begründet: <c>contactName</c> ist ein voller
    /// Name aus einem Fachsystem, <c>name</c> kann alles sein.
    ///
    /// <para><b>Die Ränge sind zeichengleich die Reihenfolge der fünf Arrays,
    /// die vorher im <c>ToastComposer</c> standen</b> — auch dort, wo sich
    /// eine andere begründen liesse. Die Abnahme von K1 war, dass die
    /// bestehenden Toast-Tests <b>unverändert</b> durchlaufen; eine
    /// „bessere" Reihenfolge hätte dieselbe Abnahme unmöglich gemacht.</para>
    /// </param>
    public sealed record Entry(
        string Name,
        string Label,
        FieldRole Role = FieldRole.None,
        FieldShape Shape = FieldShape.Text,
        int RolePreference = 100);

    /// <summary>
    /// Der Katalog.
    ///
    /// <para><b>Die Rollen-Reihenfolge ist die aus dem <c>ToastComposer</c></b>,
    /// und die Begründungen von dort gelten weiter — sie stehen jetzt hier,
    /// weil hier entschieden wird.</para>
    /// </summary>
    public static IReadOnlyList<Entry> Entries { get; } =
    [
        // --- Wer ruft an (Rolle Name) ---
        //
        // `contactName` vorn: ein Fachsystem kennt den vollen Namen. Die
        // eigenen Kontakte liefern `displayName` und stehen trotzdem oft
        // zuerst — nicht wegen dieses Rangs, sondern weil ihre Quelle
        // Priorität 0 hat. Der Rang zählt erst bei Gleichstand.
        new("contactName", "Name", FieldRole.Name, FieldShape.Text, 10),
        new("displayName", "Anzeigename", FieldRole.Name, FieldShape.Text, 20),
        new("name", "Name", FieldRole.Name, FieldShape.Text, 30),
        new("fullName", "Vollständiger Name", FieldRole.Name, FieldShape.Text, 40),
        new("firstName", "Vorname"),
        new("lastName", "Nachname"),

        // --- Firma und Kunde (Rolle Company) ---
        // <b>`customerName` gehört zu Firma und nicht zu Name.</b> Der Name
        // eines Kunden kann eine Firma sein; der Name einer Person heisst
        // `contactName`. Der `ToastComposer` hat das immer so gehalten, die
        // mitgelieferte Karte bis zum 07.09.2026 anders — zwei Antworten auf
        // eine Frage. Beide bv2-Vorlagen mappen es wie hier.
        new("company", "Firma", FieldRole.Company, FieldShape.Text, 10),
        new("customerName", "Kundenname", FieldRole.Company, FieldShape.Text, 20),
        new("primaryCustomer", "Hauptkunde", FieldRole.Company, FieldShape.Text, 30),
        new("organization", "Organisation", FieldRole.Company, FieldShape.Text, 40),
        new("customerNames", "Alle Kunden", FieldRole.None, FieldShape.List),
        new("customerCount", "Anzahl Kunden", FieldRole.None, FieldShape.Number),
        new("weitereKunden", "Weitere Kunden"),
        new("customerNumber", "Kundennummer"),

        // --- Art des Kontakts (Rolle Type) ---
        new("contactType", "Art", FieldRole.Type, FieldShape.Text, 10),
        new("category", "Kategorie", FieldRole.Type, FieldShape.Text, 20),
        new("kind", "Art", FieldRole.Type, FieldShape.Text, 30),

        // --- Die letzte Arbeit (Rolle Work) ---
        //
        // `letzteArbeitZeile` ist die im Mapping fertig gebaute Zeile
        // („04.09. · Migration Telefonie · A. Beispiel") und steht deshalb
        // vorn: sie enthält Datum, Auftrag und den Kollegen in einem.
        new("letzteArbeitZeile", "Letzte Arbeit, ganze Zeile", FieldRole.Work, FieldShape.Text, 10),
        new("letzteArbeit", "Letzte Arbeit", FieldRole.Work, FieldShape.Text, 20),
        new("lastWork", "Letzte Arbeit", FieldRole.Work, FieldShape.Text, 30),
        new("lastActivity", "Letzte Aktivität", FieldRole.Work, FieldShape.Text, 40),
        new("letzteArbeitDatum", "Letzte Arbeit, Datum", FieldRole.None, FieldShape.Date),
        new("letzteArbeitWer", "Letzte Arbeit, wer", FieldRole.Colleague, FieldShape.Text, 10),

        // --- Das letzte Gespräch (Rolle Summary) ---
        new("letzteZusammenfassung", "Letztes Gespräch", FieldRole.Summary, FieldShape.Text, 10),
        new("lastCallSummary", "Letztes Gespräch", FieldRole.Summary, FieldShape.Text, 20),
        new("summary", "Zusammenfassung", FieldRole.Summary, FieldShape.Text, 30),
        new("offeneAufgabenZeile", "Offene Punkte"),
        new("verlauf", "Verlauf"),

        // --- Wer zuständig ist (Rolle Colleague) ---
        new("accountManager", "Account Manager", FieldRole.Colleague, FieldShape.Text, 20),
        new("technician", "Techniker", FieldRole.Colleague, FieldShape.Text, 30),
        new("owner", "Zuständig", FieldRole.Colleague, FieldShape.Text, 40),

        // --- Der Rest, ohne Rolle ---
        new("email", "E-Mail"),
        new("notiz", "Notiz"),
        new("openOrders", "Offene Aufträge", FieldRole.None, FieldShape.Number),
        new("revenue", "Umsatz", FieldRole.None, FieldShape.Number),
        new("vip", "VIP", FieldRole.None, FieldShape.Boolean),
        new("externalId", "Kennung im Fremdsystem"),
    ];

    private static readonly Dictionary<string, Entry> ByName =
        Entries.ToDictionary(static e => e.Name, StringComparer.Ordinal);

    /// <summary>
    /// Die eingebauten Felder — die es <b>immer</b> gibt, auch ohne eine
    /// einzige eingerichtete Quelle.
    ///
    /// Sie stehen in der Palette unter „Anruf" und „Kontakte". Ohne sie hätte
    /// eine Karte auf einem Gerät ohne Integrationen nichts anzubieten, und
    /// die Rückfallebene „sonst die Nummer" liesse sich nicht bauen.
    /// </summary>
    public static IReadOnlyList<FieldEntry> BuiltIn { get; } =
    [
        new("number.e164", "Nummer international", "Anruf", FieldRole.None, FieldShape.Text,
            Hint: "+41791234567 — die verlässliche Form"),
        new("number.national", "Nummer national", "Anruf", FieldRole.None, FieldShape.Text,
            Hint: "079 123 45 67"),
        new("number.digits", "Nummer, nur Ziffern", "Anruf", FieldRole.None, FieldShape.Text),

        // Der Anruf selbst. Auf der Karte im Gespräch sind Dauer und Ergebnis
        // leer — beides gibt es erst, wenn er vorbei ist; die Zeilen
        // verschwinden dort einfach. Auf der Karte in der Anrufliste sind sie
        // der Grund, warum jemand den Eintrag geöffnet hat.
        new("call.startedAt", "Zeitpunkt des Anrufs", "Anruf", FieldRole.None, FieldShape.Date,
            Hint: "Datum und Uhrzeit zusammen"),
        new("call.date", "Datum des Anrufs", "Anruf", FieldRole.None, FieldShape.Text),
        new("call.time", "Uhrzeit des Anrufs", "Anruf", FieldRole.None, FieldShape.Text,
            Hint: "Kurz — auf 400 Pixeln oft genug"),
        new("call.duration", "Gesprächsdauer", "Anruf", FieldRole.None, FieldShape.Text,
            Hint: "m:ss, ab einer Stunde h:mm:ss. Leer, wenn nie verbunden"),
        new("call.outcome", "Ergebnis", "Anruf", FieldRole.None, FieldShape.Text,
            Hint: "verpasst, angenommen, besetzt …"),
        new("call.direction", "Richtung", "Anruf", FieldRole.None, FieldShape.Text,
            Hint: "eingehend oder ausgehend"),

        new("contacts.displayName", "Name aus den eigenen Kontakten", "Kontakte",
            FieldRole.Name, FieldShape.Text,
            Hint: "Aus Team, Outlook und der Anrufliste — ohne Netz"),
        new("contacts.company", "Firma aus den eigenen Kontakten", "Kontakte",
            FieldRole.Company),
        new("contacts.source", "Woher der Kontakt kommt", "Kontakte"),
    ];

    /// <summary>Der Eintrag zu einem Feldnamen, oder <c>null</c>.</summary>
    public static Entry? Find(string? name) =>
        name is null ? null : ByName.GetValueOrDefault(name);

    /// <summary>
    /// Die Beschriftung zu einem Feldnamen.
    ///
    /// <b>Für einen unbekannten Namen wird nicht geraten, sondern gebaut:</b>
    /// <c>letzteArbeitZeile</c> würde ohne Katalog zu „Letzte arbeit zeile" —
    /// genau diese maschinellen Beschriftungen standen am 07.09.2026 auf der
    /// Karte, weil keine Zeile der mitgelieferten Karte traf. Ein
    /// zusammengesetzter Name wird deshalb nur getrennt und gross begonnen,
    /// ohne Anspruch auf Eleganz — er soll erkennbar als eigenes Feld
    /// auffallen.
    /// </summary>
    public static string LabelFor(string name)
    {
        if (Find(name) is { } entry)
        {
            return entry.Label;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder(name.Length + 8);

        foreach (var (zeichen, index) in name.Select(static (c, i) => (c, i)))
        {
            if (index > 0 && char.IsUpper(zeichen))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(zeichen));
            }
            else if (index == 0)
            {
                text.Append(char.ToUpperInvariant(zeichen));
            }
            else
            {
                text.Append(zeichen);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Die Feldnamen einer Rolle, in der Reihenfolge, in der sie gewinnen.
    ///
    /// Das ist die Liste, die <c>role('name')</c> abläuft — und es ist
    /// dieselbe, die vorher als Array im <c>ToastComposer</c> stand.
    /// </summary>
    public static IReadOnlyList<string> NamesFor(FieldRole role) =>
        role == FieldRole.None
            ? []
            :
            [
                .. Entries
                    .Where(e => e.Role == role)
                    .OrderBy(static e => e.RolePreference)
                    .ThenBy(static e => e.Name, StringComparer.Ordinal)
                    .Select(static e => e.Name),
            ];

    /// <summary>
    /// Eine Rolle aus ihrem Namen im Ausdruck — <c>role('name')</c>.
    ///
    /// Die Namen sind englisch und klein, wie alles in der
    /// Ausdruckssprache; die deutschen Beschriftungen stehen in der
    /// Oberfläche.
    /// </summary>
    public static FieldRole? RoleFromExpression(string? name) => name switch
    {
        "name" => FieldRole.Name,
        "company" => FieldRole.Company,
        "type" => FieldRole.Type,
        "work" => FieldRole.Work,
        "summary" => FieldRole.Summary,
        "colleague" => FieldRole.Colleague,
        _ => null,
    };

    /// <summary>Alle Rollennamen, für Fehlermeldungen und die Oberfläche.</summary>
    public static IReadOnlyList<string> RoleNames { get; } =
        ["name", "company", "type", "work", "summary", "colleague"];
}
