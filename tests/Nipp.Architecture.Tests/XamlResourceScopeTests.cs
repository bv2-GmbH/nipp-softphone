using System.Xml.Linq;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Prüft, dass ein page-lokaler XAML-Schlüssel dort auch <b>sichtbar</b> ist,
/// wo er benutzt wird.
///
/// <para><b>Der Anlass ist ein selbst gemachter Fehler.</b> Beim Umbau der
/// Gesprächsansicht bekam sie eine zweite Knopfreihe, die denselben Stil
/// benutzte wie die erste. Der Stil stand in den <c>Grid.Resources</c> der
/// ersten Reihe — und ein Ressourcenwörterbuch gilt nur für den Teilbaum
/// darunter. Die zweite Reihe fand ihn nicht.</para>
///
/// <para>Das Ergebnis war der Ausfall, den CLAUDE.md beschreibt: kein
/// Build-Fehler, sondern eine <c>XamlParseException</c> beim Öffnen der Seite.
/// Die Gesprächsansicht liess sich damit gar nicht mehr anzeigen — bei einem
/// Telefon der schlimmste denkbare Ort dafür. Aufgefallen ist es erst beim
/// Anrufen am Gerät.</para>
///
/// <para><b>Warum <c>XamlResourceTests</c> das nicht gefunden hat:</b> der Test
/// prüft, ob ein Schlüssel <i>irgendwo</i> definiert ist. Genau das war der
/// Fall. Die Frage ist aber, ob er an der Verwendungsstelle im Baum
/// <i>erreichbar</i> ist, und das ist eine andere.</para>
///
/// <para>Geprüft werden nur Schlüssel, die in einer Seite selbst definiert
/// sind. Was in <c>App.xaml</c> oder <c>Themes/</c> steht, gilt überall und ist
/// hier nicht von Belang.</para>
/// </summary>
public sealed class XamlResourceScopeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Ein_lokal_definierter_Schluessel_ist_dort_sichtbar_wo_er_benutzt_wird()
    {
        var violations = new List<string>();

        foreach (var file in XamlFiles())
        {
            // App.xaml und die Themenwörterbücher gelten anwendungsweit.
            var name = Path.GetFileName(file);

            if (name.Equals("App.xaml", StringComparison.OrdinalIgnoreCase)
                || file.Contains(@"\Themes\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XDocument document;

            try
            {
                document = XDocument.Load(file, LoadOptions.SetLineInfo);
            }
            catch (System.Xml.XmlException)
            {
                // Eine Datei, die sich nicht lesen lässt, bemängelt der
                // Compiler deutlicher als dieser Test.
                continue;
            }

            CheckDocument(document, RepositoryFiles.Relative(file), violations);
        }

        Assert.True(
            violations.Count == 0,
            "Ein XAML-Ressourcenwoerterbuch gilt nur fuer den Teilbaum unter dem Element, das es "
                + "traegt. Ein Schluessel, der ausserhalb benutzt wird, ist kein Build-Fehler, "
                + "sondern eine XamlParseException beim Oeffnen der Seite. Den Schluessel weiter "
                + "oben definieren — beim gemeinsamen Vorfahren, in App.xaml oder in Tokens.xaml."
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }

    private static void CheckDocument(XDocument document, string file, List<string> violations)
    {
        // Wo ist welcher Schlüssel definiert, und welches Element trägt sein
        // Wörterbuch? Der Gültigkeitsbereich ist genau dieses Element samt
        // allem darunter.
        var scopes = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var dictionary in document.Descendants())
        {
            // Ein Wörterbuch steht als <Grid.Resources>, <Page.Resources>,
            // <StackPanel.Resources> und so fort.
            if (!dictionary.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal))
            {
                continue;
            }

            // Der Träger ist das Elternelement — nicht das Resources-Element
            // selbst, denn Geschwister des Wörterbuchs sehen die Schlüssel.
            var owner = dictionary.Parent;

            if (owner is null)
            {
                continue;
            }

            foreach (var entry in dictionary.Descendants())
            {
                if (entry.Attribute(Xaml + "Key")?.Value is { Length: > 0 } key)
                {
                    // Bei mehrfacher Definition gilt der äusserste Bereich —
                    // das ist die grosszügigere Annahme und vermeidet
                    // Fehlalarme.
                    if (!scopes.TryGetValue(key, out var known) || IsAncestor(owner, known))
                    {
                        scopes[key] = owner;
                    }
                }
            }
        }

        if (scopes.Count == 0)
        {
            return;
        }

        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (ReferencedKey(attribute.Value) is not { } key
                    || !scopes.TryGetValue(key, out var scope))
                {
                    continue;
                }

                if (!IsInScope(element, scope))
                {
                    var line = (attribute as System.Xml.IXmlLineInfo).LineNumber;

                    violations.Add(
                        $"{file}:{line} benutzt '{key}' ausserhalb des Bereichs, in dem der "
                            + $"Schluessel definiert ist (<{scope.Name.LocalName}>)");
                }
            }
        }
    }

    /// <summary>Der in einer <c>{StaticResource …}</c>-Angabe genannte Schlüssel.</summary>
    private static string? ReferencedKey(string value)
    {
        var trimmed = value.Trim();

        if (!trimmed.StartsWith("{StaticResource ", StringComparison.Ordinal)
            || !trimmed.EndsWith('}'))
        {
            return null;
        }

        return trimmed["{StaticResource ".Length..^1].Trim();
    }

    /// <summary>Ob <paramref name="element"/> im Teilbaum von <paramref name="scope"/> liegt.</summary>
    private static bool IsInScope(XElement element, XElement scope)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, scope))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAncestor(XElement candidate, XElement of)
    {
        for (var current = of.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(RepositoryFiles.SourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(static f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(static f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase));
}
