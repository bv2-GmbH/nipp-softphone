using System.ComponentModel;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Die Zeile der Anrufliste (ADR-035).
///
/// <para><b>Was hier geprüft wird, ist der Grund, warum es diesen Typ
/// gibt:</b> die Zeile muss ihren Wechsel von „neu" auf „gesehen"
/// <b>melden</b> können, ohne dass jemand sie in der Sammlung austauscht. Ein
/// Austausch mitten im Auswahlereignis nähme ihr die Markierung, und der
/// Kontextbereich schlösse sich beim Anklicken sofort wieder.</para>
/// </summary>
public sealed class HistoryRowTests
{
    private static CallHistoryEntry Entry(
        CallOutcome outcome = CallOutcome.Missed,
        CallDirection direction = CallDirection.Incoming,
        DateTimeOffset? seenAt = null) => new(
            Id: 7,
            Number: "+41791234567",
            DisplayName: "Muster AG",
            Direction: direction,
            Outcome: outcome,
            StartedAt: DateTimeOffset.UtcNow,
            Duration: null,
            Codec: null,
            AccountIdentity: null,
            RecordingPath: null,
            SeenAt: seenAt);

    [Fact]
    public void Ein_ungesehener_verpasster_Anruf_ist_neu()
    {
        Assert.True(new HistoryRow(Entry()).IsNew);
    }

    [Fact]
    public void Ein_angesehener_ist_es_nicht_mehr()
    {
        Assert.False(new HistoryRow(Entry(seenAt: DateTimeOffset.UtcNow)).IsNew);
    }

    [Fact]
    public void Ein_selbst_gewaehlter_Anruf_ist_nie_neu()
    {
        // Sonst waere jede zweite Zeile fett, und die Schrift sagte etwas
        // anderes als das Abzeichen daneben.
        Assert.False(new HistoryRow(
            Entry(CallOutcome.Answered, CallDirection.Outgoing)).IsNew);

        Assert.False(new HistoryRow(
            Entry(CallOutcome.Busy, CallDirection.Outgoing)).IsNew);
    }

    [Fact]
    public void Der_Wechsel_wird_gemeldet_und_die_Zeile_bleibt_dieselbe()
    {
        var zeile = new HistoryRow(Entry());
        var gemeldet = new List<string?>();

        ((INotifyPropertyChanged)zeile).PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);

        zeile.IsNew = false;

        Assert.Contains(nameof(HistoryRow.IsNew), gemeldet);

        // Und der Name fuer die Sprachausgabe zieht mit: „ungelesen" darf
        // nicht stehenbleiben, wenn die Fettschrift verschwindet (§8.4).
        Assert.Contains(nameof(HistoryRow.AccessibleName), gemeldet);
    }

    [Fact]
    public void Die_Sprachausgabe_hoert_ungelesen_nur_solange_es_stimmt()
    {
        var zeile = new HistoryRow(Entry());

        Assert.Contains("ungelesen", zeile.AccessibleName, StringComparison.Ordinal);

        zeile.IsNew = false;

        Assert.DoesNotContain("ungelesen", zeile.AccessibleName, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Richtung, Ergebnis und Zeit gehören in den Namen</b> (ADR-046).
    ///
    /// <para>Vorher nannte diese Angabe nur bei ungelesenen verpassten Anrufen
    /// etwas darüber hinaus: ein <b>gesehener</b> verpasster Anruf wurde wie
    /// ein angenommener angesagt, und wann er war, stand nur in der Spalte
    /// rechts — die der Name der Zeile überdeckt.</para>
    /// </summary>
    [Fact]
    public void Der_Name_nennt_Richtung_Ergebnis_und_Zeit()
    {
        var zeile = new HistoryRow(Entry()) { IsNew = false };

        Assert.StartsWith("Muster AG", zeile.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("eingehend", zeile.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("verpasst", zeile.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("gerade eben", zeile.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_ausgehender_angenommener_Anruf_heisst_verbunden()
    {
        var zeile = new HistoryRow(
            Entry(outcome: CallOutcome.Answered, direction: CallDirection.Outgoing))
        {
            IsNew = false,
        };

        Assert.Contains("ausgehend", zeile.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("verbunden", zeile.AccessibleName, StringComparison.Ordinal);
    }
}
