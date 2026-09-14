using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.History;

/// <summary>
/// Was in der vierten Spalte der Anrufliste steht (§20.3).
///
/// <b>Warum das eine eigene Funktion und einen eigenen Test hat.</b> Bis zum
/// 05.09.2026 leitete die Oberfläche das Ergebnis allein aus Zustand und
/// Verbindungszeit ab: „besetzt" und „abgelehnt" konnten gar nicht entstehen,
/// obwohl beide in der Aufzählung stehen und der Fehlerkatalog sie kennt. Ein
/// besetztes Ziel stand als „fehlgeschlagen" in der Liste.
/// </summary>
public sealed class CallOutcomeRulesTests
{
    private static readonly DateTimeOffset Connected = DateTimeOffset.UtcNow;

    [Fact]
    public void Wer_gesprochen_hat_hat_angenommen()
    {
        // Auch wenn die Gegenstelle danach mit „besetzt" auflegt: verbunden
        // war das Gespräch.
        Assert.Equal(
            CallOutcome.Answered,
            CallOutcomeRules.Determine(Connected, CallEndReason.Busy, CallDirection.Incoming));
    }

    [Theory]
    [InlineData(CallEndReason.Busy, CallOutcome.Busy)]
    [InlineData(CallEndReason.Declined, CallOutcome.Declined)]
    [InlineData(CallEndReason.Failed, CallOutcome.Failed)]
    public void Der_Grund_der_Anlage_entscheidet(CallEndReason reason, CallOutcome expected) =>
        Assert.Equal(expected, CallOutcomeRules.Determine(null, reason, CallDirection.Outgoing));

    [Fact]
    public void Ein_nicht_angenommener_eingehender_Anruf_ist_verpasst() =>
        Assert.Equal(
            CallOutcome.Missed,
            CallOutcomeRules.Determine(null, CallEndReason.Normal, CallDirection.Incoming));

    [Fact]
    public void Ein_eingehender_Anruf_den_wir_ablehnen_gilt_als_abgelehnt() =>
        Assert.Equal(
            CallOutcome.Declined,
            CallOutcomeRules.Determine(null, CallEndReason.Declined, CallDirection.Incoming));

    [Fact]
    public void Ein_ausgehender_Anruf_ohne_Abnahme_ist_keine_Antwort() =>
        Assert.Equal(
            CallOutcome.NoAnswer,
            CallOutcomeRules.Determine(null, CallEndReason.NoAnswer, CallDirection.Outgoing));

    [Fact]
    public void Ohne_Grund_entscheidet_die_Richtung()
    {
        Assert.Equal(
            CallOutcome.Missed,
            CallOutcomeRules.Determine(null, null, CallDirection.Incoming));

        Assert.Equal(
            CallOutcome.NoAnswer,
            CallOutcomeRules.Determine(null, null, CallDirection.Outgoing));
    }
}
