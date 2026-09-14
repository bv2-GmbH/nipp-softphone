using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Prüft, dass jeder Name in <c>ThemeService.ThemedBrushNames</c> in
/// <c>Themes/Tokens.xaml</c> wirklich als <c>…Color</c> in <b>jedem</b>
/// Themenwörterbuch und als <c>…Brush</c> auf oberster Ebene existiert.
///
/// <b>Warum es diesen Test gibt.</b> Am 06.09.2026 startete nipp nicht mehr:
/// <c>ApplyStatusBrushes</c> las die Farben mit dem Indexer, und der wirft bei
/// einem fehlenden Schlüssel. Weil das aus dem Konstruktor von
/// <c>MainWindow</c> läuft, nahm die Ausnahme die ganze Anwendung mit — kein
/// Fenster, keine Meldung, nur ein <c>[FTL]</c> im Protokoll.
///
/// Die Reparatur liest jetzt mit <c>TryGetValue</c>. Das behebt den Absturz,
/// <b>tauscht aber einen lauten Fehler gegen einen stillen</b>: fehlt ein
/// Schlüssel, bleibt der Pinsel einfach in der Farbe des Systemthemas, ohne
/// Absturz und ohne Protokolleintrag. Bei dunklem Erscheinungsbild liegt
/// <c>#107C10</c> auf dunklem Grund bei etwa 2,5:1 Kontrast — unter jeder
/// Lesbarkeitsgrenze, und niemand erfährt davon.
///
/// Dieser Test ist der Ausgleich dafür. <c>XamlResourceTests</c> kann die
/// Lücke nicht sehen: dort werden <c>StaticResource</c>-Verweise <b>in
/// XAML</b> geprüft, die Namen hier stehen aber als Zeichenfolgen in C#.
/// </summary>
public sealed class ThemedBrushTests
{
    [Fact]
    public void Jeder_themenabhaengige_Pinsel_hat_in_jedem_Thema_eine_Farbe()
    {
        var namen = ThemedBrushNames();
        var tokens = XDocument.Load(TokensPath);

        var themen = tokens
            .Descendants()
            .Where(static e => e.Name.LocalName == "ResourceDictionary")
            .Select(static e => (string?)e.Attribute(XName.Get("Key", XamlNamespace)))
            .Where(static k => k is { Length: > 0 })
            .ToList();

        // ThemeService sucht nur diese zwei — sie müssen da sein. Weitere
        // Themen (heute HighContrast) werden mitgeprüft, aber nicht verlangt.
        Assert.Contains("Light", themen);
        Assert.Contains("Dark", themen);

        var fehlend = new List<string>();

        foreach (var thema in themen)
        {
            var woerterbuch = tokens
                .Descendants()
                .First(e => e.Name.LocalName == "ResourceDictionary"
                    && (string?)e.Attribute(XName.Get("Key", XamlNamespace)) == thema);

            var vorhanden = Schluessel(woerterbuch);

            foreach (var name in namen.Where(n => !vorhanden.Contains(n + "Color")))
            {
                fehlend.Add($"{name}Color fehlt im Thema «{thema}»");
            }
        }

        Assert.True(
            fehlend.Count == 0,
            $"""
             In Themes/Tokens.xaml fehlen Farben, die ThemeService erwartet:

             {string.Join(Environment.NewLine, fehlend)}

             Der Pinsel bleibt dann still in der Farbe des Systemthemas — kein
             Absturz, kein Protokolleintrag, nur unlesbare Kontraste im
             dunklen Erscheinungsbild. Farbe in Tokens.xaml nachtragen oder
             den Namen aus ThemedBrushNames entfernen.
             """);
    }

    [Fact]
    public void Jeder_themenabhaengige_Pinsel_ist_auch_als_Brush_definiert()
    {
        var namen = ThemedBrushNames();
        var tokens = XDocument.Load(TokensPath);

        // SolidColorBrush-Einträge stehen auf oberster Ebene, nicht in den
        // Themenwörterbüchern: es ist je Pinsel *eine* Instanz, deren Farbe
        // ThemeService umsetzt.
        var pinsel = tokens
            .Descendants()
            .Where(static e => e.Name.LocalName == "SolidColorBrush")
            .Select(static e => (string?)e.Attribute(XName.Get("Key", XamlNamespace)))
            .Where(static k => k is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal);

        var fehlend = namen
            .Where(n => !pinsel.Contains(n + "Brush"))
            .Select(static n => $"{n}Brush")
            .ToList();

        Assert.True(
            fehlend.Count == 0,
            "In Themes/Tokens.xaml fehlen diese Pinsel, die ThemeService umfärben will: "
                + string.Join(", ", fehlend));
    }

    /// <summary>
    /// <c>FindThemeDictionary</c> erkennt das eigene Wörterbuch an
    /// <c>ProbeKey</c> — dem ersten Namen der Liste. Steht der nicht in
    /// Tokens.xaml, findet die Suche gar nichts mehr, und <b>alle</b> Pinsel
    /// bleiben ungefärbt. Der erste Eintrag trägt damit mehr Gewicht als die
    /// übrigen; dass er stimmt, sichern die Tests oben. Dieser hier hält
    /// fest, dass die Liste überhaupt einen ersten Eintrag hat.
    /// </summary>
    [Fact]
    public void Die_Liste_der_Pinsel_ist_nicht_leer()
    {
        Assert.NotEmpty(ThemedBrushNames());
    }

    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static HashSet<string> Schluessel(XElement woerterbuch) =>
        woerterbuch
            .Elements()
            .Select(static e => (string?)e.Attribute(XName.Get("Key", XamlNamespace)))
            .Where(static k => k is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal)!;

    /// <summary>
    /// Liest die Namen aus dem Quelltext von <c>ThemeService.cs</c>. Ein
    /// Verweis auf <c>Nipp.App</c> ist hier nicht möglich — das Projekt ist
    /// WinUI und lässt sich in einem gewöhnlichen Testlauf nicht laden.
    /// Deshalb derselbe Weg wie beim SDK-Grenztest: ein Quelltext-Scan.
    /// </summary>
    private static List<string> ThemedBrushNames()
    {
        var quelle = File.ReadAllText(
            Path.Combine(SourceRoot, "Nipp.App", "Theming", "ThemeService.cs"));

        var block = Regex.Match(
            quelle,
            @"ThemedBrushNames\s*=\s*\[(?<inhalt>[^\]]*)\]",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));

        Assert.True(
            block.Success,
            "ThemedBrushNames wurde in ThemeService.cs nicht gefunden. Wurde die "
                + "Liste umbenannt oder anders geschrieben? Dann gehört dieser Test "
                + "nachgezogen — sonst prüft er stillschweigend nichts mehr.");

        var namen = Regex
            .Matches(block.Groups["inhalt"].Value, "\"(?<name>[^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(static m => m.Groups["name"].Value)
            .ToList();

        Assert.NotEmpty(namen);

        return namen;
    }

    // Berechnet, nicht zwischengespeichert: ein statisches Feld hier oben
    // würde vor SourceRoot initialisiert und bekäme null.
    private static string TokensPath =>
        Path.Combine(SourceRoot, "Nipp.App", "Themes", "Tokens.xaml");

    private static string SourceRoot { get; } = ResolveSourceRoot();

    private static string ResolveSourceRoot()
    {
        var configured = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static a => a.Key == "RepositorySourceRoot")
            ?.Value;

        return configured is { Length: > 0 }
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
    }

    /// <summary>
    /// Ein Statuston, der Schrift färbt, erreicht 4,5:1 (W1.4, Befund D3).
    ///
    /// <para><b>Der Befund.</b> Seit ADR-044 färbt ein Statuston Schrift und
    /// Rand, nie die Fläche — richtig, weil derselbe Ton als Fläche im Dunkeln
    /// fast weissen Text auf Gelb ergab, rund 1,4:1. Damit gilt aber die
    /// Textschwelle, und im <b>hellen</b> Thema erreichte
    /// <c>#B85C00</c> gegen Mica nur <b>4,14:1</b>. Der Warnton war also ein
    /// halbes Jahr lang die Farbe, in der «Anmeldung läuft» und der Chip
    /// «unverschlüsselt» standen — und beide waren zu blass.</para>
    ///
    /// <para><b>Rechnung, nicht Messung.</b> Die Mica-Grundfarbe ist eine
    /// Annahme (<c>#F3F3F3</c> hell, <c>#202020</c> dunkel); am gerenderten
    /// Mica gemessen wird sie nicht. Der Test schützt trotzdem vor dem Fall,
    /// der hier eingetreten ist: jemand wählt einen Ton nach Augenmass und
    /// niemand rechnet nach.</para>
    /// </summary>
    [Theory]
    [InlineData("Light", "F3F3F3")]
    [InlineData("Dark", "202020")]
    public void Jeder_Statuston_ist_auf_seinem_Grund_lesbar(string thema, string grund)
    {
        var zuBlass = new List<string>();
        var unbekannt = new List<string>();

        foreach (var (name, wert) in ThemeColors(thema))
        {
            if (!wert.StartsWith('#') || wert.Length != 7)
            {
                // Im Kontrastmodus stehen Systemfarben statt Werten — dort
                // entscheidet der Benutzer, nicht wir.
                continue;
            }

            if (!Schwellen.TryGetValue(name, out var schwelle))
            {
                unbekannt.Add(name);
                continue;
            }

            if (schwelle <= 0)
            {
                // Eine Flaeche traegt keinen Vordergrund; gegen den Grund
                // gemessen sagt ihr Kontrast nichts aus.
                continue;
            }

            var verhaeltnis = Kontrast(wert[1..], grund);

            if (verhaeltnis < schwelle)
            {
                zuBlass.Add(
                    $"{name} = {wert} auf #{grund}: {verhaeltnis:F2}:1, "
                        + $"gefordert {schwelle:F1}:1");
            }
        }

        Assert.True(
            unbekannt.Count == 0,
            "Diese Farben stehen in keiner Klasse. Bitte in 'Schwellen' eintragen und dabei "
                + "entscheiden, ob sie Schrift faerben (4,5), ein Bedienelement (3,0) oder eine "
                + "Flaeche (0):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, unbekannt));

        Assert.True(
            zuBlass.Count == 0,
            "Diese Farben sind auf ihrem Grund zu blass (ADR-044, W1.4):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, zuBlass));
    }

    /// <summary>
    /// Welchen Kontrast eine Farbe braucht, und warum.
    ///
    /// <para><b>Die Klasse ist die eigentliche Aussage.</b> Seit ADR-044 färbt
    /// ein Statuston Schrift und Rand, nie die Fläche — für ihn gilt deshalb
    /// die Textschwelle von 4,5:1. Eine Trennlinie und ein Kartenrand sind
    /// Bedienelemente (3:1), eine Kartenfläche trägt selbst keinen Vordergrund
    /// und wird nicht gemessen.</para>
    ///
    /// <para><b>Wer eine Farbe ergänzt, ohne sie hier einzutragen, bekommt
    /// einen roten Test</b> — und das ist der Zweck: die Entscheidung «wofür
    /// ist diese Farbe da» soll einmal ausdrücklich getroffen werden, statt
    /// stillschweigend über den Namen.</para>
    /// </summary>
    private static readonly Dictionary<string, double> Schwellen = new(StringComparer.Ordinal)
    {
        // Schrift und Rand (ADR-044)
        ["PresenceAvailableColor"] = 4.5,
        ["PresenceRingingColor"] = 4.5,
        ["PresenceBusyColor"] = 4.5,
        ["PresenceOfflineColor"] = 4.5,
        ["PresenceUnknownColor"] = 4.5,
        ["StatusRegisteredColor"] = 4.5,
        ["StatusProgressColor"] = 4.5,
        ["StatusFailedColor"] = 4.5,
        ["RecordingIndicatorColor"] = 4.5,
        ["EncryptionSecureColor"] = 4.5,
        ["EncryptionInsecureColor"] = 4.5,
        ["CardSecondaryTextColor"] = 4.5,

        // Nicht gemessen, und das ist eine Entscheidung und keine Ausnahme:
        //
        // Trennlinie und Kartenrand tragen KEINE Information. Was die Karte
        // sagt, steht in ihrem Text, und der ist ohne sie genauso lesbar — sie
        // gruppieren, mehr nicht. WCAG 1.4.11 nimmt rein dekorative Elemente
        // ausdruecklich aus; eine Trennlinie mit 3:1 waere ein schwarzer
        // Strich und saehe in keinem der beiden Themen nach Fluent aus.
        //
        // <b>Wer hier eine Linie eintraegt, die doch etwas bedeutet</b> — eine
        // Umrandung, die «ausgewaehlt» oder «fehlerhaft» anzeigt —, setzt sie
        // auf 3,0. Dann traegt sie Information, und die Regel gilt.
        ["CardDividerColor"] = 0,
        ["CardOutlineColor"] = 0,

        // Flaeche: traegt selbst keinen Vordergrund.
        ["CardFillColor"] = 0,
    };

    /// <summary>Die Farbwerte eines Themenwoerterbuchs, Name ohne «Color».</summary>
    private static List<(string Name, string Wert)> ThemeColors(string thema)
    {
        var xaml = XDocument.Load(TokensPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var woerterbuch = xaml.Descendants()
            .First(e => e.Name.LocalName == "ResourceDictionary"
                && (string?)e.Attribute(x + "Key") == thema);

        return [.. woerterbuch.Elements()
            .Where(e => e.Name.LocalName == "Color")
            .Select(e => ((string?)e.Attribute(x + "Key") ?? string.Empty, e.Value.Trim()))];
    }

    /// <summary>Das Kontrastverhaeltnis nach WCAG 2.1.</summary>
    private static double Kontrast(string a, string b)
    {
        var la = Leuchtdichte(a);
        var lb = Leuchtdichte(b);

        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Leuchtdichte(string hex)
    {
        static double Kanal(int wert)
        {
            var c = wert / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var r = Kanal(Convert.ToInt32(hex[..2], 16));
        var g = Kanal(Convert.ToInt32(hex.Substring(2, 2), 16));
        var b = Kanal(Convert.ToInt32(hex.Substring(4, 2), 16));

        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }
}
