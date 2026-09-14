using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Nipp.App.Converters;

/// <summary>
/// Holt einen Pinsel aus den Anwendungsressourcen — <b>ohne zu werfen</b>.
///
/// <para><b>Warum es diese Klasse gibt.</b> Der Indexer
/// <c>Application.Current.Resources[key]</c> wirft bei einem fehlenden
/// Schlüssel, und diese Aufrufe stehen in Konvertern und in Code-Behind, also
/// mitten im Zeichnen einer Seite. Genau daran ist nipp schon einmal gar nicht
/// mehr gestartet: ein Schlüssel fehlte, die Ausnahme kam aus dem Konstruktor
/// von <c>MainWindow</c>, und es gab kein Fenster und keine Meldung — nur
/// einen Eintrag im Protokoll.</para>
///
/// <para><c>XamlResourceTests</c> prüft die Verweise im XAML. Diese hier stehen
/// als Zeichenfolgen im C#-Code, und dort greift der Test nicht. Ein Tippfehler
/// wäre also weiterhin erst zur Laufzeit zu sehen — aber als grauer Punkt statt
/// als Absturz.</para>
/// </summary>
internal static class ThemeBrushes
{
    /// <summary>
    /// Der Pinsel zum Schlüssel, oder ein unauffälliger Ersatz.
    ///
    /// <para>Grau und nicht rot: ein fehlender Schlüssel ist ein Fehler im
    /// Programm, kein Zustand des Telefons. Rot würde eine Störung behaupten,
    /// die es nicht gibt.</para>
    /// </summary>
    public static Brush Get(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }

        if (Application.Current.Resources.TryGetValue("PresenceOfflineBrush", out var fallback)
            && fallback is Brush offline)
        {
            return offline;
        }

        return new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 128, 128, 128));
    }
}
