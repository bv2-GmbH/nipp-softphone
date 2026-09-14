using CommunityToolkit.Mvvm.ComponentModel;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Eine Zeile der Anrufliste (§8.3, §20.3, ADR-035).
///
/// <para><b>Warum ein eigener Typ und nicht <see cref="CallHistoryEntry"/>
/// direkt</b> — derselbe Grund wie bei <see cref="ContactRow"/>, nur an einer
/// schärferen Stelle: der Eintrag ist ein unveränderlicher <c>record</c>, und
/// „gesehen" ändert sich <b>während</b> die Zeile ausgewählt wird. Den Eintrag
/// in der Sammlung auszutauschen hiesse, das gewählte Element aus der Liste zu
/// nehmen und ein neues hineinzulegen — mitten im <c>SelectionChanged</c>, der
/// gerade läuft. Die Auswahl fiele weg, der Kontextbereich schlösse sich beim
/// Anklicken sofort wieder, und der Grund dafür stünde nirgends.</para>
///
/// <para>Die Hülle meldet ihre Änderung stattdessen selbst. Die Sammlung wird
/// nicht angefasst.</para>
/// </summary>
public sealed partial class HistoryRow : ObservableObject
{
    /// <summary>
    /// Ob die Zeile noch niemandem aufgefallen ist — dann steht sie fett.
    ///
    /// Spiegelt <see cref="CallHistoryEntry.IsNew"/> beim Anlegen und wird
    /// danach von Hand gelöscht, wenn der Eintrag angesehen wurde.
    /// </summary>
    [ObservableProperty]
    private bool _isNew;

    public HistoryRow(CallHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        _isNew = entry.IsNew;
    }

    /// <summary>Der Eintrag, wie er in der Datenbank steht.</summary>
    public CallHistoryEntry Entry { get; }

    public long Id => Entry.Id;

    public string Number => Entry.Number;

    public string DisplayLabel => Entry.DisplayLabel;

    public CallOutcome Outcome => Entry.Outcome;

    public CallDirection Direction => Entry.Direction;

    public DateTimeOffset StartedAt => Entry.StartedAt;

    public bool HasRecording => Entry.HasRecording;

    /// <summary>
    /// Was ein Bildschirmleser vorliest.
    ///
    /// Ohne diese Angabe nimmt die Automation den <c>ToString()</c> des
    /// gebundenen Objekts — bei einer Hülle also den Klassennamen. Und
    /// „verpasst" gehört ausdrücklich dazu: die Fettschrift sagt es sonst
    /// allein, und eine Aussage, die nur an der Darstellung hängt, kommt bei
    /// einer Sprachausgabe nicht an (§8.4).
    /// </summary>
    /// <para><b>Richtung, Ergebnis und Zeit gehören dazu</b> (ADR-046). Vorher
    /// nannte diese Angabe nur bei ungelesenen verpassten Anrufen etwas
    /// darüber hinaus: ein <b>gesehener</b> verpasster Anruf wurde wie ein
    /// angenommener angesagt, und wann er war, stand nur in der Spalte rechts,
    /// die der Name der Zeile überdeckt.</para>
    public string AccessibleName
    {
        get
        {
            var ergebnis = CallOutcomeText.For(Outcome, Direction);
            var richtung = CallOutcomeText.For(Direction);
            var wann = RelativeTime.Describe(StartedAt, DateTimeOffset.UtcNow);

            return IsNew
                ? $"{DisplayLabel}, {richtung}, {ergebnis}, ungelesen, {wann}"
                : $"{DisplayLabel}, {richtung}, {ergebnis}, {wann}";
        }
    }

    partial void OnIsNewChanged(bool value) => OnPropertyChanged(nameof(AccessibleName));

    public override string ToString() => DisplayLabel;
}
