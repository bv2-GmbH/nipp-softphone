using Nipp.Core.Services.Contacts;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// §13 und AP6.4: die Nummernauflösung ist testpflichtig.
///
/// Getestet wird der reine Kern — <see cref="ClipResolver.IsSameNumber"/> und
/// <see cref="ClipResolver.DigitsOnly"/>. Was darüber liegt, ist eine Schleife
/// über eine Liste; was darunter schiefgeht, zeigt keinem Benutzer den
/// richtigen Namen an.
///
/// Der Fall, um den es eigentlich geht: dieselbe Person ruft an, und ihre
/// Nummer steht in Outlook anders als die Anlage sie meldet.
/// </summary>
public sealed class ClipResolverTests
{
    [Theory]
    // Die vier Schreibweisen derselben Zürcher Nummer.
    [InlineData("+41445128430", "0445128430")]
    [InlineData("+41445128430", "044 512 84 30")]
    [InlineData("+41445128430", "0041445128430")]
    [InlineData("044 512 84 30", "+41 44 512 84 30")]
    // Aus der Signalisierung kommt eine SIP-Adresse, aus Outlook eine Nummer.
    [InlineData("sip:+41445128430@pbx.example.ch", "044 512 84 30")]
    [InlineData("sips:0445128430@pbx.example.ch", "+41445128430")]
    // Mit Anzeigename davor, wie ihn manche Anlagen liefern.
    [InlineData("<sip:0445128430@pbx.example.ch>", "+41445128430")]
    public void ErkenntDieselbeNummerInVerschiedenenSchreibweisen(string left, string right) =>
        Assert.True(ClipResolver.IsSameNumber(left, right));

    [Theory]
    // Zwei verschiedene Anschlüsse derselben Firma.
    [InlineData("+41445128430", "+41445128431")]
    // Gleiche Endziffern, andere Vorwahl — Zürich gegen Bern.
    [InlineData("+41445128430", "+41315128430")]
    // Gleiche Endziffern, anderes Land: der Outlook-Kontakt «044 512 84 30»
    // wird zu «445128430», und ein Anruf aus Berlin endet genau darauf. Ohne
    // die Obergrenze für den Längenunterschied bekam der Berliner Anrufer den
    // Namen des Zürcher Kontakts.
    [InlineData("044 512 84 30", "+49 30 445128430")]
    [InlineData("+41445128430", "+1 555 41445128430")]
    [InlineData("", "+41445128430")]
    [InlineData("+41445128430", "")]
    [InlineData("anonymous", "+41445128430")]
    public void UnterscheidetVerschiedeneNummern(string left, string right) =>
        Assert.False(ClipResolver.IsSameNumber(left, right));

    /// <summary>
    /// Der Grund für die Mindestlänge von sieben Ziffern. Ohne sie würde die
    /// interne Nebenstelle 430 auf jede Nummer passen, die auf 430 endet — und
    /// jeder zwölfte Anruf von aussen erschiene als Kollege.
    /// </summary>
    [Fact]
    public void KurzeNebenstelleTrifftNichtAufEineLangeNummerZu()
    {
        Assert.False(ClipResolver.IsSameNumber("430", "+41445128430"));
        Assert.False(ClipResolver.IsSameNumber("151", "+41791234151"));
    }

    [Fact]
    public void KurzeNebenstelleTrifftAufSichSelbstZu()
    {
        Assert.True(ClipResolver.IsSameNumber("430", "430"));
        Assert.True(ClipResolver.IsSameNumber("sip:151@pbx.example.ch", "151"));
    }

    [Theory]
    [InlineData("+41 44 512 84 30", "41445128430")]
    [InlineData("0041-44-512-84-30", "00414451284 30")]
    [InlineData("sip:151@pbx.example.ch", "151")]
    [InlineData("sips:+41791112233@bv2.ch", "41791112233")]
    [InlineData("", "")]
    [InlineData("keine Ziffern", "")]
    public void ZiehtNurDieZiffernHeraus(string input, string expectedWithSpaces)
    {
        var expected = expectedWithSpaces.Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.Equal(expected, ClipResolver.DigitsOnly(input));
    }

    /// <summary>
    /// Die Domäne darf nicht mitzählen. Ohne das Abschneiden am <c>@</c> würde
    /// eine Anlage unter <c>pbx1.bv2.ch</c> die 1 aus dem Hostnamen in die
    /// Nummer hineinziehen.
    /// </summary>
    [Fact]
    public void IgnoriertZiffernInDerDomaene()
    {
        Assert.Equal("151", ClipResolver.DigitsOnly("sip:151@pbx1.bv2.ch"));
        Assert.True(ClipResolver.IsSameNumber("sip:151@pbx1.bv2.ch", "sip:151@pbx2.bv2.ch"));
    }

    [Fact]
    public void NullIstNichtsUndTrifftAufNichtsZu()
    {
        Assert.Equal(string.Empty, ClipResolver.DigitsOnly(null));
        Assert.False(ClipResolver.IsSameNumber(null, "+41445128430"));
        Assert.False(ClipResolver.IsSameNumber(null, null));
    }
}
