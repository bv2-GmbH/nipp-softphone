namespace Nipp.Core.Tests;

/// <summary>
/// Die synthetischen Anbietervorlagen für die Tests (ADR-040).
///
/// <para><b>Warum es sie gibt.</b> Bis zum 11.09.2026 trugen zwei echte
/// bv2-Systeme diese Rolle: an ihnen hing der Beweis, dass die Mapping-Maschine
/// verschachtelte Arrays, Filter auf ein Kennzeichen, <c>first()</c>,
/// <c>join()</c>, Datumsformate und <c>itemsPath</c> beherrscht. Mit dem
/// Öffentlichmachen des Repos sind sie verschwunden — die <b>Antwortformen</b>
/// mussten bleiben.</para>
///
/// <para><b>Sie liegen beim Test und nicht in <c>Nipp.Core</c>.</b> Eine
/// Testvorlage als eingebettete Ressource im Produkt wäre genau die Lehre, um
/// die es in diesem Arbeitspaket geht.</para>
/// </summary>
public static class TestTemplates
{
    /// <summary>Der Ordner mit den Vorlagendateien, neben der Testassembly.</summary>
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Connectors");

    /// <summary>Alle Vorlagendateien.</summary>
    public static IReadOnlyList<string> Files =>
        System.IO.Directory.Exists(Directory)
            ? [.. System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(static f => f, StringComparer.Ordinal)]
            : [];

    /// <summary>Der Inhalt einer Vorlage, über ihren Dateinamen ohne Endung.</summary>
    public static string Read(string id) =>
        File.ReadAllText(Path.Combine(Directory, id + ".json"));
}
