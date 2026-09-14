using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nipp.Core.Services.Settings;

namespace Nipp.App.Controls;

/// <summary>
/// Eine Zeile in den Einstellungen (AP4.9) mit Unterstützung für gesperrte
/// Felder (AP8.4, §17).
///
/// <b>Der Grund für dieses Control</b> steht in §17: ein von der Administration
/// festgelegtes Feld erscheint „ausgegraut mit Schloss-Symbol und Tooltip
/// ‚Von der Administration festgelegt‘". Ohne gemeinsamen Baustein müsste das
/// an jedem einzelnen Feld wiederholt werden — und beim nächsten neuen Feld
/// wird es vergessen. Vorher sperrte nipp deshalb auf Gruppenebene: grob, aber
/// wenigstens vollständig.
///
/// <b>Warum ein <see cref="ContentControl"/> und kein UserControl.</b> Der
/// erste Versuch war ein UserControl, das sein eigenes <c>Content</c> an einen
/// inneren Wirt weiterband. Das stürzt beim Laden ab: dasselbe Element hinge
/// dann zweimal im Baum, und WinUI meldet dazu nur „Value does not fall within
/// the expected range". Ein <c>ContentControl</c> mit Template hat genau einen
/// Platz für den Inhalt und ist der vorgesehene Weg.
///
/// <b>Das ist ein Bedienschutz, keine Sicherheitsgrenze</b> — siehe
/// <see cref="PolicyService"/>. Ein ausgegrautes Bedienelement hält niemanden
/// davon ab, <c>settings.json</c> mit einem Texteditor zu öffnen.
/// </summary>
public sealed class SettingCard : ContentControl
{
    /// <summary>Name des Teils im Template, das ausgegraut wird.</summary>
    private const string ContentHostPart = "PART_ContentHost";

    /// <summary>Name des Schloss-Symbols im Template.</summary>
    private const string LockPart = "PART_Lock";

    private Control? _contentHost;
    private FrameworkElement? _lock;

    public SettingCard() => DefaultStyleKey = typeof(SettingCard);

    /// <summary>Die Beschriftung links.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Eine Zeile Erklärung darunter. Leer lassen, wenn die Beschriftung für
    /// sich spricht — eine Erklärung, die nur wiederholt, was oben steht,
    /// kostet Platz und Aufmerksamkeit.
    /// </summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Ob die Erklärung überhaupt Platz bekommt.</summary>
    public static readonly DependencyProperty HasDescriptionProperty = DependencyProperty.Register(
        nameof(HasDescription),
        typeof(bool),
        typeof(SettingCard),
        new PropertyMetadata(false));

    public bool HasDescription
    {
        get => (bool)GetValue(HasDescriptionProperty);
        private set => SetValue(HasDescriptionProperty, value);
    }

    /// <summary>
    /// Der Pfad dieser Einstellung im Provisionierungsprofil, etwa
    /// <c>network.sip-port</c>. Nur damit kann die Seite entscheiden, ob das
    /// Feld gesperrt gehört.
    /// </summary>
    public static readonly DependencyProperty SettingPathProperty = DependencyProperty.Register(
        nameof(SettingPath),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty));

    public string SettingPath
    {
        get => (string)GetValue(SettingPathProperty);
        set => SetValue(SettingPathProperty, value);
    }

    /// <summary>Ob die Administration dieses Feld festgelegt hat.</summary>
    public static readonly DependencyProperty IsLockedProperty = DependencyProperty.Register(
        nameof(IsLocked),
        typeof(bool),
        typeof(SettingCard),
        new PropertyMetadata(false, OnIsLockedChanged));

    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    /// <summary>
    /// Fragt den <see cref="PolicyService"/> und richtet sich danach.
    ///
    /// Als Methode und nicht als Bindung: der Dienst ist kein
    /// <c>INotifyPropertyChanged</c>, und ein Profil wechselt selten genug,
    /// dass die Seite das von Hand nachziehen kann.
    /// </summary>
    public void ApplyPolicy(PolicyService policy) => IsLocked = policy.IsLocked(SettingPath);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _contentHost = GetTemplateChild(ContentHostPart) as Control;
        _lock = GetTemplateChild(LockPart) as FrameworkElement;

        UpdateHasDescription();
        UpdateLockState();
    }

    private static void OnIsLockedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        (sender as SettingCard)?.UpdateLockState();

    /// <summary>
    /// W2.6, Nebenbefund zu D15: eine spätere Änderung an
    /// <see cref="Description"/> muss ankommen.
    ///
    /// <para>Bis zum 13.09.2026 wurde <see cref="HasDescription"/> nur in
    /// <c>OnApplyTemplate</c> gesetzt — also einmal, beim Aufbau. Wer die
    /// Erklärung danach setzte (aus einer Bindung, oder weil ein Feld je nach
    /// Zustand etwas anderes erklärt), bekam den Text ins Steuerelement und
    /// eine Zeile mit Höhe null darum: <c>HasDescription</c> stand weiter auf
    /// <c>false</c>. <b>Kein Fehler, keine Meldung, nur ein unsichtbarer
    /// Satz.</b></para>
    ///
    /// <para>Im heutigen Code passiert das nirgends — jede Erklärung steht als
    /// Literal im XAML. Das ist aber eine Eigenschaft der Aufrufer und keine
    /// des Steuerelements, und die nächste Bindung wüsste nichts davon.</para>
    /// </summary>
    private static void OnDescriptionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        (sender as SettingCard)?.UpdateHasDescription();

    private void UpdateHasDescription() => HasDescription = Description is { Length: > 0 };

    private void UpdateLockState()
    {
        if (_contentHost is not null)
        {
            // Nur der Inhalt wird gesperrt, nicht die ganze Zeile: die
            // Beschriftung soll lesbar bleiben, und der Tooltip muss erreichbar
            // sein — ein deaktiviertes Element zeigt in WinUI keinen.
            _contentHost.IsEnabled = !IsLocked;
        }

        if (_lock is not null)
        {
            _lock.Visibility = IsLocked ? Visibility.Visible : Visibility.Collapsed;
        }

        ToolTipService.SetToolTip(this, IsLocked ? PolicyService.LockedHint : null);
    }
}
