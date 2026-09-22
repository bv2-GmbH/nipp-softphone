using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nipp.Core.Services.Integrations.Catalog;

/// <summary>
/// Liest eine Anbietervorlage aus Text — <b>rein, ohne Dateisystem</b>
/// (ADR-040).
///
/// <para><b>Warum das eine eigene Klasse ist.</b> Ab jetzt kommen Vorlagen von
/// aussen: jemand bekommt eine Datei und liest sie ein. Was dabei abgelehnt
/// wird und warum, ist die eigentliche Arbeit — und sie gehört an eine Stelle,
/// die sich ohne Ordner, ohne Rechte und ohne Netz prüfen lässt.</para>
///
/// <para><b>Die beiden Zusagen des Katalogs werden hier produktiv</b>, nicht
/// mehr nur im Test:</para>
/// <list type="bullet">
///   <item>
///     <b>Eine Vorlage wird nie eingeschaltet ausgeliefert.</b> Der Leser
///     normalisiert auf <c>Enabled = false</c>. Damit gilt der bestehende Test
///     automatisch auch für fremde Dateien: eingeschaltet wird nach einem
///     erfolgreichen Testabruf, nicht vorher.
///   </item>
///   <item>
///     <b>In keiner Vorlage steht ein Geheimnis.</b> Bei eigenen Dateien war
///     das ein Test; bei fremden ist es ein <i>Ablehnungsgrund</i>. Der rohe
///     <c>auth</c>-Knoten wird auf Eigenschaften abgeklopft, die nach einem
///     Geheimnis aussehen und einen Wert tragen — System.Text.Json schluckt
///     unbekannte Felder sonst still, und dann läge ein Token in einer Datei,
///     die weitergegeben wird.
///   </item>
/// </list>
/// </summary>
public static class ConnectorTemplateReader
{
    /// <summary>
    /// Das Erkennungsmerkmal. <b>Am Inhalt und nicht an der Endung:</b> die
    /// Datei heisst <c>.json</c>, weil der Dateiauswahldialog einen einzelnen
    /// Endungs-Token nimmt — und weil eine erfundene Endung niemanden hindert,
    /// das Falsche auszuwählen.
    /// </summary>
    public const string Kind = "nippConnectorTemplate";

    /// <summary>Die einzige Fassung, die dieser Leser versteht.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Obergrenze: 256 kB. Eine Vorlage ist eine Beschreibung, keine
    /// Datenlieferung — und was hier hereinkommt, hat niemand geprüft.
    /// </summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Wie lang ein <c>secretRef</c> höchstens sein darf.</summary>
    private const int MaxSecretRefLength = 40;

    /// <summary>
    /// Feldnamen, die auf ein Geheimnis hindeuten. Geprüft wird auf das Ende
    /// des Namens, kleingeschrieben.
    /// </summary>
    private static readonly string[] SecretSuffixes =
        ["token", "secret", "password", "apikey", "key"];

    /// <summary>
    /// Liest eine Vorlage. Rückgabe <c>false</c> heisst: <paramref name="error"/>
    /// sagt einem Menschen, was zu tun ist.
    /// </summary>
    public static bool TryRead(
        string? text,
        out ConnectorTemplate? template,
        out string? error)
    {
        template = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Die Datei ist leer.";
            return false;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxBytes)
        {
            error = $"Die Datei ist grösser als {MaxBytes / 1024} kB. Eine Anbietervorlage "
                + "beschreibt eine Schnittstelle; sie enthält keine Daten.";
            return false;
        }

        JsonNode? wurzel;

        try
        {
            wurzel = JsonNode.Parse(
                text,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        }
        catch (JsonException ex)
        {
            error = $"Die Datei ist kein gültiges JSON: {ex.Message}";
            return false;
        }

        if (wurzel is not JsonObject objekt)
        {
            error = "Die Datei enthält kein JSON-Objekt.";
            return false;
        }

        // Der wahrscheinlichste Fehlgriff verdient eine eigene Meldung: eine
        // ganze nipp-Konfiguration statt einer Anbietervorlage.
        if (objekt["dataSources"] is not null && objekt["kind"] is null)
        {
            error = "Das ist eine nipp-Konfiguration, keine Anbietervorlage — dafür ist "
                + "«Einlesen» zuständig. Einlesen ersetzt allerdings die ganze Konfiguration.";
            return false;
        }

        if (objekt["kind"]?.GetValue<string>() is not { } kind
            || !string.Equals(kind, Kind, StringComparison.Ordinal))
        {
            error = $"Die Datei ist keine Anbietervorlage — oben muss \"kind\": \"{Kind}\" stehen.";
            return false;
        }

        var fassung = objekt["schemaVersion"]?.GetValue<int>() ?? 0;

        if (fassung != SupportedSchemaVersion)
        {
            // Nicht heimlich versuchen: eine unbekannte Fassung kann Felder
            // anders meinen, und was dabei herauskommt, sieht gültig aus.
            error = $"Diese Vorlage ist in Fassung {fassung} geschrieben; nipp versteht "
                + $"Fassung {SupportedSchemaVersion}. Vermutlich braucht es eine neuere Fassung von nipp.";
            return false;
        }

        // Zwei Stellen, und beide sind noetig (Befund A1-18): der Aufbau der
        // Quelle UND die Liste der Geheimnisse. Bis zum 22.09.2026 wurde nur
        // "source" abgeklopft -- ein Token unter secrets[].value kam unbesehen
        // durch und lag danach im Klartext im Vorlagenordner. Gemessen, nicht
        // vermutet: die Vorlage wurde angenommen, und die Meldung lautete
        // "steht jetzt unter Quelle hinzufuegen".
        var fund = Geheimnisfund(objekt["source"]) ?? GeheimnisInListe(objekt["secrets"]);

        if (fund is not null)
        {
            error = $"In der Vorlage steht ein Wert, der wie ein Geheimnis aussieht ({fund}). "
                + "Eine Anbietervorlage beschreibt nur, wo ein Zugangsschlüssel gebraucht wird — "
                + "eingetragen wird er danach in nipp.";
            return false;
        }

        ConnectorTemplateFile? datei;

        try
        {
            datei = objekt.Deserialize<ConnectorTemplateFile>(IntegrationJson.Options);
        }
        catch (JsonException ex)
        {
            error = $"Die Vorlage liess sich nicht lesen: {ex.Message}";
            return false;
        }

        if (datei is null)
        {
            error = "Die Vorlage ist leer.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(datei.Id))
        {
            error = "Der Vorlage fehlt die Kennung «id».";
            return false;
        }

        if (datei.Source is not { } quelle || string.IsNullOrWhiteSpace(quelle.Id))
        {
            error = "Der Vorlage fehlt die Quellenbeschreibung «source» oder deren Kennung.";
            return false;
        }

        foreach (var geheimnis in datei.Secrets ?? [])
        {
            if (string.IsNullOrWhiteSpace(geheimnis.Ref)
                || geheimnis.Ref.Length > MaxSecretRefLength
                || geheimnis.Ref.Any(char.IsWhiteSpace))
            {
                error = "Ein Verweis auf ein Geheimnis («secrets[].ref») ist leer, zu lang oder "
                    + "enthält Leerzeichen. Der Verweis ist eine kurze technische Kennung, "
                    + "nicht der Wert.";
                return false;
            }
        }

        template = new ConnectorTemplate(
            datei.Id!,
            string.IsNullOrWhiteSpace(datei.DisplayName) ? quelle.DisplayName : datei.DisplayName!,
            ConnectorOrigin.Imported,
            datei.Vendor,
            datei.Summary ?? string.Empty,
            datei.Secrets ?? [],

            // Nie eingeschaltet: eingerichtet wird über Testabruf, dann
            // einschalten. Das galt für die mitgelieferten Vorlagen und gilt
            // ab hier auch für jede fremde.
            quelle with { Enabled = false },
            datei.Cards ?? [],
            datei.SampleResponse);

        return true;
    }

    /// <summary>
    /// Sucht im <b>rohen</b> Knoten nach etwas, das wie ein hinterlegter
    /// Zugangsschlüssel aussieht.
    ///
    /// <para>Am rohen Knoten und nicht am gelesenen Objekt: die Typen kennen
    /// kein Feld für einen Geheimniswert, und <c>IntegrationJson.Options</c>
    /// setzt kein <c>UnmappedMemberHandling</c> — ein <c>"token": "abc"</c>
    /// verschwände also lautlos. Wer eine so gelesene Vorlage weitergibt, gibt
    /// das Geheimnis mit.</para>
    /// </summary>
    /// <summary>
    /// Ein Geheimnis in der Liste <c>secrets</c> — also dort, wo eine Vorlage
    /// sagt, <b>welchen</b> Schlüssel sie braucht, und niemals <b>welcher</b>
    /// es ist.
    ///
    /// <para><b>Hier zählt der Ort und nicht der Name</b>, und das ist der
    /// Unterschied zu <see cref="Geheimnisfund"/>. Dort wird nach Feldnamen
    /// gesucht, die auf «token» oder «key» enden — in <c>secrets</c> heisst
    /// das Feld aber schlicht <c>value</c>, und unter diesem Namen kam am
    /// 22.09.2026 ein Token durch (Befund A1-18). In einem Eintrag dieser
    /// Liste ist deshalb <b>jedes</b> Feld ein Fund, das nicht zur
    /// Beschreibung gehört.</para>
    ///
    /// <para>Umgekehrt ginge es nicht: <c>value</c> in die Namensliste zu
    /// nehmen würde jede Feldzuordnung treffen, in der ein Zielsystem ein Feld
    /// «value» nennt — und ein Wächter, der bei jeder zweiten Vorlage falschen
    /// Alarm schlägt, wird abgeschaltet.</para>
    /// </summary>
    private static string? GeheimnisInListe(JsonNode? knoten)
    {
        if (knoten is not JsonArray liste)
        {
            return null;
        }

        foreach (var eintrag in liste)
        {
            if (eintrag is not JsonObject geheimnis)
            {
                continue;
            }

            foreach (var (name, wert) in geheimnis)
            {
                var klein = name.ToLowerInvariant();

                if (klein is "ref" or "label" or "hint")
                {
                    continue;
                }

                if (wert is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && !string.IsNullOrWhiteSpace(text))
                {
                    return $"secrets[].{name}";
                }
            }
        }

        return null;
    }

    private static string? Geheimnisfund(JsonNode? knoten)
    {
        if (knoten is JsonObject objekt)
        {
            foreach (var (name, wert) in objekt)
            {
                var klein = name.ToLowerInvariant();

                // `secretRef` ist der erlaubte Fall: ein Verweis, kein Wert.
                var istVerweis = klein.EndsWith("ref", StringComparison.Ordinal);

                if (!istVerweis
                    && SecretSuffixes.Any(s => klein.EndsWith(s, StringComparison.Ordinal))
                    && wert is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && !string.IsNullOrWhiteSpace(text))
                {
                    return name;
                }

                if (Geheimnisfund(wert) is { } tiefer)
                {
                    return tiefer;
                }
            }

            return null;
        }

        if (knoten is JsonArray feld)
        {
            foreach (var eintrag in feld)
            {
                if (Geheimnisfund(eintrag) is { } tiefer)
                {
                    return tiefer;
                }
            }
        }

        return null;
    }
}
