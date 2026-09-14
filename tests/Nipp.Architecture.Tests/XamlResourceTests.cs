using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Prüft, dass jeder <c>StaticResource</c>-Verweis in einer XAML-Datei auch
/// aufgelöst werden kann.
///
/// <b>Warum es diesen Test gibt.</b> XAML löst Ressourcen erst zur Laufzeit
/// auf, und der Compiler schweigt dazu. Ein Tippfehler oder ein Konverter, der
/// nie in <c>App.xaml</c> eingetragen wurde, fällt deshalb erst auf, wenn
/// jemand die betroffene Seite öffnet — und dann nicht als Fehlermeldung,
/// sondern als Absturz der ganzen Anwendung:
///
/// <code>
/// [FTL] Unbehandelte Ausnahme:
///       Cannot find a Resource with the Name/Key AccountStateTextConverter
/// </code>
///
/// Genau das ist am 05.09.2026 passiert, als die Einstellungsseite zum ersten
/// Mal wirklich geöffnet wurde. Der Konverter war seit Wochen im XAML
/// referenziert und nirgends definiert; der Build war die ganze Zeit grün.
///
/// Der Test ist ein Quelltext-Scan wie der SDK-Grenztest (§13 lässt das
/// ausdrücklich zu).
/// </summary>
public sealed class XamlResourceTests
{
    /// <summary>
    /// Schlüssel, die WinUI selbst mitbringt. Sie stehen in den generischen
    /// Wörterbüchern des Frameworks, nicht im Repo — der Scan kann sie nicht
    /// sehen und darf sie nicht bemängeln.
    ///
    /// Die Liste enthält nur, was tatsächlich verwendet wird. Sie wächst mit;
    /// ein neuer Framework-Schlüssel meldet sich beim ersten Testlauf.
    /// </summary>
    private static readonly HashSet<string> FrameworkKeys = new(StringComparer.Ordinal)
    {
        // Textstile
        "CaptionTextBlockStyle",
        "BodyTextBlockStyle",
        "BodyStrongTextBlockStyle",
        "SubtitleTextBlockStyle",
        "TitleTextBlockStyle",
        "TitleLargeTextBlockStyle",
        "DisplayTextBlockStyle",

        // Schaltflächen und Abzeichen
        "AccentButtonStyle",
        "DefaultButtonStyle",
        "AttentionValueInfoBadgeStyle",
        "AttentionDotInfoBadgeStyle",
        "CriticalValueInfoBadgeStyle",
        "InformationalValueInfoBadgeStyle",

        // Pinsel aus dem Farbsystem von WinUI. Sie wechseln mit dem
        // Erscheinungsbild von selbst — deshalb werden sie verwendet und nicht
        // durch eigene ersetzt (§20.4).
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "TextFillColorTertiaryBrush",
        "TextFillColorDisabledBrush",
        "TextOnAccentFillColorPrimaryBrush",
        "AccentFillColorDefaultBrush",
        "AccentFillColorSecondaryBrush",
        "ControlFillColorDefaultBrush",
        "ControlFillColorSecondaryBrush",
        "ControlStrokeColorDefaultBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardBackgroundFillColorSecondaryBrush",
        "CardStrokeColorDefaultBrush",
        "LayerFillColorDefaultBrush",
        "SubtleFillColorSecondaryBrush",
        "SystemFillColorCriticalBrush",
        "SystemFillColorSuccessBrush",
        "SystemFillColorCautionBrush",
        "SystemFillColorCautionBackgroundBrush",

        // Die Farbe hinter dem Pinsel darueber. Fluent definiert jeden
        // SystemFillColor*Brush als SolidColorBrush ueber die gleichnamige
        // *Color; die Gesprächsansicht braucht die Farbe, weil sie den
        // Auflegen-Knopf in zwei Deckkraftstufen abstuft (W2.6, D14).
        "SystemFillColorCriticalColor",

        // Die beiden Eckradien des Systems (W2.6, D10). nipp hatte sie als
        // NippControlCornerRadius und NippCardCornerRadius abgeschrieben —
        // ein eigener Name fuer einen fremden Wert. Beide sind seit dem
        // 13.09.2026 weg, und die Verwender nehmen das Original.
        "ControlCornerRadius",
        "OverlayCornerRadius",
    };

    /// <summary>
    /// Muster für <c>{StaticResource Schlüssel}</c> und
    /// <c>{ThemeResource Schlüssel}</c>, auch verschachtelt in einer längeren
    /// Markuperweiterung wie
    /// <c>{Binding X, Converter={StaticResource Y}}</c>.
    /// </summary>
    private static readonly Regex ResourceReference = new(
        @"\{\s*(?:StaticResource|ThemeResource)\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Jeder_verwendete_Ressourcenschluessel_ist_auch_definiert()
    {
        var defined = CollectDefinedKeys();
        var violations = new List<string>();

        foreach (var file in EnumerateXamlFiles())
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in ResourceReference.Matches(lines[i]))
                {
                    var key = match.Groups[1].Value;

                    if (defined.Contains(key) || FrameworkKeys.Contains(key))
                    {
                        continue;
                    }

                    violations.Add($"{Relative(file)}:{i + 1}  {key}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Diese Ressourcenschlüssel werden in XAML verwendet, sind aber nirgends definiert.
             Das gibt zur Laufzeit keinen Fehler, sondern einen Absturz beim Öffnen der Seite
             ("Cannot find a Resource with the Name/Key ...").

             Gefunden ({violations.Count}):

             {string.Join(Environment.NewLine, violations)}

             Auflösung: den Schlüssel in App.xaml (Application.Resources) oder in
             Themes/Tokens.xaml eintragen. Konverter gehören nach App.xaml — eine Seite
             in einem Frame hat ihren eigenen Ressourcenbaum und findet sie sonst nicht.
             Ist der Schlüssel vom Framework, gehört er in die Liste FrameworkKeys
             dieses Tests.
             """);
    }

    /// <summary>
    /// Alle Schlüssel, die das Repo selbst definiert: <c>x:Key</c> in einer
    /// beliebigen XAML-Datei.
    ///
    /// Der Scan ist bewusst grob — er unterscheidet nicht, in welchem
    /// Wörterbuch ein Schlüssel steht. Ein Schlüssel, der in einem
    /// Seitenwörterbuch definiert und in einer anderen Seite verwendet wird,
    /// fällt hier also nicht auf. Diesen Fall abzudecken hiesse, die
    /// Auflösungsregeln von XAML nachzubauen; der Gewinn stünde in keinem
    /// Verhältnis. Der häufige Fall — der Schlüssel existiert überhaupt nicht —
    /// ist abgedeckt.
    /// </summary>
    private static HashSet<string> CollectDefinedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var file in EnumerateXamlFiles())
        {
            XDocument document;

            try
            {
                document = XDocument.Load(file);
            }
            catch (System.Xml.XmlException)
            {
                // Eine XAML-Datei, die sich nicht als XML lesen lässt, bringt
                // der Build ohnehin zu Fall. Hier nicht doppelt melden.
                continue;
            }

            foreach (var element in document.Descendants())
            {
                if (element.Attribute(x + "Key")?.Value is { Length: > 0 } key)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    [Fact]
    public void Der_Scan_findet_ueberhaupt_Xaml_Dateien()
    {
        // Ohne diesen Test wäre ein leerer Scan grün — dieselbe Falle wie beim
        // SDK-Grenztest.
        var count = EnumerateXamlFiles().Count();

        Assert.True(
            count > 0,
            $"Es wurden keine XAML-Dateien gefunden. Erwartet unter '{SourceRoot}'.");
    }

    private static IEnumerable<string> EnumerateXamlFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) =>
        Path.GetRelativePath(SourceRoot, path);

    private static string SourceRoot { get; } = ResolveSourceRoot();

    private static string ResolveSourceRoot()
    {
        var configured = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositorySourceRoot")
            ?.Value;

        return configured is { Length: > 0 }
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
    }
}
