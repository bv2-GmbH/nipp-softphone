namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Eine Herkunft von Kontakten (§8.4, AP6.3).
///
/// Die Zusage an alle Implementierungen: <see cref="LoadAsync"/> laeuft
/// <b>nie</b> synchron auf dem UI-Thread und wirft nicht. Eine Quelle, die
/// nicht antwortet, liefert eine leere Liste und protokolliert — ein
/// Softphone, dessen Kontaktliste den Start blockiert, ist kaputt.
/// </summary>
public interface IContactSource
{
    /// <summary>Welche Art Kontakte diese Quelle liefert.</summary>
    ContactSourceKind Kind { get; }

    /// <summary>Ob die Quelle auf dieser Maschine ueberhaupt zur Verfuegung steht.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Laedt alle Kontakte. Darf lange dauern; der Aufrufer ruft sie im
    /// Hintergrund und mit Zwischenspeicher (<see cref="ContactStore"/>).
    /// </summary>
    Task<IReadOnlyList<Contact>> LoadAsync(CancellationToken cancellationToken = default);
}
