using System.Globalization;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Ein Wert im Kontextmodell (§21.1) — das, was aus einer fremden Antwort
/// nach dem Mapping übrig bleibt.
///
/// <b>Warum ein eigener Typ und nicht <c>JsonNode</c> oder <c>object?</c>.</b>
/// Ein <c>JsonNode</c> würde die Bibliothek und die Form der fremden Antwort
/// durch das ganze Programm tragen — genau das, was §21.2 für JSONPath
/// verbietet und was bei den SDK-Typen schon einmal geregelt werden musste
/// (§6). Ein <c>object?</c> wiederum verschiebt jeden Typfehler auf die
/// Laufzeit und mitten in die Oberfläche.
///
/// Die Hierarchie ist <b>geschlossen</b>: alle Ableitungen stehen in dieser
/// Datei. Wer einen <c>switch</c> darüber schreibt, kann ihn vollständig
/// machen.
///
/// <b>Zahlen sind <c>decimal</c>.</b> Was hier ankommt, sind Kundennummern,
/// Stückzahlen und Umsätze — Dezimalzahlen aus JSON-Literalen. <c>double</c>
/// würde <c>125000.50</c> zu etwas machen, das sich beim Formatieren als
/// <c>125000.49999</c> zeigen kann, und ein falsch dargestellter Umsatz auf
/// einer Karte ist schlimmer als gar keiner. Was nicht in <c>decimal</c>
/// passt, bleibt Text.
/// </summary>
public abstract record ContextValue
{
    /// <summary>Kein Wert. Ein fehlendes Feld ist kein Fehler, sondern das hier.</summary>
    public static ContextValue Null { get; } = new NullValue();

    /// <summary>Ob dieser Wert für die Anzeige leer ist — <c>isEmpty</c> in Ausdrücken.</summary>
    public abstract bool IsEmpty { get; }

    /// <summary>
    /// Die Darstellung für Text: Verkettung, Beschriftungen, Vorlagen.
    ///
    /// <b>Immer mit <see cref="CultureInfo.InvariantCulture"/>.</b> Diese
    /// Zeichenfolge geht auch in URLs und Anfrageparameter; ein Dezimalkomma
    /// aus der Schweizer Kultur würde dort zu einer anderen Zahl. Für die
    /// Anzeige gibt es <c>formatNumber</c> und <c>formatDate</c>, die
    /// ausdrücklich formatieren.
    /// </summary>
    public abstract string AsText();

    /// <summary>
    /// Der Wert, wie ihn ein Mensch lesen soll.
    ///
    /// <para>Getrennt von <see cref="AsText"/>, weil die beiden verschiedene
    /// Leser haben. <c>AsText</c> ist die kanonische Form — sie geht in
    /// Ausdrücke, Vergleiche und Vorlagen, und ein Datum muss dort ISO 8601
    /// bleiben, sonst hinge das Ergebnis an der Ländereinstellung des
    /// Arbeitsplatzes. Auf der Karte und im Journal las man dadurch aber
    /// „2027-01-01T00:00:00.0000000+01:00", wo „01.01.2027" gemeint war.</para>
    ///
    /// <para>Nur der Datumsfall weicht ab; alles andere ist in beiden Formen
    /// dasselbe.</para>
    /// </summary>
    public virtual string AsDisplayText() => AsText();

    public static ContextValue FromText(string? value) =>
        string.IsNullOrEmpty(value) ? Null : new TextValue(value);

    public static ContextValue FromNumber(decimal value) => new NumberValue(value);

    public static ContextValue FromBoolean(bool value) => new BooleanValue(value);

    public static ContextValue FromDate(DateTimeOffset value) => new DateValue(value);

    /// <summary>Eine Liste. Leer wird zu <see cref="Null"/> — sonst zeigte die Karte eine leere Zeile.</summary>
    public static ContextValue FromList(IReadOnlyList<ContextValue> items) =>
        items.Count == 0 ? Null : new ListValue(items);

    /// <summary>
    /// Der Wert als Zahl — <b>nur wenn er eine ist</b>.
    ///
    /// <b>Text wird nicht umgedeutet</b>, und das ist der wichtigste Satz an
    /// diesem Typ. Der erste Entwurf las Text mit <c>decimal.TryParse</c>, und
    /// der Test deckte auf, was das bedeutet: <c>+41791234567</c> ist für
    /// <c>TryParse</c> eine gültige Zahl mit Vorzeichen. Eine Rufnummer wurde
    /// damit zu 41'791'234'567 — <c>nummer + 1</c> ergab ein Ergebnis,
    /// <c>nummer &gt; 5</c> war wahr, und <c>formatNumber</c> hätte aus der
    /// Nummer eine gruppierte Zahl gemacht. Auf einer Karte, die Rufnummern
    /// anzeigt, ist das die schlimmste Art von Fehler: sie sieht plausibel aus.
    ///
    /// Wer eine Zahl aus einem Textfeld braucht — es gibt APIs, die
    /// <c>"openOrders": "3"</c> liefern —, sagt das im Mapping ausdrücklich
    /// mit <c>as: number</c>. Dann steht die Absicht in der Konfiguration und
    /// nicht in einer Vermutung dieser Klasse (ADR-016: kein implizites
    /// Konvertieren).
    /// </summary>
    public virtual bool TryAsNumber(out decimal number)
    {
        number = 0;
        return false;
    }

    /// <summary>
    /// Der Wert als Wahrheitswert — <b>nur</b> wenn er einer ist.
    ///
    /// Absichtlich ohne die üblichen Bequemlichkeiten: eine nicht leere
    /// Zeichenfolge ist hier nicht „wahr", und 0 ist nicht „falsch". In einer
    /// Bedingung, die entscheidet, ob eine Karte etwas anzeigt, ist eine
    /// stillschweigende Umdeutung die Ursache, die man am schwersten findet.
    /// Wer prüfen will, ob etwas da ist, schreibt <c>!isEmpty(x)</c>.
    /// </summary>
    public virtual bool TryAsBoolean(out bool value)
    {
        value = false;
        return false;
    }
}

/// <summary>
/// Ein Text.
///
/// <b>Weder Zahl noch Wahrheitswert</b> — er erbt beide <c>TryAs</c>-Methoden
/// unverändert, und die sagen Nein. Die Begründung steht an
/// <see cref="ContextValue.TryAsNumber"/>: eine Rufnummer ist für
/// <c>decimal.TryParse</c> eine gültige Zahl.
/// </summary>
public sealed record TextValue(string Value) : ContextValue
{
    public override bool IsEmpty => Value.Length == 0;

    public override string AsText() => Value;
}

/// <summary>Eine Zahl.</summary>
public sealed record NumberValue(decimal Value) : ContextValue
{
    /// <summary>
    /// Eine Zahl ist nie leer — auch nicht die Null. „Null offene Aufträge"
    /// ist eine Antwort, kein fehlender Wert.
    /// </summary>
    public override bool IsEmpty => false;

    public override string AsText() => Value.ToString(CultureInfo.InvariantCulture);

    public override bool TryAsNumber(out decimal number)
    {
        number = Value;
        return true;
    }
}

/// <summary>Ein Wahrheitswert.</summary>
public sealed record BooleanValue(bool Value) : ContextValue
{
    public override bool IsEmpty => false;

    /// <summary>
    /// <c>true</c> und <c>false</c> in Kleinbuchstaben — die Schreibweise von
    /// JSON und der Ausdruckssprache, nicht die von .NET (<c>True</c>).
    /// </summary>
    public override string AsText() => Value ? "true" : "false";

    public override bool TryAsBoolean(out bool value)
    {
        value = Value;
        return true;
    }
}

/// <summary>Ein Zeitpunkt.</summary>
public sealed record DateValue(DateTimeOffset Value) : ContextValue
{
    public override bool IsEmpty => false;

    /// <summary>ISO 8601 — dieselbe Form, in der er hereinkam.</summary>
    public override string AsText() => Value.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Für die Anzeige: Datum in der Schreibweise des Arbeitsplatzes, und die
    /// Uhrzeit nur dann, wenn sie etwas aussagt.
    ///
    /// <para>Mitternacht wird weggelassen — ein Datumsfeld aus einem CRM trägt
    /// meistens keine Uhrzeit, und „01.01.2027 00:00" behauptet eine Genauigkeit,
    /// die es nicht gibt.</para>
    /// </summary>
    public override string AsDisplayText()
    {
        var lokal = Value.ToLocalTime();

        return lokal.TimeOfDay == TimeSpan.Zero
            ? lokal.ToString("d", CultureInfo.CurrentCulture)
            : lokal.ToString("g", CultureInfo.CurrentCulture);
    }
}

/// <summary>Mehrere Werte. Entsteht bei einem Pfad mit mehreren Treffern.</summary>
public sealed record ListValue(IReadOnlyList<ContextValue> Items) : ContextValue
{
    public override bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Mit Komma verbunden. Für eine andere Trennung gibt es <c>join</c> —
    /// diese Darstellung ist die Rückfallebene, damit eine Liste auf einer
    /// Karte nicht als Typname erscheint.
    /// </summary>
    public override string AsText() => string.Join(", ", Items.Select(static i => i.AsText()));
}

/// <summary>
/// Kein Wert. Eigener Typ statt <c>null</c>, damit die Auswertung nie eine
/// <c>NullReferenceException</c> auslösen kann — in einer Kette wie
/// <c>concat(a, ' ', b)</c> ist ein fehlendes Glied der Normalfall, nicht die
/// Ausnahme.
/// </summary>
public sealed record NullValue : ContextValue
{
    public override bool IsEmpty => true;

    public override string AsText() => string.Empty;
}

/// <summary>
/// <b>Da ist etwas, aber es ist kein Wert</b> — ein Objekt aus der Antwort.
///
/// <b>Warum es diesen Typ braucht.</b> Der erste Entwurf machte aus einem
/// Objekt <see cref="ContextValue.Null"/>, und der Test deckte auf, was das
/// bedeutet: <c>isEmpty($.contact)</c> — genau die Regel aus dem Auftrag, mit
/// der eine leere Antwort erkannt wird — war damit <b>immer</b> wahr. Jede
/// Antwort galt als „nichts gefunden", und die Karte wäre leer geblieben,
/// obwohl die Daten da waren.
///
/// Der Unterschied zwischen „das Feld fehlt" und „dort steht ein Objekt" muss
/// also im Wert stehen. Angezeigt wird trotzdem nichts: ein JSON-Auszug auf
/// einer Karte wäre die Antwort im Rohzustand, und genau das soll das Mapping
/// verhindern (§21.1). Wer ein Unterfeld braucht, verlängert den Pfad — und
/// <c>MappingEngine</c> sagt es ihm.
/// </summary>
public sealed record StructureValue : ContextValue
{
    public override bool IsEmpty => false;

    public override string AsText() => string.Empty;
}
