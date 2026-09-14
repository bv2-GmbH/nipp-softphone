using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// AP7.6: das Kürzel muss aus der Einstellungsdatei kommen, und ein
/// unbrauchbarer Wert darf nicht stillschweigend zu irgendetwas führen.
///
/// Getestet wird nur das Zerlegen — <c>RegisterHotKey</c> selbst braucht eine
/// Fensterstation und gehört in die Abnahme, nicht in einen Komponententest.
/// </summary>
public sealed class GlobalHotkeyTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    [Theory]
    // Der Standard aus §9.6, in beiden Schreibweisen.
    [InlineData("Ctrl+Shift+A", ModControl | ModShift, 'A')]
    [InlineData("Strg+Umschalt+A", ModControl | ModShift, 'A')]
    [InlineData("ctrl+shift+a", ModControl | ModShift, 'A')]
    // Leerzeichen um die Pluszeichen, wie sie beim Tippen entstehen.
    [InlineData("Ctrl + Alt + N", ModControl | ModAlt, 'N')]
    [InlineData("Win+Shift+1", ModWin | ModShift, '1')]
    public void ZerlegtGebraeuchlicheKuerzel(string input, uint expectedModifiers, char expectedKey)
    {
        Assert.True(GlobalHotkeyService.TryParse(input, out var modifiers, out var key));
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal((uint)expectedKey, key);
    }

    [Fact]
    public void KenntDieFunktionstasten()
    {
        Assert.True(GlobalHotkeyService.TryParse("Ctrl+F12", out _, out var key));
        Assert.Equal(0x7Bu, key);

        Assert.True(GlobalHotkeyService.TryParse("Alt+F1", out _, out var first));
        Assert.Equal(0x70u, first);
    }

    /// <summary>
    /// Ein Kürzel ohne Modifikator würde jeden Tastendruck im ganzen System
    /// abfangen. Das ist kein Kürzel, sondern ein Tastaturdefekt — und muss
    /// deshalb abgelehnt werden, nicht registriert.
    /// </summary>
    [Theory]
    [InlineData("A")]
    [InlineData("F5")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Umlaut")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LehntUnbrauchbareKuerzelAb(string? input) =>
        Assert.False(GlobalHotkeyService.TryParse(input, out _, out _));

    [Fact]
    public void KenntBenannteTastenInBeidenSprachen()
    {
        Assert.True(GlobalHotkeyService.TryParse("Ctrl+Space", out _, out var space));
        Assert.True(GlobalHotkeyService.TryParse("Strg+Leertaste", out _, out var leertaste));
        Assert.Equal(space, leertaste);
        Assert.Equal(0x20u, space);
    }
}
