using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nipp.App.Controls;

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
    /// <summary>Eine Taste wurde gedrückt. Trägt das Zeichen als Zeichenfolge.</summary>
    public event EventHandler<string>? KeyPressed;

    public Keypad()
    {
        InitializeComponent();
    }

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            KeyPressed?.Invoke(this, key);
        }
    }
}
