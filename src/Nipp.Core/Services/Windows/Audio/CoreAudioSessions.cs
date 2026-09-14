using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Windows.Audio;

/// <summary>
/// Die echte Quelle: die aktiven Audio-Sitzungen der Standardgeräte, über
/// Core Audio (ADR-068).
///
/// <para><b>Gefragt werden beide Richtungen</b>, Wiedergabe und Aufnahme. Ein
/// Programm, das nur zuhört, benutzt das Gerät genauso — und am 14.09.2026
/// stand das Meeting in beiden Listen.</para>
///
/// <para><b>Alles gekapselt.</b> Scheitert etwas, ist die Antwort «niemand»,
/// und im Protokoll steht eine Zeile. Ein Fehler an der Audio-Schnittstelle
/// darf kein Headset ohne Lampen bedeuten (siehe
/// <see cref="AudioSessionWatch.Fremdbelegt"/>).</para>
///
/// <para><b>Die Kosten sind gemessen: 3,2 bis 10,8 ms</b> für beide Geräte
/// zusammen. Das gehört nicht in den 20-ms-Takt (§14.1) — gerufen wird es
/// beim Wechsel eines Anrufzustands, also ein paar Mal je Gespräch.</para>
/// </summary>
public sealed class CoreAudioSessions : IAudioSessionSource
{
    private readonly ILogger _logger;

    public CoreAudioSessions(ILogger logger) => _logger = logger;

    public IReadOnlyList<uint> AktiveSitzungen()
    {
        var alle = new List<uint>();

        try
        {
            Sammle(alle, Datenfluss.Render);
            Sammle(alle, Datenfluss.Capture);
        }
        catch (Exception ex)
        {
            AudioLog.SessionsUnavailable(_logger, ex.GetType().Name);
            return [];
        }

        return alle;
    }

    private enum Datenfluss
    {
        Render = 0,
        Capture = 1,
    }

    private static void Sammle(List<uint> ziel, Datenfluss fluss)
    {
        // Ueber die CLSID und nicht ueber eine ComImport-Klasse: eine solche
        // Klasse laesst sich ohne CoClass-Attribut nicht auf ihr Interface
        // casten.
        var typ = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));

        if (typ is null || Activator.CreateInstance(typ) is not IMMDeviceEnumerator enumerator)
        {
            return;
        }

        if (enumerator.GetDefaultAudioEndpoint((int)fluss, 0, out var geraet) != 0 || geraet is null)
        {
            return;
        }

        var iid = typeof(IAudioSessionManager2).GUID;

        if (geraet.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var roh) != 0
            || roh is not IAudioSessionManager2 manager)
        {
            return;
        }

        if (manager.GetSessionEnumerator(out var liste) != 0 || liste is null)
        {
            return;
        }

        liste.GetCount(out var anzahl);

        for (var i = 0; i < anzahl; i++)
        {
            if (liste.GetSession(i, out var sitzung) != 0 || sitzung is null)
            {
                continue;
            }

            sitzung.GetState(out var zustand);

            // Nur die aktiven (1). Eine inaktive oder abgelaufene Sitzung sagt
            // nichts darueber, ob jemand das Geraet gerade benutzt.
            if (zustand != 1)
            {
                continue;
            }

            if (sitzung is IAudioSessionControl2 zwei && zwei.GetProcessId(out var pid) == 0)
            {
                ziel.Add(pid);
            }
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();

        int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int NotImpl1();

        int NotImpl2();

        int GetSessionEnumerator([MarshalAs(UnmanagedType.Interface)] out IAudioSessionEnumerator? enumerator);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);

        int GetSession(int index, [MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl? session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out int state);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // <b>Die Reihenfolge ist die vtable, nicht Geschmack.</b>
        // IAudioSessionControl hat NEUN Methoden — GetState, GetDisplayName,
        // SetDisplayName, GetIconPath, SetIconPath, GetGroupingParam,
        // SetGroupingParam, Register- und UnregisterAudioSessionNotification —
        // und erst danach kommen die drei von IAudioSessionControl2. Wer hier
        // eine Zeile zu wenig schreibt, ruft die falsche Stelle auf und liest
        // Müll oder stürzt ab.
        int GetState(out int state);

        int NotImpl2();

        int NotImpl3();

        int NotImpl4();

        int NotImpl5();

        int NotImpl6();

        int NotImpl7();

        int NotImpl8();

        int NotImpl9();

        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);

        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);

        int GetProcessId(out uint pid);
    }
}
