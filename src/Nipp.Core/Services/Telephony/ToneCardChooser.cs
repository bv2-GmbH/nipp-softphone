namespace Nipp.Core.Services.Telephony;

/// <summary>Eine Soundkarte der alten SDK-API, wie sie <c>SoundDevicesList</c> nennt.</summary>
/// <param name="Name">Der Kartenname, oft mit Treiberpräfix („WASAPI: Kopfhörer (Jabra Link 400)").</param>
/// <param name="CanPlay">Ob das SDK darüber wiedergeben kann.</param>
public sealed record ToneCard(string Name, bool CanPlay);

/// <summary>
/// Welche Soundkarte die <b>Töne</b> des SDK bekommen sollen (Freizeichen,
/// Tastentöne) — als reine Funktion, damit die Regel ohne Gerät prüfbar ist.
///
/// <para><b>Warum es mehrere Kandidaten sind und nicht einer.</b> Der Setter
/// <c>Core.PlaybackDevice</c> <b>wirft</b>, wenn er den Namen nicht kennt
/// (Wrapper, <c>linphone_core_set_playback_device</c> mit Rückgabewert ≠ 0).
/// Vorher wurde ihm der Anzeigename der neuen Geräte-API blind zugewiesen. Für
/// „Default Playback" geht das gut; ein namentlich gewählter Endpunkt heisst
/// in der alten API aber „WASAPI: Kopfhörer (Jabra Link 400)" und in der neuen
/// „Kopfhörer (Jabra Link 400)". Genau der Fall — Headset in den Einstellungen
/// gewählt — endete deshalb in <c>ToneCardFailed</c> und Stille.</para>
///
/// <para>Die Liste ist absteigend nach Genauigkeit: erst der exakte Name, dann
/// der mit Treiberpräfix, dann irgendeine Karte, die überhaupt wiedergeben
/// kann. Der Aufrufer nimmt den ersten, der sich setzen lässt.</para>
/// </summary>
public static class ToneCardChooser
{
    /// <summary>
    /// Die Kandidaten in der Reihenfolge, in der sie versucht werden sollen.
    /// Eine leere Liste heisst: es gibt nichts zu tun, die aktuelle Karte
    /// passt schon.
    /// </summary>
    /// <param name="desiredDeviceName">
    /// Der Anzeigename aus <c>Core.DefaultOutputAudioDevice</c>, oder
    /// <c>null</c>, wenn keine Wahl getroffen wurde (§9.4: dann gilt der
    /// Windows-Standard).
    /// </param>
    /// <param name="currentCard">Was in <c>Core.PlaybackDevice</c> steht.</param>
    /// <param name="cards">Was <c>Core.SoundDevicesList</c> anbietet.</param>
    public static IReadOnlyList<string> Choose(
        string? desiredDeviceName,
        string? currentCard,
        IReadOnlyList<ToneCard> cards)
    {
        var playable = cards.Where(static c => c.CanPlay && !string.IsNullOrWhiteSpace(c.Name)).ToList();
        var kandidaten = new List<string>();

        if (!string.IsNullOrWhiteSpace(desiredDeviceName))
        {
            // Exakt zuerst. Die alte und die neue API bauen ihre Namen aus
            // demselben WASAPI-Endpunkt, oft sind sie also gleich.
            Add(kandidaten, playable.FirstOrDefault(
                c => string.Equals(c.Name, desiredDeviceName, StringComparison.Ordinal)));

            // Dann mit Treiberpräfix: „WASAPI: <Name>". Verglichen wird das
            // Ende, nicht der Anfang — der Präfix steht vorn.
            Add(kandidaten, playable.FirstOrDefault(
                c => c.Name.EndsWith(desiredDeviceName, StringComparison.OrdinalIgnoreCase)));

            // Und zuletzt lose: manche Treiber hängen noch etwas an.
            Add(kandidaten, playable.FirstOrDefault(
                c => c.Name.Contains(desiredDeviceName, StringComparison.OrdinalIgnoreCase)));
        }

        // Ohne Wahl bleibt es beim Standard des SDK — das ist §9.4. Aber ein
        // leeres oder unbrauchbares play_sndcard ist kein Standard, sondern
        // die Leere: dann klingt kein einziger Ton.
        var currentIsUsable = !string.IsNullOrWhiteSpace(currentCard)
            && playable.Any(c => string.Equals(c.Name, currentCard, StringComparison.Ordinal));

        if (kandidaten.Count == 0 && currentIsUsable)
        {
            return [];
        }

        foreach (var card in playable)
        {
            Add(kandidaten, card);
        }

        // Steht die gewünschte Karte schon, ist nichts zu tun. Das erspart dem
        // SDK einen Kartenwechsel bei jedem Gerätewechsel-Ereignis.
        if (kandidaten.Count > 0 && string.Equals(kandidaten[0], currentCard, StringComparison.Ordinal))
        {
            return [];
        }

        return kandidaten;
    }

    private static void Add(List<string> ziel, ToneCard? card)
    {
        if (card is not null && !ziel.Contains(card.Name, StringComparer.Ordinal))
        {
            ziel.Add(card.Name);
        }
    }
}
