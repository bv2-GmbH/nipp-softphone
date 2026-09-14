using System.Globalization;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Wie lange etwas her ist, in einem Wort (ADR-046).
///
/// <para><b>Grob und nicht genau:</b> „vor 3 Std." hilft beim Wiedererkennen,
/// „vor 3 Std. 14 Min." nicht mehr, und die Spalte ist schmal.</para>
///
/// <para>Herausgelöst aus <c>ShellViewModel</c>, weil die Zeile der Anrufliste
/// dieselbe Angabe für die Sprachausgabe braucht: der Name auf dem
/// umschliessenden Raster überdeckt die Zeitspalte, und ohne diese Angabe
/// hörte man nicht, wann ein Anruf war. Eine zweite Kopie wäre die dritte
/// Stelle für dieselbe Regel gewesen.</para>
/// </summary>
public static class RelativeTime
{
    /// <summary>
    /// Der Abstand zwischen <paramref name="when"/> und <paramref name="now"/>
    /// als Wort.
    /// </summary>
    /// <param name="now">
    /// Der Bezugszeitpunkt — als Parameter und nicht aus der Uhr geholt, damit
    /// ein Test ihn setzen kann.
    /// </param>
    public static string Describe(DateTimeOffset when, DateTimeOffset now)
    {
        var age = now - when;

        return age switch
        {
            { TotalMinutes: < 1 } => "gerade eben",
            { TotalMinutes: < 60 } => $"vor {(int)age.TotalMinutes} Min.",
            { TotalHours: < 24 } => $"vor {(int)age.TotalHours} Std.",
            { TotalDays: < 2 } => "gestern",
            { TotalDays: < 7 } => $"vor {(int)age.TotalDays} Tagen",
            _ => when.ToLocalTime().ToString("dd.MM.", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>
    /// Eine Gesprächsdauer, wie sie überall in nipp aussieht (W1.6, Befund A3).
    ///
    /// <para><b>Sie stand dreimal ausgeschrieben, und einmal anders.</b> Der
    /// Konverter der Anrufliste und die Anruferkarte schrieben <c>m:ss</c>,
    /// die Gesprächsansicht <c>mm:ss</c> — dieselbe Dauer sah im Gespräch
    /// anders aus als hinterher in der Liste. Das fällt niemandem auf, der
    /// beide nicht nebeneinander sieht, und genau deshalb blieb es
    /// stehen.</para>
    ///
    /// <para><b>Ohne führende Null</b>, also <c>3:07</c> und nicht
    /// <c>03:07</c>. Das ist die Form, die schon an zwei der drei Stellen
    /// stand; ab einer Stunde kommt <c>h:mm:ss</c>.</para>
    ///
    /// <para><c>null</c> und eine Dauer von null ergeben eine leere
    /// Zeichenfolge: ein Anruf, der nie verbunden war, hat keine Dauer, und
    /// «0:00» behauptete eine.</para>
    /// </summary>
    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } dauer || dauer == TimeSpan.Zero)
        {
            return string.Empty;
        }

        return dauer.TotalHours >= 1
            ? dauer.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : dauer.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
