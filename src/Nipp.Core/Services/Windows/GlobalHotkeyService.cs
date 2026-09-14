using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Windows;

/// <summary>
/// Der systemweite Tastenkürzel (§10, AP7.6). Standard <c>Strg+Umschalt+A</c>.
///
/// <b>Warum ein eigener Thread mit eigenem Fenster.</b> <c>RegisterHotKey</c>
/// schickt <c>WM_HOTKEY</c> entweder an ein Fenster oder in die
/// Nachrichtenschlange des aufrufenden Threads. Der zweite Weg fällt aus: die
/// Nachrichtenschleife von WinUI verwirft eine Nachricht ohne Fenster
/// stillschweigend, das Kürzel wäre registriert und täte trotzdem nichts. Also
/// ein reines Nachrichtenfenster (<c>HWND_MESSAGE</c>) mit eigener Schleife —
/// unsichtbar, ohne Taskleisteneintrag, und es blockiert nichts.
///
/// Die Rückmeldung kommt <b>nicht</b> auf dem UI-Thread. Wer <see cref="Pressed"/>
/// abonniert, muss selbst zurück in den Dispatcher (§6: alle SDK-Aufrufe
/// gehören dorthin).
/// </summary>
/// <summary>
/// Wofür ein systemweites Kürzel steht (C7).
///
/// <para><b>Zwei Rollen, zwei Registrierungen, zwei Bedeutungen — und jede
/// Bedeutung steht genau einmal im Code.</b> §22.5 sah einen Hotkey vor;
/// «stumm schalten» kam dazu, weil es im Alltag der häufigste Griff ist und
/// nipp im Infobereich lebt: ohne systemweites Kürzel heisst stummschalten
/// «Fenster suchen, nach vorn holen, hinsehen, klicken».</para>
/// </summary>
public enum HotkeyRole
{
    /// <summary>§9.6: annehmen, sonst auflegen, sonst nipp nach vorn.</summary>
    AnnehmenAuflegen,

    /// <summary>Das laufende Gespräch stumm schalten und wieder zurück.</summary>
    Stumm,
}

public sealed class GlobalHotkeyService : IDisposable
{
    /// <summary>
    /// Die Kennung je Rolle. Windows verlangt sie je Fenster eindeutig; eine
    /// zweite Registrierung auf derselben Kennung ersetzt die erste
    /// stillschweigend.
    /// </summary>
    private static int IdOf(HotkeyRole rolle) => 0xB1 + (int)rolle;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>
    /// Verhindert, dass ein gehaltenes Kürzel Wiederholungen auslöst. Ohne das
    /// öffnet und schliesst ein liegengebliebener Finger das Fenster im
    /// Tastaturwiederholtakt.
    /// </summary>
    private const uint ModNoRepeat = 0x4000;

    private const uint WmHotkey = 0x0312;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;

    private static readonly nint HwndMessage = -3;

    private readonly ILogger<GlobalHotkeyService> _logger;

    private Thread? _thread;
    private nint _windowHandle;
    private WndProc? _windowProcedure;
    /// <summary>Das wirksame Kürzel je Rolle — <c>null</c> heisst «keines».</summary>
    private readonly Dictionary<HotkeyRole, string> _registered = [];
    private bool _disposed;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger) => _logger = logger;

    /// <summary>
    /// Ein Kürzel wurde gedrückt. Kommt auf dem Hotkey-Thread, und es sagt
    /// <b>welches</b> — die Bedeutung entscheidet der Empfänger.
    /// </summary>
    public event EventHandler<HotkeyRole>? Pressed;

    /// <summary>Das gerade wirksame Kürzel dieser Rolle, oder <c>null</c>.</summary>
    /// <summary>
    /// Ob der Dienst überhaupt arbeitet (W1.7, Befund B20).
    ///
    /// <para><b>Der Befund.</b> Stirbt die Nachrichtenschleife — ein
    /// fehlgeschlagenes <c>RegisterClassEx</c>, ein
    /// <c>CreateWindowEx</c> ohne Handle, eine Ausnahme —, wirkt <b>kein</b>
    /// systemweites Kürzel mehr. Es gab weder einen Neustart noch eine
    /// Meldung: die Einstellungsseite zeigte weiterhin «ist aktiv», weil sie
    /// nur den Belegungskonflikt kannte. Der Benutzer sah ein Feld, das seine
    /// Eingabe annimmt, und ein Kürzel, das nichts tut.</para>
    ///
    /// <para>Ein Neustart des Threads steht bewusst nicht hier: scheitert
    /// <c>RegisterClassEx</c>, scheitert es beim zweiten Mal genauso. Was
    /// fehlte, war die Antwort — nicht ein Wiederholungsversuch.</para>
    /// </summary>
    public bool IstBereit => _windowHandle != 0;

    public string? ActiveHotkey(HotkeyRole rolle) =>
        _registered.TryGetValue(rolle, out var wert) ? wert : null;

    /// <summary>
    /// Meldet ein Kürzel an und ersetzt ein bestehendes. Ein leerer Text
    /// schaltet die Funktion ab.
    /// </summary>
    /// <returns>
    /// <c>true</c>, wenn das Kürzel jetzt wirkt. <c>false</c>, wenn es
    /// unlesbar war oder Windows es einer anderen Anwendung zugeteilt hat —
    /// AP7.6 verlangt, dass dieser Fall gemeldet und nicht verschluckt wird.
    /// </returns>
    public bool Apply(HotkeyRole rolle, string? hotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bisher = ActiveHotkey(rolle);

        if (string.Equals(hotkey, bisher, StringComparison.OrdinalIgnoreCase))
        {
            return bisher is not null;
        }

        EnsureWindow();

        if (_windowHandle == 0)
        {
            return false;
        }

        var id = IdOf(rolle);

        UnregisterHotKey(_windowHandle, id);
        _registered.Remove(rolle);

        // Der Name der Rolle in derselben Sprache wie die Oberflaeche: das
        // Protokoll landet beim Support, und «AnnehmenAuflegen» waere dort ein
        // Bezeichner statt einer Auskunft.
        var was = Bezeichnung(rolle);

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            WindowsIntegrationLog.HotkeyDisabled(_logger, was);
            return false;
        }

        if (!TryParse(hotkey, out var modifiers, out var key))
        {
            WindowsIntegrationLog.HotkeyUnreadable(_logger, hotkey, was);
            return false;
        }

        if (!RegisterHotKey(_windowHandle, id, modifiers | ModNoRepeat, key))
        {
            // Fast immer: eine andere Anwendung war schneller. Der Fehlercode
            // gehört in die Meldung, sonst rät der Benutzer.
            var error = Marshal.GetLastWin32Error();
            WindowsIntegrationLog.HotkeyTaken(_logger, hotkey, was, error);
            return false;
        }

        _registered[rolle] = hotkey;
        WindowsIntegrationLog.HotkeyRegistered(_logger, hotkey, was);

        return true;
    }

    /// <summary>
    /// Wie eine Rolle im Protokoll heisst — in der Sprache der Oberfläche.
    /// </summary>
    private static string Bezeichnung(HotkeyRole rolle) => rolle switch
    {
        HotkeyRole.Stumm => "stumm schalten",
        _ => "annehmen und auflegen",
    };

    /// <summary>
    /// Zerlegt <c>Strg+Umschalt+A</c> oder <c>Ctrl+Shift+A</c> in Modifikatoren
    /// und Tastencode.
    ///
    /// Beide Schreibweisen, weil die Einstellungsdatei englische Namen
    /// enthält (§9.6 nennt <c>Ctrl+Shift+A</c>), die Oberfläche aber deutsche
    /// zeigt — und niemand soll raten müssen, welche gilt.
    /// </summary>
    public static bool TryParse(string? hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "STRG":
                case "CONTROL":
                    modifiers |= ModControl;
                    break;

                case "SHIFT":
                case "UMSCHALT":
                    modifiers |= ModShift;
                    break;

                case "ALT":
                    modifiers |= ModAlt;
                    break;

                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    break;

                default:
                    if (!TryParseKey(part, out virtualKey))
                    {
                        return false;
                    }

                    break;
            }
        }

        // Ein Kürzel ohne Modifikator würde jede Eingabe im ganzen System
        // abfangen — das wäre kein Kürzel, sondern ein Tastaturdefekt.
        return virtualKey != 0 && modifiers != 0;
    }

    private static bool TryParseKey(string part, out uint virtualKey)
    {
        virtualKey = 0;

        if (part.Length == 1)
        {
            var c = char.ToUpperInvariant(part[0]);

            if (char.IsAsciiLetterOrDigit(c))
            {
                // Bei Buchstaben und Ziffern entspricht der Tastencode dem
                // ASCII-Wert des Grossbuchstabens.
                virtualKey = c;
                return true;
            }

            return false;
        }

        // F1 bis F24
        if (part.Length is 2 or 3
            && (part[0] is 'F' or 'f')
            && int.TryParse(part[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + number - 1);
            return true;
        }

        return part.ToUpperInvariant() switch
        {
            "SPACE" or "LEERTASTE" => Assign(0x20, out virtualKey),
            "ENTER" or "RETURN" or "EINGABE" => Assign(0x0D, out virtualKey),
            "INSERT" or "EINFG" => Assign(0x2D, out virtualKey),
            "DELETE" or "ENTF" => Assign(0x2E, out virtualKey),
            "HOME" or "POS1" => Assign(0x24, out virtualKey),
            "END" or "ENDE" => Assign(0x23, out virtualKey),
            _ => false,
        };

        static bool Assign(uint value, out uint target)
        {
            target = value;
            return true;
        }
    }

    /// <summary>
    /// Legt das Nachrichtenfenster an, falls es noch keines gibt, und wartet,
    /// bis es steht — <see cref="Apply"/> braucht sein Handle sofort.
    /// </summary>
    private void EnsureWindow()
    {
        if (_windowHandle != 0)
        {
            return;
        }

        using var ready = new ManualResetEventSlim(false);

        _thread = new Thread(() => RunMessageLoop(ready))
        {
            IsBackground = true,
            Name = "nipp-hotkey",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            WindowsIntegrationLog.HotkeyWindowFailed(_logger, "Zeitüberschreitung beim Anlegen des Nachrichtenfensters");
        }
    }

    private void RunMessageLoop(ManualResetEventSlim ready)
    {
        try
        {
            // Die Delegate-Instanz muss ein Feld sein: Windows hält nur einen
            // Funktionszeiger, und ein eingesammelter Delegat stürzt beim
            // ersten Tastendruck ab.
            _windowProcedure = HandleMessage;

            var className = "nipp-hotkey-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

            var windowClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
                hInstance = GetModuleHandle(null),
                lpszClassName = className,
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                WindowsIntegrationLog.HotkeyWindowFailed(_logger, $"RegisterClassEx: {Marshal.GetLastWin32Error()}");
                return;
            }

            _windowHandle = CreateWindowEx(
                0, className, className, 0, 0, 0, 0, 0, HwndMessage, 0, GetModuleHandle(null), 0);

            if (_windowHandle == 0)
            {
                WindowsIntegrationLog.HotkeyWindowFailed(_logger, $"CreateWindowEx: {Marshal.GetLastWin32Error()}");
                return;
            }

            ready.Set();

            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            WindowsIntegrationLog.HotkeyWindowFailed(_logger, ex.Message);
        }
        finally
        {
            ready.Set();
        }
    }

    private nint HandleMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            // Welche Rolle gedrueckt wurde, steht in der Kennung. Der
            // Empfaenger entscheidet, was sie bedeutet — hier wird nur
            // weitergereicht.
            case WmHotkey when wParam == IdOf(HotkeyRole.AnnehmenAuflegen):
                Pressed?.Invoke(this, HotkeyRole.AnnehmenAuflegen);
                return 0;

            case WmHotkey when wParam == IdOf(HotkeyRole.Stumm):
                Pressed?.Invoke(this, HotkeyRole.Stumm);
                return 0;

            case WmDestroy:
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_windowHandle != 0)
        {
            foreach (var rolle in Enum.GetValues<HotkeyRole>())
            {
                UnregisterHotKey(_windowHandle, IdOf(rolle));
            }

            // Das Fenster beendet seine eigene Schleife über WM_DESTROY —
            // von aussen zerstören dürfte nur der Thread, dem es gehört.
            PostMessage(_windowHandle, WmClose, 0, 0);
            _windowHandle = 0;
        }

        // Der Delegat darf erst weg, wenn der Thread wirklich zurueck ist.
        //
        // Kommt er nicht in zwei Sekunden, lebt sein Fenster noch — und dessen
        // Fensterprozedur ist dieser Delegat. Ihn dann freizugeben heisst, dem
        // Sammler eine Funktion zu ueberlassen, die Windows noch aufrufen kann;
        // das Ergebnis waere ein Absturz an einer Stelle, die mit
        // Tastenkuerzeln nichts zu tun hat. Ein Delegat, der bis zum
        // Prozessende liegen bleibt, ist der harmlosere Ausgang.
        if (_thread?.Join(TimeSpan.FromSeconds(2)) is not false)
        {
            _thread = null;
            _windowProcedure = null;
        }
        else
        {
            WindowsIntegrationLog.HotkeyThreadStillRunning(_logger);
        }
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    // Klassisches DllImport statt LibraryImport: der Quellgenerator verlangt
    // AllowUnsafeBlocks fuer das ganze Projekt und kann WNDCLASSEX mit seinen
    // Zeichenkettenfeldern ohnehin nicht marshallen (SYSLIB1051). Unsicheren
    // Code projektweit freizuschalten, damit ein Tastenkuerzel funktioniert,
    // waere ein schlechter Tausch — deshalb hier die Ausnahme mit Begruendung.
#pragma warning disable SYSLIB1054

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Msg message, nint hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

#pragma warning restore SYSLIB1054
}
