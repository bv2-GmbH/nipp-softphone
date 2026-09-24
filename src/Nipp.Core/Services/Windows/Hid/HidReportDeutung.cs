namespace Nipp.Core.Services.Windows.Hid;

/// <summary>
/// Was ein Gerätereport sagt — und welche Lampen ein Bericht anschaltet
/// (§22.5, W2.1 Etappe B7).
///
/// <para><b>Warum das herausgetrennt ist.</b> Beides stand mitten in
/// <c>HidTelephonyDevice</c> zwischen P/Invoke-Aufrufen und war damit nur
/// mit einem Headset in der Hand zu messen. Dabei ist keines von beiden
/// gerätenah: <b>aus einer Liste von Usages werden zwei Wahrheitswerte</b>,
/// und <b>aus drei Zuständen wird eine Liste von Lampen</b>. Das sind
/// Rechnungen, und Rechnungen gehören in Tests.</para>
///
/// <para><b>Was hier nicht steht:</b> ob ein Bericht überhaupt hinausgehen
/// darf. Das entscheidet <c>HeadsetSignalGate</c> — und dort, weil es dort
/// an einer Stelle steht (ADR-028 Nachtrag 4, ADR-068).</para>
/// </summary>
public static class HidReportDeutung
{
    /// <summary>Gabelschalter (Telephony Page 0x0B).</summary>
    public const ushort UsageHookSwitch = 0x20;

    /// <summary>Stummtaste am Gerät.</summary>
    public const ushort UsagePhoneMute = 0x2F;

    /// <summary>Lampe «im Gespräch» (LED Page 0x08).</summary>
    public const ushort UsageLedOffHook = 0x17;

    /// <summary>Lampe «es klingelt».</summary>
    public const ushort UsageLedRing = 0x18;

    /// <summary>Lampe «stumm».</summary>
    public const ushort UsageLedMute = 0x09;

    /// <summary>
    /// Was das Gerät gemeldet hat.
    ///
    /// <para><b>Die Länge kommt getrennt herein</b>, weil das die Windows-API
    /// so liefert: das Feld ist so gross wie die grösste mögliche Meldung,
    /// gültig sind nur die ersten <paramref name="anzahl"/> Einträge. Wer den
    /// Rest mitliest, bekommt die Usages der vorigen Meldung — ein Gerät, das
    /// nie auflegt.</para>
    /// </summary>
    public static HidTastendruck Lies(IReadOnlyList<ushort> usages, int anzahl)
    {
        ArgumentNullException.ThrowIfNull(usages);

        var offHook = false;
        var mute = false;

        for (var i = 0; i < anzahl && i < usages.Count; i++)
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

        return new HidTastendruck(offHook, mute);
    }

    /// <summary>
    /// Welche Lampen ein Zustand anschaltet — in fester Reihenfolge:
    /// Gespräch, Klingeln, Stumm.
    ///
    /// <para><b>Die Reihenfolge ist nicht gleichgültig.</b> Kennt ein Gerät
    /// eine der Lampen nicht, setzt Windows keine einzige; dann geht der
    /// Aufrufer sie einzeln durch, und dabei gilt: <b>«im Gespräch» allein
    /// ist wichtiger als alle drei</b>.</para>
    /// </summary>
    public static ushort[] Lampen(bool imGespraech, bool klingelt, bool stumm)
    {
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

        return [.. lampen];
    }
}

/// <summary>
/// Was ein Report an Tastendrücken trug.
/// </summary>
/// <param name="OffHook">
/// Ob der Gabelschalter «abgenommen» meldet. <b>Das ist noch kein
/// Tastendruck</b> — ob einer daraus wird, entscheidet <c>HookWatch</c>:
/// Loslassen, Prellen und das Echo einer eigenen Meldung sehen hier gleich
/// aus.
/// </param>
/// <param name="Mute">Ob die Stummtaste gedrückt wurde.</param>
public readonly record struct HidTastendruck(bool OffHook, bool Mute);
