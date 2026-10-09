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

    /// <summary>Der übliche Aufruf, mit den Pfaden aus <see cref="NippPfade"/>.</summary>
    public static UebernahmeErgebnis Ausfuehren() =>
        Ausfuehren(NippPfade.DatenAlt, NippPfade.Daten);
}
