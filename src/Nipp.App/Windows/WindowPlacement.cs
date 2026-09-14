using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Nipp.App.Windows;

/// <summary>
/// Merkt sich, wo das Fenster stand, und sorgt dafür, dass es auf den
/// Bildschirm passt.
///
/// <b>Warum das nötig ist.</b> nipp ist im Smartphone-Format gebaut (§20.1) und
/// war anfangs 780 Pixel hoch. Auf einem 1080p-Bildschirm mit 150 %
/// Skalierung — der häufigsten Konfiguration im Büro — sind das 1170 physische
/// Pixel bei 1080 verfügbaren: das Fenster ragte unten heraus, und der Benutzer
/// musste daran ziehen. Genau das soll er nicht müssen.
///
/// Zwei Regeln daraus:
/// <list type="number">
///   <item>Die Wunschhöhe wird auf den <b>Arbeitsbereich</b> begrenzt (nicht
///   auf die Bildschirmgrösse — die Taskleiste gehört abgezogen).</item>
///   <item>Position und Grösse werden gemerkt. Wer das Fenster einmal
///   eingerichtet hat, findet es beim nächsten Start dort wieder.</item>
/// </list>
/// </summary>
public static partial class WindowPlacement
{
    /// <summary>
    /// §20.1: schmal genug, um neben anderen Fenstern zu stehen.
    ///
    /// <b>Logische Pixel.</b> Das ist die Einheit, in der XAML rechnet — eine
    /// Schaltfläche mit <c>Width="48"</c> ist 48 logische Pixel breit. Auf
    /// einem Bildschirm mit 150 % Skalierung sind das 72 physische.
    /// </summary>
    public const int DefaultWidth = 400;

    /// <summary>
    /// Wunschhöhe. 660 statt der ursprünglichen 780 — mit der verdichteten
    /// Skala (Tokens.xaml) passt alles hinein, und 660 × 1,5 = 990 bleibt unter
    /// den 1080 eines Full-HD-Bildschirms.
    /// </summary>
    public const int DefaultHeight = 660;

    /// <summary>
    /// Wie viel vom Arbeitsbereich das Fenster höchstens einnehmen darf. Etwas
    /// Luft bleibt bewusst: ein Fenster, das exakt bis an die Taskleiste
    /// reicht, sieht aus, als sei es zu gross geraten.
    /// </summary>
    private const double MaxWorkAreaFraction = 0.92;

    /// <summary>Kleinste sinnvolle Höhe — darunter fällt die Umschaltleiste aus dem Blick.</summary>
    public const int MinimumHeight = 420;

    /// <summary>
    /// Kleinste sinnvolle Breite. Darunter brechen die Wähltastatur und die
    /// Beschriftungen der Umschaltleiste um.
    /// </summary>
    public const int MinimumWidth = 320;

    /// <summary>
    /// Setzt Grösse und Position: entweder die gemerkten Werte oder die
    /// Standardwerte, in beiden Fällen auf den Arbeitsbereich begrenzt.
    ///
    /// <b>Einheiten — hier liegt die Falle.</b> <see cref="AppWindow"/> rechnet
    /// durchgehend in <b>physischen</b> Pixeln: <c>MoveAndResize</c>,
    /// <c>Position</c>, <c>Size</c> und auch <see cref="DisplayArea.WorkArea"/>.
    /// XAML dagegen rechnet in logischen. Wer die 400 aus §20.1 unverändert an
    /// <c>MoveAndResize</c> gibt, bekommt auf einem 150-%-Bildschirm ein
    /// Fenster von 267 logischen Pixeln Breite — die Bedienelemente wirken
    /// dann riesig, und der Inhaltsbereich wird auf einen Streifen
    /// zusammengedrückt. Genau so ist es beim ersten Versuch passiert.
    ///
    /// Deshalb: die Standardwerte werden mit dem Skalierungsfaktor
    /// multipliziert, die gemerkten Werte sind bereits physisch und bleiben
    /// unverändert.
    /// </summary>
    /// <param name="window">Das Fenster.</param>
    /// <param name="stored">
    /// Die gemerkte Angabe aus den Einstellungen, oder <c>null</c> beim ersten
    /// Start.
    /// </param>
    public static void Apply(Window window, string? stored)
    {
        var appWindow = window.AppWindow;
        var scale = GetScale(window);

        // <b>Maximiert zuerst</b> (ADR-047). Ein maximiertes Fenster hat keine
        // sinnvolle Groesse zum Wiederherstellen — es hat einen Zustand. Bis
        // zum 13.09.2026 wurde nur Lage und Groesse gemerkt, und weil
        // TryApplyRemembered zusaetzlich auf 92 Prozent des Arbeitsbereichs
        // klemmt, kam nipp nach jedem Neustart als beinahe volles Fenster
        // zurueck, mit Rand ringsum. Solange das schmale Fenster der Normalfall
        // war, fiel das niemandem auf; seit es ein breites Layout gibt, ist
        // Vollbild der Anlass.
        if (IsMaximized(stored))
        {
            ApplyMinimumSize(appWindow, scale);

            if (appWindow.Presenter is OverlappedPresenter maximiert)
            {
                maximiert.Maximize();
            }

            return;
        }

        if (TryParse(stored) is { } remembered && TryApplyRemembered(appWindow, remembered, scale))
        {
            ApplyMinimumSize(appWindow, scale);
            return;
        }

        var work = GetWorkArea(appWindow);
        var (x, y, width, height) = Default(work, scale);

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        ApplyMinimumSize(appWindow, scale);
    }

    /// <summary>Wunschbreite des Karten-Designers, in logischen Pixeln (K4).</summary>
    public const int DesignerWidth = 1100;

    /// <summary>Wunschhoehe des Karten-Designers.</summary>
    public const int DesignerHeight = 760;

    /// <summary>
    /// Schmalste sinnvolle Breite des Designers, in logischen Pixeln.
    ///
    /// <b>Die drei Spalten sind nicht verhandelbar:</b> 240 fuer die Palette,
    /// 400 fuer die Vorschau in Kartenbreite und 300 fuer die Eigenschaften,
    /// dazu die Abstaende. Wer schmaler zieht, drueckt die Vorschau aus dem
    /// Bild — und das ist der Grund, aus dem es dieses Fenster gibt.
    /// </summary>
    public const int DesignerMinWidth = 1000;

    /// <summary>Schmalste sinnvolle Hoehe: Aufbau und Vorschau untereinander.</summary>
    public const int DesignerMinHeight = 600;

    /// <summary>
    /// Setzt Groesse und Lage des Karten-Designers.
    ///
    /// <para><b>Eigene Methode und nicht <see cref="Apply"/>:</b> der Designer
    /// merkt sich seine Lage nicht. Er wird selten geoeffnet, und eine
    /// gemerkte Lage waere ein zweiter Eintrag in den Einstellungen fuer
    /// wenig.</para>
    ///
    /// <para><b>Die Falle bleibt dieselbe:</b> <see cref="AppWindow"/> rechnet
    /// in physischen Pixeln, XAML in logischen. 1100 x 760 auf einem
    /// 150-%-Bildschirm sind 1650 x 1140 physische -- deutlich mehr, als ein
    /// Full-HD-Bildschirm hat. Deshalb dieselbe Begrenzung auf den
    /// Arbeitsbereich wie beim Hauptfenster.</para>
    /// </summary>
    public static void ApplyDesignerSize(AppWindow appWindow, nint handle)
    {
        ArgumentNullException.ThrowIfNull(appWindow);

        var scale = GetDpiForWindow(handle) / 96.0;

        if (scale <= 0)
        {
            scale = 1.0;
        }

        var work = GetWorkArea(appWindow);

        var width = Math.Min(
            (int)(DesignerWidth * scale),
            (int)(work.Width * MaxWorkAreaFraction));

        var height = Math.Min(
            (int)(DesignerHeight * scale),
            (int)(work.Height * MaxWorkAreaFraction));

        // Mittig: der Designer ist kein Werkzeugfenster am Rand, sondern das,
        // woran gerade gearbeitet wird.
        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    /// <summary>
    /// Versucht, die gemerkte Lage wiederherzustellen.
    ///
    /// <b>Der Fehler, der hier lag.</b> Der Arbeitsbereich wurde am Fenster
    /// ermittelt — und das liegt beim ersten Setzen noch auf dem
    /// Hauptbildschirm. Eine gemerkte Lage auf einem zweiten Monitor hat dort
    /// eine X-Koordinate jenseits der Bildschirmbreite, fiel damit durch die
    /// Sichtbarkeitsprüfung und wurde verworfen: wer nipp auf dem
    /// Zweitmonitor parkte, fand es bei <b>jedem</b> Start wieder rechts oben
    /// auf dem Hauptbildschirm.
    ///
    /// Jetzt entscheidet der Bildschirm, auf dem die gemerkte Lage liegt.
    /// </summary>
    private static bool TryApplyRemembered(
        AppWindow appWindow,
        (int X, int Y, int Width, int Height) remembered,
        double scale)
    {
        var work = GetWorkAreaFor(remembered) ?? GetWorkArea(appWindow);

        // Die gemerkten Werte sind physische Pixel und bleiben unangetastet;
        // skaliert werden nur die Grenzen, und zwar mit dem Faktor des
        // Bildschirms, auf dem das Fenster gerade steht. Auf einem zweiten
        // Monitor mit anderer Skalierung ist das eine Näherung — sie wirkt
        // nur als Untergrenze und bleibt deshalb unkritisch.
        var width = Math.Clamp(
            remembered.Width,
            (int)(MinimumWidth * scale),
            (int)(work.Width * MaxWorkAreaFraction));

        var height = Math.Clamp(
            remembered.Height,
            (int)(MinimumHeight * scale),
            (int)(work.Height * MaxWorkAreaFraction));

        if (!IsVisibleOn(work, remembered.X, remembered.Y, width, height))
        {
            return false;
        }

        appWindow.MoveAndResize(new RectInt32(remembered.X, remembered.Y, width, height));
        return true;
    }

    /// <summary>
    /// Der Arbeitsbereich des Bildschirms, auf dem die gemerkte Lage liegt.
    /// <c>null</c>, wenn sich keiner ermitteln lässt — etwa nach dem
    /// Abstecken einer Dockingstation.
    /// </summary>
    private static RectInt32? GetWorkAreaFor((int X, int Y, int Width, int Height) placement)
    {
        try
        {
            var rect = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);
            var display = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest);

            return display is not null && display.WorkArea.Height > 0 ? display.WorkArea : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Setzt die Mindestgrösse des Fensters.
    ///
    /// Ohne sie liess sich nipp auf wenige Zentimeter zusammenziehen: die
    /// Wähltastatur brach um, und die Umschaltleiste verschwand aus dem Blick.
    /// Beim Start geklemmt wurde die Grösse schon vorher — nur hielt niemand
    /// den Benutzer davon ab, danach am Rand zu ziehen.
    /// </summary>
    private static void ApplyMinimumSize(AppWindow appWindow, double scale)
    {
        if (appWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
        presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
    }

    /// <summary>
    /// Die aktuelle Lage als Zeichenkette zum Speichern, oder <c>null</c>, wenn
    /// sie sich nicht ermitteln lässt.
    /// </summary>
    public static string? Capture(AppWindow appWindow)
    {
        // Ein minimiertes Fenster meldet unbrauchbare Werte. Die alte Angabe
        // stehenzulassen ist besser, als sie mit Unsinn zu überschreiben.
        if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            return null;
        }

        // Ein maximiertes Fenster meldet die Groesse des Bildschirms. Sie
        // zurueckzuschreiben hiesse, den Zustand gegen eine Zahl zu tauschen,
        // die ihn nur ungefaehr trifft — und die naechste Klemmung auf 92
        // Prozent macht aus «maximiert» ein Fenster mit Rand.
        //
        // <b>Fuenf Teile, und die ersten vier bleiben die alten.</b> Eine
        // gemerkte Lage aus einer aelteren Fassung liest sich unveraendert
        // weiter; TryParse nimmt vier Teile wie bisher.
        if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            return MaximizedMarker;
        }

        var p = appWindow.Position;
        var s = appWindow.Size;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{p.X},{p.Y},{s.Width},{s.Height}");
    }

    /// <summary>
    /// Was in den Einstellungen steht, wenn das Fenster maximiert war.
    ///
    /// <b>Ein Wort und keine Zahlen:</b> die Lage eines maximierten Fensters
    /// gehoert dem Bildschirm, auf dem es steht, und Windows stellt sie beim
    /// Maximieren selbst her. Wer sie mitschreibt, merkt sich die Masse des
    /// Bildschirms von gestern.
    /// </summary>
    private const string MaximizedMarker = "maximized";

    /// <summary>Ob die gemerkte Angabe «maximiert» bedeutet.</summary>
    private static bool IsMaximized(string? stored) =>
        string.Equals(stored?.Trim(), MaximizedMarker, StringComparison.OrdinalIgnoreCase);

    private static (int X, int Y, int Width, int Height)? TryParse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var parts = stored.Split(',');

        if (parts.Length != 4)
        {
            return null;
        }

        var values = new int[4];

        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return (values[0], values[1], values[2], values[3]);
    }

    /// <summary>
    /// Standardlage: rechts oben, mit etwas Abstand zum Rand. Ein Softphone
    /// steht neben der Arbeit, nicht mittig darüber.
    /// </summary>
    private static (int X, int Y, int Width, int Height) Default(RectInt32 work, double scale)
    {
        var height = Math.Min((int)(DefaultHeight * scale), (int)(work.Height * MaxWorkAreaFraction));
        var width = Math.Min((int)(DefaultWidth * scale), (int)(work.Width * MaxWorkAreaFraction));

        var margin = (int)(24 * scale);

        return (
            work.X + Math.Max(0, work.Width - width - margin),
            work.Y + margin,
            width,
            height);
    }

    /// <summary>
    /// Ob genug vom Fenster im Arbeitsbereich liegt, um es noch greifen zu
    /// können. Geprüft wird die Titelleiste — wer die nicht erreicht, kann das
    /// Fenster nicht mehr verschieben.
    /// </summary>
    private static bool IsVisibleOn(RectInt32 work, int x, int y, int width, int height)
    {
        const int graspableWidth = 120;
        const int titleBarHeight = 32;

        var right = x + width;
        var bottom = y + height;

        return right - graspableWidth > work.X
            && x + graspableWidth < work.X + work.Width
            && bottom - titleBarHeight > work.Y
            && y < work.Y + work.Height;
    }

    /// <summary>
    /// Der Arbeitsbereich des Bildschirms, auf dem das Fenster liegt — ohne
    /// Taskleiste. Fällt auf einen konservativen Wert zurück, wenn Windows
    /// nichts liefert.
    /// </summary>
    private static RectInt32 GetWorkArea(AppWindow appWindow)
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);

            if (display is not null && display.WorkArea.Height > 0)
            {
                return display.WorkArea;
            }
        }
        catch (Exception)
        {
            // Ohne Bildschirminformation lieber klein und sichtbar als gross
            // und halb daneben.
        }

        return new RectInt32(0, 0, 1280, 720);
    }

    /// <summary>
    /// Der Skalierungsfaktor des Bildschirms, auf dem das Fenster liegt:
    /// 1,0 bei 100 %, 1,5 bei 150 %.
    ///
    /// Über <c>GetDpiForWindow</c> und nicht über
    /// <c>XamlRoot.RasterizationScale</c>, weil der XamlRoot beim ersten Setzen
    /// der Fenstergrösse noch nicht steht.
    /// </summary>
    /// <summary>
    /// Derselbe Faktor zu einem Fensterhandle — für Aufrufer, die das
    /// <see cref="Window"/> nicht zur Hand haben.
    /// </summary>
    public static double ScaleFor(nint handle)
    {
        try
        {
            var dpi = GetDpiForWindow(handle);

            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    private static double GetScale(Window window)
    {
        try
        {
            var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));

            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
