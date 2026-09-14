using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Liest aus einem Schnappschuss den Wert zu einer <b>Bedeutung</b> — nicht zu
/// einem Feldnamen (ADR-043).
///
/// <para><b>Warum das eine eigene Stelle ist.</b> Die Regel stand in
/// <c>ToastComposer</c> und wurde dort für drei Zeilen einer Benachrichtigung
/// gebraucht. Seit die Gesprächsansicht dieselbe Frage stellt — „wie heisst
/// der Gesprächspartner?" —, ist sie an zwei Orten nötig, und zwei Kopien einer
/// Regel sind zwei Gelegenheiten, sie falsch zu haben.</para>
///
/// <para><b>Welcher Feldname welche Bedeutung trägt, entscheidet weiterhin
/// genau eine Stelle:</b> <see cref="FieldCatalog"/>. Hier steht nur, in
/// welcher Reihenfolge gesucht wird.</para>
/// </summary>
public static class ContextRoles
{
    /// <summary>
    /// Der erste nicht leere Wert zu dieser Bedeutung, über alle Quellen in
    /// ihrer Reihenfolge.
    ///
    /// <para><b>Feld vor Quelle:</b> geprüft wird Feldname für Feldname, und
    /// innerhalb eines Feldnamens Quelle für Quelle. Sonst gewönne ein
    /// unscharfer Treffer der ersten Quelle über den genauen der zweiten.</para>
    ///
    /// <para><b>Nicht dasselbe wie <c>ContextSnapshot.FieldAcrossSources</c>:</b>
    /// das prüft <c>HasData</c> nicht und würde damit auch aus einer Quelle
    /// lesen, die gar nicht geantwortet hat.</para>
    /// </summary>
    public static string? Text(ContextSnapshot? snapshot, FieldRole role)
    {
        if (snapshot is null)
        {
            return null;
        }

        foreach (var field in FieldCatalog.NamesFor(role))
        {
            foreach (var fragment in snapshot.SourcesByPriority)
            {
                if (!fragment.HasData)
                {
                    continue;
                }

                if (fragment.Fields.TryGetValue(field, out var value)
                    && !value.IsEmpty
                    && value.AsDisplayText() is { Length: > 0 } text)
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }
}
