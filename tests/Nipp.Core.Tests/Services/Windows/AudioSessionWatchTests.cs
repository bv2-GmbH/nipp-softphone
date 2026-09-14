using Nipp.Core.Services.Windows.Audio;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Ob ein anderes Programm die Audiogeräte hält (ADR-068).
///
/// <para><b>Der Anlass.</b> nipp beendete fremde Teams-Meetings: es lehnte
/// einen Anruf ab, schickte dabei seinen Abschlussbericht ans geteilte
/// HID-Interface, und Teams las das als Tastendruck. Die bis dahin gebaute
/// Erkennung hing am Gabelzustand des Geräts — und <b>der sagt nichts</b>: im
/// Meeting meldete das Jabra durchgehend «aufgelegt» (gemessen am
/// 14.09.2026).</para>
///
/// <para><b>Warum diese Rechnung Tests hat, obwohl sie drei Zeilen ist.</b>
/// Sie entscheidet, ob nipp schweigt. Ist sie zu streng, verliert das Headset
/// seine Lampen; ist sie zu lasch, beendet nipp fremde Gespräche. Beides
/// merkt man erst am Gerät, und das zweite erst, wenn es passiert ist.</para>
/// </summary>
public sealed class AudioSessionWatchTests
{
    private sealed class Quelle(params uint[] sitzungen) : IAudioSessionSource
    {
        public IReadOnlyList<uint> AktiveSitzungen() => sitzungen;
    }

    private sealed class KaputteQuelle : IAudioSessionSource
    {
        public IReadOnlyList<uint> AktiveSitzungen() => throw new InvalidOperationException("kaputt");
    }

    private const uint Eigene = 4711;

    [Fact]
    public void Ohne_aktive_Sitzung_ist_nichts_belegt()
    {
        var watch = new AudioSessionWatch(new Quelle(), Eigene);

        Assert.False(watch.Fremdbelegt());
    }

    /// <summary>
    /// <b>Der Fall, der die Lampen rettet.</b> Im eigenen Gespräch hält nipp
    /// dieselben Geräte; wer das mitzählt, schweigt genau dann, wenn das
    /// Headset etwas anzeigen soll.
    /// </summary>
    [Fact]
    public void Die_eigene_Sitzung_zaehlt_nicht()
    {
        var watch = new AudioSessionWatch(new Quelle(Eigene), Eigene);

        Assert.False(watch.Fremdbelegt());
    }

    [Fact]
    public void Eine_fremde_Sitzung_belegt()
    {
        var watch = new AudioSessionWatch(new Quelle(9999), Eigene);

        Assert.True(watch.Fremdbelegt());
    }

    /// <summary>
    /// Der gemessene Fall: das Meeting läuft, und nipp spielt daneben seinen
    /// Klingelton — beide stehen in der Liste.
    /// </summary>
    [Fact]
    public void Eigene_und_fremde_zusammen_belegen()
    {
        var watch = new AudioSessionWatch(new Quelle(Eigene, 9999, Eigene), Eigene);

        Assert.True(watch.Fremdbelegt());
    }

    /// <summary>
    /// Die Null steht für «kein Prozess» — Systemklänge etwa. Sie ist niemand,
    /// dessen Gespräch man schonen müsste.
    /// </summary>
    [Fact]
    public void Eine_Sitzung_ohne_Prozess_belegt_nicht()
    {
        var watch = new AudioSessionWatch(new Quelle(0), Eigene);

        Assert.False(watch.Fremdbelegt());
    }

    /// <summary>
    /// <b>Im Zweifel frei.</b> Scheitert die Abfrage, verhält sich nipp wie
    /// vor ADR-068 — es meldet. Andersherum wäre ein Fehler an der
    /// Audio-Schnittstelle ein Headset, das nie wieder etwas anzeigt, und
    /// niemand käme darauf, warum.
    /// </summary>
    [Fact]
    public void Eine_kaputte_Quelle_gilt_als_frei()
    {
        var watch = new AudioSessionWatch(new KaputteQuelle(), Eigene);

        Assert.False(watch.Fremdbelegt());
    }
}
