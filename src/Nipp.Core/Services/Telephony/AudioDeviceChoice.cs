using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Was ein Gerätewechsel für den Benutzer bedeutet.
/// </summary>
/// <param name="LostNames">
/// Die Namen der Geräte, die er ausgewählt hatte und die jetzt fehlen — in
/// der Reihenfolge Mikrofon, Lautsprecher, Klingelgerät.
/// </param>
/// <param name="Notice">
/// Der Satz, der ihm angezeigt wird, oder <c>null</c>, wenn es nichts zu
/// sagen gibt. <b>Kein Gerät verloren heisst: kein Hinweis</b> — ein
/// eingestecktes Headset ist keine Meldung wert, es funktioniert einfach.
/// </param>
public sealed record DeviceChangeOutcome(IReadOnlyList<string> LostNames, string? Notice);

/// <summary>
/// Was aus einem Wechsel der Audiogeräte folgt (§9.4, W2.1 Etappe B3) —
/// ohne SDK und ohne Nebenwirkung.
///
/// <para><b>Was hier bewusst nicht entschieden wird:</b> ob die Gerätewahl
/// neu angewendet wird. Das geschieht <b>immer</b>, und zwar im Dienst: ein
/// wieder eingestecktes Gerät soll zurückkommen, ein verschwundenes wird
/// beim Anwenden übergangen — dann gilt der Windows-Standard, und genau das
/// will §9.4. Eine Bedingung davor wäre eine zweite Wahrheit darüber, wann
/// ein Gerät gilt.</para>
/// </summary>
public static class AudioDeviceChoice
{
    /// <summary>
    /// Welche gewählten Geräte verschwunden sind und was dem Benutzer dazu
    /// gesagt wird.
    /// </summary>
    /// <param name="bisher">Die Geräte, die vor dem Wechsel da waren.</param>
    /// <param name="jetzt">Die Geräte, die jetzt da sind.</param>
    /// <param name="gewaehlt">
    /// Die Kennungen, die der Benutzer eingestellt hat (Mikrofon,
    /// Lautsprecher, Klingelgerät) — leere und doppelte werden übergangen.
    /// </param>
    /// <param name="imGespraech">
    /// Ob gerade telefoniert wird. <b>Derselbe Verlust heisst dann etwas
    /// anderes:</b> ausserhalb eines Gesprächs ist es eine Einstellung, die
    /// sich geändert hat; mitten im Gespräch ist es die Frage, warum man
    /// nichts mehr hört.
    /// </param>
    public static DeviceChangeOutcome Evaluate(
        IReadOnlyList<AudioDeviceInfo> bisher,
        IReadOnlyList<AudioDeviceInfo> jetzt,
        IReadOnlyList<string?> gewaehlt,
        bool imGespraech)
    {
        ArgumentNullException.ThrowIfNull(bisher);
        ArgumentNullException.ThrowIfNull(jetzt);
        ArgumentNullException.ThrowIfNull(gewaehlt);

        var verloren = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var wunsch in gewaehlt)
        {
            if (wunsch is not { Length: > 0 })
            {
                continue;
            }

            // <b>Dieselbe Kennung zählt einmal.</b> Wer Mikrofon und
            // Klingelgerät auf dasselbe Headset stellt, hat ein Gerät
            // verloren, nicht zwei — und liest sonst seinen Namen doppelt.
            if (!gesehen.Add(wunsch))
            {
                continue;
            }

            // <b>Verloren ist nur, was vorher da war.</b> Ein eingestelltes
            // Gerät, das nie angeschlossen war, verschwindet nicht — es war
            // nie da, und eine Meldung darüber käme bei jedem Wechsel neu.
            var warDa = bisher.FirstOrDefault(d => string.Equals(d.Id, wunsch, StringComparison.Ordinal));
            var istDa = jetzt.Any(d => string.Equals(d.Id, wunsch, StringComparison.Ordinal));

            if (warDa is not null && !istDa)
            {
                verloren.Add(warDa.Name);
            }
        }

        if (verloren.Count == 0)
        {
            return new DeviceChangeOutcome([], null);
        }

        var betreff = verloren.Count == 1
            ? $"«{verloren[0]}» ist"
            : $"{verloren.Count} Audiogeräte sind";

        var satz = imGespraech
            ? $"{betreff} während des Gesprächs verschwunden. nipp hat auf das Standardgerät "
                + "umgestellt — das Gespräch läuft weiter."
            : $"{betreff} nicht mehr da. nipp verwendet wieder das Standardgerät von Windows.";

        return new DeviceChangeOutcome(verloren, satz);
    }

    /// <summary>
    /// Der Satz für den Fall, dass das Umstellen selbst scheitert.
    ///
    /// <para>Er steht hier und nicht im Dienst, damit <b>jeder</b> Text zu
    /// diesem Vorgang an einer Stelle liegt — und weil er dieselbe Frage
    /// beantwortet wie die anderen: was merkt der Benutzer, und was kann er
    /// tun (§15).</para>
    /// </summary>
    public static string SwitchFailedNotice =>
        "Ein Audiogerät hat gewechselt, und nipp konnte nicht darauf umstellen. "
        + "Wenn nichts zu hören ist: das Gerät in den Einstellungen neu wählen.";
}
