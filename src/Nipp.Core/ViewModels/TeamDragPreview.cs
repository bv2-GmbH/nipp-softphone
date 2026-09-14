namespace Nipp.Core.ViewModels;

/// <summary>
/// Ein laufender Ziehvorgang mit Vorschau (ADR-066): die gezogene Zeile steht
/// während des Zugs wirklich dort, wo sie landen würde, und die anderen sind
/// ausgewichen.
///
/// <para><b>Warum die Vorschau der Auftrag ist und nicht ein Bild davon.</b>
/// Beim Loslassen wird nichts mehr gerechnet — geschrieben wird, was dasteht
/// (<c>ShellViewModel.ApplyTeamLayout()</c> liest die Anzeige). Eine zweite
/// Rechnung fürs Zeichnen und eine fürs Speichern wären die Doppelwahrheit,
/// die dieses Projekt wiederholt bezahlt hat.</para>
///
/// <para><b>Warum das im Kern liegt.</b> Ein Zug trägt Zustand: woher die
/// Zeile kam, wo sie gerade steht, und ob ein Abbruch sie exakt
/// zurückstellen kann. <c>Nipp.App</c> hat kein Testprojekt; stünde das im
/// Code-behind, wäre der Abbruch ungeprüft — und der Abbruch ist der Fall,
/// bei dem ein Fehler die gespeicherte Reihenfolge kostet.</para>
///
/// <para><b>Die Stelle kommt von der Liste, nicht von der Zeile darunter</b>
/// (Befund F6 vom 14.09.2026). Zwischen den Zeilen liegt totes Gebiet —
/// Abstände, Padding, der Streifen zwischen zwei Gruppen —, und ein Zug, der
/// dort endete, ging stumm verloren.</para>
/// </summary>
public sealed class TeamDragPreview
{
    private readonly IReadOnlyList<ContactGroupRow> _gruppen;
    private readonly ContactGroupRow _herkunft;
    private readonly int _herkunftsstelle;

    private TeamDragPreview(
        IReadOnlyList<ContactGroupRow> gruppen,
        ContactRow zeile,
        ContactGroupRow herkunft,
        int herkunftsstelle)
    {
        _gruppen = gruppen;
        _herkunft = herkunft;
        _herkunftsstelle = herkunftsstelle;

        Row = zeile;
    }

    /// <summary>Die Zeile, die gezogen wird.</summary>
    public ContactRow Row { get; }

    /// <summary>Der Name der Gruppe, in der die Zeile gerade steht.</summary>
    public string Group { get; private set; } = string.Empty;

    /// <summary>
    /// Ob die Vorschau gegenüber dem Anfang etwas geändert hat.
    ///
    /// <para>Daran hängt, ob nach dem Loslassen überhaupt geschrieben wird —
    /// ein Zug, der endet, wo er begann, ist keine Änderung.</para>
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Beginnt einen Zug — oder gibt <c>null</c>, wenn die Zeile in keiner
    /// Gruppe steht.
    ///
    /// <para><b>Gesucht wird in <see cref="ContactGroupRow.All"/></b>, nicht
    /// nur in der Anzeige: eine zugeklappte Gruppe zeigt nichts und hat
    /// trotzdem Bestand.</para>
    /// </summary>
    public static TeamDragPreview? Start(IReadOnlyList<ContactGroupRow> gruppen, ContactRow zeile)
    {
        ArgumentNullException.ThrowIfNull(gruppen);
        ArgumentNullException.ThrowIfNull(zeile);

        foreach (var gruppe in gruppen)
        {
            var stelle = IndexIn(gruppe, zeile);

            if (stelle >= 0)
            {
                return new TeamDragPreview(gruppen, zeile, gruppe, stelle)
                {
                    Group = gruppe.Name,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Setzt die Vorschau auf eine Stelle — <b>und tut nichts, wenn sie schon
    /// dort steht</b>.
    /// </summary>
    /// <param name="group">Der Name der Zielgruppe.</param>
    /// <param name="index">
    /// Die Stelle innerhalb der Zielgruppe, <b>ohne</b> die gezogene Zeile
    /// gezählt — dieselbe Zählung wie in <see cref="TeamLayout.Move"/>.
    /// </param>
    /// <returns>Ob sich dadurch etwas bewegt hat.</returns>
    public bool MoveTo(string group, int index)
    {
        var ziel = GruppeMitNamen(group);

        if (ziel is null)
        {
            return false;
        }

        var bewegt = Setzen(ziel, index);

        if (!bewegt)
        {
            return false;
        }

        Group = ziel.Name;

        // <b>Gegen den Anfang und nicht gezählt:</b> wer hin und wieder
        // zurückzieht, hat nichts geändert, und das soll auch nichts
        // schreiben.
        Changed =
            !ReferenceEquals(ziel, _herkunft)
            || IndexIn(ziel, Row) != _herkunftsstelle;

        return true;
    }

    /// <summary>
    /// Nimmt den Zug zurück — die Zeile steht danach <b>exakt</b> dort, wo sie
    /// begonnen hat.
    ///
    /// <para>Das ist der Weg für Escape, für das Loslassen ausserhalb und für
    /// jeden Zug, den das Fenster nicht mit einer Verschiebung beendet.</para>
    /// </summary>
    public void Cancel()
    {
        Setzen(_herkunft, _herkunftsstelle);

        Group = _herkunft.Name;
        Changed = false;
    }

    private bool Setzen(ContactGroupRow ziel, int index)
    {
        var jetzige = GruppeVon(Row);

        if (jetzige is not null && !ReferenceEquals(jetzige, ziel))
        {
            jetzige.PreviewTake(Row);
        }

        return ziel.PreviewPlace(Row, index);
    }

    private ContactGroupRow? GruppeVon(ContactRow zeile)
    {
        foreach (var gruppe in _gruppen)
        {
            if (IndexIn(gruppe, zeile) >= 0)
            {
                return gruppe;
            }
        }

        return null;
    }

    private ContactGroupRow? GruppeMitNamen(string name)
    {
        foreach (var gruppe in _gruppen)
        {
            if (string.Equals(gruppe.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return gruppe;
            }
        }

        return null;
    }

    private static int IndexIn(ContactGroupRow gruppe, ContactRow zeile)
    {
        for (var i = 0; i < gruppe.All.Count; i++)
        {
            if (ReferenceEquals(gruppe.All[i], zeile))
            {
                return i;
            }
        }

        return -1;
    }
}
