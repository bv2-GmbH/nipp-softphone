using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt die Textregeln aus <c>CLAUDE.md</c> für alles, was der Benutzer
/// liest (W1.6, Befunde A4 und A5).
///
/// <para><b>Warum ein Quelltext-Scan.</b> Eine <c>Strings/de-CH.resw</c> gibt
/// es nicht (ADR-021): die Texte stehen in XAML-Attributen und C#-Literalen,
/// über rund dreissig Dateien verteilt. Beim Durchsehen von Hand fällt ein
/// <c>ue</c> in einer Kopfzeile nicht auf — «Unverschluesselt zulassen» stand
/// ein halbes Jahr in den Einstellungen.</para>
///
/// <para><b>Geprüft wird der sichtbare Text, nicht die Datei.</b> Der erste
/// Anlauf las ganze Zeilen und meldete zwei Dutzend Kommentare — dort ist
/// <c>„…"</c> richtig, und <c>ae</c> in einem Kommentar ist Absicht. Ein Test
/// mit zwanzig Fehlalarmen wird abgeschaltet und schützt dann nichts.</para>
///
/// <para><b>Was der Test nicht kann.</b> Ob ein Satz sagt, was zu tun ist, ist
/// eine inhaltliche Frage; das prüft nur ein Mensch.</para>
/// </summary>
public sealed partial class UserTextTests
{
    /// <summary>
    /// Ein Anführungszeichenpaar in der Oberfläche, nicht zwei (Befund A4).
    ///
    /// <para><b>Warum <c>«…»</c>.</b> Das deutsche Paar schliesst in diesem
    /// Projekt mit einem <b>geraden</b> Anführungszeichen — und das beendet in
    /// einem C#-Literal die Zeichenkette und in einem XAML-Attribut das
    /// Attribut. CLAUDE.md hält vier Vorfälle an einem einzigen Tag fest, und
    /// beide Compilermeldungen zeigen dabei auf die <em>Folgezeile</em>.
    /// Guillemets kollidieren mit keinem Begrenzer.</para>
    ///
    /// <para>In Kommentaren und in Markdown bleibt <c>„…"</c> richtig; dort
    /// begrenzt nichts.</para>
    /// </summary>
    [Fact]
    public void Die_Oberflaeche_benutzt_ein_Anfuehrungszeichenpaar()
    {
        var verstoesse = SichtbareTexte()
            .Where(static t => t.Text.Contains('„', StringComparison.Ordinal)
                || t.Text.Contains('“', StringComparison.Ordinal)
                || t.Text.Contains('”', StringComparison.Ordinal))
            .Select(static t => $"{t.Datei}: {t.Text}")
            .ToList();

        Assert.True(
            verstoesse.Count == 0,
            "Benutzertexte verwenden «…». Das deutsche Schlusszeichen ist ein gerades "
                + "Anfuehrungszeichen und beendet Literal beziehungsweise Attribut:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verstoesse));
    }

    /// <summary>
    /// Keine Umlautumschreibung in Benutzertexten (ADR-045, Befund A5).
    ///
    /// <para>In Kommentaren sind <c>ae</c>, <c>oe</c> und <c>ue</c> Absicht.
    /// Im Benutzertext waren sie eine unbemerkte Abweichung: «liess sich nicht
    /// oeffnen» stand an neun Stellen, und «Unverschluesselt zulassen» in den
    /// Einstellungen.</para>
    ///
    /// <para><b>Eine Wortliste und keine Buchstabenregel</b>, und das ist der
    /// Kern des Tests. Ein Muster auf <c>ae|oe|ue</c> trifft «zuerst»,
    /// «Steuer», «Dauer» und «genauer» — der erste Anlauf meldete genau solche
    /// Wörter. Eine Liste der Formen, die in diesem Projekt wirklich
    /// aufgetreten sind, hat dagegen <b>keine</b> Fehlalarme und wächst um
    /// einen Eintrag, sobald jemand eine neue findet.</para>
    /// </summary>
    [Fact]
    public void Kein_ae_oe_ue_in_Benutzertexten()
    {
        var verstoesse = new List<string>();

        foreach (var (datei, text) in SichtbareTexte())
        {
            foreach (var form in Umschreibungen)
            {
                if (text.Contains(form, StringComparison.OrdinalIgnoreCase))
                {
                    verstoesse.Add($"{datei}: «{form}» in \"{text}\"");
                }
            }
        }

        Assert.True(
            verstoesse.Count == 0,
            "Benutzertexte werden mit Umlauten geschrieben (ADR-045):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verstoesse));
    }

    /// <summary>
    /// Kein Paragrafen-, ADR- oder Dateiverweis im Benutzertext (Befund A5).
    ///
    /// <para>Wer «Nicht vorgesehen (§2)» liest, kann damit nichts anfangen —
    /// die Spezifikation hat er nicht. Dasselbe gilt für eine ADR-Nummer und
    /// für einen Pfad ins Repo. Diese Verweise gehören in den Kommentar
    /// daneben, wo sie hilfreich sind.</para>
    /// </summary>
    [Fact]
    public void Keine_internen_Verweise_im_Benutzertext()
    {
        var verstoesse = SichtbareTexte()
            .Where(static t => InternerVerweis().IsMatch(t.Text))
            .Select(static t => $"{t.Datei}: \"{t.Text}\"")
            .ToList();

        Assert.True(
            verstoesse.Count == 0,
            "Benutzertexte nennen keinen Paragrafen, keine ADR-Nummer und keinen Repo-Pfad:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verstoesse));
    }

    /// <summary>
    /// Die Umschreibungen, die in diesem Projekt schon vorgekommen sind.
    ///
    /// <para>Wer eine neue findet, trägt sie hier ein. Die Liste ist bewusst
    /// konkret: sie meldet nur, was wirklich falsch ist.</para>
    /// </summary>
    private static readonly string[] Umschreibungen =
    [
        "oeffn", "schluessel", "unverschluessel", "laesst", "laesst",
        "waehl", "koenn", "muess", "naechst", "zurueck", "hoer", "groess",
        "pruef", "aendern", "gespraech", "waehrend", "ueberg", "ueberpr",
        "moegl", "loesch", "erklaer", "ungueltig", "gueltig", "verfuegbar",
    ];

    /// <summary>
    /// Alles, was der Benutzer zu sehen bekommt: XAML-Attributwerte und
    /// C#-Zeichenkettenliterale, ohne Kommentare und ohne Bindungen.
    /// </summary>
    private static List<(string Datei, string Text)> SichtbareTexte()
    {
        var ergebnis = new List<(string, string)>();

        var app = Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App");
        var viewModels = Path.Combine(RepositoryFiles.SourceRoot, "Nipp.Core", "ViewModels");

        foreach (var datei in Xaml(app))
        {
            var name = Path.GetFileName(datei);

            foreach (Match treffer in SichtbaresAttribut().Matches(File.ReadAllText(datei)))
            {
                var wert = treffer.Groups[2].Value;

                if (wert.Length > 0 && !wert.StartsWith('{'))
                {
                    ergebnis.Add((name, wert));
                }
            }
        }

        foreach (var datei in RepositoryFiles.EnumerateSources(app)
            .Concat(RepositoryFiles.EnumerateSources(viewModels)))
        {
            var name = Path.GetFileName(datei);

            // Protokollzeilen sind kein Benutzertext.
            //
            // <b>Das ist keine Ausnahme, sondern die Regel aus CLAUDE.md:</b>
            // dort sind ae, oe und ue ausdruecklich Absicht — ein Protokoll
            // soll unabhaengig von der Kodierung lesbar bleiben, und es landet
            // beim Support, nicht beim Benutzer. Eine Datei, die nur
            // Protokollvorlagen enthaelt, faellt ganz heraus; sonst wird die
            // Zeile uebersprungen, die eine Vorlage traegt oder eine
            // Protokollklasse ruft.
            if (Path.GetFileNameWithoutExtension(datei).EndsWith("Log", StringComparison.Ordinal))
            {
                continue;
            }

            var inVorlage = false;
            var inWurf = false;

            foreach (var zeile in File.ReadAllLines(datei))
            {
                var t = zeile.TrimStart();

                if (t.StartsWith("[LoggerMessage", StringComparison.Ordinal))
                {
                    inVorlage = true;
                }

                if (inVorlage)
                {
                    if (t.EndsWith(']'))
                    {
                        inVorlage = false;
                    }

                    continue;
                }

                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*'))
                {
                    continue;
                }

                if (LogAufruf().IsMatch(zeile))
                {
                    continue;
                }

                // Eine Ausnahme fuer einen Programmierfehler ist kein
                // Benutzertext.
                //
                // <b>In diesem Suchbereich gilt das ausnahmslos.</b> Geprueft
                // werden Nipp.App und die ViewModels; die Ausnahmen dort
                // melden falsche Thread-Zustaende und nicht unterstuetzte
                // Aufrufe — Saetze fuer den, der den Fehler gebaut hat, und
                // dort ist ein Paragrafenverweis genau richtig. Die
                // Ausnahmen, die der Benutzer WIRKLICH zu sehen bekommt,
                // entstehen in SipService und werden von den Befehlen
                // gefangen und als Hint gezeigt — die Datei liegt ausserhalb.
                if (inWurf || zeile.Contains("throw new", StringComparison.Ordinal))
                {
                    inWurf = !zeile.TrimEnd().EndsWith(';');
                    continue;
                }

                foreach (Match treffer in Literal().Matches(zeile))
                {
                    var wert = treffer.Groups[1].Value;

                    // Nur Saetze, keine Bezeichner: ein Benutzertext hat ein
                    // Leerzeichen. Damit fallen Schluessel, Dateinamen und
                    // Formatangaben heraus, ohne dass sie einzeln
                    // ausgenommen werden muessen.
                    if (wert.Contains(' ', StringComparison.Ordinal))
                    {
                        ergebnis.Add((name, wert));
                    }
                }
            }
        }

        return ergebnis;
    }

    private static IEnumerable<string> Xaml(string wurzel) =>
        Directory.EnumerateFiles(wurzel, "*.xaml", SearchOption.AllDirectories)
            .Where(static p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Ein XAML-Attribut, dessen Wert der Benutzer sieht.</summary>
    [GeneratedRegex(
        "(Header|Content|Text|Description|Message|PlaceholderText|Title|OnContent|OffContent)=\"([^\"]*)\"",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex SichtbaresAttribut();

    /// <summary>Ein einfaches C#-Zeichenkettenliteral.</summary>
    [GeneratedRegex(
        "\"([^\"\\\\\\r\\n]{3,})\"",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex Literal();

    /// <summary>Ein Aufruf einer Protokollklasse — alle heissen <c>…Log</c>.</summary>
    [GeneratedRegex(
        @"\b[A-Za-z]*Log\.[A-Z]",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex LogAufruf();

    /// <summary>Ein Paragraf, eine ADR-Nummer oder ein Pfad ins Repo.</summary>
    [GeneratedRegex(
        "(§\\s?[0-9]|ADR-[0-9]|docs/|\\.md\\b)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex InternerVerweis();
}
