using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nipp.App.Controls;

/// <summary>
/// Ein Tastendruck auf der Wähltastatur: welche Taste, und womit gedrückt.
///
/// <para><b>Warum die Herkunft mitkommt</b> (Befund A1-12, 17.09.2026): wer mit
/// Tabulator auf einer Taste steht und die Leertaste drückt, will dort bleiben;
/// wer mit der Maus tippt, will danach im Nummernfeld stehen. Die Seite kann
/// das nicht mehr selbst herausfinden — <b>Windows setzt den Fokus beim
/// Mausklick auf den Knopf, bevor <c>Click</c> feuert</b>, und danach sehen
/// beide Fälle gleich aus. Also sagt es die Taste, die es weiss.</para>
/// </summary>
/// <param name="Key">Das Zeichen der Taste, etwa <c>"5"</c> oder <c>"#"</c>.</param>
/// <param name="VonTastatur">
/// Ob der Druck von der Tastatur kam. <c>false</c> bei Maus, Stift und Finger.
/// </param>
public readonly record struct KeypadPress(string Key, bool VonTastatur);

/// <summary>
/// Wähltastatur 3×4 mit Buchstabenzeile (§8.1).
///
/// Bewusst ohne eigene Zustandsverwaltung: das Control meldet nur, welche
/// Taste gedrückt wurde. Was damit geschieht — an die Nummer anhängen oder als
/// DTMF senden — entscheidet die Seite. Dadurch lässt es sich auf der
/// Wählseite und in der Gesprächsansicht gleichermassen verwenden (§8.2
/// verlangt dort ein DTMF-Feld).
/// </summary>
public sealed partial class Keypad : UserControl
{
    /// <summary>Eine Taste wurde gedrückt.</summary>
    public event EventHandler<KeypadPress>? KeyPressed;

    public Keypad()
    {
        InitializeComponent();
    }

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } taste)
        {
            return;
        }

        // FocusState sagt, WOMIT der Fokus auf diese Taste kam: Tastatur,
        // Zeiger oder programmatisch. Bei einem Mausklick ist es Pointer, beim
        // Weitergehen mit Tabulator oder Pfeiltaste Keyboard — und genau das
        // ist der Unterschied, den die Seite braucht.
        //
        // Gemessen wird hier und nicht in der Seite, weil der Knopf die
        // einzige Stelle ist, die es noch weiss.
        KeyPressed?.Invoke(this, new KeypadPress(key, taste.FocusState == FocusState.Keyboard));
    }
}
