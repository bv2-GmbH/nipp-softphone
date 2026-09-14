using CommunityToolkit.Mvvm.ComponentModel;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Das breite Layout (ADR-047, §23).
///
/// <para><b>§20.1 gibt ein schmales Fenster im Smartphone-Format vor, und das
/// bleibt der Normalfall.</b> ADR-046 hat die Inhaltsbreite auf 480 Pixel
/// gedeckelt, weil eine Zeile darüber hinaus in zwei weit auseinanderliegende
/// Hälften zerfällt — und dabei festgehalten, dass der Platz nur mit einer
/// zweiten Spalte zu nutzen wäre. Das hier ist diese zweite Spalte.</para>
///
/// <para><b>Seit ADR-052 deckelt nichts mehr:</b> der Inhalt füllt das
/// Fenster, und im breiten Layout ist die linke Spalte <b>fest</b> 480 breit,
/// während die rechte den ganzen Rest nimmt. Die zerfallende Zeile aus
/// ADR-046 ist damit bewusst getragen; was diese Klasse entscheidet, ist
/// unverändert nur, <b>ob</b> zwei Spalten stehen.</para>
///
/// <para><b>Warum der Zustand im Kern liegt.</b> <c>Nipp.App</c> hat kein
/// Testprojekt, und an dieser einen Entscheidung hängt die halbe Seite: wo die
/// Anrufliste steht, ob die Nebenstellen als Liste oder als Kacheln erscheinen,
/// und was die Umschaltleiste überhaupt umschaltet. Eine Regel, die nur im
/// XAML steht, ist eine ungeprüfte Regel.</para>
///
/// <para><b>Und warum nicht ein <c>AdaptiveTrigger</c>.</b> Der kann die
/// Fensterbreite messen, aber er kann sie nicht mit der gewählten Sektion
/// verrechnen — «zeige das Team links, aber nur im schmalen Layout» stünde
/// dann ein zweites Mal ausgeschrieben im XAML. Zwei Kopien einer Regel sind
/// zwei Gelegenheiten, sie falsch zu haben.</para>
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// Ab welcher Breite in logischen Pixeln zwei Spalten stehen.
    ///
    /// <para><b>Gerechnet und nicht geschätzt:</b> 24 Seitenrand + linke Spalte
    /// 480 + 12 Spaltenabstand + Rahmen und Bildlaufleiste des Kachelbereichs
    /// (rund 30) + zwei Kachelzellen à 200 = 946. Aufgerundet auf 960, weil
    /// eine Kachelspalte rechts den Umbau nicht lohnt: dafür ist die Liste die
    /// bessere Form.</para>
    ///
    /// <para>Bei genau 960 bleiben nach Seitenrand, Spaltenabstand und den 480
    /// der linken Spalte rund 450 Pixel — zwei Kachelzellen à 208. <b>Die
    /// Rechnung trägt seit ADR-052 unverändert</b>, nur nimmt die Kachelspalte
    /// darüber hinaus den ganzen Rest statt höchstens 1070.</para>
    /// </summary>
    public const double WideThreshold = 960;

    /// <summary>
    /// Wie weit die Breite unter die Schwelle fallen muss, bis wieder eine
    /// Spalte steht.
    ///
    /// <para><b>Das ist kein Feinschliff.</b> Wer das Fenster genau an der
    /// Schwelle zieht, baut ohne Hysterese bei jedem Pixel die halbe Seite neu
    /// auf — auf dem Thread, der alle 20 ms <c>Core.Iterate()</c> bedient.
    /// Vierzig Pixel Abstand, und die Umschaltung passiert einmal.</para>
    /// </summary>
    public const double WideHysteresis = 40;

    /// <summary>
    /// Welches Layout gerade gilt. Gesetzt wird es <b>ausschliesslich</b> über
    /// <see cref="ApplyWidth"/>.
    /// </summary>
    [ObservableProperty]
    private ShellLayout _layout = ShellLayout.Narrow;

    /// <summary>Kurzform für die Oberfläche.</summary>
    public bool IsWide => Layout == ShellLayout.Wide;

    /// <summary>
    /// Ob die Nebenstellen in der linken Spalte stehen — also als Liste im
    /// Abschnitt «Kontakte», wie bisher.
    ///
    /// <para>Im breiten Layout stehen sie rechts als Kacheln, und die linke
    /// Spalte zeigt unter «Kontakte» nur noch Outlook und die Suchtreffer.
    /// <b>Zweimal dieselbe Liste in einem Fenster</b> wäre die Doppelanzeige,
    /// wegen der ADR-046 die beiden Suchfelder zusammengelegt hat.</para>
    /// </summary>
    public bool ShowTeamInLeftColumn => Layout == ShellLayout.Narrow;

    /// <summary>Ob das Kachelraster rechts steht.</summary>
    public bool ShowTiles => Layout == ShellLayout.Wide;

    /// <summary>
    /// Meldet die verfügbare Breite und entscheidet daraus das Layout.
    ///
    /// <para><b>Die einzige Stelle, die entscheidet.</b> Sie meldet nur, wenn
    /// sich wirklich etwas ändert — eine Grössenmeldung kommt bei jedem Pixel,
    /// und ein <c>PropertyChanged</c> je Pixel zeichnete die Seite so oft
    /// neu.</para>
    /// </summary>
    /// <param name="logischeBreite">
    /// Die Breite in <b>logischen</b> Pixeln — die Einheit, in der XAML
    /// rechnet. Nicht die des <c>AppWindow</c>: das rechnet in physischen, und
    /// auf einem 150-%-Bildschirm läge die Schwelle sonst bei 600.
    /// </param>
    public void ApplyWidth(double logischeBreite)
    {
        // Beim ersten Messen steht die Seite manchmal auf 0 — darauf hin das
        // Layout zu wechseln hiesse, es gleich danach wieder zu wechseln.
        if (double.IsNaN(logischeBreite) || logischeBreite <= 0)
        {
            return;
        }

        var neu = Layout switch
        {
            ShellLayout.Narrow when logischeBreite >= WideThreshold => ShellLayout.Wide,
            ShellLayout.Wide when logischeBreite < WideThreshold - WideHysteresis => ShellLayout.Narrow,
            var bisher => bisher,
        };

        if (neu == Layout)
        {
            return;
        }

        Layout = neu;
    }

    partial void OnLayoutChanged(ShellLayout value)
    {
        OnPropertyChanged(nameof(IsWide));
        OnPropertyChanged(nameof(ShowTeamInLeftColumn));
        OnPropertyChanged(nameof(ShowTiles));

        // Eine offene Nebenstelle steht im breiten Layout nicht mehr in der
        // linken Spalte — ihre Zeile gibt es dort nicht mehr, und der Bereich
        // zeigte auf eine Zeile, die niemand sieht. Ein Outlook-Kontakt bleibt
        // offen: seine Liste wechselt nur den Platz, nicht die Form.
        if (value == ShellLayout.Wide && ExpandedContact is { IsTeam: true })
        {
            CloseContactDetails();
        }
    }
}

/// <summary>
/// Wie breit die Hauptansicht steht (ADR-047).
///
/// <para>Zwei Werte und keine Zahl: was dazwischen liegt, entscheidet
/// <see cref="ShellViewModel.ApplyWidth"/> an genau einer Stelle. Ein Enum mit
/// einem dritten Wert für «sehr breit» steht hier bewusst nicht — drei Spalten
/// sind ein eigener Entscheid, und ein Wert für einen Zustand, den es nicht
/// gibt, führt später jemanden in die Irre.</para>
/// </summary>
public enum ShellLayout
{
    /// <summary>Eine Spalte, Smartphone-Format — die Vorgabe aus §20.1.</summary>
    Narrow,

    /// <summary>Zwei Spalten: links wählen und Anrufe, rechts die Nebenstellen als Kacheln.</summary>
    Wide,
}
