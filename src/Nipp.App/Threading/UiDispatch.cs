using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Nipp.App.Diagnostics;

namespace Nipp.App.Threading;

/// <summary>
/// Die eine Stelle, über die ein Dienstereignis den UI-Thread erreicht
/// (R1.2, <c>docs/plans/LOGBEFUNDE-PLAN.md</c>, Befund L2).
///
/// <b>Gemessen, <c>crash.txt</c> vom 20.10.2026, 11:51:47 Uhr:</b>
/// <c>COMException (0x80070580): Unzulässiges Fenster. Es gehört zu einem
/// anderen Thread.</c>, über <c>MainWindow.OnSettingsChanged</c> und
/// <c>TrayIconHost.UsePlaybackDevice</c>. Das Kontextmenü des
/// Infobereich-Symbols läuft im Nachrichtenfenster von <c>H.NotifyIcon</c>,
/// <b>nicht</b> im UI-Thread — ein Wechsel des Wiedergabegeräts dort
/// schreibt die Einstellungen, <c>SettingsService.Changed</c> feuert auf
/// diesem fremden Thread, und der Zugriff auf das <c>AppWindow</c> danach
/// brachte nipp zum Absturz. Zwei weitere Zeilen im Protokoll derselben
/// Nacht ( „Ein Abonnent von OnAccountRegistrationStateChanged hat eine
/// Ausnahme ausgeloest" ) zeigen, dass Dienstereignisse an mehreren Stellen
/// ohne Thread-Wechsel ankommen — nicht nur an dieser einen.
///
/// <para><b>Die Regel ist deshalb der Thread, nicht die Zeile.</b> Jeder
/// Empfänger eines Dienstereignisses in <c>Nipp.App</c> reicht seine Arbeit
/// über diese eine Methode weiter, statt <c>DispatcherQueue.TryEnqueue</c>
/// an zwanzig Stellen von Hand hinzuschreiben.</para>
/// </summary>
internal static class UiDispatch
{
    /// <summary>
    /// Führt <paramref name="aktion"/> auf dem Thread von
    /// <paramref name="dispatcher"/> aus.
    ///
    /// <para>Läuft der Aufruf schon dort (<see cref="DispatcherQueue.HasThreadAccess"/>),
    /// geschieht das sofort und synchron — ein Ereignis, das ohnehin vom
    /// UI-Thread kommt, bekommt keinen unnötigen Umweg über die
    /// Warteschlange. Sonst wird eingereiht.</para>
    ///
    /// <para>Steht die Warteschlange nicht mehr (Herunterfahren), verfällt die
    /// Aktion lautlos — W1.7 gilt auch hier: <paramref name="logger"/> und
    /// <paramref name="was"/> sorgen dafür, dass das nicht unbemerkt
    /// bleibt.</para>
    /// </summary>
    public static void Post(
        this DispatcherQueue dispatcher,
        ILogger logger,
        string was,
        Action aktion)
    {
        if (dispatcher.HasThreadAccess)
        {
            aktion();
            return;
        }

        if (dispatcher.TryEnqueue(() => aktion()))
        {
            return;
        }

        AppLog.DispatcherRejected(logger, was);
    }
}
