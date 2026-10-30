using Nipp.Core.Services;

namespace Nipp.Core.Tests.Services;

/// <summary>
/// Der einmalige Umzug der Daten (ADR-079).
///
/// <para><b>Warum das besonders sorgfältig geprüft wird.</b> Diese Klasse
/// bewegt die Anrufliste und die verschlüsselten Zugangsdaten. Ein Fehler hier
/// kostet keinen Testfall, sondern die Daten eines Arbeitsplatzes — und sie
/// liegen danach nirgends mehr. Der Anlass des Umzugs war genau dieses Risiko:
/// Installations- und Datenverzeichnis fielen zusammen.</para>
/// </summary>
public class DatenUebernahmeTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(
        Path.GetTempPath(),
        "nipp-uebernahme-" + Guid.NewGuid().ToString("N"));

    private string Alt => Path.Combine(_wurzel, "alt");

    private string Neu => Path.Combine(_wurzel, "neu");

    private void SchreibeAlt(string name, string inhalt)
    {
        Directory.CreateDirectory(Alt);
        File.WriteAllText(Path.Combine(Alt, name), inhalt);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_wurzel))
            {
                Directory.Delete(_wurzel, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Temp-Verzeichnis ist kein Testfehler.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Der Normalfall: alles wandert mit.</summary>
    [Fact]
    public void Dateien_und_Ordner_wandern_mit()
    {
        SchreibeAlt("history.db", "anrufe");
        SchreibeAlt("secrets.dat", "geheim");
        Directory.CreateDirectory(Path.Combine(Alt, "logs"));
        File.WriteAllText(Path.Combine(Alt, "logs", "a.log"), "zeile");

        var e = DatenUebernahme.Ausfuehren(Alt, Neu);

        Assert.Equal(3, e.Uebernommen);
        Assert.Equal(0, e.Gescheitert);
        Assert.Equal("anrufe", File.ReadAllText(Path.Combine(Neu, "history.db")));
        Assert.Equal("zeile", File.ReadAllText(Path.Combine(Neu, "logs", "a.log")));
    }

    /// <summary>
    /// <b>Was am Ziel liegt, gewinnt — immer.</b> Sonst überschriebe ein alter
    /// Rest die Daten, mit denen gerade gearbeitet wird, und das wäre der
    /// Datenverlust, den dieser Umzug gerade verhindern soll.
    /// </summary>
    [Fact]
    public void Vorhandenes_am_Ziel_wird_nicht_ueberschrieben()
    {
        SchreibeAlt("history.db", "alt");
        Directory.CreateDirectory(Neu);
        File.WriteAllText(Path.Combine(Neu, "history.db"), "neu");

        var e = DatenUebernahme.Ausfuehren(Alt, Neu);

        Assert.Equal(0, e.Uebernommen);
        Assert.Equal(1, e.Uebersprungen);
        Assert.Equal("neu", File.ReadAllText(Path.Combine(Neu, "history.db")));
        Assert.Equal("alt", File.ReadAllText(Path.Combine(Alt, "history.db")));
    }

    /// <summary>
    /// <b>Eine Velopack-Installation am alten Ort wird nicht mitgenommen.</b>
    /// Genau das liegt dort, seit Setup und Daten sich ein Verzeichnis teilten:
    /// <c>current</c>, <c>packages</c>, <c>Update.exe</c>. Die Übernahme holt
    /// deshalb eine namentliche Liste und nicht «alles».
    /// </summary>
    [Fact]
    public void Fremdes_bleibt_liegen()
    {
        SchreibeAlt("history.db", "anrufe");
        SchreibeAlt("Update.exe", "binaer");
        Directory.CreateDirectory(Path.Combine(Alt, "current"));
        Directory.CreateDirectory(Path.Combine(Alt, "packages"));

        var e = DatenUebernahme.Ausfuehren(Alt, Neu);

        Assert.Equal(1, e.Uebernommen);
        Assert.False(File.Exists(Path.Combine(Neu, "Update.exe")));
        Assert.False(Directory.Exists(Path.Combine(Neu, "current")));
        Assert.True(File.Exists(Path.Combine(Alt, "Update.exe")));
    }

    /// <summary>
    /// Ein zweiter Lauf findet nichts mehr und tut nichts — die Übernahme ist
    /// damit unschädlich, wenn sie bei jedem Start läuft, und genau das tut sie.
    /// </summary>
    [Fact]
    public void Ein_zweiter_Lauf_ist_folgenlos()
    {
        SchreibeAlt("history.db", "anrufe");

        DatenUebernahme.Ausfuehren(Alt, Neu);
        var zweiter = DatenUebernahme.Ausfuehren(Alt, Neu);

        Assert.False(zweiter.EtwasGeschehen);
        Assert.Equal("anrufe", File.ReadAllText(Path.Combine(Neu, "history.db")));
    }

    /// <summary>
    /// <b>Das alte Verzeichnis bleibt stehen</b>, auch leer. Es zu löschen
    /// wäre der eine Schritt, der im Fehlerfall Daten vernichtet.
    /// </summary>
    [Fact]
    public void Der_alte_Ort_wird_nicht_geloescht()
    {
        SchreibeAlt("history.db", "anrufe");

        DatenUebernahme.Ausfuehren(Alt, Neu);

        Assert.True(Directory.Exists(Alt));
    }

    /// <summary>
    /// Kein alter Ort, nichts zu tun — der Fall jedes frisch eingerichteten
    /// Arbeitsplatzes.
    /// </summary>
    [Fact]
    public void Ohne_alten_Ort_geschieht_nichts()
    {
        var e = DatenUebernahme.Ausfuehren(Path.Combine(_wurzel, "gibtsnicht"), Neu);

        Assert.False(e.EtwasGeschehen);
    }

    /// <summary>
    /// <b>Gleicher Pfad heisst: Finger weg.</b> Ohne diese Prüfung verschöbe
    /// die Übernahme Dateien in ihr eigenes Verzeichnis.
    /// </summary>
    [Fact]
    public void Gleicher_Pfad_wird_nicht_angefasst()
    {
        SchreibeAlt("history.db", "anrufe");

        var e = DatenUebernahme.Ausfuehren(Alt, Alt + Path.DirectorySeparatorChar);

        Assert.False(e.EtwasGeschehen);
        Assert.Equal("anrufe", File.ReadAllText(Path.Combine(Alt, "history.db")));
    }

    private string Einstellungen => Path.Combine(_wurzel, "settings.json");

    private const string AlterStandard = @"C:\Users\jemand\AppData\Local\nipp\recordings";

    private const string NeuerStandard = @"C:\Users\jemand\AppData\Local\bv2\nipp\recordings";

    private void SchreibeEinstellungen(string recordingDirectoryJson)
    {
        Directory.CreateDirectory(_wurzel);
        File.WriteAllText(
            Einstellungen,
            $$"""
            {
              "Accounts": [],
              "Advanced": {
                "RecordingDirectory": {{recordingDirectoryJson}},
                "CountryPrefix": "+41"
              }
            }
            """);
    }

    /// <summary>
    /// <b>Der Normalfall des Befunds (L8):</b> der alte, eingefrorene Standard
    /// wird auf den neuen gezogen.
    /// </summary>
    [Fact]
    public void Eingefrorener_alter_Standard_wird_korrigiert()
    {
        SchreibeEinstellungen($"\"{AlterStandard.Replace(@"\", @"\\")}\"");

        DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard);

        var inhalt = File.ReadAllText(Einstellungen);
        Assert.Contains(NeuerStandard.Replace(@"\", @"\\"), inhalt);
        Assert.DoesNotContain(AlterStandard.Replace(@"\", @"\\"), inhalt);

        // Der Rest der Datei bleibt erhalten — geprüft am Inhalt, nicht am
        // Text: System.Text.Json schreibt das Plus mit dem Standard-Encoder
        // als «+», und genau so steht es auch in der echten
        // settings.json (nachgesehen am 30.10.2026). Ein Assert auf «+41»
        // misst den Encoder, nicht den erhaltenen Wert.
        var gelesen = System.Text.Json.Nodes.JsonNode.Parse(inhalt);
        Assert.Equal(
            "+41",
            gelesen?["Advanced"]?["CountryPrefix"]?.GetValue<string>());
    }

    /// <summary>
    /// <b>Die wichtigere Hälfte (ADR-054):</b> ein Pfad, der nicht exakt dem
    /// alten Standard entspricht, ist eine Entscheidung und bleibt stehen —
    /// auch ohne eigenen Eintrag im <c>ProvisioningCatalog</c>, der diesen
    /// Pfad gar nicht kennt.
    /// </summary>
    [Fact]
    public void Ein_eigener_Aufnahmeordner_bleibt_unangetastet()
    {
        const string eigen = @"D:\Aufnahmen";
        SchreibeEinstellungen($"\"{eigen.Replace(@"\", @"\\")}\"");

        DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard);

        var inhalt = File.ReadAllText(Einstellungen);
        Assert.Contains(eigen.Replace(@"\", @"\\"), inhalt);
    }

    /// <summary>Kein Wert gesetzt (<c>null</c>) — nichts zu korrigieren.</summary>
    [Fact]
    public void Kein_gesetzter_Wert_bleibt_null()
    {
        SchreibeEinstellungen("null");

        DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard);

        var inhalt = File.ReadAllText(Einstellungen);
        Assert.Contains("\"RecordingDirectory\": null", inhalt);
    }

    /// <summary>Fehlt die Datei ganz, geschieht nichts — und es wird nicht geworfen.</summary>
    [Fact]
    public void Fehlende_Einstellungsdatei_wird_ignoriert()
    {
        var exception = Record.Exception(() =>
            DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard));

        Assert.Null(exception);
        Assert.False(File.Exists(Einstellungen));
    }

    /// <summary>
    /// Eine beschädigte Datei darf den Start nicht verhindern — dieselbe
    /// Garantie wie bei <see cref="DatenUebernahme.Ausfuehren(string, string)"/>.
    /// </summary>
    [Fact]
    public void Beschaedigte_Einstellungsdatei_wird_nicht_angefasst()
    {
        Directory.CreateDirectory(_wurzel);
        File.WriteAllText(Einstellungen, "{ das ist kein json");

        var exception = Record.Exception(() =>
            DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard));

        Assert.Null(exception);
        Assert.Equal("{ das ist kein json", File.ReadAllText(Einstellungen));
    }

    /// <summary>
    /// Zweiter Lauf nach der Korrektur ist folgenlos — derselbe Grundsatz wie
    /// bei der übrigen Übernahme.
    /// </summary>
    [Fact]
    public void Ein_zweiter_Lauf_der_Korrektur_ist_folgenlos()
    {
        SchreibeEinstellungen($"\"{AlterStandard.Replace(@"\", @"\\")}\"");

        DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard);
        DatenUebernahme.KorrigiereRecordingDirectory(Einstellungen, AlterStandard, NeuerStandard);

        var inhalt = File.ReadAllText(Einstellungen);
        Assert.Contains(NeuerStandard.Replace(@"\", @"\\"), inhalt);
    }

    /// <summary>
    /// <b>Eine gesperrte Datei hält die übrigen nicht auf</b> — und wirft
    /// nicht. Diese Methode läuft vor WinUI; eine Ausnahme dort hiesse, dass
    /// nipp wegen einer offenen Protokolldatei nicht mehr startet.
    /// </summary>
    [Fact]
    public void Eine_gesperrte_Datei_haelt_die_uebrigen_nicht_auf()
    {
        SchreibeAlt("history.db", "anrufe");
        SchreibeAlt("secrets.dat", "geheim");

        using var sperre = new FileStream(
            Path.Combine(Alt, "history.db"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var e = DatenUebernahme.Ausfuehren(Alt, Neu);

        Assert.Equal(1, e.Uebernommen);
        Assert.Equal(1, e.Gescheitert);
        Assert.True(File.Exists(Path.Combine(Neu, "secrets.dat")));
    }
}
