namespace Nipp.Core.ViewModels;

/// <summary>
/// Welche Gruppe den Umschalter «Reihenfolge ändern» trägt (ADR-064).
///
/// <para><b>Warum er überhaupt eine Gruppe braucht.</b> Bis zum 13.09.2026
/// stand er in einer eigenen Leiste über der Liste — rechtsbündig, mit einer
/// leeren Füllspalte daneben. Das war die Notlösung, nachdem der Aufklapper
/// «Nebenstellen (n)» weggefallen war (ADR-063), und sah entsprechend aus: ein
/// einzelner Knopf über einer Fläche, die sonst nichts enthielt.</para>
///
/// <para><b>Warum nicht einfach immer die erste.</b> Gruppen lassen sich
/// zuklappen. Ein Kopf bleibt dabei sichtbar, aber wer «Team» zuklappt und
/// darunter mit «Dienste» arbeitet, sucht den Umschalter dort — nicht über
/// einer Gruppe, die gerade nichts zeigt.</para>
///
/// <para><b>Und warum trotzdem die erste, wenn alle zu sind.</b> Dann gibt es
/// keine bessere, und <c>null</c> hiesse: <b>der Umschalter ist unerreichbar.</b>
/// Eine Funktion, die sich selbst wegsperrt, ist schlimmer als eine, die an
/// einem mittelguten Platz steht.</para>
///
/// <para>Reine Funktion und kein Code im Fenster: <c>Nipp.App</c> hat kein
/// Testprojekt, und das hier ist eine Regel mit drei Fällen.</para>
/// </summary>
public static class ReorderHost
{
    /// <summary>
    /// Die Gruppe, die den Umschalter zeigt — die erste offene, sonst die
    /// erste, sonst <c>null</c> (dann gibt es gar keine Gruppe).
    /// </summary>
    public static ContactGroupRow? Pick(IReadOnlyList<ContactGroupRow> gruppen)
    {
        ArgumentNullException.ThrowIfNull(gruppen);

        foreach (var gruppe in gruppen)
        {
            if (gruppe.IsExpanded)
            {
                return gruppe;
            }
        }

        return gruppen.Count > 0 ? gruppen[0] : null;
    }
}
