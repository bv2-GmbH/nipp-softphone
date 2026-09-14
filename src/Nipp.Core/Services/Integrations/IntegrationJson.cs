using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nipp.Core.Services.Integrations;

/// <summary>
/// Ein <see cref="JsonStringEnumConverter"/>, der kleingeschriebene Namen
/// erzeugt — <c>activeExpanded</c>, nicht <c>ActiveExpanded</c>.
///
/// <para><b>Warum es diesen Typ überhaupt gibt.</b> Ein Attribut nimmt keine
/// Argumente für seinen Konverter mit, und ein <c>[JsonConverter]</c> an einer
/// Eigenschaft <b>schlägt</b> den Konverter aus den
/// <see cref="JsonSerializerOptions"/>. Die Enums hier trugen deshalb
/// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> — ohne
/// Benennungsregel —, während <c>IntegrationConfigStore</c> seine Optionen mit
/// <c>JsonNamingPolicy.CamelCase</c> aufsetzte. Das Attribut gewann, und nipp
/// schrieb <c>"Bearer"</c> und <c>"ActiveExpanded"</c> in eine Datei, deren
/// Beispiele überall <c>"bearer"</c> und <c>"activeExpanded"</c> zeigen.</para>
///
/// <para><b>Aufgefallen ist das nie</b>, weil das Lesen von Enums
/// unabhängig von der Schreibweise funktioniert: eine von Hand geschriebene
/// Vorlage lief, eine ausgegebene Datei lief, und dass beide verschieden
/// aussahen, merkte nur, wer sie nebeneinanderlegte. Beim Ausgeben zählt es
/// trotzdem — diese Datei wird gelesen, in Tickets gelegt und von Hand
/// geändert, und sie soll aussehen wie die Beispiele in
/// <c>docs/plans/INTEGRATION-PLAN.md</c> D.2.</para>
/// </summary>
public sealed class CamelCaseEnumConverter : JsonStringEnumConverter
{
    public CamelCaseEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>
/// Die JSON-Einstellungen der Integrationsplattform — <b>an einer Stelle</b>.
///
/// <para>Vorher gab es zwei Sätze: einen im <c>IntegrationConfigStore</c> und
/// einen im <c>IntegrationSettingsViewModel</c> für das Ausgeben. Dem zweiten
/// fehlte der Enum-Konverter. Das fiel nur deshalb nicht auf, weil jedes Enum
/// sein eigenes Attribut trug — wäre eines davon je entfernt worden, hätte die
/// ausgegebene Datei an dieser Stelle eine <b>Zahl</b> enthalten, und ein
/// Administrator hätte gerätselt, was <c>"type": 2</c> bedeutet.</para>
/// </summary>
public static class IntegrationJson
{
    /// <summary>
    /// Wie <c>integrations.json</c> gelesen und geschrieben wird.
    ///
    /// <para><b>Nachsichtig beim Lesen, streng beim Schreiben:</b> Kommentare
    /// und abschliessende Kommas sind erlaubt, weil die Datei von Hand
    /// gepflegt wird und die mitgelieferten Vorlagen ihre Hinweise als
    /// Kommentar tragen.</para>
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new CamelCaseEnumConverter() },
    };
}
