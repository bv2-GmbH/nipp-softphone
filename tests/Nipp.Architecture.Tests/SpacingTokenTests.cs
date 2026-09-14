using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Ein Abstand steht als Name da, nicht als Zahl (W2.6, Befund D5).
///
/// <para><b>Warum es diesen Test gibt.</b> Bis zum 13.09.2026 liefen in nipp
/// zwei Abstandsraster nebeneinander. Die Skala in <c>Tokens.xaml</c> sagte
/// 3/6/10/14; gezeichnet wurde ein zweites Raster aus Literalen — einundfünfzig
/// mal die 8, fünfzehn mal die 4, zehn mal die 1 —, und die beiden grossen
/// Stufen der Skala hatten zusammen <b>drei</b> Verwendungen. Eine Änderung an
/// der Skala griff damit nur zur Hälfte, und welche der beiden Zahlen an einer
/// Stelle gewollt war, stand nirgends.</para>
///
/// <para><b>Was der Test prüft und was nicht.</b> Er prüft
/// <c>Spacing</c>, <c>ColumnSpacing</c> und <c>RowSpacing</c> — die Abstände
/// <em>zwischen</em> Geschwistern, also genau das, was eine Skala regeln soll.
/// <c>Margin</c> und <c>Padding</c> lässt er in Ruhe: eine <c>Thickness</c>
/// trägt vier Werte, und ein Rand von <c>12,8,12,8</c> ist keine Stufe einer
/// Skala, sondern die Form einer bestimmten Fläche. Dafür gibt es eigene
/// Tokens (<c>NippPagePadding</c> und die Nachbarn), und die Grenze zwischen
/// «Skala» und «Form» von Hand zu ziehen ist ehrlicher, als sie von einem Test
/// raten zu lassen.</para>
///
/// <para><b>Null bleibt erlaubt.</b> «kein Abstand» ist keine Stufe einer
/// Skala, sondern ihre Abwesenheit — und ein Token dafür hiesse, dass jemand
/// ihn eines Tages auf etwas anderes als null setzt.</para>
/// </summary>
public sealed class SpacingTokenTests
{
    /// <summary>
    /// <c>Spacing="8"</c> und die beiden Grid-Geschwister. Das
    /// <c>(?&lt;![A-Za-z])</c> davor trennt <c>Spacing</c> von
    /// <c>ColumnSpacing</c> nicht — beide sind gemeint —, es verhindert nur,
    /// dass ein Attribut wie <c>LineSpacing</c> eines fremden Steuerelements
    /// zufällig mitgefangen wird.
    /// </summary>
    private static readonly Regex Abstandsliteral = new(
        @"(?<![A-Za-z])(?:Column|Row)?Spacing=""(?<wert>\d+(?:\.\d+)?)""",
        RegexOptions.Compiled);

    [Fact]
    public void Kein_Abstand_steht_als_Zahl_im_XAML()
    {
        var verstoesse = new List<string>();

        foreach (var datei in XamlFiles())
        {
            var zeilen = File.ReadAllLines(datei);

            for (var i = 0; i < zeilen.Length; i++)
            {
                foreach (Match treffer in Abstandsliteral.Matches(zeilen[i]))
                {
                    var wert = treffer.Groups["wert"].Value;

                    if (wert is "0")
                    {
                        continue;
                    }

                    verstoesse.Add(
                        $"{RepositoryFiles.Relative(datei)}:{i + 1}  {treffer.Value}");
                }
            }
        }

        Assert.True(
            verstoesse.Count == 0,
            "Abstaende gehoeren in die Skala in Tokens.xaml, nicht ins XAML: "
                + $"NippGapHair (1), NippGapSmall (3), NippGapMedium (6), "
                + $"NippGapLarge (8), NippGapSection (12). Gefunden:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verstoesse));
    }

    [Fact]
    public void Die_Skala_hat_genau_die_fuenf_Stufen()
    {
        // Die Gegenprobe. Ohne sie liesse sich der Test oben dadurch
        // beruhigen, dass jemand fuer jede Zahl ein eigenes Token anlegt —
        // dann stuenden die zwei Raster wieder nebeneinander, nur mit Namen.
        var tokens = Path.Combine(
            RepositoryFiles.SourceRoot, "Nipp.App", "Themes", "Tokens.xaml");

        var text = File.ReadAllText(tokens);

        var gefunden = Regex.Matches(text, @"x:Key=""(?<name>NippGap\w+)""")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["NippGapHair", "NippGapLarge", "NippGapMedium", "NippGapSection", "NippGapSmall"],
            gefunden);
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App"),
                "*.xaml",
                SearchOption.AllDirectories)
            .Where(static f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(static f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase));
}
