using System.Text.Json.Serialization;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Wofür eine Karte gedacht ist (§21).
///
/// Dieselben Daten, verschiedene Layouts — der Auftrag nennt das
/// ausdrücklich: eine kompakte Karte für den eingehenden Anruf, eine
/// ausführliche im Gespräch, später eine für die Anrufliste.
/// </summary>
public enum CardKind
{
    /// <summary>Wenige Zeilen, für den Moment des Klingelns.</summary>
    IncomingCompact,

    /// <summary>Die ausführliche Karte im laufenden Gespräch.</summary>
    ActiveExpanded,

    /// <summary>
    /// Der aufgeklappte Eintrag in der Anrufliste (§22.3, ADR-036).
    ///
    /// <para>Stand hier bis zum 07.09.2026 als „noch nicht verwendet" —
    /// gebaut, aber nirgends angeschlossen. Der Kontextbereich baute seine
    /// Zeilen selbst und beschriftete sie maschinell aus dem Feldnamen; das
    /// war dieselbe Lücke wie bei den Karten in der Konfiguration.</para>
    /// </summary>
    History,

    /// <summary>
    /// Die Benachrichtigung beim eingehenden Anruf (§8.6, ADR-030, ADR-034).
    ///
    /// <b>Eine Karte mit einer harten Grenze:</b> Windows nimmt drei
    /// Textzeilen plus die Attributionszeile, und was nicht passt, schneidet es
    /// mitten im Wort ab. Deshalb steht der Deckel im
    /// <see cref="CardDefinitionValidator"/> und nicht in der Anleitung — eine
    /// vierte Zeile wäre nicht falsch aussehend, sondern unsichtbar.
    /// </summary>
    Toast,
}

/// <summary>Wie ein Text dargestellt wird.</summary>
public enum CardTextStyle
{
    /// <summary>Der Name oben.</summary>
    Title,

    /// <summary>Die Zeile darunter.</summary>
    Subtitle,

    /// <summary>Gewöhnlicher Text.</summary>
    Body,

    /// <summary>Klein und zurückhaltend.</summary>
    Caption,
}

/// <summary>
/// Eine Karte, wie der Administrator sie beschreibt (§21).
///
/// <b>Kein HTML und kein XAML</b> (§21.2). Eine Karte besteht aus einer festen
/// Menge eigener Bausteine, und das Layout wird über Struktur beschrieben —
/// Abschnitte, Zeilen, Spalten —, nicht über Pixelkoordinaten. Der Grund ist
/// nicht Bequemlichkeit: freies Markup aus einer verteilten Konfigurationsdatei
/// wäre eine Ausführungsfläche im Programm, und ein Pixelraster wäre in einem
/// 400 Pixel breiten Fenster ohnehin unbedienbar.
/// </summary>
/// <param name="Id">Kennung der Karte.</param>
/// <param name="Name">Wie sie in den Einstellungen heisst.</param>
/// <param name="Kind">Wofür sie gedacht ist.</param>
/// <param name="Sections">Die Abschnitte, von oben nach unten.</param>
/// <param name="SchemaVersion">
/// Version der Kartenbeschreibung. Ein Renderer, der eine neuere Karte
/// bekommt, zeigt, was er versteht, statt abzustürzen — die Version sagt ihm,
/// dass er mit Unbekanntem rechnen muss.
/// </param>
public sealed record CardDefinition(
    string Id,
    string Name,
    [property: JsonConverter(typeof(CamelCaseEnumConverter))] CardKind Kind,
    IReadOnlyList<CardSection> Sections,
    int SchemaVersion = 1);

/// <summary>
/// Ein Abschnitt der Karte.
/// </summary>
/// <param name="Id">Kennung, für den späteren Designer.</param>
/// <param name="Title">Überschrift, oder <c>null</c>.</param>
/// <param name="Rows">Die Zeilen.</param>
/// <param name="VisibleWhen">
/// Bedingung als Ausdruck. Leer heisst „immer". Ein Abschnitt, dessen
/// Bedingung sich nicht entscheiden lässt, wird <b>nicht</b> gezeigt — die
/// Gegenrichtung wäre eine Karte, die bei fehlenden Daten etwas behauptet.
/// </param>
public sealed record CardSection(
    string Id,
    string? Title,
    IReadOnlyList<CardRow> Rows,
    string? VisibleWhen = null);

/// <summary>Eine Zeile aus Spalten.</summary>
public sealed record CardRow(IReadOnlyList<CardColumn> Columns);

/// <summary>
/// Eine Spalte.
/// </summary>
/// <param name="Span">
/// Wie viele Rastereinheiten sie belegt, von <see cref="CardLayout.Columns"/>.
/// </param>
/// <param name="Elements">Was darin steht, von oben nach unten.</param>
public sealed record CardColumn(int Span, IReadOnlyList<CardElement> Elements);

/// <summary>Feste Grössen des Kartenrasters.</summary>
public static class CardLayout
{
    /// <summary>
    /// Die Breite des Rasters in Einheiten.
    ///
    /// <b>Sechs und nicht zwölf.</b> Das Fenster ist rund 400 logische Pixel
    /// breit (§20.1); eine Zwölftelspalte wäre dort gut dreissig Pixel und
    /// zeigte nichts. Sechs erlaubt Hälften, Drittel und Zweidrittel — mehr
    /// braucht in dieser Breite niemand.
    /// </summary>
    public const int Columns = 6;

    /// <summary>Höchstzahl Abschnitte, Zeilen je Abschnitt und Elemente je Spalte.</summary>
    public const int MaxSections = 16;
    public const int MaxRowsPerSection = 24;
    public const int MaxElementsPerColumn = 12;
}

/// <summary>
/// Ein Baustein auf der Karte.
///
/// <b>Die Menge ist geschlossen</b> und steht vollständig in dieser Datei
/// (§21.2). Was hier nicht steht, lässt sich in einer Konfiguration nicht
/// erzeugen — und ein unbekannter Typ aus einer neueren Kartenversion wird
/// beim Zeichnen übergangen, nicht geraten.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CardText), "text")]
[JsonDerivedType(typeof(CardField), "field")]
[JsonDerivedType(typeof(CardBadge), "badge")]
[JsonDerivedType(typeof(CardDivider), "divider")]
[JsonDerivedType(typeof(CardSpacer), "spacer")]
[JsonDerivedType(typeof(CardButton), "button")]
[JsonDerivedType(typeof(CardLink), "link")]
[JsonDerivedType(typeof(CardSourceStatus), "sourceStatus")]
public abstract record CardElement
{
    /// <summary>Bedingung als Ausdruck. Leer heisst „immer".</summary>
    public string? VisibleWhen { get; init; }
}

/// <summary>Ein Text. <paramref name="Value"/> ist ein Ausdruck, kein Literal.</summary>
public sealed record CardText(string Value) : CardElement
{
    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public CardTextStyle Style { get; init; } = CardTextStyle.Body;

    /// <summary>Über wie viele Zeilen der Text laufen darf.</summary>
    public int MaxLines { get; init; } = 1;
}

/// <summary>Eine Beschriftung mit Wert — die häufigste Zeile einer Karte.</summary>
public sealed record CardField(string Label, string Value) : CardElement
{
    /// <summary>
    /// Was steht, wenn der Wert leer ist. <c>null</c> heisst: die Zeile
    /// verschwindet.
    ///
    /// Beides ist sinnvoll und der Unterschied gehört dem Administrator:
    /// „Offene Aufträge: —" sagt, dass gefragt wurde; eine fehlende Zeile
    /// hält die Karte kurz.
    ///
    /// <para><b>Wird immer geschrieben, auch als <c>null</c></b> — und das ist
    /// kein Schönheitswunsch. Die Optionen der Datei stehen auf
    /// <c>WhenWritingNull</c>, damit sie kurz bleibt. Für ein Feld, dessen
    /// Vorgabe <b>nicht</b> <c>null</c> ist, heisst das: das ausdrückliche
    /// <c>null</c> verschwindet beim Speichern und wird beim Lesen zu
    /// <c>"—"</c>. Eine Karte ändert dann beim Weg durch die Datei ihr
    /// Verhalten — Zeilen, die verschwinden sollten, stehen als <c>"—"</c>
    /// da. Gefunden hat das der Rundlauf-Test in K0; die mitgelieferten Karten
    /// setzen <c>null</c> sechsmal.</para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? EmptyText { get; init; } = "—";

    /// <summary>
    /// Ob die Beschriftung dasteht. <c>false</c> heisst: nur der Wert, ohne
    /// Einzug — „Hans Muster" statt „Name: Hans Muster" (ADR-037).
    ///
    /// <para><b>Ein eigener Schalter und keine leere Beschriftung.</b> Die
    /// leere Zeichenkette liesse offen, ob jemand die Beschriftung abgewählt
    /// oder schlicht vergessen hat — und sie wird auch bei abgeschalteter
    /// Anzeige noch gebraucht: der Renderer trägt sie als
    /// <c>AutomationProperties.Name</c> an den Wert. Eine Sprachausgabe soll
    /// „Firma: Muster AG" sagen, auch wenn nur „Muster AG" dasteht (§8.4).</para>
    ///
    /// <para>Ein <c>bool</c> wird immer geschrieben — <c>WhenWritingNull</c>
    /// betrifft ihn nicht. Die Falle, die <c>EmptyText</c> das Speichern nicht
    /// überleben liess, gibt es hier also nicht; geprüft ist es trotzdem.</para>
    /// </summary>
    public bool ShowLabel { get; init; } = true;
}

/// <summary>Ein kleines Abzeichen, etwa „VIP".</summary>
public sealed record CardBadge(string Text) : CardElement
{
    /// <summary>
    /// Der Ton als <b>Ausdruck</b>, damit er von den Daten abhängen kann:
    /// <c>if(erp.openOrders &gt; 5, 'warning', 'neutral')</c>. Erlaubt sind
    /// <c>neutral</c>, <c>info</c>, <c>success</c>, <c>warning</c> und
    /// <c>danger</c>; alles andere wird neutral.
    /// </summary>
    public string Tone { get; init; } = "'neutral'";
}

/// <summary>Eine Trennlinie.</summary>
public sealed record CardDivider : CardElement;

/// <summary>Wie viel Luft ein <see cref="CardSpacer"/> lässt.</summary>
public enum CardSpacerSize
{
    /// <summary>Etwas Luft zwischen zwei Zeilen.</summary>
    Small,

    /// <summary>Ein Bereichswechsel.</summary>
    Medium,

    /// <summary>Deutlich getrennt.</summary>
    Large,
}

/// <summary>
/// Ein Abstand — Bereiche trennen, ohne einen Strich zu ziehen.
///
/// <para><b>Drei Stufen und keine Pixelzahl</b> (ADR-037). §21.2 beschreibt
/// Layout über Struktur und nicht über Bildschirmmasse; ein Feld für Pixel
/// wäre die erste Stelle, an der eine Konfigurationsdatei welche setzt — und
/// die zweite wäre die Frage, was bei 150 % Skalierung gilt. Klein, mittel und
/// gross decken ab, wofür ein Abstand auf 400 Pixeln taugt.</para>
///
/// <para>Im Toast erscheint er nicht, so wenig wie die Trennlinie: Windows
/// nimmt dort nur Text. Übergangen wird er, nicht gemeldet.</para>
/// </summary>
public sealed record CardSpacer : CardElement
{
    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public CardSpacerSize Size { get; init; } = CardSpacerSize.Medium;
}

/// <summary>Eine Schaltfläche mit einer Aktion.</summary>
public sealed record CardButton(string Label, CardAction Action) : CardElement
{
    /// <summary>Bedingung, ob sie bedienbar ist. Leer heisst „immer".</summary>
    public string? EnabledWhen { get; init; }
}

/// <summary>Ein Verweis. <paramref name="Url"/> ist eine Vorlage.</summary>
public sealed record CardLink(string Label, string Url) : CardElement;

/// <summary>
/// Der Zustand einer Quelle — „CRM wird gefragt …", „ERP antwortet nicht".
///
/// Ein eigener Baustein, weil er nicht aus Daten entsteht, sondern aus dem
/// Verlauf der Abfrage. §21.1 verlangt, dass die Oberfläche auf diese
/// Zustände reagieren kann.
/// </summary>
public sealed record CardSourceStatus(string Source) : CardElement;

/// <summary>
/// Was eine Schaltfläche tut.
///
/// <b>Geschlossene Menge</b>, aus demselben Grund wie bei den Bausteinen: was
/// hier nicht steht, lässt sich über eine Konfigurationsdatei nicht auslösen.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpenUrlAction), "openUrl")]
[JsonDerivedType(typeof(DialAction), "dial")]
[JsonDerivedType(typeof(CopyAction), "copy")]
public abstract record CardAction;

/// <summary>
/// Öffnet eine Adresse im Browser.
///
/// Nur <c>http</c> und <c>https</c>, geprüft beim Bauen der Karte <b>und</b>
/// beim Ausführen — alles andere landete sonst in <c>ShellExecute</c>, und
/// das startet, was auch immer Windows hinter einem Schema vermutet (§21.2).
/// </summary>
public sealed record OpenUrlAction(string Url) : CardAction;

/// <summary>Wählt eine Nummer.</summary>
public sealed record DialAction(string Number) : CardAction;

/// <summary>Legt einen Wert in die Zwischenablage.</summary>
public sealed record CopyAction(string Value) : CardAction;
