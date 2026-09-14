using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// §9.4 gibt Lautstärken als „0–100" vor, das SDK arbeitet in Dezibel. Diese
/// Umrechnung ist eine Festlegung, keine Ableitung — und deshalb genau das,
/// was getestet gehört: sie ändert sich sonst unbemerkt.
/// </summary>
public sealed class AudioGainTests
{
    [Fact]
    public void Die_Skalenmitte_ist_neutral()
    {
        // 50 muss exakt 0 dB ergeben — der Pegel bleibt unverändert.
        Assert.Equal(0f, AudioGain.ToDecibel(50));
    }

    [Fact]
    public void Der_Mikrofonstandard_aus_Paragraph_9_4_ist_neutral()
    {
        // §9.4: Mikrofonpegel Standard 50.
        Assert.Equal(0f, AudioGain.ToDecibel(50));
    }

    [Fact]
    public void Der_Wiedergabestandard_liegt_leicht_darueber()
    {
        // §9.4: Wiedergabe Standard 72. Soll hörbar, aber nicht übersteuert
        // sein — bei ±15 dB Spanne sind das +6,6 dB.
        var db = AudioGain.ToDecibel(72);

        Assert.InRange(db, 6f, 7f);
    }

    [Theory]
    [InlineData(0, -15f)]
    [InlineData(100, 15f)]
    public void Die_Grenzen_liegen_bei_plus_minus_15_dB(int scale, float expected)
    {
        Assert.Equal(expected, AudioGain.ToDecibel(scale), precision: 3);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(1000)]
    public void Werte_ausserhalb_der_Skala_werden_begrenzt(int scale)
    {
        var db = AudioGain.ToDecibel(scale);

        // Die Toleranz ist kein Nachgeben, sondern Fliesskomma: 50 × 0,3f
        // ergibt 15,000001. Ein Millionstel Dezibel ist für einen
        // Lautstärkeregler ohne jede Bedeutung — der Test soll die Begrenzung
        // prüfen, nicht die Rundung von float.
        Assert.InRange(db, -15.001f, 15.001f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(72)]
    [InlineData(100)]
    public void Hin_und_zurueck_ergibt_denselben_Wert(int scale)
    {
        // Wichtig, weil beide Richtungen gebraucht werden: die Oberfläche
        // schreibt Skalenwerte, ein Provisioning-Profil kann Dezibel direkt
        // in die linphonerc geschrieben haben.
        var db = AudioGain.ToDecibel(scale);

        Assert.Equal(scale, AudioGain.ToScaleValue(db));
    }

    [Fact]
    public void Mehr_Skalenwert_bedeutet_immer_mehr_Dezibel()
    {
        // Ein Regler, der sich nicht monoton verhält, ist unbedienbar.
        var previous = float.MinValue;

        for (var scale = 0; scale <= 100; scale++)
        {
            var db = AudioGain.ToDecibel(scale);
            Assert.True(db > previous, $"Bei {scale} ging es nicht aufwärts: {db} nach {previous}");
            previous = db;
        }
    }
}
