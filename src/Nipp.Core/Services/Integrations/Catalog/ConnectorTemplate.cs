using System.Text.Json.Nodes;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Services.Integrations.Catalog;

/// <summary>
/// Woher eine Vorlage kommt (ADR-040).
///
/// <para><b>Das setzt der Lader, nie die Datei.</b> Vorher stand in der Datei
/// ein <c>vendor</c>, der zugleich Herkunft und Hersteller meinte — eine
/// eingelesene Datei hätte damit behaupten können, mitgeliefert zu sein. Der
/// Herstellername ist jetzt ein eigenes Freitextfeld und nur Anzeige.</para>
/// </summary>
public enum ConnectorOrigin
{
    /// <summary>Von nipp mitgeliefert.</summary>
    BuiltIn,

    /// <summary>Über „API-Anbieter importieren" hinzugekommen.</summary>
    Imported,
}

/// <summary>
/// Ein Geheimnis, das eine Vorlage braucht.
/// </summary>
/// <param name="Ref">
/// Der Verweis, unter dem der Wert im <c>SecretStore</c> liegt — derselbe, der
/// in <c>auth.secretRef</c> steht. <b>Ein Verweis, nie ein Wert.</b>
/// </param>
/// <param name="Label">
/// Was in der Oberfläche über dem Eingabefeld steht, etwa „API-Token".
///
/// <b>Der Grund, warum das hier steht:</b> vorher war das Feld für den
/// Verweis freier Text mit einem technischen Platzhalter. Wer ein Token
/// eintragen wollte, musste vorher im JSON nachlesen, wie der Verweis heisst.
/// Eine technische Kennung hat in einem Formular nichts verloren.
/// </param>
/// <param name="Hint">
/// <b>Wo das Token herkommt</b> — „Profil → API-Token; Lesezugriff genügt".
/// Das ist der Schritt, der ausserhalb von nipp passiert, und der einzige, bei
/// dem eine Anleitung wirklich hilft.
/// </param>
public sealed record ConnectorSecret(string Ref, string Label, string? Hint = null);

/// <summary>
/// Die Datei, wie eine Anbietervorlage auf der Platte aussieht (ADR-040).
///
/// <para><b>Alles in einer Datei.</b> Vorher lag eine Vorlage in drei Teilen:
/// ein Eintrag im Manifest, die Quellenbeschreibung unter <c>docs/</c> und die
/// Beispielantwort daneben. Das war richtig, solange nur nipp selbst Vorlagen
/// mitbrachte — weitergeben lässt sich so etwas nicht.</para>
///
/// <para><b>Kein <c>sourceId</c> mehr:</b> eine Vorlage ist ein Anbieter ist
/// eine Quelle. Welche von mehreren gemeint war, musste man vorher dazusagen.</para>
/// </summary>
public sealed record ConnectorTemplateFile
{
    /// <summary>Muss <see cref="ConnectorTemplateReader.Kind"/> sein.</summary>
    public string? Kind { get; init; }

    public int SchemaVersion { get; init; }

    public string? Id { get; init; }

    public string? DisplayName { get; init; }

    public string? Summary { get; init; }

    /// <summary>
    /// Der Hersteller, als Freitext — <b>nur Anzeige</b>. Die Herkunft
    /// (mitgeliefert oder importiert) setzt der Lader.
    /// </summary>
    public string? Vendor { get; init; }

    public List<ConnectorSecret>? Secrets { get; init; }

    /// <summary>Genau eine Quellenbeschreibung.</summary>
    public DataSourceDefinition? Source { get; init; }

    public List<CardDefinition>? Cards { get; init; }

    public JsonNode? SampleResponse { get; init; }
}

/// <summary>
/// Ein Eintrag im Katalog „Quelle hinzufügen" (ADR-033, ADR-040).
///
/// <para><b>Warum es das gibt.</b> Bis zum 07.09.2026 entstand eine neue
/// Quelle ausschliesslich dadurch, dass jemand eine JSON-Datei von aussen
/// einlas — und dieses Einlesen <b>ersetzte die ganze Konfiguration</b>. Eine
/// zweite Quelle ging nur über Handarbeit im JSON, und genau das verhinderte,
/// was der Auftrag verlangt: weitere APIs anbinden.</para>
///
/// <para><b>Die Vorlage ist Daten, kein Sonderfall im Code.</b> Sie trägt
/// dieselbe <see cref="DataSourceDefinition"/>, die auch von Hand geschrieben
/// würde. Seit ADR-040 kommt sie entweder mit nipp mit oder über den Import —
/// im Code unterscheidet sie nichts als <see cref="Origin"/>.</para>
/// </summary>
/// <param name="Id">Kennung des Katalogeintrags.</param>
/// <param name="DisplayName">Wie der Eintrag im Katalog heisst.</param>
/// <param name="Origin">Mitgeliefert oder importiert — gesetzt vom Lader.</param>
/// <param name="Vendor">Der Hersteller als Freitext, oder <c>null</c>.</param>
/// <param name="Summary">Ein Satz, was diese Quelle beiträgt.</param>
/// <param name="Secrets">Welche Geheimnisse sie braucht, mit Beschriftung und Herkunft.</param>
/// <param name="Source">Die Quellenbeschreibung selbst — immer abgeschaltet.</param>
/// <param name="Cards">
/// Karten, die zur Vorlage gehören. Meist leer — die mitgelieferten Karten
/// fragen seit K1 über <c>role(...)</c> und brauchen keine quellenspezifische
/// Fassung.
/// </param>
/// <param name="SampleResponse">
/// Eine <b>erfundene</b> Beispielantwort, für die Vorschau im Karten-Designer
/// auf einem Gerät, an dem noch kein Testabruf gelaufen ist.
///
/// <para><b>Erfunden und nicht mitgeschnitten</b>, und das ist keine
/// Formsache: eine echte Antwort ist die Kundenkarte eines echten Anrufers.
/// §21.2 verbietet einen Cache auf der Platte; eine Beispielantwort im
/// Installationsverzeichnis wäre genau das, nur dauerhaft. Ein echter
/// Testabruf schlägt sie zur Laufzeit im Arbeitsspeicher
/// (<see cref="TestSampleStore"/>).</para>
/// </param>
public sealed record ConnectorTemplate(
    string Id,
    string DisplayName,
    ConnectorOrigin Origin,
    string? Vendor,
    string Summary,
    IReadOnlyList<ConnectorSecret> Secrets,
    DataSourceDefinition Source,
    IReadOnlyList<CardDefinition> Cards,
    JsonNode? SampleResponse)
{
    /// <summary>
    /// Ob dieser Eintrag über den Import kam — die Oberfläche schreibt dann
    /// „importiert" daneben.
    ///
    /// <b>Das ist eine Herkunftsangabe und keine Bewertung.</b> Vorher stand
    /// dort „intern bv2", was zwei verschiedene Dinge meinte: von uns, und
    /// nicht für Kunden gedacht.
    /// </summary>
    public bool IsImported => Origin == ConnectorOrigin.Imported;

    /// <summary>Was diese Quelle kann, für die Anzeige im Katalog.</summary>
    public IReadOnlyList<Capability> Capabilities => Source.Capabilities;
}
