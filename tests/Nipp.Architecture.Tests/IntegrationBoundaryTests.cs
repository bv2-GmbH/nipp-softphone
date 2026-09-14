using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt die Grenzen der Integrationsplattform aus NIPP-BUILD.md §21.2.
///
/// Drei Zusagen, die nur so viel wert sind, wie sie überwacht werden — das ist
/// dieselbe Überlegung wie bei <see cref="SdkBoundaryTests"/> für das SDK:
/// <list type="number">
///   <item>
///     <b>Der Integrationskern kennt weder das SDK noch WinUI.</b> Er soll
///     sich später ohne Codeänderung in ein eigenes Projekt herauslösen
///     lassen (ADR-015), und er muss vollständig ohne Fenster und ohne
///     Telefonanlage testbar bleiben.
///   </item>
///   <item>
///     <b>JSONPath bleibt in einer Ecke.</b> ADR-016 hält fest, dass die
///     Bibliothek austauschbar sein muss; verstreut über den Kern wäre sie es
///     nicht mehr.
///   </item>
///   <item>
///     <b>Keine personenbezogenen Angaben im Protokoll.</b> §21.2 und die
///     Regel, die <c>ContactLog</c> schon trägt: gezählt wird, nicht
///     aufgezählt. Ein Softphone, das Rufnummern und Suchtexte protokolliert,
///     legt ein Bewegungsprofil an.
///   </item>
/// </list>
///
/// Quelltext-Scan, in §13 ausdrücklich zugelassen.
/// </summary>
public sealed partial class IntegrationBoundaryTests
{
    /// <summary>Der Integrationskern.</summary>
    private static string IntegrationRoot { get; } =
        Path.Combine(RepositoryFiles.SourceRoot, "Nipp.Core", "Services", "Integrations");

    /// <summary>Der einzige Ort, an dem JSONPath bekannt sein darf (ADR-016).</summary>
    private static string MappingDirectory { get; } = Path.Combine(IntegrationRoot, "Mapping");

    /// <summary>
    /// Platzhalternamen, die in einer Protokollmeldung nichts zu suchen haben.
    ///
    /// Die Liste ist grob, und das ist Absicht: sie trifft den häufigsten
    /// Fehler — beim Suchen eines Mapping-Problems „nur eben schnell" die
    /// Antwort oder die Nummer mitzuloggen — und lässt sich mit einer
    /// Umbenennung nicht versehentlich umgehen, weil ein anderer Name den
    /// Verstoss auch für den Leser sichtbar machen würde.
    ///
    /// Erlaubt bleibt, was nichts über eine Person sagt: <c>Source</c>,
    /// <c>State</c>, <c>Status</c>, <c>Path</c> (ohne Query), <c>Elapsed</c>,
    /// <c>Count</c>, <c>Bytes</c>, <c>Reason</c>.
    /// </summary>
    private static readonly string[] ForbiddenPlaceholders =
    [
        "Number", "PhoneNumber", "Phone", "Caller", "CallerNumber", "Party",
        "Query", "Search", "SearchText", "Term",
        "Name", "DisplayName", "Company", "Email",
        "Url", "Uri", "Header", "Headers", "Body", "Payload", "Content", "Response",
        "Secret", "Token", "ApiKey", "Password", "Credential",
    ];

    [Fact]
    public void Der_Integrationskern_kennt_weder_das_Sdk_noch_die_Oberflaeche()
    {
        var violations = new List<string>();

        foreach (var file in RepositoryFiles.EnumerateSources(IntegrationRoot))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (RepositoryFiles.IsCode(lines[i]) && ForbiddenReference(lines[i]) is { } what)
                {
                    violations.Add($"{RepositoryFiles.Relative(file)}:{i + 1}  [{what}]  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Services/Integrations darf laut NIPP-BUILD.md §21.2 weder das Linphone SDK
             noch WinUI kennen. Gefundene Verstösse ({violations.Count}):

             {string.Join(Environment.NewLine, violations)}

             Auflösung: Telefonie ausschliesslich über ISipService und die Modelltypen aus
             Services/Telephony/Model/. Alles, was ein Fenster braucht, gehört nach
             Nipp.App — der Kern liefert ihm ein Modell, keinen Steuerelementbaum.
             """);
    }

    [Fact]
    public void Jsonpath_bleibt_in_der_Mapping_Schicht()
    {
        var violations = RepositoryFiles.EnumerateSources(IntegrationRoot)
            .Where(static file => !file.StartsWith(MappingDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(static file => File.ReadAllLines(file)
                .Any(static line => RepositoryFiles.IsCode(line) && ReferencesJsonPath(line)))
            .Select(RepositoryFiles.Relative)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"""
             JSONPath ist laut ADR-016 austauschbar zu halten und deshalb nur unter
             Services/Integrations/Mapping/ erlaubt. Betroffen: {string.Join(", ", violations)}

             Auflösung: den Pfadzugriff in der MappingEngine kapseln und nach aussen
             ContextValue liefern — kein JsonNode und kein JsonPath verlässt die Schicht.
             """);
    }

    [Fact]
    public void Protokollmeldungen_der_Integrationen_tragen_keine_personenbezogenen_Angaben()
    {
        var violations = new List<string>();

        foreach (var file in RepositoryFiles.EnumerateSources(IntegrationRoot))
        {
            var lines = File.ReadAllLines(file);
            var insideAttribute = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains("[LoggerMessage", StringComparison.Ordinal))
                {
                    insideAttribute = true;
                }

                if (insideAttribute)
                {
                    foreach (Match match in Placeholder().Matches(line))
                    {
                        var name = match.Groups[1].Value;

                        if (ForbiddenPlaceholders.Contains(name, StringComparer.Ordinal))
                        {
                            violations.Add($"{RepositoryFiles.Relative(file)}:{i + 1}  {{{name}}}");
                        }
                    }

                    if (line.Contains(")]", StringComparison.Ordinal))
                    {
                        insideAttribute = false;
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Diese Protokollmeldungen würden Rufnummern, Suchtexte, Namen oder
             Antwortinhalte ins Log schreiben (§21.2, §11). Gefunden ({violations.Count}):

             {string.Join(Environment.NewLine, violations)}

             Auflösung: zählen statt aufzählen. Die Quelle, ihr Zustand, der
             HTTP-Status, die Dauer und die Anzahl Felder genügen zur Diagnose —
             und ein Log voller Rufnummern ist ein Bewegungsprofil.
             """);
    }

    [Fact]
    public void Der_Quelltextscan_findet_ueberhaupt_Dateien()
    {
        // Ohne diesen Test wären die drei anderen grün, solange der Ordner
        // fehlt oder umbenannt wurde — die Grenze wäre unbewacht, ohne dass es
        // auffällt. Dieselbe Absicherung wie bei SdkBoundaryTests.
        var count = RepositoryFiles.EnumerateSources(IntegrationRoot).Count();

        Assert.True(
            count > 0,
            $"Unter '{IntegrationRoot}' liegt keine einzige Quelldatei. Entweder ist der "
                + "Integrationskern verschoben worden — dann gehört dieser Test nachgezogen — "
                + "oder die Grenze aus §21.2 bewacht gerade nichts.");
    }

    /// <summary>
    /// Was eine Zeile unzulässig macht, oder <c>null</c>. Der Name der Gruppe
    /// steht in der Fehlermeldung, damit man nicht raten muss, welche der
    /// beiden Grenzen gemeint ist.
    /// </summary>
    private static string? ForbiddenReference(string line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("using Linphone;", StringComparison.Ordinal)
            || trimmed.StartsWith("using Linphone.", StringComparison.Ordinal)
            || trimmed.StartsWith("using static Linphone.", StringComparison.Ordinal)
            || SdkType().IsMatch(trimmed))
        {
            return "SDK";
        }

        return UiType().IsMatch(trimmed) ? "Oberfläche" : null;
    }

    private static bool ReferencesJsonPath(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("using Json.Path", StringComparison.Ordinal)
            || trimmed.Contains("Json.Path.", StringComparison.Ordinal)
            || trimmed.Contains("JsonPath.Parse", StringComparison.Ordinal);
    }

    /// <summary><c>Linphone.</c> vor einem Grossbuchstaben, aber nicht als Teil eines längeren Namens.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_])Linphone\.[A-Z]")]
    private static partial Regex SdkType();

    /// <summary>
    /// Die Namensräume der Oberfläche. <c>Windows.UI</c> und
    /// <c>Windows.Foundation</c> stehen mit dabei, weil auch sie einen Typ aus
    /// dem Fensterstapel in den Kern holen würden.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])(Microsoft\.UI|Microsoft\.Xaml|Windows\.UI|Windows\.Foundation|CommunityToolkit\.WinUI)\.")]
    private static partial Regex UiType();

    /// <summary>Ein Platzhalter in einer Protokollvorlage: <c>{Name}</c>.</summary>
    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex Placeholder();
}
