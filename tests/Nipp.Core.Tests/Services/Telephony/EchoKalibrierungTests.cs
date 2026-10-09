using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Was eine Echo-Kalibrierung dem Benutzer sagt (§9.4, ADR-078).
///
/// <para><b>Warum das geprüft wird.</b> Nicht wegen der Umschaltung — die ist
/// trivial —, sondern wegen des Falls, den es vier Wochen lang nicht gab:
/// <b>durchgelaufen, kein Echo gefunden</b>. Das SDK kennt ihn als eigenen
/// Status, nipp machte daraus ein <c>null</c> und schrieb «lieferte kein
/// Ergebnis». Am 09.10.2026 stand im Protokoll <c>Echo calibration succeeded,
/// no echo has been detected</c>, und in der Oberfläche eine Fehlermeldung.</para>
///
/// <para>Der Unterschied ist nicht kosmetisch: «kein Echo» heisst, dass dieser
/// Arbeitsplatz die Echounterdrückung <b>nicht braucht</b> — und die kostet
/// Pufferlatenz, die man hört.</para>
/// </summary>
public class EchoKalibrierungTests
{
    /// <summary>
    /// <b>Kein Echo ist keine Fehlermeldung</b>, und der Satz sagt, was daraus
    /// folgt — nicht nur, was gemessen wurde.
    /// </summary>
    [Fact]
    public void Kein_Echo_wird_als_Ergebnis_erklaert()
    {
        var k = new EchoKalibrierung(EchoKalibrierungsErgebnis.KeinEcho, 0);

        Assert.Contains("Kein Echo", k.Beschreibung, StringComparison.Ordinal);
        Assert.Contains("ausgeschaltet bleiben", k.Beschreibung, StringComparison.Ordinal);
        Assert.DoesNotContain("gescheitert", k.Beschreibung, StringComparison.Ordinal);
    }

    /// <summary>Ein gemessenes Echo nennt seine Zahl.</summary>
    [Fact]
    public void Gemessenes_Echo_nennt_die_Verzoegerung()
    {
        var k = new EchoKalibrierung(EchoKalibrierungsErgebnis.EchoGemessen, 128);

        Assert.Contains("128 ms", k.Beschreibung, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein Fehlschlag sagt, was zu tun ist — §15 verlangt das von jeder
    /// Meldung.
    /// </summary>
    [Fact]
    public void Fehlschlag_sagt_was_zu_tun_ist()
    {
        var k = new EchoKalibrierung(EchoKalibrierungsErgebnis.Fehlgeschlagen, 0);

        Assert.Contains("prüfen", k.Beschreibung, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Die drei Sätze sind verschieden.</b> Das klingt selbstverständlich
    /// und war es nicht: vorher trugen zwei der drei Ausgänge denselben Text
    /// («lieferte kein Ergebnis»), und genau deshalb ist vier Wochen lang
    /// niemandem aufgefallen, dass hier kein Echo zu finden ist.
    /// </summary>
    [Fact]
    public void Jeder_Ausgang_hat_seinen_eigenen_Satz()
    {
        var saetze = new[]
        {
            new EchoKalibrierung(EchoKalibrierungsErgebnis.EchoGemessen, 128).Beschreibung,
            new EchoKalibrierung(EchoKalibrierungsErgebnis.KeinEcho, 0).Beschreibung,
            new EchoKalibrierung(EchoKalibrierungsErgebnis.Fehlgeschlagen, 0).Beschreibung,
        };

        Assert.Equal(3, saetze.Distinct(StringComparer.Ordinal).Count());
    }
}
