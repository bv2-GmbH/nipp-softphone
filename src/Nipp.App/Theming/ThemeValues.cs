using Microsoft.UI.Xaml;

namespace Nipp.App.Theming;

/// <summary>
/// Werte aus dem Ressourcenwörterbuch für Code, der ohne XAML zeichnet
/// (W2.6, Befund D10).
///
/// <para><b>Warum es das gibt.</b> <c>CardView</c> und der Karten-Designer
/// bauen ihre Ränder im Code — eine Karte hat keine feste Form, also gibt es
/// kein XAML dafür. Beide schrieben ihren Eckradius als <c>new
/// CornerRadius(4)</c> aus, also dieselbe Zahl, die in der Fluent-Ressource
/// steht, an zwei Stellen ein zweites Mal. Ein geänderter Radius hätte die
/// Karten stehen lassen, ohne dass etwas bricht — und niemand hätte den
/// Unterschied gesucht, weil vier Pixel Rundung keine Meldung wert sind.</para>
///
/// <para><b>Der Rückfallwert steht im Aufruf und nicht hier.</b> Was ein
/// fehlender Schlüssel bedeuten soll, weiss die zeichnende Stelle; ein
/// Standardwert an dieser Stelle wäre die dritte Wahrheit über denselben
/// Radius. Ein fehlender Schlüssel ist im Übrigen ein Fehler im Programm und
/// kein Zustand des Telefons — er darf deshalb aussehen wie ein Rechteck und
/// muss nicht werfen.</para>
/// </summary>
internal static class ThemeValues
{
    /// <summary>Der Eckradius zum Schlüssel, oder <paramref name="ersatz"/>.</summary>
    public static CornerRadius Radius(string key, double ersatz) =>
        Application.Current.Resources.TryGetValue(key, out var wert) && wert is CornerRadius radius
            ? radius
            : new CornerRadius(ersatz);
}
