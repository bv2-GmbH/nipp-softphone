using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// ADR-053: die Grenze zwischen dem SDK und allem, was daran hängt.
///
/// <para>Die Zusage, die hier festgehalten wird, ist die wichtigste des ganzen
/// Schrittes: <b>eine Ausnahme aus einem Abonnenten kommt nie in den nativen
/// Rahmen zurück.</b> Sie lässt sich hier prüfen, weil der Wächter eine reine
/// Klasse ist — ohne SDK, ohne Core, ohne Anruf.</para>
/// </summary>
public sealed class CallbackGuardTests
{
    [Fact]
    public void Eine_werfende_Aktion_kommt_nicht_durch()
    {
        // Der Fall, den es in echt gab: eine XamlParseException aus dem
        // Konstruktor der Gesprächsansicht, ausgelöst mitten im SDK-Callback
        // beim Klingeln. Ohne diesen Fänger war das ein beendeter Prozess.
        var ergebnis = CallbackGuard.Run(
            NullLogger.Instance,
            "OnCallStateChanged",
            () => throw new InvalidOperationException("Irgendetwas ging schief"));

        Assert.False(ergebnis);
    }

    [Fact]
    public void Eine_gelungene_Aktion_laeuft_durch()
    {
        var gelaufen = false;

        var ergebnis = CallbackGuard.Run(
            NullLogger.Instance,
            "OnCallStateChanged",
            () => gelaufen = true);

        Assert.True(ergebnis);
        Assert.True(gelaufen);
    }

    [Fact]
    public void Auch_eine_Ausnahme_ohne_Fangzweig_kommt_nicht_durch()
    {
        // Bewusst eine Ausnahme, die niemand erwartet: der Wächter ist die
        // letzte Linie vor dem Absturz, und welcher Typ ihn erreicht, ist
        // dafür gleichgültig. Ein Fänger, der nur das Erwartete fängt, ist
        // genau dann keiner, wenn es darauf ankommt.
        var ergebnis = CallbackGuard.Run(
            NullLogger.Instance,
            "OnNotifyPresenceReceived",
            () => throw new NotSupportedException());

        Assert.False(ergebnis);
    }

    [Fact]
    public void Eine_Meldung_mit_einer_Rufnummer_wird_maskiert()
    {
        // §21.2 und ADR-022: was ins Protokoll geht, trägt keine Rufnummer.
        // Eine Ausnahmemeldung trägt oft genau das — ein fehlgeschlagener
        // Abruf nennt die angefragte Adresse, ein Datenbankfehler die Zeile,
        // die er nicht schreiben konnte. Wer eine fremde Meldung ungeprüft
        // weitergibt, gibt weiter, was darin steht.
        var text = CallbackGuard.Describe(
            new InvalidOperationException("Abruf für 0791234567 fehlgeschlagen"));

        Assert.DoesNotContain("0791234567", text, StringComparison.Ordinal);
        Assert.Contains("Abruf für", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Ausnahme_ohne_Meldung_bekommt_einen_Text()
    {
        // Eine leere Zeile im Protokoll ist schlechter als keine: sie sieht
        // aus wie ein Fehler beim Protokollieren.
        var text = CallbackGuard.Describe(new LeereAusnahme());

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    private sealed class LeereAusnahme : Exception
    {
        public override string Message => string.Empty;
    }
}
