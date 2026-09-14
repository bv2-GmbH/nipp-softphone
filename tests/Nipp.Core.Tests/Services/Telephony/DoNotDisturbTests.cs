using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// §10 und ADR-055: «Nicht stören» schaltet den Klingelton auf Zeit stumm.
///
/// <para>§10 verlangt «Präsenz setzen» im Infobereich-Menü, und gebaut war es
/// nicht; C15 aus dem zweiten UX-Review wartete seit dem 12.09.2026 auf einen
/// Entscheid. Gebaut wird die kleine Fassung: der Klingelton schweigt, Anrufe
/// kommen weiterhin an.</para>
/// </summary>
public sealed class DoNotDisturbTests
{
    private static readonly DateTimeOffset Jetzt =
        new(2026, 9, 13, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Aus_heisst_aus()
    {
        Assert.False(DoNotDisturb.Aus.IsActive(Jetzt));
        Assert.Null(DoNotDisturb.Aus.Describe(Jetzt));
    }

    [Fact]
    public void Innerhalb_der_Zeit_ist_es_aktiv()
    {
        var dnd = DoNotDisturb.Fuer(Jetzt, TimeSpan.FromMinutes(30));

        Assert.True(dnd.IsActive(Jetzt));
        Assert.True(dnd.IsActive(Jetzt.AddMinutes(29)));
    }

    [Fact]
    public void Nach_der_Zeit_ist_es_aus()
    {
        // Der Kern der Entscheidung: auf Zeit und nicht auf Dauer. Ein
        // Schalter, den man einschaltet und vergisst, nimmt Anrufe entgegen,
        // die niemand hört.
        var dnd = DoNotDisturb.Fuer(Jetzt, TimeSpan.FromMinutes(30));

        Assert.False(dnd.IsActive(Jetzt.AddMinutes(30)));
        Assert.Null(dnd.Describe(Jetzt.AddMinutes(31)));
    }

    [Theory]
    [InlineData(0, "noch 30 Minuten")]
    [InlineData(29, "noch weniger als eine Minute")]
    [InlineData(28, "noch 2 Minuten")]
    public void Die_Restzeit_wird_aufgerundet(int vergangen, string erwartet)
    {
        // Aufgerundet auf Minuten: «noch 29 Min.» ist für eine Besprechung
        // genau genug, und eine Sekundenanzeige im Kontextmenü liest sich wie
        // ein Countdown.
        var dnd = DoNotDisturb.Fuer(Jetzt, TimeSpan.FromMinutes(30));

        Assert.Equal(erwartet, dnd.Describe(Jetzt.AddMinutes(vergangen)));
    }

    [Fact]
    public void Eine_Dauer_von_null_schaltet_nichts_ein()
    {
        Assert.False(DoNotDisturb.Fuer(Jetzt, TimeSpan.Zero).IsActive(Jetzt));
        Assert.False(DoNotDisturb.Fuer(Jetzt, TimeSpan.FromMinutes(-5)).IsActive(Jetzt));
    }
}
