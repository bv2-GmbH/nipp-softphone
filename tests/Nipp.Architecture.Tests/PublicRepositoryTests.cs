namespace Nipp.Architecture.Tests;

/// <summary>
/// Das Repo ist öffentlich — in ihm stehen keine Namen, Adressen oder Daten
/// zweier bv2-eigener Systeme mehr (ADR-040).
///
/// <para><b>Warum es diesen Wächter braucht.</b> Die Bereinigung ist einmalig,
/// die Gewohnheit nicht: der nächste Kommentar „bei dem einen System war das
/// so" kommt innerhalb eines Monats zurück, und er kommt aus guter Absicht —
/// es ist die Erfahrung, an der etwas gelernt wurde. <b>Der Erfahrungswert
/// gehört erhalten, der Systemname nicht.</b> Wer eine solche Stelle schreibt,
/// nennt die Art des Systems („das CRM", „das Gesprächsjournal") und die
/// Beispielkennung, nicht den Hersteller.</para>
///
/// <para><b>Dieser Test nennt die beiden Systeme selbst nicht beim Namen.</b>
/// Er prüft die Muster, aus denen sie bestehen — sonst bräuchte er eine
/// Ausnahmeliste für sich selbst, und Ausnahmelisten wachsen.</para>
///
/// <para><b>Was ausdrücklich erlaubt bleibt:</b> <c>www.bv2.ch</c>,
/// <c>kontakt@bv2.ch</c> und „bv2 GmbH". Das sind Herstellerangaben, und die
/// gehören in ein Produkt.</para>
/// </summary>
public sealed class PublicRepositoryTests
{
    /// <summary>
    /// Die Muster, zusammengesetzt statt ausgeschrieben — damit dieser Test
    /// nicht selbst der einzige Treffer ist.
    /// </summary>
    private static readonly (string Muster, string Warum)[] Verboten =
    [
        (string.Concat("cock", "pit"),
            "Der Name eines bv2-eigenen Systems. Gemeint ist «das CRM»; die Beispielkennung heisst crm."),

        (string.Concat("call", "memory"),
            "Der Name eines bv2-eigenen Systems. Gemeint ist «das Gesprächsjournal»; die Beispielkennung heisst journal."),
    ];

    /// <summary>
    /// Hosts, die nicht in ein öffentliches Repo gehören — interne Anlagen und
    /// interne Dienste. <c>www.bv2.ch</c> und <c>kontakt@bv2.ch</c> bleiben.
    /// </summary>
    private static readonly (string Muster, string Warum)[] VerbotenHosts =
    [
        (string.Concat("remote", ".bv2.ch"),
            "Der Hostname der internen Telefonanlage. In Beispielen steht pbx.example.ch."),
    ];

    /// <summary>
    /// Wo gesucht wird: Quelltext, Tests, die Produktdokumentation und die
    /// Markdown-Dateien in der Wurzel.
    /// </summary>
    private static IEnumerable<string> Dateien()
    {
        var repo = Path.GetFullPath(Path.Combine(RepositoryFiles.SourceRoot, ".."));

        foreach (var ordner in new[] { "src", "tests", "docs", "tools", "build" })
        {
            var pfad = Path.Combine(repo, ordner);

            if (!Directory.Exists(pfad))
            {
                continue;
            }

            foreach (var datei in Directory.EnumerateFiles(pfad, "*.*", SearchOption.AllDirectories))
            {
                if (datei.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
                    || datei.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Passt(datei))
                {
                    yield return datei;
                }
            }
        }

        // Die Markdown-Dateien in der Wurzel.
        //
        // Hier stand bis zum 13.09.2026: «Die Planungsdokumente wandern ins
        // private; sie zu bereinigen kostet mehr und verfaelscht sie.» Deshalb
        // pruefte dieser Test **nur diese drei** — und die dreizehn Plaene und
        // Reviews im Wurzelverzeichnis sah er nie. Als sie mit W2.7 nach
        // docs/plans/ umgezogen sind, meldete er beim ersten Lauf **67
        // Stellen**: die beiden Systemnamen und den Hostnamen der Anlage, seit
        // ADR-040 unbemerkt im Repo.
        //
        // <b>Sie sind jetzt bereinigt, und der Ordner wird ueber docs/
        // mitgeprueft.</b> Der Einwand von damals war also richtig gerechnet
        // und falsch geschlossen: es kostete zwei Ersetzungen und eine
        // Durchsicht auf Grammatik — und «kostet mehr» hiess in Wahrheit «wird
        // nicht geprueft», was in einem Repo, das oeffentlich werden soll, die
        // teurere Antwort ist.
        foreach (var name in new[] { "README.md", "NIPP-BUILD.md", "CLAUDE.md" })
        {
            var pfad = Path.Combine(repo, name);

            if (File.Exists(pfad))
            {
                yield return pfad;
            }
        }
    }

    private static bool Passt(string datei) =>
        datei.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || datei.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || datei.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || datei.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
        || datei.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
        || datei.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Kein_Systemname_eines_bv2_eigenen_Systems_steht_im_Repo()
    {
        var treffer = new List<string>();

        foreach (var datei in Dateien())
        {
            // Diese Datei selbst beschreibt die Muster; sie darf sie kennen.
            if (datei.EndsWith(nameof(PublicRepositoryTests) + ".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var zeilen = File.ReadAllLines(datei);

            for (var i = 0; i < zeilen.Length; i++)
            {
                foreach (var (muster, warum) in Verboten)
                {
                    if (zeilen[i].Contains(muster, StringComparison.OrdinalIgnoreCase))
                    {
                        treffer.Add($"{Path.GetFileName(datei)}:{i + 1} — {warum}");
                    }
                }
            }
        }

        Assert.True(
            treffer.Count == 0,
            "Im Repo steht wieder der Name eines bv2-eigenen Systems:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, treffer.Take(20)));
    }

    [Fact]
    public void Kein_interner_Hostname_steht_im_Repo()
    {
        var treffer = new List<string>();

        foreach (var datei in Dateien())
        {
            if (datei.EndsWith(nameof(PublicRepositoryTests) + ".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var zeilen = File.ReadAllLines(datei);

            for (var i = 0; i < zeilen.Length; i++)
            {
                foreach (var (muster, warum) in VerbotenHosts)
                {
                    if (zeilen[i].Contains(muster, StringComparison.OrdinalIgnoreCase))
                    {
                        treffer.Add($"{Path.GetFileName(datei)}:{i + 1} — {warum}");
                    }
                }
            }
        }

        Assert.True(
            treffer.Count == 0,
            "Im Repo steht wieder ein interner Hostname:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, treffer.Take(20)));
    }

    /// <summary>
    /// <b>Und keine mitgeschnittene Antwort eines fremden Systems.</b> Der
    /// Befund, der die Bereinigung ausgelöst hat, war nicht der Systemname,
    /// sondern eine als „echte Antwort" deklarierte Gesprächszusammenfassung
    /// mit Klarnamen, Firma und Rufnummer. Erlaubt sind erfundene
    /// Beispielantworten — sie sagen es selbst.
    /// </summary>
    [Fact]
    public void Keine_Antwort_gibt_sich_als_echt_mitgeschnitten_aus()
    {
        var treffer = new List<string>();

        string[] verdaechtig =
        [
            "woertlich uebernommen am",
            "wörtlich übernommen am",
            "die tatsächliche Antwort von",
            "die tatsaechliche Antwort von",
        ];

        foreach (var datei in Dateien())
        {
            if (datei.EndsWith(nameof(PublicRepositoryTests) + ".cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(datei);

            foreach (var muster in verdaechtig)
            {
                if (text.Contains(muster, StringComparison.OrdinalIgnoreCase))
                {
                    treffer.Add($"{Path.GetFileName(datei)} — «{muster}»");
                }
            }
        }

        Assert.True(
            treffer.Count == 0,
            "Eine Datei gibt an, die Antwort eines echten Systems wörtlich zu enthalten. "
                + "Das ist die Kundenkarte eines echten Anrufers:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, treffer));
    }
}
