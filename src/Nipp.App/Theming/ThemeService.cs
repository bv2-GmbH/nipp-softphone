using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Nipp.Core.Services.Settings;

namespace Nipp.App.Theming;

/// <summary>
/// Steuert das Erscheinungsbild (§20.4): hell, dunkel oder wie Windows.
///
/// <b>Standard ist „wie Windows"</b> — und zwar nicht nur beim Start: eine
/// Änderung der Systemeinstellung wird im laufenden Betrieb nachgezogen.
///
/// WinUI bietet dafür keinen Ereignishaken an, der bei einem Wechsel im
/// Betriebssystem zuverlässig feuert. Deshalb hängt diese Klasse an
/// <c>SystemEvents.UserPreferenceChanged</c> — dasselbe Ereignis, über das
/// Windows auch Schriftgrössen und Farbschemata meldet.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ILogger<ThemeService> _logger;
    private FrameworkElement? _root;
    private AppTheme _preference = AppTheme.System;
    private bool _disposed;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
    }

    /// <summary>Wird ausgelöst, wenn sich das wirksame Erscheinungsbild ändert.</summary>
    public event EventHandler<ElementTheme>? EffectiveThemeChanged;

    /// <summary>Das tatsächlich angewandte Erscheinungsbild.</summary>
    public ElementTheme EffectiveTheme { get; private set; } = ElementTheme.Default;

    /// <summary>Ob gerade dunkel dargestellt wird — für die Wahl des Symbols.</summary>
    public bool IsDark => EffectiveTheme == ElementTheme.Dark
        || (EffectiveTheme == ElementTheme.Default && !IsSystemLight());

    /// <summary>
    /// Verbindet den Dienst mit dem Wurzelelement des Fensters. Ohne das kann
    /// er nichts setzen — WinUI kennt kein anwendungsweites Erscheinungsbild,
    /// es hängt am Element.
    /// </summary>
    public void Attach(FrameworkElement root, AppTheme preference)
    {
        _root = root;
        _preference = preference;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ApplyCore();
    }

    /// <summary>Setzt die Vorliebe des Benutzers und wendet sie an.</summary>
    public void SetPreference(AppTheme preference)
    {
        _preference = preference;
        ApplyCore();
        ThemeLog.PreferenceChanged(_logger, preference.ToString(), EffectiveTheme.ToString());
    }

    /// <summary>
    /// Ob Windows im Kontrastmodus läuft.
    ///
    /// <para>Der Aufruf kann in ungewöhnlichen Umgebungen fehlschlagen (kein
    /// Fenster, kein Sitzungskontext). Dann gilt „kein Kontrastmodus" — das ist
    /// der bisherige Zustand und damit die harmlose Annahme.</para>
    /// </summary>
    private bool IsHighContrast()
    {
        try
        {
            return new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        }
        catch (Exception ex)
        {
            // Erwartet auf einem System ohne diese API — aber einmal je
            // Sitzung sichtbar (W1.7). Ohne die Zeile waere «der
            // Kontrastmodus wirkt nicht» nicht von «er ist aus» zu
            // unterscheiden.
            QuietFailures.Report(_logger, "LiestKontrastmodus", ex);
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // W1.4 (D4): der Kontrastmodus gilt unabhaengig von der Vorliebe.
        //
        // <b>Hier stand bis zum 13.09.2026 nur die Pruefung darunter</b>, und
        // sie verwarf das Ereignis, sobald der Benutzer «Hell» oder «Dunkel»
        // fest eingestellt hatte. Wer den Kontrastmodus einschaltete, bekam
        // dann gar keine Reaktion — und das trifft genau die Leute, die ihn
        // brauchen: wer ihn benutzt, hat oft auch ein festes Thema gewaehlt.
        if (e.Category is UserPreferenceCategory.Accessibility)
        {
            _root?.DispatcherQueue.TryEnqueue(ApplyStatusBrushes);
            return;
        }

        // Nur bei „wie Windows" ist ein Systemwechsel überhaupt von Belang.
        if (_preference != AppTheme.System)
        {
            return;
        }

        // Nur die allgemeine Kategorie: das Ereignis feuert auch bei
        // Maus-, Schrift- und Energieeinstellungen, und jedes Mal wurde das
        // Symbol im Infobereich neu geladen — ein Handle-Zyklus fuer nichts.
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
        {
            return;
        }

        // Das Ereignis kommt nicht auf dem UI-Thread. Ohne Marshalling wirft
        // jeder Zugriff auf RequestedTheme.
        _root?.DispatcherQueue.TryEnqueue(() =>
        {
            ApplyCore();
            ThemeLog.SystemChanged(_logger, EffectiveTheme.ToString());
        });
    }

    private void ApplyCore()
    {
        if (_root is null)
        {
            return;
        }

        var theme = _preference switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,

            // ElementTheme.Default heisst „nimm, was das System sagt" — WinUI
            // löst das selbst auf. Nachrechnen müssen wir nur für IsDark, weil
            // die Symbolwahl den konkreten Wert braucht.
            _ => ElementTheme.Default,
        };

        _root.RequestedTheme = theme;
        EffectiveTheme = theme;

        ApplyStatusBrushes();

        EffectiveThemeChanged?.Invoke(this, theme);
    }

    /// <summary>
    /// Färbt die Status- und Präsenzpinsel nach dem <b>gewählten</b>
    /// Erscheinungsbild um.
    ///
    /// <b>Warum das nötig ist.</b> Die Pinsel liegen in
    /// <c>Application.Current.Resources</c> und beziehen ihre Farbe über
    /// <c>ThemeResource</c>. Auf Anwendungsebene löst <c>ThemeResource</c>
    /// aber nach <c>Application.RequestedTheme</c> auf — und das ist das
    /// Systemthema, nicht das, was §20.4 den Benutzer wählen lässt. Wer bei
    /// hellem Windows „Dunkel" einstellte, bekam Lampen und Chips in den
    /// Farben des hellen Themas: <c>#107C10</c> auf dunklem Grund liegt bei
    /// etwa 2,5:1 Kontrast und ist damit unter jeder Lesbarkeitsgrenze.
    ///
    /// Der Weg über die Instanz statt über die Ressourcenauflösung ist
    /// Absicht: dieselbe <see cref="SolidColorBrush"/>-Instanz hängt überall,
    /// wo der Pinsel verwendet wird, und eine geänderte Farbe zieht ohne
    /// weiteres Zutun durch die ganze Oberfläche.
    ///
    /// <b>Im Kontrastmodus geschieht das nicht.</b> Dort gelten die
    /// Systemfarben aus dem HighContrast-Wörterbuch in <c>Tokens.xaml</c> — und
    /// die wurden hier zur Laufzeit wieder überschrieben, sodass das Wörterbuch
    /// wirkungslos war. Wer den Kontrastmodus einschaltet, hat einen Grund
    /// dafür, und der wiegt schwerer als eine einheitliche Statusfarbe.
    /// </summary>
    private void ApplyStatusBrushes()
    {
        // W1.4 (Befund D4): im Kontrastmodus gilt das HighContrast-Woerterbuch
        // — und zwar auch dann, wenn er im Betrieb eingeschaltet wird.
        //
        // <b>Hier stand bis zum 13.09.2026 nur ein return.</b> Das liess die
        // Pinsel stehen, die vorher per brush.Color umgefaerbt worden waren:
        // wer den Kontrastmodus einschaltete, behielt Gelb, Gruen und Rot aus
        // dem vorherigen Thema. Das Woerterbuch wirkte nur, wenn der Modus
        // beim Start schon an war — also genau dann nicht, wenn jemand ihn
        // gerade braucht.
        var quelle = IsHighContrast()
            ? FindThemeDictionary("HighContrast")
            : FindThemeDictionary(IsDark ? "Dark" : "Light");

        if (IsHighContrast())
        {
            ThemeLog.HighContrastRespected(_logger);
        }

        var source = quelle;

        if (source is null)
        {
            ThemeLog.BrushDictionaryMissing(_logger);
            return;
        }

        foreach (var name in ThemedBrushNames)
        {
            // TryGetValue, nicht der Indexer: der wirft bei einem fehlenden
            // Schluessel eine Ausnahme, und weil das hier aus dem Konstruktor
            // von MainWindow laeuft, nimmt sie die ganze Anwendung mit —
            // nipp startet dann gar nicht mehr.
            if (source.TryGetValue(name + "Color", out var value)
                && value is global::Windows.UI.Color color
                && Application.Current.Resources.TryGetValue(name + "Brush", out var target)
                && target is SolidColorBrush brush)
            {
                brush.Color = color;
            }
        }
    }

    /// <summary>
    /// Die Pinsel, deren Farbe vom Erscheinungsbild abhängt. Der Name ist
    /// jeweils der gemeinsame Stamm von <c>…Color</c> und <c>…Brush</c> in
    /// <c>Themes/Tokens.xaml</c>.
    /// </summary>
    private static readonly string[] ThemedBrushNames =
    [
        "PresenceAvailable",
        "PresenceRinging",
        "PresenceBusy",
        "PresenceOffline",
        "PresenceUnknown",
        "StatusRegistered",
        "StatusProgress",
        "StatusFailed",
        "RecordingIndicator",
        "EncryptionSecure",
        "EncryptionInsecure",

        // W1.4 (D2): die Farben, die CardView und die Konverter im Code
        // zeichnen. Sie standen vorher als WinUI-Pinsel da und folgten damit
        // dem Systemthema statt der Wahl des Benutzers.
        "CardSecondaryText",
        "CardDivider",
        "CardOutline",
        "CardFill",
    ];

    /// <summary>
    /// Sucht ein Themenwörterbuch in den zusammengeführten Wörterbüchern der
    /// Anwendung. <c>Tokens.xaml</c> ist eines davon, also stehen seine
    /// ThemeDictionaries nicht direkt an <c>Application.Resources</c>.
    /// </summary>
    private static ResourceDictionary? FindThemeDictionary(string theme)
    {
        foreach (var merged in Application.Current.Resources.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue(theme, out var value)
                && value is ResourceDictionary dictionary
                && dictionary.ContainsKey(ProbeKey))
            {
                return dictionary;
            }
        }

        return null;
    }

    /// <summary>
    /// Woran ein Themenwörterbuch als <b>unseres</b> zu erkennen ist.
    ///
    /// <b>Warum das nötig ist.</b> <c>XamlControlsResources</c> steht in
    /// <c>App.xaml</c> vor <c>Tokens.xaml</c> und bringt eigene
    /// ThemeDictionaries für „Light", „Dark" und „HighContrast" mit. Wer nur
    /// nach dem Namen sucht, findet also das der Steuerelemente — und dort
    /// gibt es keinen einzigen unserer Schlüssel.
    /// </summary>
    private static readonly string ProbeKey = ThemedBrushNames[0] + "Color";

    /// <summary>
    /// Liest aus der Registrierung, ob Windows hell darstellt.
    ///
    /// <c>AppsUseLightTheme</c> ist der Wert für Anwendungen; daneben gibt es
    /// <c>SystemUsesLightTheme</c> für Taskleiste und Startmenü. Für nipp zählt
    /// der erste.
    /// </summary>
    private bool IsSystemLight()
    {
        try
        {
            var value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
            return value is int i ? i != 0 : true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
            // Im Zweifel hell — das ist die Voreinstellung von Windows.
            ThemeLog.RegistryUnreadable(_logger, ex.Message);
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}

internal static partial class ThemeLog
{
    [LoggerMessage(EventId = 2800, Level = LogLevel.Information,
        Message = "Erscheinungsbild auf {Preference} gesetzt, wirksam: {Effective}")]
    public static partial void PreferenceChanged(ILogger logger, string preference, string effective);

    [LoggerMessage(EventId = 2805, Level = LogLevel.Information,
        Message = "Kontrastmodus erkannt — die Statusfarben bleiben die des Systems")]
    public static partial void HighContrastRespected(ILogger logger);

    [LoggerMessage(EventId = 2801, Level = LogLevel.Information,
        Message = "Windows hat das Erscheinungsbild gewechselt, jetzt: {Effective}")]
    public static partial void SystemChanged(ILogger logger, string effective);

    [LoggerMessage(EventId = 2802, Level = LogLevel.Warning,
        Message = "Erscheinungsbild von Windows nicht lesbar ({Reason}) — es gilt hell")]
    public static partial void RegistryUnreadable(ILogger logger, string reason);

    [LoggerMessage(EventId = 2803, Level = LogLevel.Warning,
        Message = "Kein Themenwoerterbuch in Tokens.xaml gefunden — Status- und Praesenzfarben "
            + "folgen dann dem Systemthema statt der Einstellung")]
    public static partial void BrushDictionaryMissing(ILogger logger);
}
