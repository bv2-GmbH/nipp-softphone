using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Integrations.Phone;

/// <summary>
/// Die Nummernformen für Integrationen (§21).
///
/// <b>Warum das geprüft wird.</b> Diese Klasse entscheidet, was eine externe
/// Abfrage überhaupt zu sehen bekommt — und ob sie stattfindet. Ein Fehler hier
/// hat zwei Gesichter: eine Nebenstelle oder eine Notrufnummer, die an ein
/// fremdes System geht (§21.4), oder eine Kundennummer, die dort nicht
/// gefunden wird, weil sie in der falschen Schreibweise ankam.
///
/// Die Fälle folgen §13, wo dieselbe Liste für die Normalisierung verbindlich
/// ist: <c>044 512 84 30</c>, <c>+41445128430</c>, <c>0041445128430</c>,
/// <c>*8010</c>, <c>40</c>, <c>112</c> und eine Auslandsnummer.
/// </summary>
public sealed class PhoneNumberKeyTests
{
    private static readonly NumberNormalizer Schweiz = new("+41");

    [Theory]
    [InlineData("044 512 84 30")]
    [InlineData("+41445128430")]
    [InlineData("0041445128430")]
    [InlineData("0445128430")]
    [InlineData("+41 44 512 84 30")]
    public void Dieselbe_Nummer_ergibt_in_jeder_Schreibweise_denselben_Schluessel(string eingabe)
    {
        var key = PhoneNumberKey.From(eingabe, Schweiz);

        Assert.Equal("+41445128430", key.E164);
        Assert.Equal("41445128430", key.Digits);
        Assert.Equal("0445128430", key.National);
        Assert.False(key.IsInternal);
        Assert.False(key.IsAddress);
    }

    [Theory]
    [InlineData("151")]
    [InlineData("40")]
    [InlineData("*8010")]
    [InlineData("112")]
    [InlineData("144")]
    public void Interne_Ziele_und_Notrufe_bekommen_keine_E164_Form(string eingabe)
    {
        var key = PhoneNumberKey.From(eingabe, Schweiz);

        Assert.True(key.IsInternal);
        Assert.Equal(string.Empty, key.E164);
        Assert.Equal(eingabe, key.National);
    }

    /// <summary>
    /// Der Standardfall aus §21.4: interne Nummern gehen nicht nach aussen.
    /// Die Notrufnummer ist der Grund, warum das kein Feinschliff ist.
    /// </summary>
    [Fact]
    public void Interne_Ziele_sind_ab_Werk_kein_Kandidat_fuer_eine_Abfrage()
    {
        var key = PhoneNumberKey.From("112", Schweiz);

        Assert.False(key.IsLookupCandidate(allowInternal: false));
        Assert.True(key.IsLookupCandidate(allowInternal: true));
    }

    [Fact]
    public void Eine_gewoehnliche_Nummer_ist_ein_Kandidat()
    {
        var key = PhoneNumberKey.From("079 123 45 67", Schweiz);

        Assert.True(key.IsLookupCandidate(allowInternal: false));
        Assert.Equal("+41791234567", key.E164);
    }

    [Fact]
    public void Eine_Auslandsnummer_wird_von_hier_aus_waehlbar_dargestellt()
    {
        var key = PhoneNumberKey.From("0049 211 1234567", Schweiz);

        Assert.Equal("+492111234567", key.E164);
        Assert.Equal("492111234567", key.Digits);

        // Von der Schweiz aus wählt man 00 und die Landesvorwahl.
        Assert.Equal("00492111234567", key.National);
    }

    [Theory]
    [InlineData("sip:151@pbx.bv2.ch")]
    [InlineData("hans.muster@bv2.ch")]
    [InlineData("sips:200@pbx.bv2.ch")]
    public void Eine_Adresse_ist_keine_Nummer_und_wird_nicht_abgefragt(string eingabe)
    {
        var key = PhoneNumberKey.From(eingabe, Schweiz);

        Assert.True(key.IsAddress);
        Assert.Equal(string.Empty, key.E164);
        Assert.False(key.IsLookupCandidate(allowInternal: true));
    }

    /// <summary>
    /// Ohne Länderpräfix wird nicht geraten — dieselbe Haltung wie in
    /// <see cref="NumberNormalizer"/>. Eine Nummer mit führender Null könnte
    /// aus jedem Land stammen.
    /// </summary>
    [Fact]
    public void Ohne_Landespraefix_entsteht_keine_erfundene_E164_Form()
    {
        var ohneLand = new NumberNormalizer(countryPrefix: null);

        var key = PhoneNumberKey.From("0445128430", ohneLand);

        Assert.Equal(string.Empty, key.E164);
        Assert.Equal("445128430", key.Digits);
        Assert.False(key.IsLookupCandidate(allowInternal: false));
    }

    /// <summary>
    /// Eine Nummer, die nach der Normalisierung zu kurz oder zu lang für E.164
    /// ist, bekommt keine — lieber nichts als etwas Erfundenes.
    /// </summary>
    [Theory]
    [InlineData("+4144")]
    [InlineData("+4144512843012345678")]
    public void Was_keine_gueltige_E164_Laenge_hat_bekommt_keine(string eingabe)
    {
        var key = PhoneNumberKey.From(eingabe, Schweiz);

        Assert.Equal(string.Empty, key.E164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Eingabe_gibt_es_nichts_abzufragen(string? eingabe)
    {
        var key = PhoneNumberKey.From(eingabe, Schweiz);

        Assert.Equal(PhoneNumberKey.None, key);
        Assert.False(key.IsLookupCandidate(allowInternal: true));
    }

    [Fact]
    public void Ohne_Normalisierer_wird_nicht_geraten_sondern_geworfen() =>
        Assert.Throws<ArgumentNullException>(() => PhoneNumberKey.From("0791234567", null!));
}
