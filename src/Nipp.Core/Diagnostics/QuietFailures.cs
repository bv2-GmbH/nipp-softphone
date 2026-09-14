using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Diagnostics;

/// <summary>
/// Erwartete Fehlschläge, die trotzdem einmal sichtbar sein sollen (W1.7,
/// Befund A7).
///
/// <para><b>Das Problem, und es hat zwei Seiten.</b> An etwa einem Dutzend
/// Stellen liest nipp eine Eigenschaft des SDK, die vor der Verhandlung wirft:
/// der Codec, die Verschlüsselung, der Grund des Anrufendes, die Messwerte des
/// Ruftons. Jede dieser Stellen fing die Ausnahme und gab einen Ersatzwert
/// zurück — <b>ohne eine Zeile</b>. Das ist genau das Muster, das dieses
/// Projekt zweimal teuer bezahlt hat («die Taste tut nichts» war nicht von
/// «hier kommt gar nichts an» zu unterscheiden).</para>
///
/// <para><b>Und die andere Seite:</b> der Pump läuft alle 20 ms. Würde jede
/// dieser Stellen bei jedem Durchlauf protokollieren, stünden im Gespräch
/// Tausende gleicher Zeilen, und das Protokoll wäre für alles andere
/// unbrauchbar. Eine Warnung, die im Minutentakt kommt, liest niemand
/// mehr.</para>
///
/// <para><b>Die Auflösung: einmal je Stelle und Sitzung.</b> Der erste
/// Fehlschlag steht mit Typ und maskierter Meldung im Protokoll, jeder weitere
/// schweigt. Der erwartete Fall kostet damit eine Zeile beim ersten Anruf; ein
/// <em>neuer</em> Grund — ein SDK-Wechsel, ein Gerät, das sich anders verhält —
/// ist sichtbar, statt sich hinter einem Ersatzwert zu verstecken.</para>
///
/// <para>Der Zustand ist bewusst statisch und prozessweit: «schon gemeldet»
/// ist eine Aussage über den Lauf, nicht über eine Instanz.</para>
/// </summary>
public static class QuietFailures
{
    private static readonly ConcurrentDictionary<string, byte> Gemeldet = new(StringComparer.Ordinal);

    /// <summary>
    /// Meldet einen erwarteten Fehlschlag, aber nur beim ersten Mal.
    /// </summary>
    /// <param name="logger">Wohin.</param>
    /// <param name="site">
    /// Die Stelle, etwa <c>ReadCodec</c>. Sie ist der Schlüssel: dieselbe
    /// Stelle meldet genau einmal.
    /// </param>
    /// <param name="exception">Was geworfen wurde.</param>
    /// <returns><c>true</c>, wenn diesmal protokolliert wurde.</returns>
    public static bool Report(ILogger logger, string site, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!Gemeldet.TryAdd(site, 0))
        {
            return false;
        }

        DiagnosticsLog.QuietFailure(
            logger,
            site,
            exception.GetType().Name,
            Describe(exception));

        return true;
    }

    /// <summary>
    /// Vergisst, was schon gemeldet wurde.
    ///
    /// <para><b>Für Tests, und öffentlich aus demselben Grund wie
    /// <c>CallbackGuard</c>:</b> ohne <c>InternalsVisibleTo</c> liesse sich
    /// die Zusage «einmal je Stelle» nicht prüfen. Im Betrieb ruft das
    /// niemand — eine Sitzung ist eine Sitzung.</para>
    /// </summary>
    public static void Reset() => Gemeldet.Clear();

    /// <summary>
    /// Der Text einer Ausnahme, wie er ins Protokoll darf — maskiert (§21.2).
    /// </summary>
    private static string Describe(Exception exception)
    {
        var message = exception.Message;

        return string.IsNullOrWhiteSpace(message)
            ? "(ohne Meldung)"
            : LogMasking.Line(message);
    }
}

internal static partial class DiagnosticsLog
{
    [LoggerMessage(EventId = 2902, Level = LogLevel.Debug,
        Message = "{Site} hat einen erwarteten Fehlschlag ({ExceptionType}): {Reason}. "
            + "Es gilt der Ersatzwert. Diese Zeile kommt einmal je Sitzung.")]
    public static partial void QuietFailure(
        ILogger logger, string site, string exceptionType, string reason);
}
