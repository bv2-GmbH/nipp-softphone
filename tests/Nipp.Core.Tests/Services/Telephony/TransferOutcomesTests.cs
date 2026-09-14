using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// W1.2 und Befund B5: das Ergebnis einer Weiterleitung wird beobachtet.
///
/// <para><b>Der Befund.</b> <c>OnTransferStateChanged</c> war nicht abonniert,
/// und <c>TransferAsync</c> protokollierte «übergeben» <b>vor</b> jeder
/// Antwort der Anlage. Ein 403, 404 oder 603 auf den REFER war damit
/// unsichtbar: kein Fehlertext, das Gespräch blieb je nach Anlage gehalten
/// stehen, und im Protokoll stand Erfolg.</para>
/// </summary>
public sealed class TransferOutcomesTests
{
    [Theory]
    [InlineData("Connected")]
    [InlineData("StreamsRunning")]
    public void Ein_verbundenes_Ziel_heisst_uebergeben(string zustand) =>
        Assert.Equal(TransferOutcome.Succeeded, TransferOutcomes.From(zustand));

    [Theory]
    [InlineData("Error")]
    [InlineData("End")]
    [InlineData("Released")]
    public void Ein_abgelehntes_Ziel_heisst_gescheitert(string zustand) =>
        Assert.Equal(TransferOutcome.Failed, TransferOutcomes.From(zustand));

    [Theory]
    [InlineData("OutgoingInit")]
    [InlineData("OutgoingProgress")]
    [InlineData("OutgoingRinging")]
    public void Waehrend_das_Ziel_gerufen_wird_gibt_es_keine_Aussage(string zustand) =>
        Assert.Equal(TransferOutcome.InProgress, TransferOutcomes.From(zustand));

    [Theory]
    [InlineData("EinNeuerZustand")]
    [InlineData("")]
    [InlineData(null)]
    public void Ein_unbekannter_Zustand_erfindet_keine_Fehlermeldung(string? zustand)
    {
        // Die Regel, die diese Klasse überhaupt begründet: das SDK kann neue
        // Zustände bekommen, und einer davon darf dem Benutzer nicht sagen,
        // seine Weiterleitung sei gescheitert. Dieselbe Lehre wie bei
        // «Refreshing» in der Registrierung — dort galt ein unbekannter Wert
        // als Fehler, und die Lampe wurde für eine halbe Sekunde rot.
        Assert.Equal(TransferOutcome.InProgress, TransferOutcomes.From(zustand));
    }
}
