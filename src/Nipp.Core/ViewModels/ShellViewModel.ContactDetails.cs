using CommunityToolkit.Mvvm.ComponentModel;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Der Detailbereich einer Kontaktzeile (ADR-041, ADR-048).
///
/// <para><b>Er klappt in der Zeile auf</b>, unter dem angeklickten Kontakt —
/// in allen drei Listen gleich. Bis zum 13.09.2026 stand er als ein Bereich
/// unter der ganzen Liste (ADR-046); wer unten in einer Liste von 137 Namen
/// etwas anklickte, las die Antwort am anderen Ende des Fensters.</para>
///
/// <para><b>Deutlich einfacher als der Bereich der Anrufliste</b>
/// (<see cref="ShellViewModel.HistoryDetails"/> in der Nachbardatei): hier wird
/// nichts über das Netz geholt. Alles, was er zeigt, liegt in der
/// <see cref="ContactRow"/> — also kein <c>CancellationTokenSource</c>, keine
/// Überhol-Prüfung, kein Wartekringel.</para>
///
/// <para><b>Er geht an der Auswahl auf, nicht am Doppelklick</b> — dasselbe
/// Muster und dieselbe Begründung wie in der Anrufliste: der Doppelklick wählt
/// und muss das weiter tun.</para>
///
/// <para><b>Die Präsenz zieht ohne Zusatzcode nach.</b> Es ist dieselbe
/// <c>ContactRow</c>-Instanz wie in der Liste, und <c>OnPresenceChanged</c>
/// meldet <c>PresenceText</c> mit. §8.4 gilt auch hier: Farbe <b>und</b>
/// Text.</para>
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// Die Zeile, deren Bereich gerade offen ist — oder <c>null</c>.
    ///
    /// <para><b>Eine Regel für alle Listen</b> (ADR-046, präzisiert in
    /// ADR-048): die Zeile klappt auf, für Nebenstellen wie für
    /// Outlook-Kontakte und Suchtreffer. Bis zum 12.09.2026 klappte eine
    /// Nebenstelle in der Zeile auf und die anderen darunter — dieselbe
    /// Handlung, zwei Ergebnisse, an optisch gleichen Zeilen. <b>Der Fehler
    /// war die Ungleichheit, nicht der Ort</b>; ADR-046 hat den einen Ort
    /// unter der Liste genommen, ADR-048 den in der Zeile, und beide Male ist
    /// es einer.</para>
    ///
    ///
    /// <b>Die Zeile und nicht ihr Kontakt:</b> an der Zeile hängt die Präsenz,
    /// und sie ist dieselbe Instanz, die die Liste zeichnet. Ein zweites Objekt
    /// wäre eine Lampe, die einfriert.
    /// </summary>
    [ObservableProperty]
    private ContactRow? _expandedContact;

    /// <summary>Ob der Bereich etwas zu zeigen hat.</summary>
    public bool HasContactDetails => ExpandedContact is not null;

    /// <summary>
    /// Führt die Fahnen an den Zeilen nach — <b>im Setter und nicht bei den
    /// Aufrufern</b>.
    ///
    /// <para>Damit ist «es ist höchstens eine Zeile offen» eine Eigenschaft
    /// dieser Eigenschaft und kein Vertrag, an den sich drei Aufrufer erinnern
    /// müssen. Dasselbe Muster wie bei <c>TeamOrder.ApplyLayout</c>, das mit
    /// <c>TeamGroups.Normalize</c> endet (ADR-042).</para>
    /// </summary>
    partial void OnExpandedContactChanged(ContactRow? oldValue, ContactRow? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsDetailExpanded = false;
        }

        if (newValue is not null)
        {
            newValue.IsDetailExpanded = true;
        }

        OnPropertyChanged(nameof(HasContactDetails));
    }

    /// <summary>
    /// Öffnet den Bereich zu einer Zeile, oder schliesst ihn bei erneuter
    /// Auswahl derselben.
    ///
    /// <b>Es ist immer höchstens einer offen</b>, und die Fahne an der Zeile
    /// wird genau hier gesetzt — eine Zeile, die das selbst täte, wüsste
    /// nichts von der vorigen.
    ///
    /// <para><b>Im Sortiermodus bleibt er zu</b> (ADR-066). Bis zum 14.09.2026
    /// stand diese Regel nur im Kommentar an <see cref="IsTeamReorderMode"/> —
    /// «kein offener Detailbereich», als eine von drei —, und es gab sie
    /// nicht: <c>OnIsTeamReorderModeChanged</c> schliesst ihn beim
    /// <em>Einschalten</em>, ein Klick öffnete ihn danach wieder. Eine Zeile
    /// mit ausgeklapptem Innenleben ist als Ziehziel weder zu treffen noch zu
    /// erklären, und ungleich hohe Zeilen sind für die Vorschau eine Quelle
    /// von Sprüngen. <b>Geschlossen wird trotzdem immer</b> — sonst bliebe
    /// beim Einschalten des Modus einer offen stehen.</para>
    /// </summary>
    public void ToggleContactDetails(ContactRow? row)
    {
        var neu = row is null || ReferenceEquals(row, ExpandedContact) ? null : row;

        if (IsTeamReorderMode)
        {
            neu = null;
        }

        ExpandedContact = neu;
    }

    /// <summary>Schliesst den Bereich.</summary>
    public void CloseContactDetails() => ToggleContactDetails(null);

    /// <summary>
    /// Führt den offenen Bereich einer neu aufgebauten Liste nach.
    ///
    /// <para><b>Sonst geht das still daneben:</b> <c>RefreshContacts</c> tauscht
    /// die Zeilen aus, und der Bereich zeigte danach auf eine verwaiste Zeile,
    /// deren Lampe einfriert — sie bekommt keine Präsenzmeldung mehr, weil sie
    /// in keiner Sammlung mehr steht.</para>
    ///
    /// <para>Gefunden wird über <c>Contact.Id</c>. Ist der Kontakt weg — aus
    /// den Einstellungen entfernt, aus Outlook verschwunden —, schliesst der
    /// Bereich.</para>
    /// </summary>
    private void ReattachContactDetails()
    {
        if (ExpandedContact is not { } offen)
        {
            return;
        }

        var gesucht = offen.Contact.Id;

        var wieder = TeamContacts.Concat(OutlookContacts).Concat(SearchResults)
            .FirstOrDefault(r => string.Equals(r.Contact.Id, gesucht, StringComparison.Ordinal));

        ExpandedContact = wieder;
    }
}
