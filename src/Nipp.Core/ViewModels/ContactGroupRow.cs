using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Eine Gruppe der Team-Nebenstellen, wie die Kontaktliste sie zeigt
/// (ADR-041).
///
/// <para><b>Es bleibt eine einzige ListView.</b> N Gruppen als N Expander mit N
/// ListViews wären genau der Fehler, den CLAUDE.md festhält: zwei Listen in
/// einem ScrollViewer verlieren ihre Virtualisierung und erzeugen jede ihrer
/// Zeilen — bei vier Gruppen und über hundert Outlook-Kontakten auf dem Thread,
/// der alle 20 ms <c>Core.Iterate()</c> bedient. Gruppiert wird deshalb über
/// eine <c>CollectionViewSource</c>, so wie es die Palette im Karten-Designer
/// vormacht. Damit bleibt auch die Höhenlogik der Seite wörtlich
/// unverändert.</para>
///
/// <para><b>Die Zeilen sind dieselben Instanzen wie in
/// <c>TeamContacts</c>.</b> Eine Kopie hätte eine Lampe, die einfriert:
/// <c>OnPresenceChanged</c> läuft über die Zeilen der flachen Sammlung.</para>
/// </summary>
public sealed partial class ContactGroupRow : ObservableObject
{
    /// <param name="name">Der Gruppenname.</param>
    /// <param name="rows">Die Zeilen der Gruppe — dieselben Instanzen wie in <c>TeamContacts</c>.</param>
    /// <param name="isExpanded">Ob sie aufgeklappt beginnt.</param>
    public ContactGroupRow(
        string name,
        IReadOnlyList<ContactRow> rows,
        bool isExpanded)
    {
        Name = name;
        All = rows;
        _isExpanded = isExpanded;

        if (isExpanded)
        {
            foreach (var row in rows)
            {
                Rows.Add(row);
            }
        }
    }

    /// <summary>Der Gruppenname, so wie er in den Einstellungen steht.</summary>
    public string Name { get; }

    /// <summary>
    /// Führt Bestand und Anzeige nach, ohne die Gruppe zu ersetzen (ADR-042).
    ///
    /// <para><b>Warum nicht die ganze Sammlung neu aufbauen.</b>
    /// <c>Clear()</c> auf der Quelle einer <c>CollectionViewSource</c> setzt
    /// die Liste zurück — Bildlaufposition und Auswahl gehen verloren. Beim
    /// Umsortieren geschähe das unmittelbar nach jedem Loslassen, also genau
    /// dann, wenn jemand mit der Maus mitten in der Liste steht.</para>
    ///
    /// <para>Nebenbei erledigt das den falschen Zähler im Kopf: <c>All</c> wird
    /// nachgeführt statt zu veralten.</para>
    /// </summary>
    public void SetRows(IReadOnlyList<ContactRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        All = rows;

        if (IsExpanded)
        {
            Fuellen();
        }
        else
        {
            Rows.Clear();
        }

        OnPropertyChanged(nameof(All));
        OnPropertyChanged(nameof(Header));
    }

    /// <summary>
    /// Bringt <see cref="Rows"/> auf den Stand von <see cref="All"/> —
    /// <b>ohne die Sammlung zu leeren, wenn sie ohnehin passt</b>.
    /// </summary>
    private void Fuellen()
    {
        if (Rows.Count == All.Count)
        {
            var gleich = true;

            for (var i = 0; i < Rows.Count; i++)
            {
                if (!ReferenceEquals(Rows[i], All[i]))
                {
                    gleich = false;
                    break;
                }
            }

            if (gleich)
            {
                return;
            }
        }

        Rows.Clear();

        foreach (var row in All)
        {
            Rows.Add(row);
        }
    }

    /// <summary>
    /// Nimmt eine Zeile heraus, während gezogen wird (ADR-066).
    ///
    /// <para><b>Aus <see cref="All"/> und aus <see cref="Rows"/>.</b> Wer nur
    /// die Anzeige anfasst, hat eine Gruppe, deren gespeicherter Bestand etwas
    /// anderes sagt als ihr Bild — und <see cref="TeamLayout.From"/> liest bei
    /// einer zugeklappten Gruppe genau diesen Bestand.</para>
    /// </summary>
    /// <returns>Ob die Zeile hier stand.</returns>
    public bool PreviewTake(ContactRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var alle = All.ToList();

        if (!alle.Remove(row))
        {
            return false;
        }

        All = alle;
        Rows.Remove(row);

        OnPropertyChanged(nameof(All));
        OnPropertyChanged(nameof(Header));

        return true;
    }

    /// <summary>
    /// Setzt eine Zeile an eine Stelle dieser Gruppe, während gezogen wird
    /// (ADR-066) — steht sie schon hier, wird sie verschoben.
    ///
    /// <para><b>Verschoben und nicht neu eingehängt:</b>
    /// <c>ObservableCollection.Move</c> meldet eine Bewegung, und die Liste
    /// behält ihren Container. Ein <c>Remove</c> mit folgendem <c>Insert</c>
    /// verwirft ihn und baut ihn neu auf — bei jedem Überfahren einer Zeile,
    /// also zwanzig Mal in einem Zug.</para>
    ///
    /// <para><paramref name="index"/> zählt <b>ohne</b> die gezogene Zeile,
    /// genau wie bei <see cref="TeamLayout.Move"/>. Ein Wert daneben landet am
    /// Rand; wer unter der letzten Zeile loslässt, meint das Ende.</para>
    /// </summary>
    /// <returns>Ob sich dadurch etwas geändert hat.</returns>
    public bool PreviewPlace(ContactRow row, int index)
    {
        ArgumentNullException.ThrowIfNull(row);

        var alle = All.ToList();
        var jetzt = alle.IndexOf(row);

        if (jetzt < 0)
        {
            var stelle = Math.Clamp(index, 0, alle.Count);

            alle.Insert(stelle, row);
            All = alle;

            if (IsExpanded)
            {
                Rows.Insert(Math.Clamp(stelle, 0, Rows.Count), row);
            }
        }
        else
        {
            var stelle = Math.Clamp(index, 0, alle.Count - 1);

            if (stelle == jetzt)
            {
                return false;
            }

            alle.RemoveAt(jetzt);
            alle.Insert(stelle, row);
            All = alle;

            if (IsExpanded)
            {
                var von = Rows.IndexOf(row);

                if (von >= 0)
                {
                    Rows.Move(von, Math.Clamp(stelle, 0, Rows.Count - 1));
                }
            }
        }

        OnPropertyChanged(nameof(All));
        OnPropertyChanged(nameof(Header));

        return true;
    }

    /// <summary>
    /// Alle Zeilen der Gruppe — auch wenn sie zugeklappt ist.
    ///
    /// <b>Daran hängt das Umsortieren.</b> Eine zugeklappte Gruppe zeigt keine
    /// Zeilen, ihre Einträge stehen aber weiter in der gespeicherten
    /// Reihenfolge; wer die Ordnung aus der Anzeige allein zusammensetzt,
    /// verliert sie.
    /// </summary>
    public IReadOnlyList<ContactRow> All { get; private set; }

    /// <summary>
    /// Was die Liste zeichnet. Zugeklappt leer — dann erzeugt WinUI für diese
    /// Gruppe auch keine Container.
    /// </summary>
    public ObservableCollection<ContactRow> Rows { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Ob <b>dieser</b> Kopf den Umschalter «Reihenfolge ändern» trägt
    /// (ADR-064). Genau eine Gruppe tut das.
    ///
    /// <para><b>Wird ausschliesslich von
    /// <c>ShellViewModel.RefreshReorderHost</c> gesetzt.</b> Die Gruppe kennt
    /// ihre Nachbarn nicht und kann die Frage nicht selbst beantworten — sie
    /// trägt hier nur das Ergebnis, damit die Kopfzeile es binden kann. Ein
    /// <c>x:Name</c> im Gruppenkopf wäre aus dem Fenster nicht erreichbar:
    /// Kopfzeilen liegen in einem <c>DataTemplate</c> und werden beim Scrollen
    /// erzeugt und verworfen.</para>
    /// </summary>
    [ObservableProperty]
    private bool _showReorderToggle;

    /// <summary>
    /// Ob der Sortiermodus läuft — für den gedrückten Zustand des Umschalters.
    ///
    /// <para>Dieselbe Herkunft wie <see cref="ShowReorderToggle"/>: die
    /// Wahrheit steht in <c>ShellViewModel.IsTeamReorderMode</c>, hier steht
    /// ihr Abbild. <b>Die Bindung ist einseitig</b>; wer den Knopf drückt,
    /// meldet das dem ViewModel, nicht dieser Eigenschaft.</para>
    /// </summary>
    [ObservableProperty]
    private bool _isReordering;

    /// <summary>Was im Kopf steht — „Support (3)".</summary>
    public string Header => $"{Name} ({All.Count})";

    /// <summary>Das Zeichen am Kopf: aufgeklappt zeigt es nach unten.</summary>
    public string Glyph => IsExpanded ? "" : "";

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            Fuellen();
        }
        else
        {
            Rows.Clear();
        }

        OnPropertyChanged(nameof(Glyph));
    }
}
