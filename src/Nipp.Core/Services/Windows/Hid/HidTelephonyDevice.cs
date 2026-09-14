using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using static Nipp.Core.Services.Windows.Hid.HidTelephonyInterop;

namespace Nipp.Core.Services.Windows.Hid;

/// <summary>
/// Ein einzelnes HID-Telefoniegerät: liest seine Tasten und setzt seine Lampen.
///
/// <para><b>Was diese Klasse nicht weiss.</b> Sie kennt keinen Anruf. Sie
/// meldet „die Gabeltaste wurde betätigt" und nimmt entgegen „ein Gespräch
/// läuft, lass die Lampe leuchten". Was daraus folgt, entscheidet
/// <see cref="HeadsetCallControl"/> — hier steht nur die Mechanik.</para>
///
/// <para><b>Und sie behauptet nichts über das Gerät.</b> Ein Headset führt
/// einen eigenen Gabelzustand, den es unterschiedlich meldet: als Schalter
/// oder als Momentan-Taster, mit Echo auf einen gesetzten Zustand oder ohne.
/// Was davon ein Tastendruck war, entscheidet <see cref="HookWatch"/> —
/// aus <b>Gerätemeldungen</b>, nie aus dem, was nipp gerade will. Der
/// Vorgänger tat beides in einem Feld und legte damit 10 ms nach jedem
/// Annehmen wieder auf.</para>
/// </summary>
internal sealed class HidTelephonyDevice : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly IntPtr _preparsed;
    private readonly FileStream _stream;
    private readonly int _inputLength;
    private readonly int _outputLength;
    private readonly Thread _reader;

    /// <summary>
    /// Wie viele Usages ein Report höchstens gleichzeitig meldet.
    ///
    /// <para><c>NumberInputDataIndices</c> ist die vom Gerät selbst genannte
    /// Obergrenze. Ein festes Mass wäre eine Wette; bei einem Headset mit
    /// Tastenfeld sind es zwanzig und mehr.</para>
    /// </summary>
    private readonly int _maxUsages;

    /// <summary>
    /// Die Report-Kennung, unter der das Gerät seine Lampen führt.
    ///
    /// <para>Beim Öffnen aus den Button-Caps abgelesen. Beim Jabra Link 400
    /// ist es die 2 — und ein fest verdrahtetes 0 hätte
    /// <c>HidP_InitializeReportForID</c> scheitern lassen, worauf der Report
    /// leer geblieben und die Lampe nie angegangen wäre. Da ein Gerät ohne
    /// Off-Hook-Lampe seinen Gabelzustand nicht mitzieht, wäre der Fehler
    /// nicht „keine Lampe", sondern „jeder zweite Tastendruck falsch".</para>
    /// </summary>
    private readonly byte _outputReportId;

    /// <summary>
    /// Der Thread, der die Lampen setzt — <b>und der Grund, warum es ihn
    /// gibt.</b>
    ///
    /// <para>Ein Report an das Gerät blockiert, bis es ihn bestätigt. Beim
    /// ersten Versuch stand dieser Aufruf im Startpfad, also auf dem
    /// UI-Thread — und <b>nipp startete nicht mehr</b>: kein Fenster, keine
    /// Anmeldung, das Protokoll endete mitten im Start. Dass die Anbindung
    /// selbst gelang, machte es schwer zu sehen, denn die letzte Zeile im
    /// Protokoll war eine Erfolgsmeldung.</para>
    ///
    /// <para>Der Thread bleibt auch, seit der Weg über
    /// <see cref="_writeStream"/> läuft und ein Report drei Millisekunden statt
    /// dreiundachtzig Sekunden braucht. Eine Funkstrecke, die schnell ist,
    /// bleibt eine Funkstrecke.</para>
    ///
    /// <para>Es zählt immer nur der neueste Zustand, deshalb braucht es keine
    /// Warteschlange: ein Feld für das Gewünschte, ein Signal, und dieser
    /// Thread schreibt, was gerade gilt. Bleiben Reports aus, weil das Gerät
    /// nicht antwortet, wartet nur er.</para>
    /// </summary>
    private readonly Thread _writer;

    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>
    /// Was dem Gerät gemeldet werden soll. Als Bits, damit ein Schreibvorgang
    /// immer einen in sich stimmigen Zustand sieht.
    ///
    /// <para><b>Beginnt bei −1, und das ist kein Zierwert.</b> Der Riegel in
    /// <see cref="SetState"/> schreibt nur bei einer Änderung — mit 0 als
    /// Anfang wäre die erste Meldung „alles aus" also gar keine, und das Gerät
    /// bliebe in dem Zustand, in dem nipp es vorfand. Genau das war beim
    /// Anbinden der Fall: kein Report, keine definierte Lampe, und keine
    /// Messung, wie lange ein Report zu diesem Gerät überhaupt braucht. Ein
    /// unmöglicher Anfangswert macht die erste Meldung zu einer echten.</para>
    /// </summary>
    private volatile int _wanted = -1;

    /// <summary>
    /// Wie lange der Schreib-Thread nach einem Weckruf noch auf weitere
    /// Aenderungen wartet, bevor er schreibt.
    /// </summary>
    private const int Sammelfenster = 60;

    private const int BitInCall = 1;
    private const int BitRinging = 2;
    private const int BitMuted = 4;

    private volatile bool _closing;

    /// <summary>
    /// Ob eine gemeldete Gabelstellung ein Tastendruck war.
    ///
    /// <para>Zwei Threads fassen sie an — der Lese-Thread meldet, der
    /// UI-Thread erwartet —, deshalb die Sperre. Vorher lag hier ein blankes
    /// <c>bool</c>, das von beiden Seiten geschrieben wurde.</para>
    /// </summary>
    private readonly HookWatch _hook = new();

    private readonly object _hookLock = new();

    private readonly ILogger _logger;

    /// <summary>
    /// Der Weg, auf dem Ausgangsreports hinausgehen — <b>und der Grund, warum
    /// es nicht <c>HidD_SetOutputReport</c> ist.</b>
    ///
    /// <para>Diese Funktion schickt den Report über die Control-Pipe und
    /// wartet auf die Bestätigung des Geräts. Am Jabra Engage 75 gemessen
    /// (09.09.2026): <b>10,9 s, 13,8 s, 21,3 s und 83,2 s</b> für einen
    /// Report von drei Byte. Die Folge war nicht eine späte Lampe, sondern
    /// eine kaputte Bedienung: das Gerät erfuhr nie rechtzeitig, dass es
    /// klingelt, und deshalb nahm „aus der Ladeschale nehmen" keinen Anruf an
    /// — die Funktion, die Bria an demselben Headset hat.</para>
    ///
    /// <para><c>WriteFile</c> auf dem Gerätehandle nimmt die
    /// Interrupt-Out-Pipe. Das ist der reguläre Weg für Output-Reports, und es
    /// ist auch der, den HIDAPI ging — also das Modul, das Linphone bis 5.4
    /// mitbrachte (ADR-028). Bleibt <c>null</c>, wenn sich kein zweites Handle
    /// öffnen lässt; dann gilt der alte Weg als Rückfall.</para>
    ///
    /// <para><b>Ein eigenes Handle, nicht das des Lesers.</b> Ein
    /// <c>FileStream</c> ist nicht threadsicher, und im Lese-Thread steht ein
    /// blockierender <c>Read</c>.</para>
    /// </summary>
    private readonly FileStream? _writeStream;

    private readonly SafeFileHandle? _writeHandle;

    /// <summary>Der Produktname, wie das Gerät ihn nennt — für das Protokoll.</summary>
    public string Product { get; }

    /// <summary>Der Gerätepfad. Dient als Kennung beim Wiederfinden.</summary>
    public string Path { get; }

    /// <summary>
    /// Ob ein <b>anderes Programm</b> dieses Gerät gerade im Gespräch hält.
    ///
    /// <para>Das Gerät meldet „abgenommen", nipp hat keinen Anruf, und es ist
    /// auch keine eigene Meldung unterwegs, deren Echo das sein könnte. Am
    /// 10.09.2026 im Protokoll belegt: beim Beitritt zu einem Teams-Meeting
    /// meldete das Engage 75 off-hook, ohne dass nipp etwas geschickt
    /// hatte.</para>
    ///
    /// <para><b>Was das nicht sieht</b>, steht bei
    /// <see cref="HeadsetSignalGate"/> — unter anderem ein Gerät, das seinen
    /// Gabelzustand nur als Momentan-Taster meldet, und ein Programm, das das
    /// Gerät nur für Audio benutzt.</para>
    /// </summary>
    public bool Fremdbelegung(bool eigeneAnrufe)
    {
        lock (_hookLock)
        {
            return _hook.Fremdbelegung(eigeneAnrufe, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Die Gabeltaste wurde betätigt.
    ///
    /// <para><b>Ohne Richtung, mit Absicht.</b> Ob das Gerät den Druck als
    /// „abgenommen" oder als „aufgelegt" meldet, hängt an seinem
    /// Report-Deskriptor und sagt nichts darüber, was der Benutzer will — die
    /// Begründung steht bei <see cref="HookWatch"/>. Loslassen und das Echo
    /// einer eigenen Meldung lösen dieses Ereignis nicht aus.</para>
    ///
    /// <para>Wird aus dem Lese-Thread ausgelöst, nicht aus dem UI-Thread.</para>
    /// </summary>
    public event Action? HookPressed;

    /// <summary>Die Stummtaste am Gerät wurde gedrückt.</summary>
    public event Action? MutePressed;

    /// <summary>Das Gerät ist weg — abgezogen, oder der Treiber hat es entfernt.</summary>
    public event Action? Lost;

    /// <summary>
    /// Das Gerät hat einen Zustandsbericht nicht angenommen. Aus dem
    /// Schreib-Thread.
    /// </summary>
    public event Action? WriteFailed;

    private HidTelephonyDevice(
        SafeFileHandle handle,
        IntPtr preparsed,
        HidCaps caps,
        byte outputReportId,
        string path,
        string product,
        int telefoniegeraete,
        ILogger logger)
    {
        _logger = logger;
        _handle = handle;
        _outputReportId = outputReportId;
        _preparsed = preparsed;
        _inputLength = caps.InputReportByteLength;
        _outputLength = caps.OutputReportByteLength;
        _maxUsages = caps.NumberInputDataIndices > 0 ? caps.NumberInputDataIndices : 1;
        Path = path;
        Product = product;

        // Ein eigener Datenstrom auf demselben Handle: FileStream kann
        // synchron lesen, und genau das braucht der Lese-Thread. Die
        // Alternative waere ReadFile per P/Invoke mit eigenem Puffer-Pinning —
        // mehr Code fuer dasselbe Ergebnis.
        _stream = new FileStream(_handle, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);

        _reader = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "nipp HID-Telefonie lesen",
        };

        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "nipp HID-Telefonie schreiben",
        };

        // Der eigene Schreibweg. Scheitert er, bleibt der Rueckfall — dann ist
        // es langsam, aber nicht kaputt.
        var schreibHandle = CreateFile(
            path, GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (!schreibHandle.IsInvalid && _outputLength > 0)
        {
            try
            {
                _writeHandle = schreibHandle;
                _writeStream = new FileStream(
                    schreibHandle, FileAccess.Write, bufferSize: 1, isAsync: false);
            }
            catch (Exception)
            {
                _writeStream = null;
                _writeHandle = null;
                schreibHandle.Dispose();
            }
        }
        else
        {
            schreibHandle.Dispose();
        }

        _reader.Start();
        _writer.Start();

        // <b>Mit der Anzahl.</b> Open nimmt das ERSTE Geraet mit
        // Telefonieseite; an einem Rechner mit drei Headsets ist das keine
        // rhetorische Frage, und im Protokoll stand die Antwort bisher nicht.
        HeadsetLog.DeviceOpened(
            logger,
            product,
            path,
            caps.UsagePage,
            caps.Usage,
            caps.InputReportByteLength,
            caps.OutputReportByteLength,
            outputReportId,
            telefoniegeraete,
            TastenListe(preparsed, caps));
    }

    /// <summary>
    /// Sucht das erste angeschlossene HID-Telefoniegerät und öffnet es.
    /// <c>null</c>, wenn keines da ist.
    /// </summary>
    /// <param name="grund">
    /// Wenn nichts gefunden wurde: warum. Taugt fürs Protokoll.
    /// </param>
    public static HidTelephonyDevice? Open(ILogger logger, out string grund)
    {
        var alle = HidPfade(out var listenFehler);

        if (listenFehler is not null)
        {
            grund = listenFehler;
            return null;
        }

        var telefonie = new List<string>();

        foreach (var pfad in alle)
        {
            if (IstTelefoniePfad(pfad))
            {
                telefonie.Add(pfad);
            }
        }

        foreach (var pfad in telefonie)
        {
            var device = TryOpen(pfad, logger, telefonie.Count);

            if (device is not null)
            {
                grund = string.Empty;
                return device;
            }
        }

        grund = alle.Count == 0
            ? "kein HID-Geraet gefunden"
            : $"keines der {alle.Count} HID-Geraete bietet Telefonie-Tasten an";

        return null;
    }

    /// <summary>
    /// Der Pfad des ersten HID-Telefoniegeräts — <b>ohne es zu öffnen und ohne
    /// einen Report zu schreiben.</b>
    ///
    /// <para><b>Wofür das gut ist.</b> Bis zum 10.09.2026 hat jeder gemeldete
    /// Audiogerätewechsel das Headset abgelegt und neu angebunden, und jedes
    /// Anbinden endete in einem Ausgangsreport. Ein Report an ein Gerät, das
    /// nipp mit anderen Programmen teilt, ist nie folgenlos — also wird zuerst
    /// gefragt, ob es überhaupt ein anderes Gerät ist. Das prüfende
    /// <c>CreateFile</c> mit <c>access=0</c> kostet Millisekunden.</para>
    /// </summary>
    public static string? ErsterTelefoniePfad()
    {
        foreach (var pfad in HidPfade(out _))
        {
            if (IstTelefoniePfad(pfad))
            {
                return pfad;
            }
        }

        return null;
    }

    /// <summary>Die Pfade aller angeschlossenen HID-Geräte.</summary>
    private static List<string> HidPfade(out string? fehler)
    {
        var pfade = new List<string>();

        HidD_GetHidGuid(out var hidGuid);

        var set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DeviceInfoFlags);

        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            fehler = "die Geraeteliste liess sich nicht oeffnen";
            return pfade;
        }

        fehler = null;

        try
        {
            for (var index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData();
                interfaceData.Size = System.Runtime.InteropServices.Marshal.SizeOf(interfaceData);

                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    break;
                }

                var detail = new DeviceInterfaceDetail
                {
                    // Nicht sizeof(struct): das Feld erwartet die Groesse des
                    // Kopfes, nicht die des Puffers — 8 auf 64 Bit, 6 auf 32.
                    // Ein falscher Wert laesst den Aufruf mit
                    // ERROR_INVALID_USER_BUFFER scheitern.
                    Size = IntPtr.Size == 8 ? 8 : 6,
                    DevicePath = string.Empty,
                };

                if (!SetupDiGetDeviceInterfaceDetail(
                        set, ref interfaceData, ref detail, 1048, IntPtr.Zero, IntPtr.Zero))
                {
                    continue;
                }

                pfade.Add(detail.DevicePath);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return pfade;
    }

    /// <summary>
    /// Ob unter diesem Pfad ein Telefoniegerät steckt. Nur prüfend geöffnet,
    /// mit <c>access=0</c> — das bekommt man auch für Geräte, die Windows
    /// exklusiv führt.
    /// </summary>
    private static bool IstTelefoniePfad(string path)
    {
        try
        {
            using var pruefen = CreateFile(
                path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

            return !pruefen.IsInvalid && IstTelefonie(pruefen);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static HidTelephonyDevice? TryOpen(string path, ILogger logger, int telefoniegeraete)
    {
        // <b>Zweimal oeffnen, mit Absicht.</b> Erst nur pruefend, ohne
        // Zugriffsrechte: ein Handle mit access=0 bekommt man auch fuer
        // Geraete, die Windows exklusiv fuehrt (Tastaturen, Maeuse), und die
        // Report-Beschreibung genuegt zur Entscheidung. Erst wenn es wirklich
        // ein Telefoniegeraet ist, wird lesend und schreibend geoeffnet.
        using (var pruefen = CreateFile(
                   path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
        {
            if (pruefen.IsInvalid || !IstTelefonie(pruefen))
            {
                return null;
            }
        }

        var handle = CreateFile(
            path,
            GenericRead | GenericWrite,
            FileShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            // Kommt vor: manche Geraete lassen sich pruefen, aber nicht
            // schreiben. Ohne Schreibrecht gaebe es keine Lampen und damit
            // keinen verlaesslichen Zustand — dann lieber ueberspringen.
            handle.Dispose();
            return null;
        }

        if (!HidD_GetPreparsedData(handle, out var preparsed))
        {
            handle.Dispose();
            return null;
        }

        if (HidP_GetCaps(preparsed, out var caps) != HidP_StatusSuccess
            || caps.InputReportByteLength == 0)
        {
            HidD_FreePreparsedData(preparsed);
            handle.Dispose();
            return null;
        }

        try
        {
            return new HidTelephonyDevice(
                handle,
                preparsed,
                caps,
                LampenReportId(preparsed, caps),
                path,
                Produktname(handle),
                telefoniegeraete,
                logger);
        }
        catch (Exception)
        {
            // Der Konstruktor legt einen FileStream an, und der kann
            // scheitern. Ohne dieses Netz blieben ein offenes Handle und der
            // vorverarbeitete Deskriptor liegen — ein Leck, das erst beim
            // Abziehen des Geraets auffaellt.
            HidD_FreePreparsedData(preparsed);
            handle.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Liest ab, unter welcher Report-Kennung das Gerät seine Lampen führt.
    ///
    /// <para>Genommen wird die Kennung der ersten Lampe auf der LED-Seite.
    /// Findet sich keine, bleibt es bei 0 — dann hat das Gerät keine Lampen,
    /// und der Report geht ins Leere, was folgenlos ist.</para>
    /// </summary>
    private static byte LampenReportId(IntPtr preparsed, HidCaps caps)
    {
        if (caps.NumberOutputButtonCaps == 0)
        {
            return 0;
        }

        var anzahl = caps.NumberOutputButtonCaps;
        var liste = new HidButtonCaps[anzahl];

        if (HidP_GetButtonCaps(HidP_Output, liste, ref anzahl, preparsed) != HidP_StatusSuccess)
        {
            return 0;
        }

        for (var i = 0; i < anzahl; i++)
        {
            if (liste[i].UsagePage == LedPage)
            {
                return liste[i].ReportID;
            }
        }

        return 0;
    }

    private static bool IstTelefonie(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsed))
        {
            return false;
        }

        try
        {
            if (HidP_GetCaps(preparsed, out var caps) != HidP_StatusSuccess)
            {
                return false;
            }

            return caps.UsagePage == TelephonyPage
                && Array.IndexOf(TelephonyUsages, caps.Usage) >= 0;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static string Produktname(SafeFileHandle handle)
    {
        var buffer = new byte[254];

        if (!HidD_GetProductString(handle, buffer, buffer.Length))
        {
            return "unbekannt";
        }

        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    /// <summary>
    /// Meldet dem Gerät, was gerade gilt: läuft ein Gespräch, klingelt es,
    /// ist stummgeschaltet.
    ///
    /// <para>Immer alle drei zusammen. Ein Report enthält alle Lampen; ihn
    /// jedes Mal frisch zu bauen ist einfacher und weniger fehleranfällig, als
    /// einzelne Bits zu löschen.</para>
    /// </summary>
    public void SetState(bool imGespraech, bool klingelt, bool stumm)
    {
        if (_closing)
        {
            return;
        }

        var bits = (imGespraech ? BitInCall : 0)
            | (klingelt ? BitRinging : 0)
            | (stumm ? BitMuted : 0);

        if (Interlocked.Exchange(ref _wanted, bits) == bits)
        {
            return;
        }

        try
        {
            // Der Zaehler ist auf eins begrenzt: mehr als „es gibt etwas zu
            // schreiben" muss das Signal nicht sagen, weil der Thread ohnehin
            // den neuesten Zustand nimmt. Ein voller Zaehler ist deshalb kein
            // Fehler, sondern die Auskunft, dass schon geweckt wurde.
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void WriteLoop()
    {
        while (!_closing)
        {
            try
            {
                _wake.Wait();
            }
            catch (Exception)
            {
                break;
            }

            if (_closing)
            {
                break;
            }

            // <b>Kurz sammeln, dann einmal schreiben.</b> Ein Anruf
            // durchlaeuft mehrere Zustaende in Millisekunden — bei einem
            // Selbstanruf am 09.09.2026 gingen zwei Reports 1 ms auseinander
            // hinaus, und der erste war schon ueberholt, als er ankam. Das
            // waere gleichgueltig, wenn ein Report billig waere; am Jabra
            // Engage 75 dauerte einer <b>2,94 Sekunden</b>, und solange
            // laeutet das Headset weiter. Es zaehlt ohnehin nur der neueste
            // Zustand.
            if (!_closing)
            {
                Thread.Sleep(Sammelfenster);
            }

            if (_closing)
            {
                break;
            }

            var bits = _wanted;

            var imGespraech = (bits & BitInCall) != 0;
            var klingelt = (bits & BitRinging) != 0;
            var stumm = (bits & BitMuted) != 0;

            var begonnen = DateTimeOffset.UtcNow;

            lock (_hookLock)
            {
                _hook.SchreibenBeginnt(begonnen);
            }

            bool ok;

            try
            {
                ok = Schreibe(imGespraech, klingelt, stumm);
            }
            finally
            {
                lock (_hookLock)
                {
                    _hook.SchreibenFertig(DateTimeOffset.UtcNow);
                }
            }

            // <b>Mit der Dauer.</b> Auch der Erfolg gehoert ins Protokoll —
            // ein Weg, dessen Erfolg niemand prueft, faellt genau dann aus,
            // wenn er gebraucht wird. Und ohne die Dauer war „das Headset
            // laeutet weiter" nicht zu erklaeren: der Report war unterwegs,
            // nur eben drei Sekunden lang.
            HeadsetLog.StateWritten(
                _logger,
                _outputReportId,
                imGespraech,
                klingelt,
                stumm,
                ok,
                (int)(DateTimeOffset.UtcNow - begonnen).TotalMilliseconds);

            if (!ok)
            {
                WriteFailed?.Invoke();
            }
        }
    }

    /// <summary>
    /// Baut den Report und schickt ihn ans Gerät. Läuft nur auf dem
    /// Schreib-Thread.
    /// </summary>
    private bool Schreibe(bool imGespraech, bool klingelt, bool stumm)
    {
        if (_closing || _outputLength == 0)
        {
            return false;
        }

        var report = new byte[_outputLength];

        // Die Kennung steht im ersten Byte des Reports, und
        // InitializeReportForID setzt sie mit. Scheitert der Aufruf, wird sie
        // selbst gesetzt — ein Report mit falscher Kennung wird vom Geraet
        // stillschweigend verworfen.
        if (HidP_InitializeReportForID(
                HidP_Output, _outputReportId, _preparsed, report, report.Length)
            != HidP_StatusSuccess)
        {
            Array.Clear(report);
            report[0] = _outputReportId;
        }

        var lampen = new List<ushort>(3);

        if (imGespraech)
        {
            lampen.Add(UsageLedOffHook);
        }

        if (klingelt)
        {
            lampen.Add(UsageLedRing);
        }

        if (stumm)
        {
            lampen.Add(UsageLedMute);
        }

        if (lampen.Count > 0)
        {
            var liste = lampen.ToArray();
            var anzahl = liste.Length;

            // Nicht abbrechen, wenn ein Geraet eine der Lampen nicht kennt:
            // Off-Hook allein ist wichtiger als alle drei. HidP_SetUsages
            // meldet dann USAGE_NOT_FOUND und setzt keines — deshalb im
            // Fehlerfall einzeln, damit die anderen trotzdem gelten.
            if (HidP_SetUsages(
                    HidP_Output, LedPage, 0, liste, ref anzahl,
                    _preparsed, report, report.Length) != HidP_StatusSuccess)
            {
                var gesetzt = 0;

                foreach (var lampe in liste)
                {
                    var einzeln = new[] { lampe };
                    var eins = 1;

                    if (HidP_SetUsages(
                            HidP_Output, LedPage, 0, einzeln, ref eins,
                            _preparsed, report, report.Length) == HidP_StatusSuccess)
                    {
                        gesetzt++;
                    }
                }

                if (gesetzt == 0)
                {
                    // Keine der Lampen liess sich setzen. Der Report ginge
                    // dann als leerer hinaus und loeschte, was leuchtet — das
                    // ist schlimmer als nichts zu tun.
                    return false;
                }
            }
        }

        // <b>Der Interrupt-Weg zuerst.</b> Warum nicht
        // HidD_SetOutputReport: siehe <see cref="_writeStream"/> — dort steht
        // die Messung, die es ausgeschlossen hat.
        if (_writeStream is { } strom)
        {
            try
            {
                strom.Write(report, 0, report.Length);
                strom.Flush();

                return true;
            }
            catch (Exception ex)
            {
                // Ein Geraet ohne Interrupt-Out-Endpunkt lehnt das sofort ab
                // (keine Wartezeit). Dann bleibt der alte Weg.
                HeadsetLog.WriteFellBack(_logger, ex.GetType().Name);
            }
        }

        try
        {
            return HidD_SetOutputReport(_handle, report, report.Length);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ReadLoop()
    {
        var puffer = new byte[_inputLength];
        var usages = new ushort[_maxUsages];

        while (!_closing)
        {
            int gelesen;

            try
            {
                gelesen = _stream.Read(puffer, 0, puffer.Length);
            }
            catch (Exception ex)
            {
                // Abgezogen, oder wir schliessen gerade. Beides endet hier.
                //
                // Der Grund stand bis zum 13.09.2026 nirgends (ADR-053): «die
                // Taste tut nichts» war damit nicht von «das Geraet ist weg»
                // zu unterscheiden — dieselbe Luecke, die dieses Projekt mit
                // den HID-Reports schon einmal zwei Tage gekostet hat.
                if (!_closing)
                {
                    HeadsetLog.ReadFailed(_logger, ex.GetType().Name, ex.Message);
                }

                break;
            }

            if (gelesen <= 0)
            {
                break;
            }

            if (_closing)
            {
                break;
            }

            // Deute() stand bis zum 13.09.2026 AUSSERHALB des try (ADR-053).
            // Eine Ausnahme hier — ein Report mit mehr Usages als erwartet,
            // ein Geraet, das sich anders meldet als die drei bekannten — war
            // eine unbehandelte Thread-Ausnahme und damit das Ende des
            // Prozesses. nipp laeuft an drei Headsets, geprueft ist eines.
            try
            {
                Deute(puffer, gelesen, usages);
            }
            catch (Exception ex)
            {
                HeadsetLog.ReportNotUnderstood(_logger, ex.GetType().Name, ex.Message);
            }
        }

        if (!_closing)
        {
            Lost?.Invoke();
        }
    }

    private void Deute(byte[] report, int length, ushort[] usages)
    {
        var anzahl = usages.Length;

        var status = HidP_GetUsages(
            HidP_Input, TelephonyPage, 0, usages, ref anzahl, _preparsed, report, length);

        if (status != HidP_StatusSuccess)
        {
            // Ein Report, der keine Telefonie-Usages traegt — etwa der einer
            // anderen Report-Kennung desselben Geraets. Nichts zu tun.
            HeadsetLog.ReportIgnored(_logger, report.Length > 0 ? report[0] : (byte)0, length, status);
            return;
        }

        var offHook = false;
        var mute = false;

        for (var i = 0; i < anzahl; i++)
        {
            switch (usages[i])
            {
                case UsageHookSwitch:
                    offHook = true;
                    break;

                case UsagePhoneMute:
                    mute = true;
                    break;
            }
        }

        // <b>Was das Geraet wirklich geschickt hat.</b> Ohne diese Zeile war
        // „die Taste tut nichts" nicht von „hier kommt gar nichts an" zu
        // unterscheiden — und genau daran hat die Suche nach dem Befund vom
        // 09.09.2026 gehangen, weil ueber HID-Reports nie eine Zeile im
        // Protokoll stand.
        HeadsetLog.ReportRead(
            _logger,
            report.Length > 0 ? report[0] : (byte)0,
            length,
            UsageListe(usages, anzahl),
            offHook);

        if (mute)
        {
            MutePressed?.Invoke();
        }

        HookVerdict verdict;

        lock (_hookLock)
        {
            verdict = _hook.Melde(offHook, DateTimeOffset.UtcNow);
        }

        if (verdict != HookVerdict.Betaetigung)
        {
            // Loslassen, Prellen oder das Echo einer eigenen Meldung. Auch das
            // gehoert ins Protokoll: eine verworfene Meldung ist der Beleg,
            // dass gelesen wurde, und der Unterschied zur Stille.
            HeadsetLog.HookDiscarded(_logger, offHook, verdict.ToString());
            return;
        }

        HookPressed?.Invoke();
    }

    /// <summary>
    /// Listet die gemeldeten Usages als Hex — genau die Angabe, mit der sich
    /// entscheiden lässt, ob ein Gerät seine Gabeltaste überhaupt auf
    /// <c>HookSwitch</c> (0x20) legt.
    /// </summary>
    private static string UsageListe(ushort[] usages, int anzahl)
    {
        if (anzahl <= 0)
        {
            return "keine";
        }

        var teile = new string[anzahl];

        for (var i = 0; i < anzahl; i++)
        {
            teile[i] = "0x" + usages[i].ToString("X2", CultureInfo.InvariantCulture);
        }

        return string.Join(" ", teile);
    }

    /// <summary>
    /// Listet, welche Telefonie-Tasten das Gerät überhaupt führt.
    ///
    /// <para><b>Die Antwort auf die Frage, die zuerst zu stellen ist.</b> Fehlt
    /// <c>0x20</c> in dieser Liste, legt das Gerät seine Gabeltaste woanders
    /// hin, und kein Nachziehen von Zuständen wird daran etwas ändern. Und
    /// weil <see cref="Open"/> das <b>erste</b> Gerät mit Telefonieseite nimmt,
    /// steht hier auch, ob nipp am richtigen hängt: an einem Rechner mit drei
    /// Headsets ist das keine rhetorische Frage.</para>
    /// </summary>
    private static string TastenListe(IntPtr preparsed, HidCaps caps)
    {
        if (caps.NumberInputButtonCaps == 0)
        {
            return "keine";
        }

        var anzahl = caps.NumberInputButtonCaps;
        var liste = new HidButtonCaps[anzahl];

        if (HidP_GetButtonCaps(HidP_Input, liste, ref anzahl, preparsed) != HidP_StatusSuccess)
        {
            return "nicht lesbar";
        }

        var teile = new List<string>(anzahl);

        for (var i = 0; i < anzahl; i++)
        {
            var cap = liste[i];

            teile.Add(cap.IsRange != 0
                ? $"Bericht {cap.ReportID}: Seite 0x{cap.UsagePage:X2} "
                    + $"0x{cap.UsageMin:X2}-0x{cap.UsageMax:X2}"
                : $"Bericht {cap.ReportID}: Seite 0x{cap.UsagePage:X2} 0x{cap.UsageMin:X2}");
        }

        return string.Join(", ", teile);
    }

    public void Dispose()
    {
        _closing = true;

        // Erst den stehenden Lesevorgang abbrechen, dann das Handle aufgeben.
        try
        {
            CancelIoEx(_handle, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Ein Geraet, das schon weg ist, braucht keinen Abbruch.
        }

        // <b>Mit Frist, nicht unbegrenzt.</b> Der Lesestrom ist synchron
        // geoeffnet (isAsync: false), und sein Dispose wartet auf den
        // stehenden Read. Greift CancelIoEx nicht — am 10.09.2026 gemessen,
        // weil das Geraet nicht antwortete —, dauert genau diese Zeile
        // Sekunden: `Beenden: HeadsetCallControl brauchte 6428 ms` steht so im
        // Protokoll.
        //
        // Das Handle aufzugeben ist beim Beenden kein Selbstzweck: der
        // Lesethread ist ein Hintergrundthread, und Windows raeumt beides beim
        // Prozessende ohnehin ab. Wer hier wartet, laesst den Benutzer warten.
        try
        {
            var geschlossen = Task.Run(_stream.Dispose);

            if (!geschlossen.Wait(TimeSpan.FromMilliseconds(500)))
            {
                HeadsetLog.CloseSlow(_logger);
            }
        }
        catch (Exception)
        {
            // Dasselbe: der Strom kann bereits tot sein.
        }

        try
        {
            _writeStream?.Dispose();
            _writeHandle?.Dispose();
        }
        catch (Exception)
        {
            // Und noch einmal dasselbe fuer den Schreibweg.
        }

        // Hoechstens kurz warten. Der Thread ist ein Hintergrundthread und
        // haelt den Prozess nicht auf; ein unbegrenztes Join beim Beenden
        // waere ein Aufhaenger fuer den Fall, dass der Abbruch nicht greift.
        try
        {
            _wake.Release();
        }
        catch (Exception)
        {
        }

        _reader.Join(TimeSpan.FromMilliseconds(500));
        _writer.Join(TimeSpan.FromMilliseconds(500));
        _wake.Dispose();

        if (_preparsed != IntPtr.Zero)
        {
            HidD_FreePreparsedData(_preparsed);
        }

        // <b>Kein eigenes Dispose auf dem Handle.</b> Der FileStream hat es
        // uebernommen und beim eigenen Dispose geschlossen. Ein zweiter Aufruf
        // waere zwar folgenlos, aber er verschleiert, wem das Handle gehoert —
        // und solange der Strom lebt, ist genau das die Antwort darauf, warum
        // HidD_SetOutputReport auf _handle erlaubt ist.
    }
}
