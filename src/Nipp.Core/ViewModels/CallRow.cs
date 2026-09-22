using CommunityToolkit.Mvvm.ComponentModel;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Ein Gespräch, wie die Makel-Liste es sieht — eine Zeile mit <b>stabiler
/// Identität</b> (ADR-043).
///
/// <para><b>Warum es diese Hülle gibt.</b> <see cref="CallInfo"/> ist
/// unveränderlich: jeder Zustandswechsel erzeugt eine neue Instanz, und
/// <c>ActiveCallViewModel</c> tauschte sie in der Sammlung aus. Die ListView
/// verlor damit bei jedem „Halten" ihre Auswahl; umgangen wurde das über eine
/// Auswahl nach Kennung und ein <c>ReferenceEquals</c> in der Seite. Eine
/// Zeile, die bleibt, beseitigt die Ursache statt ihrer Wirkung.</para>
///
/// <para><b>Und sie trägt den Namen.</b> Die Liste band
/// <c>CallInfo.DisplayLabel</c> — die einzige richtungsneutrale Beschriftung im
/// System, und sie kannte keine Kontakte. Ein Konverter hätte das nicht lösen
/// können: er ist zustandslos, käme an den Auflöser nur statisch heran und
/// könnte auf <c>PartyChanged</c> nicht reagieren. Diese Zeile kann es, weil
/// jemand ihr <see cref="Label"/> neu setzt.</para>
///
/// <para>Sie entscheidet nichts. Im Zuschnitt von <c>ContactRow</c> und
/// <c>HistoryRow</c>.</para>
/// </summary>
public sealed partial class CallRow : ObservableObject
{
    public CallRow(CallInfo call, string label)
    {
        _call = call;
        _label = label;
    }

    /// <summary>Die Kennung — sie überlebt jeden Zustandswechsel.</summary>
    public CallHandle Handle => Call.Handle;

    /// <summary>Das Gespräch in seinem aktuellen Zustand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private CallInfo _call;

    /// <summary>Der Name, sonst die formatierte Nummer — nie leer.</summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>Was rechts unter dem Namen steht.</summary>
    public CallStatus Status => Call.Status;

    /// <summary>
    /// Was eine Sprachausgabe vorliest (Befund A1-16).
    ///
    /// <para><b>Der Zustand gehört dazu.</b> In der Zeile steht er als zweite,
    /// kleinere Zeile; wer nur <see cref="Label"/> hört, weiss nicht, ob das
    /// Gespräch läuft oder gehalten wird — und genau zwischen diesen beiden
    /// wird hier umgeschaltet. Wie der Zustand heisst, entscheidet
    /// <see cref="CallStateCatalog"/> und nicht diese Stelle (ADR-044).</para>
    /// </summary>
    public string AccessibleName => $"{Label}, {CallStateCatalog.Of(Status)}";

    /// <summary>Für die Fehlersuche. Was vorgelesen wird, steht in
    /// <see cref="AccessibleName"/>.</summary>
    public override string ToString() => Label;
}
