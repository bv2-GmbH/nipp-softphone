using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Das Gegenstück zu <see cref="NumberNormalizerTests"/>: dort wird eine
/// Nummer wählbar gemacht, hier lesbar.
///
/// Die Regel, um die es geht: <b>lieber ungruppiert als falsch gruppiert.</b>
/// Eine Nummer, die nicht erkannt wird, kommt unverändert zurück. Eine falsch
/// getrennte Nummer lässt jemanden glauben, er habe sich verwählt.
/// </summary>
public sealed class PhoneNumberFormatTests
{
    [Theory]
    // Schweizer Mobilnummern, international geschrieben.
    [InlineData("+41786672728", "+41 78 667 27 28")]
    [InlineData("+41795872371", "+41 79 587 23 71")]
    // Festnetz.
    [InlineData("+41445128430", "+41 44 512 84 30")]
    // Schon gruppiert: das Ergebnis ist dasselbe, die Funktion ist idempotent.
    [InlineData("+41 78 667 27 28", "+41 78 667 27 28")]
    // Mit der alten Auslandsvorwahl.
    [InlineData("0041786672728", "+41 78 667 27 28")]
    // National mit führender Null.
    [InlineData("0797540803", "079 754 08 03")]
    [InlineData("079 754 08 03", "079 754 08 03")]
    [InlineData("0445128430", "044 512 84 30")]
    public void GruppiertSchweizerNummern(string input, string expected) =>
        Assert.Equal(expected, PhoneNumberFormat.ForDisplay(input));

    /// <summary>
    /// Eine Nebenstelle ist keine Rufnummer. „151" in Dreiergruppen zu
    /// zerlegen macht sie nicht lesbarer, und „15 1" wäre schlicht falsch.
    /// </summary>
    [Theory]
    [InlineData("151")]
    [InlineData("40")]
    [InlineData("*8010")]
    public void LaesstKurzeZieleUnveraendert(string input) =>
        Assert.Equal(input, PhoneNumberFormat.ForDisplay(input));

    /// <summary>
    /// Was nicht sicher erkannt wird, bleibt unverändert. Eine deutsche oder
    /// amerikanische Nummer nach Schweizer Schema zu gruppieren wäre falsch,
    /// und falsch ist schlechter als ungruppiert.
    /// </summary>
    [Theory]
    [InlineData("+4930123456789")]
    [InlineData("+12125551234")]
    [InlineData("sip:151@pbx.example.ch")]
    [InlineData("hotline")]
    public void LaesstUnbekannteFormenUnveraendert(string input) =>
        Assert.Equal(input, PhoneNumberFormat.ForDisplay(input));

    [Fact]
    public void LeeresBleibtLeer()
    {
        Assert.Equal(string.Empty, PhoneNumberFormat.ForDisplay(null));
        Assert.Equal(string.Empty, PhoneNumberFormat.ForDisplay(""));
        Assert.Equal(string.Empty, PhoneNumberFormat.ForDisplay("   "));
    }

    /// <summary>
    /// Dieselbe Nummer in zwei Schreibweisen muss gleich aussehen — sonst
    /// wirken zwei Einträge derselben Person in der Liste wie zwei Personen.
    /// </summary>
    [Fact]
    public void GleicheNummerSiehtGleichAus()
    {
        var a = PhoneNumberFormat.ForDisplay("+41786672728");
        var b = PhoneNumberFormat.ForDisplay("0041786672728");
        var c = PhoneNumberFormat.ForDisplay("+41 786 672 728");

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }
}
