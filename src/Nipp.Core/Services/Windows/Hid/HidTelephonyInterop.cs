using System.Runtime.InteropServices;

namespace Nipp.Core.Services.Windows.Hid;

/// <summary>
/// Die Win32-Aufrufe für HID-Telefoniegeräte.
///
/// <para><b>Warum nicht WinRT.</b> Es gibt in WinRT genau eine Klasse für
/// diesen Zweck, <c>Windows.Media.Devices.CallControl</c>, und die ist auf dem
/// Desktop nicht implementiert: sowohl <c>FromId</c> als auch
/// <c>GetDefault</c> scheitern mit <c>0x80040111</c>
/// (<c>CLASS_E_CLASSNOTAVAILABLE</c>) — belegt am Gerät, nicht vermutet. Die
/// andere WinRT-Möglichkeit, <c>Windows.Devices.HumanInterfaceDevice</c>,
/// sperrt genau die Usage Page aus, um die es hier geht: Telefonie (0x0B)
/// gehört zu den für Anwendungen reservierten Seiten.</para>
///
/// <para><b>Also der Weg, den auch das SDK ging.</b> Linphone hatte bis 5.4
/// eine HIDAPI-Anbindung für Jabra-Geräte; mit 5.5.0 wurde sie entfernt
/// (NIPP-BUILD.md §5). Übrig bleibt, was Softphones seit je tun: die
/// HID-Schnittstelle des Geräts über SetupAPI finden und über
/// <c>hid.dll</c> lesen und schreiben.</para>
///
/// <para><b>Nichts hier deutet Bits von Hand.</b> Die Funktionen
/// <c>HidP_GetUsages</c> und <c>HidP_SetUsages</c> arbeiten mit dem
/// vorverarbeiteten Report-Deskriptor des Geräts. Ein selbst geschriebener
/// Bit-Versatz wäre für genau ein Modell richtig; über den Deskriptor gilt es
/// für jedes.</para>
/// </summary>
internal static class HidTelephonyInterop
{
    /// <summary>Usage Page „Telephony Device" — die Tasten am Headset.</summary>
    internal const ushort TelephonyPage = 0x0B;

    /// <summary>Usage Page „LED" — die Lampen und der Zustand, den wir melden.</summary>
    internal const ushort LedPage = 0x08;

    /// <summary>
    /// „Hook Switch": die Taste zum Abnehmen und Auflegen.
    ///
    /// <para>Sie meldet einen <b>Zustand</b>, nicht einen Tastendruck: 1
    /// heisst abgenommen, 0 aufgelegt. Was daraus folgt, hängt davon ab, was
    /// nipp gerade tut — deshalb steht die Deutung nicht hier, sondern in
    /// <see cref="HeadsetCallControl"/>.</para>
    /// </summary>
    internal const ushort UsageHookSwitch = 0x20;

    /// <summary>„Phone Mute": die Stummtaste am Headset.</summary>
    internal const ushort UsagePhoneMute = 0x2F;

    /// <summary>LED „Off-Hook": leuchtet, solange ein Gespräch läuft.</summary>
    internal const ushort UsageLedOffHook = 0x17;

    /// <summary>LED „Ring": blinkt, solange es klingelt.</summary>
    internal const ushort UsageLedRing = 0x18;

    /// <summary>LED „Mute": leuchtet, solange das Mikrofon stumm ist.</summary>
    internal const ushort UsageLedMute = 0x09;

    // Welche Usages ein Gerät als Headset, Handset oder Telefon ausweisen.
    // Angeschlossen wird an jedes davon; ein Tischtelefon mit HID-Anbindung
    // verhält sich hier nicht anders als ein Headset.
    internal static readonly ushort[] TelephonyUsages = [0x01, 0x02, 0x03, 0x04, 0x05];

    internal const int HidP_Input = 0;
    internal const int HidP_Output = 1;

    internal const int HidP_StatusSuccess = 0x00110000;

    private const int DigcfPresent = 0x00000002;
    private const int DigcfDeviceInterface = 0x00000010;

    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareReadWrite = 0x00000003;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagOverlapped = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Guid16
    {
        public uint Data1;
        public ushort Data2;
        public ushort Data3;
        public byte B0, B1, B2, B3, B4, B5, B6, B7;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceData
    {
        public int Size;
        public Guid16 InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DeviceInterfaceDetail
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string DevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    internal static extern void HidD_GetHidGuid(out Guid16 hidGuid);

    [DllImport("hid.dll")]
    internal static extern bool HidD_GetPreparsedData(
        Microsoft.Win32.SafeHandles.SafeFileHandle device, out IntPtr preparsed);

    [DllImport("hid.dll")]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    internal static extern bool HidD_GetProductString(
        Microsoft.Win32.SafeHandles.SafeFileHandle device, byte[] buffer, int length);

    [DllImport("hid.dll")]
    internal static extern bool HidD_SetOutputReport(
        Microsoft.Win32.SafeHandles.SafeFileHandle device, byte[] report, int length);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr preparsed, out HidCaps caps);

    /// <summary>
    /// Beschreibt eine Taste oder Lampe: auf welcher Usage Page, mit welcher
    /// Usage, und <b>in welchem Report</b>.
    ///
    /// <para>Das letzte ist der Grund, warum diese Struktur hier steht. Ein
    /// Gerät nummeriert seine Reports selbst; das Jabra Link 400 benutzt
    /// Kennung 2. Ein fest verdrahtetes 0 oder 1 hätte für dieses Gerät
    /// funktioniert oder auch nicht — abgelesen ist es für jedes richtig.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HidButtonCaps
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] Reserved;

        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [DllImport("hid.dll")]
    internal static extern int HidP_GetButtonCaps(
        int reportType,
        [Out] HidButtonCaps[] buttonCaps,
        ref ushort length,
        IntPtr preparsed);

    /// <summary>
    /// Welche Tasten in einem eingegangenen Report gedrückt sind.
    ///
    /// <para>Gibt die Liste der <b>gesetzten</b> Usages zurück. Was nicht
    /// darin steht, ist nicht gedrückt — es gibt keine Meldung „losgelassen"
    /// ausser der Abwesenheit.</para>
    /// </summary>
    [DllImport("hid.dll")]
    internal static extern int HidP_GetUsages(
        int reportType,
        ushort usagePage,
        ushort linkCollection,
        [Out] ushort[] usageList,
        ref int usageLength,
        IntPtr preparsed,
        byte[] report,
        int reportLength);

    [DllImport("hid.dll")]
    internal static extern int HidP_SetUsages(
        int reportType,
        ushort usagePage,
        ushort linkCollection,
        ushort[] usageList,
        ref int usageLength,
        IntPtr preparsed,
        byte[] report,
        int reportLength);

    /// <summary>
    /// Legt einen leeren Report der gewünschten Kennung an — alle Bits null.
    ///
    /// <para>Damit braucht es kein <c>HidP_UnsetUsages</c>: für jeden
    /// Zustandswechsel wird ein frischer Report gebaut und nur gesetzt, was
    /// leuchten soll. Ein Bit, das niemand setzt, bleibt aus.</para>
    /// </summary>
    [DllImport("hid.dll")]
    internal static extern int HidP_InitializeReportForID(
        int reportType,
        byte reportId,
        IntPtr preparsed,
        byte[] report,
        int reportLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid16 classGuid, string? enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll")]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid16 interfaceClassGuid,
        int memberIndex,
        ref DeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref DeviceInterfaceData interfaceData,
        ref DeviceInterfaceDetail detail,
        int detailSize,
        IntPtr requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll")]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string fileName,
        uint access,
        uint share,
        IntPtr security,
        uint creationDisposition,
        uint flags,
        IntPtr template);

    /// <summary>
    /// Bricht ein laufendes <c>ReadFile</c> ab.
    ///
    /// <para><b>Nötig, weil das Lesen blockiert.</b> Ein HID-Gerät schickt
    /// nur etwas, wenn jemand eine Taste drückt — ein Lesevorgang steht also
    /// beliebig lange. Beim Beenden bloss das Handle freizugeben, während ein
    /// Lesevorgang darauf steht, ist laut Dokumentation unbestimmtes
    /// Verhalten. Erst abbrechen, dann schliessen.</para>
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CancelIoEx(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, IntPtr overlapped);

    internal const int DeviceInfoFlags = DigcfPresent | DigcfDeviceInterface;
}
