using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Sorgt dafür, dass nipp **einmal** läuft — auch ohne Paketidentität
/// (W2.4, Befund B23).
///
/// <para><b>Der Befund.</b> §10 verlangt eine Instanz, und
/// <c>AppInstance.FindOrRegisterForKey</c> liefert sie — aber nur mit
/// Paketidentität. Der Kommentar in <c>App.xaml.cs</c> gab das zu: «Ohne
/// Paketidentität gibt es keine AppInstance. Dann läuft nipp eben mehrfach —
/// unschön, aber kein Grund, den Start abzubrechen.» <b>Ausgeliefert wird
/// unpackaged</b> (ADR-008, ADR-038); der Normalfall im Feld war also genau
/// der Fall ohne Schutz.</para>
///
/// <para><b>Was zwei Instanzen anrichten.</b> Beide registrieren dasselbe
/// SIP-Konto — die zweite Registrierung verdrängt die erste an der Anlage,
/// und eingehende Anrufe landen unvorhersehbar bei einer von beiden. Beide
/// schreiben <c>settings.json</c> gegeneinander. Beide öffnen dasselbe
/// HID-Handle. Und das systemweite Kürzel der zweiten scheitert mit Fehler
/// 1408 — mit der Meldung «von einer anderen Anwendung belegt», die dann in
/// die Irre führt, weil die andere Anwendung nipp selbst ist.</para>
///
/// <para><b>Warum Mutex <em>und</em> Pipe.</b> Der Mutex beantwortet «bin ich
/// der Erste?». Das allein genügt nicht: ein <c>tel:</c>-Klick aus Outlook
/// startet einen zweiten Prozess mit der Nummer in der Kommandozeile, und
/// diese Nummer darf nicht verlorengehen. Die Pipe reicht sie an die laufende
/// Instanz weiter — dasselbe, was <c>AppInstance.RedirectActivationToAsync</c>
/// mit Paketidentität tut.</para>
///
/// <para><b>Beides sitzungsweit, nicht maschinenweit.</b> Zwei angemeldete
/// Benutzer auf demselben Rechner bekommen jeder ihr nipp: der Mutex trägt
/// das Präfix <c>Local\</c>, der Pipename die Sitzungskennung. Ein
/// maschinenweiter Mutex hiesse, dass der zweite Benutzer gar nicht
/// telefonieren kann.</para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly ILogger _logger;
    private Mutex? _mutex;
    private CancellationTokenSource? _stop;
    private Thread? _listener;
    private bool _disposed;

    public SingleInstanceGuard(ILogger logger) => _logger = logger;

    /// <summary>
    /// Wird gerufen, wenn eine zweite Instanz etwas weiterreicht. Der Wert ist
    /// ihre Kommandozeile; leer, wenn sie ohne Argumente gestartet wurde.
    ///
    /// <para><b>Kommt vom Lauschthread</b>, nicht vom UI-Thread. Der Empfänger
    /// marshallt selbst.</para>
    /// </summary>
    public event Action<string>? Activated;

    /// <summary>
    /// Ob dieser Prozess die erste Instanz ist.
    ///
    /// <para><c>true</c>: der Mutex gehört uns, der Lauschthread läuft, und
    /// der Start geht weiter. <c>false</c>: es läuft schon eine, die
    /// Kommandozeile ist ihr gereicht worden, und dieser Prozess soll sich
    /// beenden.</para>
    ///
    /// <para><b>Im Zweifel <c>true</c>.</b> Scheitert der Mutex oder die
    /// Pipe, startet nipp trotzdem — ein Telefon, das wegen einer
    /// Synchronisierungsfrage nicht hochkommt, ist schlimmer als zwei
    /// Telefone. Der Grund steht dann im Protokoll.</para>
    /// </summary>
    public bool TryBecomeFirstInstance(string commandLine)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName);

            // Eine Sekunde: der übliche Fall ist «sofort ja» oder «sofort
            // nein». Wartezeit entsteht nur, wenn eine Instanz gerade
            // hochfährt oder herunterfährt — und dann ist eine Sekunde der
            // Unterschied zwischen «richtig entschieden» und «zwei Telefone».
            if (_mutex.WaitOne(TimeSpan.FromSeconds(1), exitContext: false))
            {
                StarteLauscher();
                return true;
            }

            _mutex.Dispose();
            _mutex = null;

            SendeAnLaufende(commandLine);
            return false;
        }
        catch (Exception ex) when (ex is AbandonedMutexException)
        {
            // Die vorige Instanz ist abgestürzt, ohne den Mutex freizugeben.
            // Er gehört jetzt uns — das ist genau der Fall, für den es
            // AbandonedMutexException gibt.
            WindowsIntegrationLog.SingleInstanceTookOver(_logger);
            StarteLauscher();
            return true;
        }
        catch (Exception ex)
        {
            WindowsIntegrationLog.SingleInstanceFailed(_logger, ex.GetType().Name, ex.Message);
            return true;
        }
    }

    /// <summary>Der Mutexname — sitzungsweit, nicht maschinenweit.</summary>
    private static string MutexName => @"Local\nipp-single-instance";

    /// <summary>
    /// Der Pipename. <b>Named Pipes sind maschinenweit</b>, auch wenn Mutexe
    /// es mit <c>Local\</c> nicht sind — deshalb die Sitzungskennung im Namen.
    /// </summary>
    private static string PipeName =>
        "nipp-activation-" + Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture);

    private void SendeAnLaufende(string commandLine)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            // Zwei Sekunden. Kommt die laufende Instanz nicht dazu, ist die
            // Nummer verloren — aber der Benutzer sieht ein nipp, das schon
            // da ist, und kann sie eintippen. Ewig zu warten hiesse, dass ein
            // tel:-Klick den Explorer blockiert.
            pipe.Connect(2000);

            var rohdaten = Encoding.UTF8.GetBytes(commandLine);
            pipe.Write(rohdaten, 0, rohdaten.Length);
            pipe.Flush();

            WindowsIntegrationLog.SingleInstanceForwarded(_logger);
        }
        catch (Exception ex)
        {
            // Die laufende Instanz antwortet nicht. Dieser Prozess beendet
            // sich trotzdem: zwei Telefone waeren schlimmer als eine verlorene
            // Nummer, und die Zeile hier sagt, was passiert ist.
            WindowsIntegrationLog.SingleInstanceNotForwarded(_logger, ex.GetType().Name);
        }
    }

    private void StarteLauscher()
    {
        _stop = new CancellationTokenSource();

        _listener = new Thread(() => Lausche(_stop.Token))
        {
            IsBackground = true,
            Name = "nipp Einzelinstanz",
        };

        _listener.Start();
    }

    /// <summary>
    /// Wartet auf zweite Instanzen. <b>Eine Verbindung nach der anderen</b> —
    /// ein tel:-Klick ist kein Lastfall.
    /// </summary>
    private void Lausche(CancellationToken abbruch)
    {
        while (!abbruch.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                pipe.WaitForConnectionAsync(abbruch).GetAwaiter().GetResult();

                using var leser = new StreamReader(pipe, Encoding.UTF8);
                var text = leser.ReadToEnd();

                if (!abbruch.IsCancellationRequested)
                {
                    Activated?.Invoke(text);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (abbruch.IsCancellationRequested)
                {
                    return;
                }

                // Ein Fehler auf der Pipe darf den Lauscher nicht beenden:
                // sonst wirkt ab da jeder tel:-Klick wie ein zweiter Start.
                WindowsIntegrationLog.SingleInstanceListenerFailed(_logger, ex.GetType().Name);

                // Kurz warten, damit ein dauerhafter Fehler nicht zur
                // Endlosschleife wird.
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stop?.Cancel();

            // Eine eigene Verbindung loest ein haengendes WaitForConnection —
            // der Abbruch allein genuegt nicht, wenn der Thread schon drin
            // steht.
            using (var wecker = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
            {
                wecker.Connect(200);
            }
        }
        catch (Exception)
        {
            // Beim Herunterfahren ist das folgenlos.
        }

        _listener?.Join(TimeSpan.FromSeconds(1));
        _stop?.Dispose();

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (Exception)
        {
            // Nicht im Besitz — dann gibt es nichts freizugeben.
        }

        _mutex?.Dispose();
    }
}
