using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Eine Quelle, die zu einer Rufnummer etwas beitragen kann (§21.1).
///
/// <b>Die Zusage an alle Implementierungen</b> ist dieselbe wie bei der Suche:
/// <see cref="LookupAsync"/> wirft nicht. Eine Quelle, die nicht kann, liefert
/// ein Fragment mit ihrem Zustand und einer Meldung.
///
/// Das ist hier noch wichtiger als bei der Suche: der Anruferkontext hängt am
/// Anrufpfad, und der läuft auf dem Thread, der alle 20 ms
/// <c>Core.Iterate()</c> bedient (§6). Eine Ausnahme von hier stiege in einen
/// SDK-Ereignishandler und nähme die Anwendung mit.
/// </summary>
public interface ICallerContextProvider
{
    /// <summary>Die Kennung der Quelle — zugleich der Namensraum auf der Karte.</summary>
    string SourceId { get; }

    /// <summary>Wie die Quelle in der Oberfläche heisst.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Reihenfolge bei Konflikten — kleinere Zahl gewinnt, wie in
    /// <c>DataSourceDefinition.Priority</c>.
    ///
    /// Sie entscheidet, welche Quelle <c>role('name')</c> auf einer Karte
    /// beantwortet, wenn mehrere ein Feld dieser Bedeutung liefern. Die
    /// lokalen Anbieter stehen mit 0 vorn: sie antworten ohne Netz.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Wie lange auf sie gewartet wird.
    ///
    /// <b>Ein Versprechen an den Benutzer</b>, keine blosse
    /// Vorsichtsmassnahme: länger als das wartet die Karte nicht auf diese
    /// Quelle, und was danach kommt, kommt zu spät.
    /// </summary>
    TimeSpan Timeout { get; }

    /// <summary>
    /// Ob diese Quelle für diesen Anruf überhaupt gefragt wird.
    ///
    /// Trennt zwei Fälle, die verschieden aussehen sollen: eine Quelle, die
    /// nichts weiss (<see cref="SourceState.Empty"/>), und eine, die gar
    /// nicht gefragt wurde (<see cref="SourceState.Skipped"/>) — etwa weil
    /// eine interne Nummer nicht nach aussen geht (§21.4).
    /// </summary>
    bool AppliesTo(PhoneNumberKey number, CallDirection direction);

    /// <summary>
    /// Fragt die Quelle. Wirft nicht — ausser bei Abbruch von aussen.
    /// </summary>
    Task<ContextFragment> LookupAsync(PhoneNumberKey number, CancellationToken cancellationToken);
}
