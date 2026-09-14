using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.App.Converters;

/// <summary>
/// Farbe der Status-LED je Konto (§20.1, §20.2).
///
/// §8.4 verlangt, dass ein Zustand nie <b>nur</b> über Farbe erkennbar ist —
/// deshalb steht in der Kontoauswahl der Name daneben, und der Zustand
/// erscheint zusätzlich als Text in den Einstellungen.
/// </summary>
public sealed partial class RegistrationBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is RegistrationStatus status
            ? status switch
            {
                RegistrationStatus.Registered => "StatusRegisteredBrush",
                RegistrationStatus.InProgress => "StatusProgressBrush",
                RegistrationStatus.Failed => "StatusFailedBrush",
                _ => "PresenceOfflineBrush",
            }
            : "PresenceOfflineBrush";

        return ThemeBrushes.Get(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Die Status-LED wird nur angezeigt, nie zurückgeschrieben.");
}

/// <summary>Symbol für das Ergebnis eines Anrufs (§20.3, vierte Spalte).</summary>
public sealed partial class OutcomeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is CallOutcome outcome
            ? outcome switch
            {
                CallOutcome.Answered => "",   // Pfeil, verbunden
                CallOutcome.Missed => "",     // Kreuz
                CallOutcome.Declined => "",
                CallOutcome.NoAnswer => "",   // Uhr
                CallOutcome.Busy => "",       // Warndreieck
                _ => "",                      // Fehler
            }
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Farbe für das Ergebnis. Verpasste Anrufe stechen heraus — sie sind der
/// Grund, warum jemand die Liste überhaupt öffnet.
/// </summary>
public sealed partial class OutcomeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is CallOutcome outcome
            ? outcome switch
            {
                CallOutcome.Answered => "StatusRegisteredBrush",
                CallOutcome.Missed => "StatusFailedBrush",
                CallOutcome.Failed => "StatusFailedBrush",
                _ => "PresenceOfflineBrush",
            }
            : "PresenceOfflineBrush";

        return ThemeBrushes.Get(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Die Zeile unter der Nummer: <b>Ergebnis</b> und <b>Dauer</b> (§20.3).
///
/// Zwei der vier geforderten Angaben stehen hier zusammen, weil sie im
/// schmalen Fenster nebeneinander keinen Platz hätten und inhaltlich
/// zusammengehören: was passiert ist, und wie lange es dauerte.
/// </summary>
public sealed partial class HistoryDetailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not CallHistoryEntry entry)
        {
            return string.Empty;
        }

        // Die Wörter stehen im Kern (CallOutcomeText) und nicht hier: seit die
        // Karte in der Anrufliste `call.outcome` kennt, gäbe es sie sonst
        // zweimal — und die zweite Kopie wäre die, die beim nächsten Mal
        // vergessen wird.
        var outcome = CallOutcomeText.For(entry.Outcome, entry.Direction);

        var teile = new List<string> { outcome };

        // W1.6 (A3): eine Stelle fuer die Dauer. Hier stand sie ausgeschrieben,
        // in CallFacts noch einmal, und in der Gespraechsansicht in einer
        // anderen Form.
        if (RelativeTime.Duration(entry.Duration) is { Length: > 0 } dauer)
        {
            teile.Add(dauer);
        }

        // Die Nummer, sobald oben ein Name steht (C18).
        //
        // <b>Warum sie dazugehört.</b> Ein Kollege hat Festnetz und Mobil; in
        // der Anrufliste stand zweimal sein Name, und welcher Eintrag welche
        // Nummer war, war nicht zu sehen. Ein Doppelklick ruft zurück — man
        // wusste vorher nicht, wohin.
        //
        // Nur wenn ein Name aufgelöst ist: sonst steht die Nummer schon oben,
        // und zweimal dieselbe Angabe in zwei Zeilen ist Rauschen. Dasselbe
        // Muster wie die Attributionszeile des Toasts (ADR-030).
        if (!string.IsNullOrWhiteSpace(entry.DisplayName)
            && entry.Number is { Length: > 0 } nummer)
        {
            teile.Add(PhoneNumberFormat.ForDisplay(nummer));
        }

        return string.Join(" · ", teile);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Fett, solange ein verpasster Anruf noch niemandem aufgefallen ist
/// (ADR-035).
///
/// <para><b>Ein eigener Konverter und kein <c>FontWeight</c> aus einem Stil:</b>
/// die Zeile wechselt ihren Zustand, während sie sichtbar ist — ein Stil
/// müsste dafür ausgetauscht werden, und ausgetauscht wird hier nichts (die
/// Begründung steht an <c>HistoryRow</c>).</para>
///
/// <para>Die Fettschrift ist nicht die einzige Aussage: der
/// <c>AccessibleName</c> der Zeile sagt „ungelesen" mit. §8.4 verlangt genau
/// das — eine Aussage, die nur an der Darstellung hängt, kommt bei einer
/// Sprachausgabe nicht an.</para>
/// </summary>
public sealed partial class NewToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Farbe der Präsenzlampe im Besetztlampenfeld (§8.4).
///
/// Die Farbe ist die halbe Information. Die andere Hälfte steht als Text
/// daneben (<c>ContactRow.PresenceText</c>) — §8.4 verlangt das ausdrücklich,
/// und ein Kontrastthema hebt die Farbunterschiede ohnehin auf.
///
/// <para>Mit <c>ConverterParameter="border"</c> liefert er den <b>Rand einer
/// Kachel</b> (ADR-047). Der Unterschied betrifft genau einen Fall: ist der
/// Zustand unbekannt, kommt die gewöhnliche Kartenrandfarbe zurück und nicht
/// das Grau der Lampe. Eine Kachel mit grauem Sonderrand behauptete sonst
/// einen Zustand, den niemand beobachtet — dieselbe Überlegung, aus der
/// <c>ContactRow.HasPresence</c> die Lampe ganz weglässt.</para>
/// </summary>
public sealed partial class PresenceBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var alsRand = parameter is string text
            && text.Equals("border", StringComparison.OrdinalIgnoreCase);

        var key = value is PresenceStatus presence
            ? presence switch
            {
                PresenceStatus.Available => "PresenceAvailableBrush",
                PresenceStatus.Ringing => "PresenceRingingBrush",
                PresenceStatus.OnCall => "PresenceBusyBrush",
                PresenceStatus.Away => "PresenceRingingBrush",
                PresenceStatus.Offline => "PresenceOfflineBrush",
                _ => alsRand ? "CardOutlineBrush" : "PresenceUnknownBrush",
            }
            : alsRand ? "CardOutlineBrush" : "PresenceUnknownBrush";

        return ThemeBrushes.Get(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Die Präsenzlampe wird nur angezeigt, nie zurückgeschrieben.");
}

/// <summary>
/// <c>bool</c> nach <see cref="Visibility"/>.
///
/// WinUI bringt einen solchen Konverter nicht mit — anders als WPF. Mit
/// <c>ConverterParameter="invert"</c> wird die Bedingung umgedreht, damit nicht
/// für jeden Gegenfall eine zweite Eigenschaft im ViewModel entstehen muss.
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;

        if (parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility visibility && visibility == Visibility.Visible;
}

/// <summary>
/// Zeigt etwas nur, wenn es da ist: <c>null</c> und leere Zeichenketten werden
/// zu <see cref="Visibility.Collapsed"/>.
///
/// Für optionale Angaben, die als eigene Zeile erscheinen — eine leere Zeile
/// mit nichts darin sieht aus wie ein Darstellungsfehler.
/// </summary>
public sealed partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string text
            ? text.Length > 0 ? Visibility.Visible : Visibility.Collapsed
            : value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Nur zur Anzeige.");
}

/// <summary>
/// Der Registrierungszustand eines Kontos als <b>Text</b> (§8.4).
///
/// „Die Anzeige erfolgt farblich und als Text — nie nur über Farbe." Die LED
/// daneben zeigt dasselbe in Farbe; wer Rot und Grün nicht unterscheidet,
/// liest hier, was los ist.
///
/// Im Fehlerfall steht der erklärte Grund dabei (§15: Ursache und Abhilfe,
/// nicht der rohe SDK-Text) — <c>SipErrorCatalog</c> hat ihn beim Auslösen
/// des Ereignisses schon übersetzt.
///
/// <b>Dieser Konverter hat gefehlt</b> und die Einstellungsseite beim ersten
/// Öffnen mit <c>Cannot find a Resource with the Name/Key</c> abstürzen
/// lassen. XAML löst Ressourcen erst zur Laufzeit auf; der Compiler schweigt
/// dazu. Ein Test in <c>Nipp.Architecture.Tests</c> gleicht deshalb jetzt alle
/// <c>StaticResource</c>-Schlüssel gegen die Wörterbücher ab.
/// </summary>
public sealed partial class AccountStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AccountStatus account)
        {
            return string.Empty;
        }

        // Dieselben Woerter wie ueberall sonst — der Wortlaut steht in
        // AccountStateText und nur dort (ADR-044). Hier ohne den Nachsatz
        // „Anrufe sind nicht moeglich": die Spalte neben der Lampe ist knapp,
        // und in der Hauptansicht steht er ohnehin.
        var state = Nipp.Core.ViewModels.AccountStateCatalog.Of(account.Status);

        // Die Meldung nur zeigen, wenn sie etwas hinzufügt. Bei einer
        // erfolgreichen Registrierung liefert das SDK „Registration
        // successful" — das doppelt nur, was links schon steht.
        if (account.Message is not { Length: > 0 } message
            || account.Status is not RegistrationStatus.Failed)
        {
            return state;
        }

        // Im Fehlerfall steht die erklärte Meldung für sich. Ein
        // vorangestelltes „Fehlgeschlagen · " davor ergab in der Anzeige
        // „Fehlgeschlagen · Registrierung … fehlgeschlagen: …" — dasselbe Wort
        // zweimal in einer Zeile, die ohnehin knapp ist.
        return message;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Der Kontozustand wird nur angezeigt, nie zurückgeschrieben.");
}

/// <summary>
/// Wann ein Anruf war — nicht nur, um welche Uhrzeit (§20.3, §8.3).
///
/// „07:05" ist nach einem Wochenende wertlos: heute, gestern oder letzte Woche
/// sehen gleich aus. Die Spalte ist schmal, deshalb so wenig wie möglich und
/// so viel wie nötig:
///
/// <list type="bullet">
///   <item>heute → <c>07:05</c></item>
///   <item>gestern → <c>gestern</c></item>
///   <item>diese Woche → <c>Mo 07:05</c></item>
///   <item>älter → <c>01.09.</c></item>
///   <item>anderes Jahr → <c>01.09.25</c></item>
/// </list>
/// </summary>
public sealed partial class RelativeDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var moment = value switch
        {
            DateTimeOffset offset => offset.ToLocalTime(),
            DateTime local => new DateTimeOffset(local.ToLocalTime()),
            _ => (DateTimeOffset?)null,
        };

        if (moment is not { } when)
        {
            return string.Empty;
        }

        var culture = CultureInfo.CurrentCulture;
        var today = DateTimeOffset.Now.Date;
        var day = when.Date;

        if (day == today)
        {
            return when.ToString("HH:mm", culture);
        }

        if (day == today.AddDays(-1))
        {
            return "gestern";
        }

        // Innerhalb der letzten Woche sagt der Wochentag mehr als ein Datum:
        // „Mo" ist sofort einzuordnen, „01.09." muss man nachrechnen.
        if (day > today.AddDays(-7))
        {
            return $"{culture.DateTimeFormat.AbbreviatedDayNames[(int)when.DayOfWeek]} {when:HH:mm}";
        }

        return when.Year == today.Year
            ? when.ToString("dd.MM.", culture)
            : when.ToString("dd.MM.yy", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Der Zeitpunkt eines Anrufs wird nur angezeigt.");
}
