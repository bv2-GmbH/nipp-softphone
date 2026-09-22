using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Jede Listenzeile sagt einer Sprachausgabe, was sie ist (Befund A1-16).
///
/// <para><b>Was sonst passiert.</b> Ohne <c>AutomationProperties.Name</c> nimmt
/// die Automation den <c>ToString()</c> des Zeilenmodells. Bei einer Klasse ist
/// das der volle Typname, bei einem <c>record</c> die ganze Aufstellung seiner
/// Werte. Am 22.09.2026 gemessen:</para>
///
/// <list type="bullet">
///   <item>Quellenliste der Einstellungen —
///     «Nipp.Core.ViewModels.IntegrationSourceRow»</item>
///   <item>Wählvorschläge — «DialSuggestion { Title = …, Subtitle = …,
///     Number = …, Source = Team, HasSubtitle = True }», also die Nummer
///     zweimal und ein internes Feld dazu, bei jedem Tastendruck fünfmal</item>
/// </list>
///
/// <para><b>Dasselbe war schon am 07.09.2026 da</b>, damals an Katalog und
/// Palette (T109). Es ist zweimal reingekommen, ohne dass etwas es gemerkt
/// hätte — <c>UserTextTests</c> sieht nur Literale, und ein Komponententest
/// sieht die Oberfläche gar nicht.</para>
///
/// <para><b>Ein eigenes <c>ToString()</c> am Modell täte es auch</b> —
/// <c>CallRow</c> macht das seit jeher und schreibt hin, warum. Dieser Test
/// lässt es trotzdem nicht als Ausweg gelten, aus zwei Gründen: er müsste
/// dafür den Quelltext des Kerns durchsuchen (einen Verweis auf
/// <c>Nipp.Core</c> hat dieses Projekt bewusst nicht), und ein
/// <c>ToString()</c> ist für die Fehlersuche da, nicht für die Sprachausgabe —
/// wer es ändert, denkt an das Debugfenster und nicht an den Menschen, der
/// zuhört. <b>Die Vorlage ist der Ort, an dem beides zusammenfällt.</b></para>
/// </summary>
public sealed class ListenNamenTests
{
    /// <summary>
    /// Vorlagen, die <b>keinen</b> eigenen Container bekommen: was in einem
    /// <c>ItemsControl</c> steht, wird von der Automation über seinen Inhalt
    /// gelesen, nicht über das Modell. Hier ist ein Name nicht falsch, aber
    /// überflüssig — und ein überflüssiger Name verdeckt den Inhalt.
    ///
    /// <para><b>Die Liste ist eine Sperrliste und keine Erlaubnisliste</b>
    /// (dieselbe Überlegung wie bei <c>NurAnzeige</c> in den Einstellungen):
    /// wer eine Vorlage vergisst, bekommt einen roten Test und keinen stillen
    /// Ausfall.</para>
    /// </summary>
    private static readonly string[] OhneEigenenContainer =
    [
        // Der Detailbereich einer Kontaktzeile: ein ItemsControl mit
        // Nummernzeilen, die ihre eigenen Namen tragen.
        "NippContactDetailTemplate",
    ];

    [Fact]
    public void Jede_Listenzeile_traegt_einen_Namen_fuer_die_Sprachausgabe()
    {
        var app = Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App");
        var fehler = new List<string>();
        var geprueft = 0;

        foreach (var datei in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(static f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(static f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(datei);

            foreach (Match treffer in Regex.Matches(text, """<DataTemplate\b[^>]*?x:DataType="(?<typ>[^"]+)"[^>]*>"""))
            {
                var kopf = treffer.Value;
                var typ = treffer.Groups["typ"].Value;
                var name = typ.Contains(':') ? typ[(typ.IndexOf(':') + 1)..] : typ;

                if (OhneEigenenContainer.Any(a => kopf.Contains(a, StringComparison.Ordinal)))
                {
                    continue;
                }

                geprueft++;

                var rest = text[treffer.Index..];
                var ende = rest.IndexOf("</DataTemplate>", StringComparison.Ordinal);
                var block = ende > 0 ? rest[..ende] : rest;

                if (block.Contains("AutomationProperties.Name", StringComparison.Ordinal))
                {
                    continue;
                }

                var zeile = text[..treffer.Index].Count(c => c == '\n') + 1;

                fehler.Add(
                    $"{RepositoryFiles.Relative(datei)}:{zeile} — die Vorlage für «{name}» hat weder "
                        + "AutomationProperties.Name noch ein eigenes ToString() am Modell. Eine "
                        + "Sprachausgabe liest sonst den Typnamen oder die ganze Werteaufstellung "
                        + "(Befund A1-16).");
            }
        }

        Assert.True(geprueft > 10, $"Nur {geprueft} Vorlagen gefunden — sucht der Test noch am richtigen Ort?");
        Assert.True(fehler.Count == 0, string.Join(Environment.NewLine, fehler));
    }

}
