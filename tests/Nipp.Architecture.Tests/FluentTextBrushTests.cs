using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Fluent-Systempinsel, die nipp nicht benutzt (Befund A1-17).
///
/// <para><b>Warum es diesen Test gibt.</b> <see cref="ThemedBrushTests"/> misst
/// die Farben aus <c>Themes/Tokens.xaml</c> — die eigenen. Ein Pinsel, der von
/// Windows kommt, steht dort nicht und wird deshalb nie gemessen. Am 22.09.2026
/// an den Pixeln nachgerechnet: die Copyright-Zeile stand im hellen Thema bei
/// <b>3,28:1</b>, gefordert sind 4,5:1 für Schrift. Die Farbe war
/// <c>TextFillColorTertiaryBrush</c>, und sie färbte <b>23 Textstellen in vier
/// Dateien</b>.</para>
///
/// <para><b>Warum die dritte Stufe und nicht die zweite.</b> Fluent kennt vier
/// Textfarben; die dritte ist für Inhalt gedacht, der <i>nebensächlich</i> ist.
/// Nur kennt WCAG diese Kategorie nicht — eine Zeile, die dasteht, wird gelesen
/// oder sie gehört weg. <c>TextFillColorSecondaryBrush</c> liegt bei 6,53:1
/// hell und 7,02:1 dunkel und ist optisch immer noch zurückgenommen.</para>
///
/// <para><b>Eine Sperrliste, keine Erlaubnisliste</b> — dieselbe Bauart wie bei
/// den Abständen und den Speicherlisten: verboten ist, was gemessen zu blass
/// war, und nicht erlaubt, was jemand geprüft hat.</para>
/// </summary>
public sealed class FluentTextBrushTests
{
    /// <summary>
    /// Systempinsel, die in nipp nicht als Vordergrund vorkommen dürfen, mit
    /// dem gemessenen Grund für das Verbot.
    /// </summary>
    private static readonly Dictionary<string, string> Verboten = new(StringComparer.Ordinal)
    {
        ["TextFillColorTertiaryBrush"] =
            "3,28:1 im hellen Thema (Befund A1-17, gefordert 4,5:1) — "
                + "TextFillColorSecondaryBrush nehmen",
        ["TextFillColorDisabledBrush"] =
            "die Farbe eines gesperrten Bedienelements; als Text ist sie unter jeder "
                + "Lesbarkeitsgrenze und behauptet ausserdem eine Sperre",
    };

    [Fact]
    public void Kein_zu_blasser_Systempinsel_faerbt_Text()
    {
        var app = Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App");
        var fehler = new List<string>();

        foreach (var datei in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(static f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(static f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)))
        {
            var zeilen = File.ReadAllLines(datei);

            for (var i = 0; i < zeilen.Length; i++)
            {
                foreach (var (name, grund) in Verboten)
                {
                    if (!zeilen[i].Contains(name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    fehler.Add($"{RepositoryFiles.Relative(datei)}:{i + 1} — {name}: {grund}");
                }
            }
        }

        Assert.True(fehler.Count == 0, string.Join(Environment.NewLine, fehler));
    }

    /// <summary>
    /// Die Gegenprobe: der Ersatz muss auch wirklich verwendet werden. Ohne das
    /// wäre der Test darüber auch dann grün, wenn jemand alle Farbangaben
    /// gelöscht hätte.
    /// </summary>
    [Fact]
    public void Der_Ersatz_ist_im_Einsatz()
    {
        var app = Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App");
        var treffer = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(static f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(static f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .Sum(f => Regex.Matches(File.ReadAllText(f), "TextFillColorSecondaryBrush").Count);

        Assert.True(
            treffer >= 20,
            $"Nur {treffer} Verwendungen von TextFillColorSecondaryBrush — es waren 23 "
                + "Stellen, die von der dritten Stufe darauf umgestellt wurden (Befund A1-17). "
                + "Sind sie verschwunden, misst der Test daneben nichts mehr.");
    }
}
