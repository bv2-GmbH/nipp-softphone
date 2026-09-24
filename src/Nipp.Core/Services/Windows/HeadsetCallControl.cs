using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.Services.Windows.Audio;
using Nipp.Core.Services.Windows.Hid;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Annehmen und Auflegen über die Taste am Headset (§22.5).
///
/// <para><b>Warum das nicht von selbst geht.</b> Das Linphone SDK hatte bis
/// Version 5.4 eine eigene Anbindung über HIDAPI, die Jabra-Geräte direkt
/// ansprach. Mit 5.5.0 wurde sie <b>entfernt</b> (NIPP-BUILD.md §5). Seither
/// gibt es dafür im SDK nichts, und die Taste am Headset tut in nipp
/// nichts.</para>
///
/// <para><b>Zwei Wege wurden verworfen, beide belegt.</b>
/// <c>Windows.Media.Devices.CallControl</c> ist die dafür vorgesehene
/// WinRT-Schnittstelle und auf dem Desktop nicht implementiert: <c>FromId</c>
/// und <c>GetDefault</c> scheitern beide mit <c>0x80040111</c>,
/// <c>CLASS_E_CLASSNOTAVAILABLE</c>. Und
/// <c>Windows.Devices.HumanInterfaceDevice</c> sperrt genau die Usage Page
/// aus, um die es hier geht — Telefonie (0x0B) ist für Anwendungen
/// reserviert. Übrig bleibt die Win32-HID-Schnittstelle, siehe
/// <see cref="HidTelephonyDevice"/>.</para>
///
/// <para><b>Was hier steht und was nicht.</b> Diese Klasse deutet: sie weiss,
/// was ein Tastendruck bedeutet, wenn es klingelt, und was er bedeutet, wenn
/// ein Gespräch läuft. Wie die Taste ins Programm kommt, weiss sie nicht —
/// das ist die andere Klasse.</para>
///
/// <para><b>Und was schiefgehen darf: alles.</b> Jeder Aufruf ist gekapselt.
/// Ein Headset ohne Telefonieseite, ein Gerät, das gerade abgezogen wird,
/// eine Windows-Fassung, die sich anders verhält — nichts davon darf ein
/// Gespräch kosten. Ohne diese Klasse telefoniert nipp unverändert, nur eben
/// mit der Maus.</para>
/// </summary>
public sealed class HeadsetCallControl : IDisposable
{
    private readonly ISipService _sip;
    private readonly ILogger<HeadsetCallControl> _logger;

    /// <summary>
    /// Für den Notausgang aus H5 — gelesen wird er bei jedem Bericht, nicht
    /// einmal beim Start: eine Einstellung, die erst nach einem Neustart
    /// wirkt, hilft am Telefon niemandem (ADR-045).
    /// </summary>
    private readonly SettingsService _settings;

    /// <summary>
    /// Der Thread, auf dem dieser Dienst gebaut wurde — bei nipp der UI-Thread.
    ///
    /// <para>Die Tastendrücke kommen aus dem Lese-Thread des HID-Geräts. Von
    /// dort in <c>ISipService</c> zu greifen wäre derselbe Fehler, den der
    /// Anruferkontext schon hatte: alles am SDK gehört auf den Thread, der
    /// <c>Core.Iterate()</c> bedient (§6).</para>
    /// </summary>
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private HidTelephonyDevice? _device;

    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Ob nipp <b>diesem</b> Gerät schon einmal einen nicht leeren Zustand
    /// gemeldet hat.
    ///
    /// <para>Erst dann ist „alles aus" eine Mitteilung und kein Bericht ohne
    /// Anlass (<see cref="HeadsetSignalGate"/>). Gehört zum Gerät und wird beim
    /// Ablegen zurückgesetzt: ein neu angebundenes Gerät hat von nipp noch
    /// nichts gehört.</para>
    /// </summary>
    private bool _jeGemeldet;

    /// <summary>
    /// Ob ein anderes Programm die Audiogeräte gerade benutzt (ADR-068) — die
    /// Quelle, die ein Teams-Meeting erkennt.
    /// </summary>
    private readonly AudioSessionWatch _audio;

    public HeadsetCallControl(
        ISipService sip,
        SettingsService settings,
        ILogger<HeadsetCallControl> logger)
    {
        _sip = sip;
        _settings = settings;
        _logger = logger;
        _audio = new AudioSessionWatch(
            new CoreAudioSessions(logger),
            (uint)Environment.ProcessId);
    }

    /// <summary>
    /// Beginnt zuzuhören.
    ///
    /// <para>Getrennt vom Konstruktor, wie bei den übrigen Diensten: nichts
    /// davon darf zwischen dem Start und dem ersten möglichen Anruf
    /// stehen.</para>
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;

        // Die Anrufzustände in jedem Fall verfolgen, auch wenn heute kein
        // Headset da ist: wird eines eingesteckt, gilt sofort der richtige
        // Zustand. Andernfalls bliebe die Lampe aus, bis das nächste Gespräch
        // beginnt.
        _sip.CallStateChanged += OnCallStateChanged;
        _sip.AudioDevicesChanged += OnAudioDevicesChanged;

        Anbinden();
    }

    /// <summary>
    /// Sucht das Headset — <b>nicht auf dem UI-Thread.</b>
    ///
    /// <para>Die Suche öffnet jedes angeschlossene HID-Gerät der Reihe nach.
    /// Das dauert normalerweise Millisekunden, aber ein Gerät, dessen Treiber
    /// hängt, hängt hier mit — und auf dem UI-Thread wäre das ein nipp, das
    /// nicht startet. Genau das ist beim ersten Versuch passiert, an der
    /// Stelle danach: das Setzen der Lampen (siehe
    /// <see cref="HidTelephonyDevice"/>).</para>
    /// </summary>
    private void Anbinden()
    {
        var thread = new Thread(TryAttach)
        {
            IsBackground = true,
            Name = "nipp Headset suchen",
        };

        thread.Start();
    }

    private void TryAttach()
    {
        try
        {
            var device = HidTelephonyDevice.Open(_logger, out var grund);

            if (device is null)
            {
                HeadsetLog.NoDevice(_logger, grund);
                return;
            }

            device.HookPressed += OnHookPressed;
            device.MutePressed += OnMutePressed;
            device.Lost += OnDeviceLost;
            device.WriteFailed += OnWriteFailed;

            if (_disposed)
            {
                // Zwischen Start und Fund kann nipp beendet worden sein. Dann
                // gehoert das Geraet wieder zu, sonst bleibt ein Lese-Thread
                // auf einem Handle stehen, das niemand mehr aufgibt.
                device.Dispose();
                return;
            }

            _device = device;
            HeadsetLog.Attached(_logger, device.Product);

            // Den gegenwärtigen Zustand gleich melden. Wird nipp während eines
            // Gesprächs neu gestartet oder das Headset mitten im Gespräch
            // eingesteckt, stimmt sonst die Vorstellung des Geräts nicht mit
            // der Wirklichkeit — und der erste Tastendruck hätte die falsche
            // Bedeutung.
            //
            // Über den UI-Thread, weil ISipService dort gelesen wird (§6).
            RunOnUi(PushState);
        }
        catch (Exception ex)
        {
            HeadsetLog.AttachFailed(_logger, ex.GetType().Name);
        }
    }

    private void Detach()
    {
        var device = _device;
        _device = null;
        _jeGemeldet = false;

        if (device is null)
        {
            return;
        }

        try
        {
            device.HookPressed -= OnHookPressed;
            device.MutePressed -= OnMutePressed;
            device.Lost -= OnDeviceLost;
            device.WriteFailed -= OnWriteFailed;
            device.Dispose();
        }
        catch (Exception ex)
        {
            HeadsetLog.DetachFailed(_logger, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Ein Gerätewechsel kann ein neues Headset bedeuten — dann gehört das
    /// HID-Gerät neu gesucht.
    ///
    /// <para>Sonst hörte nipp weiter auf ein Headset, das nicht mehr da ist,
    /// und das neue bliebe stumm.</para>
    /// </summary>
    private void OnAudioDevicesChanged(object? sender, AudioDevicesChangedEventArgs e)
    {
        // <b>Zuerst fragen, ob es überhaupt ein anderes Gerät ist.</b> Jedes
        // Anbinden endet in einem Ausgangsbericht, und ein Bericht an ein
        // Gerät, das nipp mit anderen Programmen teilt, ist nie folgenlos
        // (HeadsetSignalGate). Windows meldet die Geräteliste auch dann neu,
        // wenn ein anderes Programm das Standardgerät umstellt — beim Beitritt
        // zu einem Teams-Meeting zum Beispiel.
        //
        // <b>Und die Frage selbst nicht auf dem UI-Thread</b>, so wenig wie
        // die Suche: sie öffnet jedes HID-Gerät prüfend, und ein Gerät mit
        // hängendem Treiber hängt hier mit. Dieses Ereignis kommt aus dem
        // Kern, also vom Thread, der alle 20 ms Core.Iterate() bedient.
        var anzahl = e.Devices.Count;

        var thread = new Thread(() => PruefeGeraetewechsel(anzahl))
        {
            IsBackground = true,
            Name = "nipp Headset pruefen",
        };

        thread.Start();
    }

    private void PruefeGeraetewechsel(int audiogeraete)
    {
        string? pfad;

        try
        {
            pfad = HidTelephonyDevice.ErsterTelefoniePfad();
        }
        catch (Exception ex)
        {
            HeadsetLog.AttachFailed(_logger, ex.GetType().Name);
            return;
        }

        // Ablegen und Anbinden bleiben auf dem Thread, auf dem sie immer
        // liefen — hier wird nur nachgesehen.
        RunOnUi(() =>
        {
            var jetzt = _device?.Path;

            if (string.Equals(pfad, jetzt, StringComparison.OrdinalIgnoreCase))
            {
                HeadsetLog.DeviceUnchanged(_logger, audiogeraete, jetzt ?? "keines");
                return;
            }

            HeadsetLog.DeviceChanged(_logger, jetzt ?? "keines", pfad ?? "keines");
            Detach();
            Anbinden();
        });
    }

    private void OnWriteFailed() =>
        HeadsetLog.IndicateFailed(_logger, "das Geraet nahm den Bericht nicht an");

    /// <summary>
    /// Das Gerät hat sich abgemeldet. Aus dem Lese-Thread — deshalb nichts
    /// hier tun, was den UI-Thread braucht.
    /// </summary>
    private void OnDeviceLost() => RunOnUi(() =>
    {
        HeadsetLog.DeviceLost(_logger);
        Detach();

        // W1.7 (Befund B18): einmal nachsehen, ob das Geraet noch da ist.
        //
        // <b>Warum das noetig ist.</b> Jeder Lesefehler galt als «Geraet weg»,
        // und neu angebunden wurde nur bei einem Audiogeraetewechsel. Ein
        // Treiber-Reset oder ein Aussetzer am USB-Hub erzeugt aber kein
        // WASAPI-Ereignis — die Tasten am Headset waren danach bis zum
        // Neustart tot, und im Protokoll stand «DeviceLost» ohne Grund.
        //
        // <b>Und warum nur einmal, mit Verzoegerung.</b> Ist das Geraet
        // wirklich abgezogen, findet die Suche nichts und es bleibt bei einer
        // Zeile. Ein Wiederholungsschleife dagegen oeffnete im Sekundentakt
        // jedes HID-Geraet pruefend — auf einem Rechner mit haengendem Treiber
        // waere das der naechste Aufhaenger. Zwei Sekunden, ein Versuch.
        var thread = new Thread(NachGeraetSehen)
        {
            IsBackground = true,
            Name = "nipp Headset erneut suchen",
        };

        thread.Start();
    });

    /// <summary>
    /// Sieht nach einem Lesefehler einmal nach, ob das Geraet wieder da ist
    /// (W1.7).
    /// </summary>
    private void NachGeraetSehen()
    {
        Thread.Sleep(TimeSpan.FromSeconds(2));

        if (_disposed)
        {
            return;
        }

        string? pfad;

        try
        {
            pfad = HidTelephonyDevice.ErsterTelefoniePfad();
        }
        catch (Exception ex)
        {
            HeadsetLog.AttachFailed(_logger, ex.GetType().Name);
            return;
        }

        if (pfad is null)
        {
            HeadsetLog.DeviceStillGone(_logger);
            return;
        }

        RunOnUi(() =>
        {
            if (_device is not null || _disposed)
            {
                return;
            }

            HeadsetLog.DeviceBackAgain(_logger);
            Anbinden();
        });
    }

    /// <summary>
    /// Die Gabeltaste am Headset wurde betätigt. <b>Hier steht die ganze
    /// Deutung</b> — und sie kennt den Gabelzustand des Geräts nicht mehr.
    ///
    /// <para>Klingelt es, wird angenommen; sonst wird das Gespräch im
    /// Vordergrund aufgelegt, dasselbe, das der Knopf in der Oberfläche
    /// auflegt. Warum die Richtung der Meldung dabei keine Rolle spielt,
    /// steht bei <see cref="HookWatch"/> und
    /// <see cref="HeadsetPolicy.Interpret"/>.</para>
    /// </summary>
    private void OnHookPressed() => RunOnUi(() =>
    {
        var (action, call) = HeadsetPolicy.Interpret(_sip.ActiveCalls);

        switch (action)
        {
            case HeadsetAction.Annehmen when call is not null:
                HeadsetLog.AnswerPressed(_logger);
                _ = _sip.AcceptAsync(call.Handle);
                break;

            case HeadsetAction.Auflegen when call is not null:
                HeadsetLog.HangUpPressed(_logger);
                _ = _sip.HangUpAsync(call.Handle);
                break;

            default:
                // Ein Druck ins Leere ist bedeutungslos, nicht falsch. Er
                // braucht auch keine Korrektur am Gerät mehr: die Bedeutung
                // des nächsten Drucks hängt am Anrufzustand, nicht daran, was
                // das Gerät von seiner Gabel hält.
                HeadsetLog.HookWithoutCall(_logger);
                break;
        }
    });

    /// <summary>
    /// Die Stummtaste am Headset schaltet das Mikrofon des laufenden
    /// Gesprächs.
    /// </summary>
    private void OnMutePressed() => RunOnUi(() =>
    {
        var laufend = Verbundenes();

        if (laufend is null)
        {
            return;
        }

        HeadsetLog.MutePressed(_logger);
        _ = _sip.SetMutedAsync(laufend.Handle, !laufend.IsMuted);
    });

    private CallInfo? Verbundenes()
    {
        foreach (var call in _sip.ActiveCalls)
        {
            if (call.Status == CallStatus.Connected)
            {
                return call;
            }
        }

        return null;
    }

    /// <summary>
    /// Meldet dem Gerät den Zustand des Gesprächs: Lampen und Gabelstellung.
    ///
    /// <para><b>Wofür das noch gut ist.</b> Bis zum 09.09.2026 hing hier die
    /// Bedeutung der Taste — das Gerät führte den Gabelzustand, nipp zog ihn
    /// nach, und lief er auseinander, war jeder zweite Druck falsch. Diese
    /// Abhängigkeit ist aufgelöst: was die Taste bedeutet, entscheidet der
    /// Anrufzustand (<see cref="HeadsetPolicy.Interpret"/>). Was hier gemeldet
    /// wird, ist deshalb <b>Anzeige</b> — die Off-Hook-Lampe, das Blinken beim
    /// Klingeln, die Stummlampe. Wichtig genug (T84), aber kein Gespräch hängt
    /// mehr daran.</para>
    /// </summary>
    private void OnCallStateChanged(object? sender, CallStateEventArgs e) => PushState();

    private void PushState()
    {
        var device = _device;

        if (device is null)
        {
            return;
        }

        try
        {
            var calls = _sip.ActiveCalls;
            var state = HeadsetPolicy.StateFor(calls);

            // <b>Und ob es hinausgehen darf.</b> Ein Ausgangsbericht ist keine
            // Lampe, sondern eine Mitteilung an ein Geraet, das nipp mit
            // anderen Programmen teilt — die ganze Begruendung steht bei
            // HeadsetSignalGate.
            // <b>Ein eigenes Gespraech ist mehr als ein Anruf.</b> Bis zum
            // 14.09.2026 stand hier calls.Count > 0 — und weil beim Klingeln
            // immer ein Anruf da ist, war die Fremdbelegung genau im einzigen
            // Fall, fuer den sie gebaut war, immer falsch (ADR-068).
            var eigenesGespraech = calls.Any(static c => c.Status is not CallStatus.Incoming);

            // <b>Zwei Quellen fuer dieselbe Frage, und beide braucht es.</b>
            // Der Gabelzustand erkennt einen Anruf des anderen Programms, die
            // Audio-Sitzung ein Meeting — das Jabra meldete darin durchgehend
            // «aufgelegt» (gemessen am 14.09.2026).
            var fremd = device.Fremdbelegung(eigenesGespraech) || _audio.Fremdbelegt();

            var urteil = HeadsetSignalGate.Erlaubt(
                state,
                _jeGemeldet,
                fremd,
                eigenesGespraech,
                _settings.Current.Advanced.SendHeadsetSignals);

            if (!urteil.Schreiben)
            {
                HeadsetLog.StateSuppressed(
                    _logger,
                    urteil.Grund,
                    state.ImGespraech,
                    state.Klingelt,
                    state.Stumm);

                return;
            }

            if (state.ImGespraech || state.Klingelt || state.Stumm)
            {
                _jeGemeldet = true;
            }

            // <b>Nur melden, nichts behaupten.</b> Hier stand bis zum
            // 09.09.2026 ein SyncHook, das die Gabelstellung des Geraets im
            // Lese-Thread gleich mitschrieb — und damit eine Flanke erfand,
            // die es physisch nie gab: 10 ms nach jedem Annehmen legte nipp
            // wieder auf. SetState fuehrt die Erwartung jetzt selbst, mit
            // Ablauf (HookWatch).
            device.SetState(state.ImGespraech, state.Klingelt, state.Stumm);
        }
        catch (Exception ex)
        {
            // <b>Nie ein Gespräch dafür.</b> Läuft aus einem SDK-Ereignis, und
            // eine Ausnahme hier nähme die ganze Anwendung mit.
            HeadsetLog.IndicateFailed(_logger, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Führt aus, was das SDK anfasst — auf dem Thread, auf dem dieser Dienst
    /// gebaut wurde.
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (_ui is null || _ui == SynchronizationContext.Current)
        {
            Guarded(action);
            return;
        }

        _ui.Post(_ => Guarded(action), null);
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Der Anruf kann zwischen Tastendruck und Ausführung geendet sein;
            // SipService wirft dann eine InvalidOperationException.
            HeadsetLog.ActionFailed(_logger, ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_started)
        {
            _sip.CallStateChanged -= OnCallStateChanged;
            _sip.AudioDevicesChanged -= OnAudioDevicesChanged;
        }

        Detach();
    }
}

internal static partial class HeadsetLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Headset-Tasten angebunden an {Produkt} - Annehmen, Auflegen und Stumm "
            + "am Geraet moeglich")]
    public static partial void Attached(ILogger logger, string produkt);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "Kein Headset mit Telefonie-Tasten gefunden ({Grund}). nipp telefoniert "
            + "unveraendert, nur ohne die Tasten am Geraet.")]
    public static partial void NoDevice(ILogger logger, string grund);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning,
        Message = "Die Tastensteuerung des Headsets liess sich nicht anbinden ({Reason}). "
            + "Das Telefonieren ist davon nicht betroffen.")]
    public static partial void AttachFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Debug,
        Message = "Die Tastensteuerung liess sich nicht loesen ({Reason})")]
    public static partial void DetachFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information,
        Message = "Annehmen am Headset gedrueckt")]
    public static partial void AnswerPressed(ILogger logger);

    [LoggerMessage(EventId = 3106, Level = LogLevel.Information,
        Message = "Auflegen am Headset gedrueckt")]
    public static partial void HangUpPressed(ILogger logger);

    [LoggerMessage(EventId = 3107, Level = LogLevel.Warning,
        Message = "Der Gespraechszustand liess sich dem Headset nicht melden ({Reason}). Die "
            + "Taste am Geraet wirkt dann moeglicherweise nicht.")]
    public static partial void IndicateFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3108, Level = LogLevel.Warning,
        Message = "Der Tastendruck am Headset liess sich nicht ausfuehren ({Reason})")]
    public static partial void ActionFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3109, Level = LogLevel.Information,
        Message = "Das Headset hat sich abgemeldet. Die Tasten am Geraet wirken erst wieder, "
            + "wenn es erneut angeschlossen ist.")]
    public static partial void DeviceLost(ILogger logger);

    [LoggerMessage(EventId = 3110, Level = LogLevel.Information,
        Message = "Stummtaste am Headset gedrueckt")]
    public static partial void MutePressed(ILogger logger);

    [LoggerMessage(EventId = 3111, Level = LogLevel.Debug,
        Message = "Gabeltaste am Headset ohne Anruf - ohne Wirkung")]
    public static partial void HookWithoutCall(ILogger logger);

    // ---- Was am Geraet wirklich passiert (Befund vom 09.09.2026) ----
    //
    // Ueber empfangene HID-Reports stand nie eine Zeile im Protokoll. Damit
    // war „die Taste tut nichts" nicht von „hier kommt gar nichts an" zu
    // unterscheiden, und der Fehler, der jedes Gespraech kostete, lag zwei
    // Tage im Log ohne Spur. Dieselbe Luecke wie beim Symbol im Infobereich.
    //
    // Alles auf Debug: im Alltag still, bei Bedarf da.

    // <b>Mit der Anzahl der Telefoniegeraete.</b> Genommen wird das erste;
    // an einem Rechner mit drei Headsets ist das keine rhetorische Frage, und
    // die Antwort stand bisher in keinem Protokoll.
    [LoggerMessage(EventId = 3112, Level = LogLevel.Debug,
        Message = "Headset geoeffnet: {Produkt} - Seite 0x{UsagePage:X2}/0x{Usage:X2}, "
            + "Bericht ein {InputLength} B / aus {OutputLength} B, Lampen-Bericht "
            + "{OutputReportId}, eines von {Telefoniegeraete} Telefoniegeraeten, "
            + "Tasten: {Tasten} | {Pfad}")]
    public static partial void DeviceOpened(
        ILogger logger,
        string produkt,
        string pfad,
        ushort usagePage,
        ushort usage,
        ushort inputLength,
        ushort outputLength,
        byte outputReportId,
        int telefoniegeraete,
        string tasten);

    [LoggerMessage(EventId = 3113, Level = LogLevel.Debug,
        Message = "Headset-Bericht {ReportId}: {Laenge} B, Usages {Usages}, "
            + "Gabel abgenommen={OffHook}")]
    public static partial void ReportRead(
        ILogger logger, byte reportId, int laenge, string usages, bool offHook);

    [LoggerMessage(EventId = 3114, Level = LogLevel.Debug,
        Message = "Headset-Bericht {ReportId} ohne Telefonie-Usages uebergangen "
            + "({Laenge} B, Status 0x{Status:X8})")]
    public static partial void ReportIgnored(
        ILogger logger, byte reportId, int laenge, int status);

    [LoggerMessage(EventId = 3115, Level = LogLevel.Debug,
        Message = "Gabelmeldung abgenommen={OffHook} verworfen: {Grund}")]
    public static partial void HookDiscarded(ILogger logger, bool offHook, string grund);

    [LoggerMessage(EventId = 3117, Level = LogLevel.Debug,
        Message = "Der Interrupt-Weg zum Headset ging nicht ({Reason}) - es bleibt beim "
            + "langsamen Weg ueber die Control-Pipe")]
    public static partial void WriteFellBack(ILogger logger, string reason);

    [LoggerMessage(EventId = 3116, Level = LogLevel.Debug,
        Message = "Headset-Zustand gemeldet (Bericht {ReportId}): imGespraech={ImGespraech}, "
            + "klingelt={Klingelt}, stumm={Stumm} - angenommen={Erfolg} nach {Dauer} ms")]
    public static partial void StateWritten(
        ILogger logger,
        byte reportId,
        bool imGespraech,
        bool klingelt,
        bool stumm,
        bool erfolg,
        int dauer);

    // <b>Was nicht hinausging, und warum.</b> Ohne diese Zeile ist "ich fliege
    // trotzdem aus dem Meeting" nicht von "die Erkennung hat nicht
    // angeschlagen" zu unterscheiden — dieselbe Luecke wie beim Symbol im
    // Infobereich und bei den HID-Berichten, und die stand hier schon zweimal.
    [LoggerMessage(EventId = 3118, Level = LogLevel.Debug,
        Message = "Ausgangsbericht unterdrueckt ({Grund}): imGespraech={ImGespraech}, "
            + "klingelt={Klingelt}, stumm={Stumm}")]
    public static partial void StateSuppressed(
        ILogger logger,
        string grund,
        bool imGespraech,
        bool klingelt,
        bool stumm);

    [LoggerMessage(EventId = 3119, Level = LogLevel.Debug,
        Message = "Geraetewechsel gemeldet ({Audiogeraete} Audiogeraete) - Telefoniegeraet "
            + "{Pfad} unveraendert, nicht neu angebunden")]
    public static partial void DeviceUnchanged(ILogger logger, int audiogeraete, string pfad);

    [LoggerMessage(EventId = 3120, Level = LogLevel.Information,
        Message = "Telefoniegeraet gewechselt: {Alt} -> {Neu}, wird neu angebunden")]
    public static partial void DeviceChanged(ILogger logger, string alt, string neu);

    [LoggerMessage(EventId = 3121, Level = LogLevel.Warning,
        Message = "Der Lesestrom des Headsets liess sich nicht in 500 ms schliessen und wird "
            + "beim Beenden uebergangen. Der Lesethread ist ein Hintergrundthread; Windows "
            + "raeumt das Handle mit dem Prozess ab")]
    public static partial void CloseSlow(ILogger logger);

    // ADR-053: der Lesethread des Geraets. Beide Zeilen schliessen dieselbe
    // Luecke, die dieses Projekt schon einmal zwei Tage gekostet hat — ueber
    // empfangene HID-Reports stand lange gar nichts im Protokoll, und «die
    // Taste tut nichts» war nicht von «hier kommt nichts an» zu unterscheiden.
    [LoggerMessage(EventId = 3122, Level = LogLevel.Warning,
        Message = "Lesen vom Headset fehlgeschlagen ({ExceptionType}): {Reason}. "
            + "Die Tasten wirken erst wieder nach einem Geraetewechsel.")]
    public static partial void ReadFailed(ILogger logger, string exceptionType, string reason);

    [LoggerMessage(EventId = 3123, Level = LogLevel.Warning,
        Message = "Ein Report des Headsets liess sich nicht deuten ({ExceptionType}): "
            + "{Reason}. Der Lesethread laeuft weiter.")]
    public static partial void ReportNotUnderstood(
        ILogger logger, string exceptionType, string reason);

    // W1.7: die Gegenprobe zum Neuanbinden. Ohne diese beiden Zeilen waere
    // «die Taste tut wieder nichts» nicht von «es wurde gar nicht gesucht» zu
    // unterscheiden — dieselbe Luecke, die dieses Projekt bei den HID-Reports
    // schon einmal zwei Tage gekostet hat.
    [LoggerMessage(EventId = 3124, Level = LogLevel.Information,
        Message = "Das Headset ist wieder da — Tasten erneut angebunden")]
    public static partial void DeviceBackAgain(ILogger logger);

    [LoggerMessage(EventId = 3125, Level = LogLevel.Information,
        Message = "Kein Telefoniegeraet mehr gefunden. Die Tasten wirken erst wieder, wenn ein "
            + "Geraet angeschlossen oder das Audiogeraet gewechselt wird.")]
    public static partial void DeviceStillGone(ILogger logger);
}
