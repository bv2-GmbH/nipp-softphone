<#
.SYNOPSIS
    Listet HID-Telefoniegeraete und ihre Tasten und Lampen auf.

.DESCRIPTION
    Fuer die Frage "warum tut die Taste an diesem Headset nichts?".

    Ausgegeben wird je Geraet, das die Usage Page 0x0B (Telephony) anbietet:
    Usage, Report-Laengen, VID/PID, und dann jede Taste und jede Lampe mit
    ihrer Usage und ihrer Report-Kennung.

    Genau diese Angaben braucht HidTelephonyDevice zur Laufzeit, und genau
    damit wurde die Anbindung gebaut statt geraten (ADR-028). Beim Jabra
    Link 400 kommt heraus:

        UsagePage 0x0B, Usage 0x05 (Headset), In/Out je 3 Bytes
        Eingang, Report 2:  Hook Switch 0x20, Phone Mute 0x2F, Flash 0x21, ...
        Ausgang, Report 2:  Off-Hook 0x17, Ring 0x18, Mute 0x09 (LED-Seite 0x08)

    Die Report-Kennung ist der Punkt, an dem Raten scheitert: hier ist sie 2,
    nicht 0. Erscheint ein Geraet gar nicht, bietet es keine Telefonieseite an
    — dann ist nicht die Anbindung schuld.

.NOTES
    Oeffnet die Geraete nur pruefend (access=0) und geteilt. Aendert nichts.
#>

$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class H2 {
    [StructLayout(LayoutKind.Sequential)] public struct GUID { public uint a; public ushort b,c; public byte d0,d1,d2,d3,d4,d5,d6,d7; }
    [StructLayout(LayoutKind.Sequential)] public struct DID { public int cbSize; public GUID g; public int Flags; public IntPtr R; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DETAIL { public int cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=512)] public string Path; }
    [StructLayout(LayoutKind.Sequential)] public struct CAPS {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Res;
        public ushort Nodes, NInBtn, NInVal, NInIdx, NOutBtn, NOutVal, NOutIdx, NFeatBtn, NFeatVal, NFeatIdx;
    }
    // HIDP_BUTTON_CAPS: 104 Bytes auf x64/ARM64
    [StructLayout(LayoutKind.Sequential)] public struct BTNCAPS {
        public ushort UsagePage; public byte ReportID; public byte IsAlias;
        public ushort BitField; public ushort LinkCollection; public ushort LinkUsage, LinkUsagePage;
        public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=10)] public uint[] Reserved;
        public ushort UsageMin, UsageMax;      // bei IsRange
        public ushort StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
    }
    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out GUID g);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr d);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr d);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr d, out CAPS c);
    [DllImport("hid.dll")] public static extern int HidP_GetButtonCaps(int kind, [Out] BTNCAPS[] b, ref ushort len, IntPtr d);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SetupDiGetClassDevs(ref GUID g, string e, IntPtr h, int f);
    [DllImport("setupapi.dll")] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref GUID g, int i, ref DID a);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref DID a, ref DETAIL d, int size, IntPtr r, IntPtr dv);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr CreateFile(string p, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@
$g = New-Object H2+GUID; [H2]::HidD_GetHidGuid([ref]$g)
$set = [H2]::SetupDiGetClassDevs([ref]$g,$null,[IntPtr]::Zero,0x12)
$i=0
while($true){
  $d = New-Object H2+DID; $d.cbSize=[Runtime.InteropServices.Marshal]::SizeOf($d)
  if(-not [H2]::SetupDiEnumDeviceInterfaces($set,[IntPtr]::Zero,[ref]$g,$i,[ref]$d)){break}
  $i++
  $det = New-Object H2+DETAIL; $det.cbSize = if([IntPtr]::Size -eq 8){8}else{6}
  if(-not [H2]::SetupDiGetDeviceInterfaceDetail($set,[ref]$d,[ref]$det,1048,[IntPtr]::Zero,[IntPtr]::Zero)){continue}
  $h = [H2]::CreateFile($det.Path,0,3,[IntPtr]::Zero,3,0,[IntPtr]::Zero)
  if($h -eq [IntPtr]::new(-1)){continue}
  $pre=[IntPtr]::Zero
  if([H2]::HidD_GetPreparsedData($h,[ref]$pre)){
    $c = New-Object H2+CAPS
    if([H2]::HidP_GetCaps($pre,[ref]$c) -eq 0x110000 -and $c.UsagePage -eq 0x0B){
      "PFAD: $($det.Path)"
      "  Usage=0x{0:X2} InLen={1} OutLen={2} InBtn={3} OutBtn={4}" -f $c.Usage,$c.InLen,$c.OutLen,$c.NInBtn,$c.NOutBtn
      "  sizeof(BTNCAPS)=$([Runtime.InteropServices.Marshal]::SizeOf([type]'H2+BTNCAPS'))"
      foreach($kind in 0,1){   # 0=Input, 1=Output
        $n = if($kind -eq 0){[ushort]$c.NInBtn}else{[ushort]$c.NOutBtn}
        if($n -eq 0){continue}
        $arr = New-Object H2+BTNCAPS[] $n
        $len = [ushort]$n
        $r = [H2]::HidP_GetButtonCaps($kind,$arr,[ref]$len,$pre)
        $label = if($kind -eq 0){'EINGANG (Tasten)'}else{'AUSGANG (LEDs)'}
        "  $label  status=0x{0:X8} anzahl=$len" -f $r
        foreach($b in $arr[0..($len-1)]){
          $u = if($b.IsRange -ne 0){"0x{0:X2}..0x{1:X2}" -f $b.UsageMin,$b.UsageMax}else{"0x{0:X2}" -f $b.UsageMin}
          "    Page=0x{0:X2} Usage=$u ReportID={1} DataIdx={2}..{3} Range={4}" -f $b.UsagePage,$b.ReportID,$b.DataIndexMin,$b.DataIndexMax,$b.IsRange
        }
      }
    }
    [void][H2]::HidD_FreePreparsedData($pre)
  }
  [void][H2]::CloseHandle($h)
}
