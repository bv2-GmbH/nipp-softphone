using Nipp.Core.Services.Windows;
using Velopack;

namespace Nipp.App;

/// <summary>
/// Der Einstiegspunkt des Prozesses (ADR-038).
///
/// <para>Er existiert nur aus einem Grund: <see cref="VelopackApp"/> muss
/// laufen, <b>bevor</b> irgendetwas von WinUI anläuft. Ein Update wird beim
/// Neustart angewandt, und dabei ruft Velopack die eigene EXE mit Argumenten
/// wie <c>--veloapp-install</c> oder <c>--veloapp-updated</c> auf. Diese Läufe
/// sind keine Programmstarts: <c>Run()</c> erledigt den Haken und beendet den
/// Prozess. Käme WinUI vorher hoch, blitzte ein Fenster auf, und die
/// Installation bliebe halbfertig.</para>
///
/// <para>Deshalb steht hier nichts anderes. Kein DI, kein Serilog, keine
/// Einstellungen — und vor allem nichts Wartendes. Ein blockierender Aufruf im
/// Startpfad sieht wie ein Erfolg aus: die letzte Protokollzeile liest sich wie
/// ein abgeschlossener Schritt und ist die Stelle, an der alles stehenblieb
/// (die Lehre aus der Headset-Anbindung, CLAUDE.md).</para>
///
/// <para><b>Warum nicht die Einzelinstanz zuerst?</b>
/// <c>AppInstance.FindOrRegisterForKey</c> steht in <see cref="App.OnLaunched"/>
/// und leitet einen zweiten Start an die laufende Instanz weiter. Für einen
/// Velopack-Hook wäre das falsch — er gehört nicht weitergeleitet, sondern
/// ausgeführt und beendet. Die Reihenfolge ist also nicht Geschmack: erst der
/// Hook, dann die Einzelinstanz.</para>
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        // Kehrt bei einem Hook-Aufruf nie zurueck.
        VelopackApp.Build()

            // W1.5 (Befund E4): beim Deinstallieren aufraeumen.
            //
            // <b>Das stand seit dem 07.09.2026 im RELEASE-PLAN als erledigt</b>
            // — «Velopack ruft dafuer einen Hook auf; die Rueckbaulogik hat
            // WindowsIntegration bereits». Gerufen hat ihn niemand: der Hook
            // war nicht angemeldet. Nach dem Deinstallieren zeigten der
            // Autostart-Eintrag in HKCU und die Handler fuer tel:, sip:, sips:
            // und callto: weiter auf eine geloeschte EXE — ein Anmeldefehler
            // bei jedem Windows-Start und ein tel:-Klick aus Outlook, der ins
            // Leere geht. Das Muster «gebaut, nicht angeschlossen», zum
            // sechsten Mal.
            //
            // <b>FastCallback heisst: danach beendet Velopack den Prozess.</b>
            // Was hier laeuft, hat 30 Sekunden und darf nichts erwarten —
            // kein Fenster, keine Dienste, kein Protokoll auf Platte.
            .OnBeforeUninstallFastCallback(_ => RaeumeAuf())

            .Run();

        // Ab hier der Weg, den der XAML-Compiler sonst selbst genommen haette.
        // Der Aufruf bleibt seiner - ComWrappers, Application.Start und der
        // DispatcherQueueSynchronizationContext stehen dort in einer
        // Reihenfolge, die wir nicht nachbauen wollen (siehe csproj). Das
        // "new App()" darin erzeugt der Generator ebenfalls selbst
        // (App.g.cs, _XamlGeneratedCreateApplicationInstance) - eine eigene
        // Implementierung davon ist ein Compilerfehler, kein Zusatz.
        XamlGeneratedProgram.XamlGeneratedMain();
    }

    /// <summary>
    /// Was nipp beim Deinstallieren hinterlässt, und zwar nichts (W1.5, E4).
    ///
    /// <para><b>Ohne Protokoll und ohne Dienste.</b> Dieser Aufruf läuft in
    /// einem Prozess, der gleich beendet wird; ein Serilog-Aufbau wäre hier
    /// eine Datei, die niemand mehr liest. Was scheitert, scheitert still —
    /// und das ist vertretbar: ein zurückgebliebener Registrierungseintrag ist
    /// ärgerlich, ein hängendes Deinstallationsprogramm schlimmer.</para>
    /// </summary>
    private static void RaeumeAuf()
    {
        try
        {
            var integration = new WindowsIntegration(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsIntegration>.Instance);

            integration.SetAutostart(enabled: false, startMinimized: false);
            integration.RegisterProtocolHandlers(register: false);
        }
        catch (Exception)
        {
            // Siehe oben: hier gibt es niemanden mehr, dem man es sagen
            // koennte.
        }
    }
}
