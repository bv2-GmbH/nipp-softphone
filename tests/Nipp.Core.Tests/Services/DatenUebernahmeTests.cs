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
