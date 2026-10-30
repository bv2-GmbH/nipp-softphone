using System.Text.Json;
using System.Text.Json.Nodes;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services;

/// <summary>Was eine Übernahme bewirkt hat — zum Protokollieren und Prüfen.</summary>
/// <param name="Uebernommen">Wie viele Einträge verschoben wurden.</param>
/// <param name="Uebersprungen">
/// Wie viele liegen blieben, weil am Ziel schon etwas stand. <b>Was am Ziel
/// liegt, gewinnt immer</b> — sonst überschriebe ein alter Rest die Daten, mit
/// denen gerade gearbeitet wird.
/// </param>
/// <param name="Gescheitert">
/// Wie viele nicht verschoben werden konnten, meist weil sie jemand offen
/// hält. Sie bleiben am alten Ort liegen und werden beim nächsten Start
/// erneut versucht.
/// </param>
public readonly record struct UebernahmeErgebnis(int Uebernommen, int Uebersprungen, int Gescheitert)
{
    /// <summary>Ob überhaupt etwas zu tun war.</summary>
    public bool EtwasGeschehen => Uebernommen > 0 || Gescheitert > 0;
}

/// <summary>
/// Holt die Daten einmalig vom alten an den neuen Ort (ADR-079).
///
/// <para><b>Diese Klasse ist dafür gebaut, nie zu werfen.</b> Sie läuft vor
/// allem anderen — vor dem Protokoll, vor den Einstellungen, vor der Telefonie
/// —, und ein Fehler hier dürfte nipp nicht am Starten hindern. Im
/// schlimmsten Fall bleibt eine Datei liegen und wird beim nächsten Start
/// erneut versucht; telefonieren lässt sich auch ohne Anrufliste.</para>
///
/// <para><b>Verschoben wird, nicht kopiert.</b> Eine Kopie liesse zwei
/// Wahrheiten zurück, und beim nächsten Start wäre nicht zu entscheiden,
/// welche gilt. Was am Ziel schon steht, bleibt unangetastet.</para>
///
/// <para><b>Das alte Verzeichnis wird nicht gelöscht</b>, auch nicht wenn es
/// leer ist. Es kostet nichts, es stehen zu lassen, und ein Löschen wäre der
/// eine Schritt, der im Fehlerfall Daten vernichtet — genau das Risiko, dessen
/// Beseitigung der Anlass dieses Umzugs war.</para>
/// </summary>
public static class DatenUebernahme
{
    /// <summary>
    /// Die Einträge, die vom alten Ort geholt werden. <b>Namentlich und nicht
    /// «alles, was dort liegt»</b>: unter dem alten Pfad kann inzwischen eine
    /// Velopack-Installation liegen (<c>current</c>, <c>packages</c>,
    /// <c>Update.exe</c>), und die gehört nicht mitgenommen.
    /// </summary>
    private static readonly string[] Eintraege =
    [
        "logs",
        "diagnostics",
        "recordings",
        "history.db",
        "secrets.dat",
        "rootca.pem",
        "test-trunk.json",
    ];

    /// <summary>
    /// Holt, was zu holen ist. Tut nichts, wenn der alte Ort nicht existiert
    /// oder mit dem neuen zusammenfällt.
    /// </summary>
    /// <param name="alt">Der alte Ort; normalerweise <see cref="NippPfade.DatenAlt"/>.</param>
    /// <param name="neu">Der neue Ort; normalerweise <see cref="NippPfade.Daten"/>.</param>
    public static UebernahmeErgebnis Ausfuehren(string alt, string neu)
    {
        var uebernommen = 0;
        var uebersprungen = 0;
        var gescheitert = 0;

        try
        {
            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(alt),
                    Path.TrimEndingDirectorySeparator(neu),
                    StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(alt))
            {
                return new UebernahmeErgebnis(0, 0, 0);
            }

            Directory.CreateDirectory(neu);

            foreach (var name in Eintraege)
            {
                var quelle = Path.Combine(alt, name);
                var ziel = Path.Combine(neu, name);

                try
                {
                    var istOrdner = Directory.Exists(quelle);

                    if (!istOrdner && !File.Exists(quelle))
                    {
                        continue;
                    }

                    if (istOrdner ? Directory.Exists(ziel) : File.Exists(ziel))
                    {
                        uebersprungen++;
                        continue;
                    }

                    if (istOrdner)
                    {
                        Directory.Move(quelle, ziel);
                    }
                    else
                    {
                        File.Move(quelle, ziel);
                    }

                    uebernommen++;
                }
                catch (Exception)
                {
                    // Eine gesperrte Datei ist kein Grund, die übrigen liegen
                    // zu lassen — und erst recht keiner, den Start abzubrechen.
                    gescheitert++;
                }
            }
        }
        catch (Exception)
        {
            // Siehe Klassenkommentar: diese Methode wirft nicht.
            gescheitert++;
        }

        return new UebernahmeErgebnis(uebernommen, uebersprungen, gescheitert);
    }

    /// <summary>
    /// Der übliche Aufruf, mit den Pfaden aus <see cref="NippPfade"/>. Zieht
    /// im selben Zug auch <see cref="KorrigiereRecordingDirectory()"/> nach
    /// (L8/R1.4) — <c>Program.Main</c> ruft nur diese eine Methode, und beide
    /// Korrekturen gehören demselben einmaligen Anlass (ADR-079).
    /// </summary>
    public static UebernahmeErgebnis Ausfuehren()
    {
        var ergebnis = Ausfuehren(NippPfade.DatenAlt, NippPfade.Daten);
        KorrigiereRecordingDirectory();
        return ergebnis;
    }

    /// <summary>
    /// Korrigiert <c>Advanced.RecordingDirectory</c> in den Einstellungen,
    /// falls dort noch der Standardpfad von vor ADR-079 steht (L8, R1.4).
    ///
    /// <para><b>Warum das hier und nicht mit den übrigen Daten passiert.</b>
    /// <c>settings.json</c> liegt unter <c>%APPDATA%\nipp</c> und zieht bei
    /// ADR-079 bewusst <b>nicht</b> um (siehe Klassenkommentar von
    /// <see cref="NippPfade"/>) — dort gab es nie einen Konflikt mit Velopack.
    /// Der gespeicherte Aufnahmeordner ist aber trotzdem betroffen: er wird
    /// nie als <c>null</c> persistiert, sobald die Einstellungsseite einmal
    /// gespeichert hat, denn das Ansichtsmodell löst <c>null</c> schon beim
    /// Laden zu einem konkreten Pfad auf und schreibt diesen bei jeder
    /// Änderung zurück — auch wenn der Aufnahmeordner selbst nie angefasst
    /// wurde. Vor dem 09.10.2026 ergab diese Auflösung den alten Standard
    /// ohne <c>bv2</c>; dieser Wert friert seither als Literal ein.</para>
    ///
    /// <para><b>ADR-054 trägt diesen Pfad nicht</b> — er steht nicht im
    /// <see cref="ProvisioningCatalog"/> und damit auch nicht in
    /// <c>NippSettings.UserOverrides</c>; es gibt also keine Markierung, die
    /// «Benutzer hat selbst gewählt» von «das ist der eingefrorene Standard»
    /// unterscheidet. Deshalb die enge Regel an dieser Stelle: korrigiert wird
    /// nur, wenn der gespeicherte Wert <b>exakt</b> dem alten Standard
    /// entspricht. Jeder andere Wert — auch einer, der nur ähnlich aussieht —
    /// gilt als bewusste Entscheidung und bleibt stehen.</para>
    ///
    /// <para>Wirft nie, aus demselben Grund wie <see cref="Ausfuehren()"/>:
    /// das läuft in <c>Program.Main</c>, vor dem Protokoll und vor WinUI.</para>
    /// </summary>
    /// <param name="einstellungen">Der Pfad zu <c>settings.json</c>.</param>
    /// <param name="alterStandard">
    /// Der Pfad, den eine Auflösung vor ADR-079 ergeben hätte.
    /// </param>
    /// <param name="neuerStandard">Der heutige Standard aus <see cref="NippPfade"/>.</param>
    public static void KorrigiereRecordingDirectory(
        string einstellungen, string alterStandard, string neuerStandard)
    {
        try
        {
            if (!File.Exists(einstellungen))
            {
                return;
            }

            if (JsonNode.Parse(File.ReadAllText(einstellungen)) is not JsonObject wurzel)
            {
                return;
            }

            if (wurzel["Advanced"] is not JsonObject advanced)
            {
                return;
            }

            var wert = advanced["RecordingDirectory"]?.GetValue<string>();

            if (wert is null
                || !string.Equals(
                    Path.TrimEndingDirectorySeparator(wert),
                    Path.TrimEndingDirectorySeparator(alterStandard),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Kein Wert, oder einer, der nicht dem alten Standard
                // entspricht — und damit eine Entscheidung, die stehen bleibt.
                return;
            }

            advanced["RecordingDirectory"] = neuerStandard;

            File.WriteAllText(
                einstellungen,
                wurzel.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Siehe Klassenkommentar: auch diese Korrektur wirft nicht.
        }
    }

    /// <summary>Der übliche Aufruf, mit den Pfaden aus <see cref="NippPfade"/> und <see cref="SettingsService"/>.</summary>
    public static void KorrigiereRecordingDirectory() =>
        KorrigiereRecordingDirectory(
            SettingsService.DefaultPath,
            Path.Combine(NippPfade.DatenAlt, "recordings"),
            NippPfade.Aufnahmen);
}
