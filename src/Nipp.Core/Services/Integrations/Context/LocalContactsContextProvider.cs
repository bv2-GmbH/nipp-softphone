using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Was nipp selbst über den Anrufer weiss (§21.2).
///
/// <b>Diese Quelle ist der Grund, warum die Karte nie leer ist.</b> Sie
/// antwortet aus dem Zwischenspeicher der Kontakte — synchron, in derselben
/// Iteration, in der der Anruf gemeldet wird. Was danach von fremden Systemen
/// kommt, ergänzt; was nicht kommt, fehlt eben.
///
/// Aufgelöst wird über <see cref="ClipResolver"/> — dieselbe Stelle, die auch
/// den Toast, die Gesprächsleiste und die Anrufliste bedient. Ein zweiter
/// Auflöser hier hätte früher oder später einen anderen Namen gezeigt als der
/// Toast, und das ist genau die Art Fehler, für die es den Auflöser gibt.
///
/// <b>Sie gilt für jede Nummer</b>, auch für interne: eine Nebenstelle ist
/// der Fall, in dem sie am meisten weiss.
/// </summary>
public sealed class LocalContactsContextProvider(ClipResolver clip) : ICallerContextProvider
{
    /// <summary>
    /// Der Namensraum auf der Karte. Reserviert — der Validator lehnt eine
    /// externe Quelle mit dieser Kennung ab, damit sie die eigenen Angaben
    /// nicht verdeckt.
    /// </summary>
    public string SourceId => "contacts";

    public string DisplayName => "Kontakte";

    /// <summary>
    /// Vorn, ohne Zahl aus einer Konfiguration: diese Quelle antwortet ohne
    /// Netz, noch in derselben Iteration, in der der Anruf gemeldet wird.
    ///
    /// <b>Für <c>role('name')</c> hat das eine Folge, die gewollt ist:</b> den
    /// Namen bestimmen die eigenen Kontakte, wenn sie einen kennen. Wer einen
    /// Kollegen im Adressbuch als „Andi" führt, will nicht den Eintrag aus
    /// einem CRM sehen. Für Firma und Gesprächsinhalt gibt es hier ohnehin
    /// nichts, dort greift die nächste Quelle.
    /// </summary>
    public int Priority => 0;

    /// <summary>
    /// Ohne Bedeutung: die Antwort kommt aus dem Arbeitsspeicher, bevor eine
    /// Zeitgrenze greifen könnte.
    /// </summary>
    public TimeSpan Timeout => TimeSpan.Zero;

    public bool AppliesTo(PhoneNumberKey number, CallDirection direction)
    {
        ArgumentNullException.ThrowIfNull(number);

        return number.Digits.Length > 0;
    }

    public Task<ContextFragment> LookupAsync(PhoneNumberKey number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(number);

        // Die rohe Nummer genügt: ClipResolver vergleicht selbst über die
        // Ziffern und von hinten.
        var contact = clip.Resolve(number.E164.Length > 0 ? number.E164 : number.National);

        if (contact is null)
        {
            return Task.FromResult(
                new ContextFragment(
                    SourceId,
                    DisplayName,
                    SourceState.Empty,
                    EmptyFields,
                    Priority: Priority));
        }

        var fields = new Dictionary<string, ContextValue>(StringComparer.Ordinal)
        {
            ["displayName"] = ContextValue.FromText(contact.DisplayName),
            ["company"] = ContextValue.FromText(contact.Company),
            ["email"] = ContextValue.FromText(contact.Email),
            ["source"] = ContextValue.FromText(contact.EffectiveSourceId),

            // Ob der Anrufer eine Team-Nebenstelle ist. Auf einer Karte ist
            // das der Unterschied zwischen „Kollege" und „Kunde", und eine
            // Bedingung darauf ist der häufigste Fall überhaupt.
            ["isTeam"] = ContextValue.FromBoolean(contact.Source == ContactSourceKind.Team),
        };

        return Task.FromResult(
            new ContextFragment(
                SourceId,
                DisplayName,
                SourceState.Success,
                fields,
                Priority: Priority));
    }

    private static readonly Dictionary<string, ContextValue> EmptyFields =
        new(StringComparer.Ordinal);
}
