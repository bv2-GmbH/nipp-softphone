<#
.SYNOPSIS
    Liest und bedient die Oberflaeche von nipp ueber UI Automation.

.DESCRIPTION
    Fuer die Zeilen der Testmatrix, die sich ohne Augen pruefen lassen: steht
    der Text da, ist der Knopf erreichbar, heisst der Zustand ueberall gleich,
    haelt ein Wert den Neustart.

    <b>Was es NICHT kann und nicht koennen soll:</b> Ruckeln, Farben,
    abgeschnittene Beschriftungen, Kontrastverhaeltnisse. Das bleibt am
    Menschen; diese Datei nimmt ihm nur das Abklappern ab.

    <b>Seit dem 24.09.2026 kann es ausserdem die echte Maus</b>
    (Move-NippMaus, Invoke-NippKlick, Invoke-NippZug). Das ist kein Luxus:
    ueber UI Automation laesst sich nicht ziehen, und ein InvokePattern
    bewegt den Fokus nicht — beides steht weiter unten begruendet.

    Nebenbei ist es das Werkzeug, mit dem sich die Sprachausgabe pruefen
    laesst: was hier als Name steht, liest ein Bildschirmleser vor. Steht dort
    ein Klassenname, ist das ein Befund (Paragraf 8.4).

.EXAMPLE
    . .\tools\Test-Ui.ps1
    Get-NippTree | Select-Object -First 40
    Get-NippElement -Name 'Anrufen' | Invoke-NippElement
#>

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

<#
    Die Fenster des Prozesses. Ohne -Titel das erste — das ist das
    Hauptfenster, solange keins davor geoeffnet wurde.

    <b>Der Karten-Designer ist ein zweites Fenster</b>, und wer ihn messen
    will, muss ihn ansprechen koennen: `Get-NippWindow -Titel 'Designer'`.
    Vorher griff jede Messung stillschweigend das Hauptfenster ab und sah
    dort natuerlich nichts von dem, was sie suchte.
#>
function Get-NippWindow {
    param([string]$Titel)

    $p = Get-Process Nipp.App -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $p) {
        throw 'nipp laeuft nicht.'
    }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)

    if (-not $Titel) {
        $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)

        if (-not $w) {
            throw 'Kein Fenster ueber UI Automation gefunden.'
        }

        return $w
    }

    $alle = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)

    foreach ($w in $alle) {
        if ($w.Current.Name -like "*$Titel*") { return $w }
    }

    throw "Kein Fenster mit '$Titel' im Titel. Offen sind: " +
        (($alle | ForEach-Object { $_.Current.Name }) -join ', ')
}

<#
    Alle Fenster des Prozesses mit ihrem Titel — zum Nachsehen, was gerade
    offen ist, bevor man eins davon ansteuert.
#>
function Get-NippWindows {
    $p = Get-Process Nipp.App -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $p) { throw 'nipp laeuft nicht.' }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)

    $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) |
        ForEach-Object {
            [pscustomobject]@{
                Titel   = $_.Current.Name
                Klasse  = $_.Current.ClassName
                Element = $_
            }
        }
}

function Get-NippTree {
    param([System.Windows.Automation.AutomationElement]$Wurzel)

    if (-not $Wurzel) { $Wurzel = Get-NippWindow }

    $alle = $Wurzel.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)

    foreach ($e in $alle) {
        $r = $e.Current.BoundingRectangle

        # Ein Element, das gerade nicht dargestellt wird — eingeklappt,
        # weggescrollt, ueber x:Load noch nicht im Baum gewesen — liefert
        # KEIN Rechteck, sondern Rect.Empty: alle vier Werte unendlich.
        # Gemessen am 17.09.2026; ohne diese Pruefung wirft die Umwandlung
        # nach Int32, und zwar je Element einmal.
        $sichtbar = -not [double]::IsInfinity($r.Width)

        [pscustomobject]@{
            Typ      = $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', '')
            Name     = $e.Current.Name
            Id       = $e.Current.AutomationId
            Aktiv    = $e.Current.IsEnabled
            Sichtbar = $sichtbar
            # PHYSISCHE Pixel, siehe Get-NippSkalierung. Wer damit gegen eine
            # Zahl aus docs/test-matrix.md prueft, teilt vorher.
            X        = if ($sichtbar) { [int]$r.X } else { $null }
            Y        = if ($sichtbar) { [int]$r.Y } else { $null }
            Breite   = if ($sichtbar) { [int]$r.Width } else { $null }
            Hoehe    = if ($sichtbar) { [int]$r.Height } else { $null }
            Element  = $e
        }
    }
}

<#
    <b>Der Umrechnungsfaktor, und warum er hier steht.</b>

    UI Automation liefert Rechtecke in PHYSISCHEN Pixeln, XAML rechnet in
    LOGISCHEN. Auf dieser Maschine steht Windows auf 150 %: 830 physische
    Pixel sind logisch 553. Jede Zahl in docs/test-matrix.md ist logisch
    gemeint — und die Schwelle aus ADR-047 (960, Hysterese 40) gilt ausserdem
    fuer die Breite der SEITE, nicht die des Fensters.

    Wer das uebersieht, misst einen Fehlschlag, der keiner ist.
#>
function Get-NippSkalierung {
    Add-Type -AssemblyName System.Windows.Forms
    $s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

    # SystemParametersInfo ueber die Grafik: 96 dpi ist 100 %.
    $g = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    $faktor = $g.DpiX / 96.0
    $g.Dispose()

    [pscustomobject]@{
        Faktor            = $faktor
        Prozent           = [int]($faktor * 100)
        PhysischeBreite   = $s.Width
        LogischeBreite    = [int]($s.Width / $faktor)
    }
}

<#
    Das Fenster auf eine Groesse ziehen — fuer die Zeilen, die eine Breite
    verlangen (schmal gegen breit, ADR-047).

    <b>Die Angabe ist LOGISCH</b>, wie in docs/test-matrix.md; umgerechnet
    wird hier. Wer physisch messen will, nimmt -Physisch.
#>
function Set-NippWindowSize {
    param(
        [Parameter(Mandatory = $true)][int]$Breite,
        [int]$Hoehe,
        [switch]$Physisch,
        [System.Windows.Automation.AutomationElement]$Fenster)

    if (-not $Fenster) { $Fenster = Get-NippWindow }

    $faktor = 1.0
    if (-not $Physisch) { $faktor = (Get-NippSkalierung).Faktor }

    $muster = $null

    if (-not $Fenster.TryGetCurrentPattern(
            [System.Windows.Automation.TransformPattern]::Pattern, [ref]$muster)) {
        throw 'Das Fenster laesst sich nicht ueber UI Automation groessenaendern.'
    }

    if (-not $muster.Current.CanResize) {
        throw 'Das Fenster ist maximiert oder fest — erst wiederherstellen.'
    }

    $r = $Fenster.Current.BoundingRectangle
    if (-not $Hoehe) { $Hoehe = [int]($r.Height / $faktor) }

    $muster.Resize($Breite * $faktor, $Hoehe * $faktor)
    Start-Sleep -Milliseconds 400

    $neu = $Fenster.Current.BoundingRectangle

    [pscustomobject]@{
        PhysischeBreite = [int]$neu.Width
        LogischeBreite  = [int]($neu.Width / $faktor)
    }
}

function Get-NippElement {
    param(
        [string]$Name,
        [string]$Typ,
        [string]$Id,
        [switch]$Enthaelt)

    # Ohne Ternary: diese Datei muss auch in Windows PowerShell 5.1 laufen,
    # und dort ist "? :" ein Parserfehler.
    Get-NippTree | Where-Object {
        $passtName = $true

        if ($Name) {
            if ($Enthaelt) { $passtName = $_.Name -like "*$Name*" }
            else { $passtName = $_.Name -eq $Name }
        }

        $passtName -and (-not $Typ -or $_.Typ -eq $Typ) -and (-not $Id -or $_.Id -eq $Id)
    }
}

function Invoke-NippElement {
    param([Parameter(ValueFromPipeline = $true)]$Eintrag)

    process {
        $e = if ($Eintrag.Element) { $Eintrag.Element } else { $Eintrag }
        $muster = $null

        if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$muster)) {
            $muster.Invoke()
            return $true
        }

        if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$muster)) {
            $muster.Toggle()
            return $true
        }

        if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$muster)) {
            $muster.Select()
            return $true
        }

        # Die Gruppen der Einstellungsseite sind Expander: sie kennen weder
        # Invoke noch Toggle, nur ExpandCollapse.
        if ($e.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$muster)) {
            if ($muster.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                $muster.Collapse()
            }
            else {
                $muster.Expand()
            }

            return $true
        }

        return $false
    }
}

<#
    <b>Vorsicht: Setzen ist nicht Tippen.</b>

    Am 14.09.2026 an einer NumberBox gemessen: ueber ValuePattern wird ein
    Wert ausserhalb des Bereichs auf das Maximum geklemmt UND uebernommen —
    beim echten Tippen bleibt er stehen und wird nicht gespeichert. Wer eine
    Eingabepruefung so testet, misst das Bedienelement und nicht die Regel
    dahinter, und bekommt ein Ergebnis, das wie ein Befund aussieht.

    Ausserdem loest Setzen kein LostFocus aus. Wo eine Regel erst beim
    Verlassen des Feldes greift (ADR-045), muss der Fokus von Hand auf ein
    anderes Element gesetzt werden.

    <b>Und es schreibt in die echten Einstellungen.</b> Vorher sichern.
#>
function Set-NippText {
    param(
        [Parameter(ValueFromPipeline = $true)]$Eintrag,
        # Ohne Mandatory: ein Feld zu LEEREN ist der haeufigste
        # Aufraeumschritt, und '' waere sonst kein gueltiges Argument.
        [AllowEmptyString()][string]$Text = '')

    process {
        $e = if ($Eintrag.Element) { $Eintrag.Element } else { $Eintrag }
        $muster = $null

        if ($e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$muster)) {
            $muster.SetValue($Text)
            return $true
        }

        return $false
    }
}

<#
    Die Namen, die ein Bildschirmleser vorlesen wuerde — und die, bei denen er
    einen Klassennamen vorliest. Letzteres ist immer ein Befund (Paragraf 8.4).
#>
function Test-NippAccessibleNames {
    Get-NippTree | Where-Object { $_.Name -match '^Nipp\.|^Microsoft\.|^System\.' } |
        Select-Object Typ, Name
}

# ---------------------------------------------------------------------------
# Die echte Maus
#
# <b>Warum es das braucht.</b> Ueber UI Automation laesst sich nicht ziehen:
# ein Zug in nipp gehoert seit ADR-065 uns selbst und haengt an den
# Zeigerereignissen der Liste (Druck merken, nach acht Pixeln StartDragAsync).
# Ein InvokePattern erzeugt davon keines. Dasselbe beim Klicken: ein
# InvokePattern bewegt den Fokus gar nicht — und genau daran haengt die Falle
# aus ADR-044, dass Windows den Fokus beim Mausklick setzt, BEVOR Click
# feuert. Am 23.09.2026 waere der Verlaufsknopf ueber UIA nie wieder
# zugegangen.
#
# <b>Was das kostet.</b> Diese Funktionen bewegen den echten Zeiger auf dem
# echten Bildschirm. Wer sie laufen laesst, fasst waehrenddessen die Maus
# nicht an, und ein Fenster, das dazwischenkommt, faengt den Druck ab.
# ---------------------------------------------------------------------------

if (-not ('NippMaus' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NippMaus {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int groesse);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }

    const uint BEWEGEN = 0x0001, ABSOLUT = 0x8000, VIRTUELL = 0x4000;

    // <b>SetCursorPos reicht fuer einen Zug nicht</b> (24.09.2026 gemessen):
    // der Zeiger steht danach richtig, und WinUI faengt trotzdem keinen Zug
    // an -- weder innerhalb einer Gruppe noch auf einen Gruppenkopf. Erst
    // ueber SendInput entstehen Eingabeereignisse, die den Zeigerstapel
    // durchlaufen; daran haengt die Acht-Pixel-Schwelle aus ADR-065.
    public static void Bewege(int x, int y) {
        int breite = GetSystemMetrics(78);   // SM_CXVIRTUALSCREEN
        int hoehe  = GetSystemMetrics(79);   // SM_CYVIRTUALSCREEN
        int links  = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
        int oben   = GetSystemMetrics(77);   // SM_YVIRTUALSCREEN

        var ein = new INPUT[1];
        ein[0].type = 0;
        ein[0].mi.dx = (int)(((double)(x - links) * 65535) / (breite - 1));
        ein[0].mi.dy = (int)(((double)(y - oben) * 65535) / (hoehe - 1));
        ein[0].mi.dwFlags = BEWEGEN | ABSOLUT | VIRTUELL;
        SendInput(1, ein, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Taste(uint flagge) {
        var ein = new INPUT[1];
        ein[0].type = 0;
        ein[0].mi.dwFlags = flagge;
        SendInput(1, ein, Marshal.SizeOf(typeof(INPUT)));
    }
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    public const uint LinksAb  = 0x0002;
    public const uint LinksAuf = 0x0004;
    public const uint Rad      = 0x0800;

    // Eine Raste des Rads, als vorzeichenlose 32-Bit-Zahl: -120 nach unten,
    // +120 nach oben. mouse_event nimmt uint, WHEEL_DELTA ist aber
    // vorzeichenbehaftet -- ohne die Umrechnung wirft PowerShell.
    public const uint RadRunter = unchecked((uint)(-120));
    public const uint RadHoch   = 120;
}
'@
}

<#
    <b>Der Prozess muss DPI-bewusst sein, sonst klickt er daneben</b>
    (24.09.2026, gemessen). Windows steht hier auf 150 %. Ein Prozess ohne
    DPI-Bewusstsein bekommt von SetCursorPos nicht die Koordinaten, die er
    hineingibt: gesetzt auf (1766, 1496), gelandet bei (1840, 1475) -- 74
    Pixel daneben, und der Klick traf ein anderes Bedienelement.

    <b>Warum das lange nicht auffiel:</b> bei grossen Knoepfen liegt der
    Versatz noch im Ziel. Erst ein Reiter am unteren Rand hat es gezeigt --
    und es sah aus, als wuerde der Klick gar nicht ankommen.

    UI Automation liefert PHYSISCHE Rechtecke; erst mit dieser Zeile bewegt
    sich der Zeiger auch in physischen Pixeln. Sie steht hier und nicht in
    den Klickfunktionen, weil sie einmal je Prozess gilt.
#>
[void][NippMaus]::SetProcessDPIAware()

if (-not ('NippFenster' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NippFenster {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);

    // Windows gibt den Vordergrund nur her, wenn der rufende Prozess selbst
    // vorn steht oder gerade eine Eingabe verarbeitet hat. Ein kurzer
    // Alt-Impuls erfuellt die zweite Bedingung -- ohne ihn scheitert
    // SetForegroundWindow aus einer Konsole heraus sporadisch.
    public static void AltImpuls() {
        keybd_event(0x12, 0, 0, IntPtr.Zero);
        keybd_event(0x12, 0, 2, IntPtr.Zero);
    }
}
'@
}

<#
    Ein Element ins Bild holen, wenn es weggescrollt ist — und den Eintrag
    mit dem dann gueltigen Rechteck zurueckgeben.

    <b>Warum das hier steht und nicht beim Aufrufer.</b> An genau dieser
    Stelle ist T311 am 22.09.2026 gescheitert: das Formular stand unter einer
    langen Liste, der Klick ging auf die errechnete Stelle, und die lag in
    einem fremden Fenster. Ein Element ohne Rechteck ist kein Fehler des
    Werkzeugs, sondern ein Element, das noch niemand ins Bild geholt hat.

    <b>Und warum es trotzdem scheitern darf:</b> ein zugeklappter Expander
    hat seinen Inhalt ueber x:Load ueberhaupt nicht im Baum. Wer ihn nicht
    aufklappt, findet ihn auch mit Scrollen nicht.
#>
function Show-NippElement {
    param([Parameter(Mandatory = $true)]$Eintrag)

    $e = if ($Eintrag.Element) { $Eintrag.Element } else { $Eintrag }
    $r = $e.Current.BoundingRectangle

    if ([double]::IsInfinity($r.Width)) {
        $muster = $null
        if ($e.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$muster)) {
            $muster.ScrollIntoView()
            Start-Sleep -Milliseconds 600
            $r = $e.Current.BoundingRectangle
        }
    }

    $sichtbar = -not [double]::IsInfinity($r.Width)

    [pscustomobject]@{
        Typ      = $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', '')
        Name     = $e.Current.Name
        Id       = $e.Current.AutomationId
        Aktiv    = $e.Current.IsEnabled
        Sichtbar = $sichtbar
        X        = if ($sichtbar) { [int]$r.X } else { $null }
        Y        = if ($sichtbar) { [int]$r.Y } else { $null }
        Breite   = if ($sichtbar) { [int]$r.Width } else { $null }
        Hoehe    = if ($sichtbar) { [int]$r.Height } else { $null }
        Element  = $e
    }
}

<#
    Der Mittelpunkt eines Elements in PHYSISCHEN Pixeln — das ist, was
    SetCursorPos erwartet, und was Get-NippTree liefert. Nicht umrechnen.

    Mit -Anteil laesst sich die Stelle innerhalb des Elements waehlen: 0.25
    ist das obere Viertel, 0.75 das untere. Beim Ziehen entscheidet genau das
    ueber "davor" oder "dahinter".
#>
function Get-NippPunkt {
    param(
        [Parameter(Mandatory = $true)]$Eintrag,
        [double]$Anteil = 0.5)

    if (-not $Eintrag.Sichtbar) {
        # Weggescrollt ist kein Grund aufzugeben — erst ins Bild holen.
        $Eintrag = Show-NippElement -Eintrag $Eintrag
    }

    if (-not $Eintrag.Sichtbar) {
        throw "Das Element '$($Eintrag.Name)' wird auch nach dem Scrollen nicht dargestellt. Steht es in einem zugeklappten Expander?"
    }

    [pscustomobject]@{
        X = [int]($Eintrag.X + $Eintrag.Breite / 2)
        Y = [int]($Eintrag.Y + $Eintrag.Hoehe * $Anteil)
    }
}

<#
    Den Zeiger auf ein Element setzen, ohne zu klicken. Fuer alles, was am
    Ueberfahren haengt.
#>
function Move-NippMaus {
    param(
        [Parameter(Mandatory = $true)]$Eintrag,
        [double]$Anteil = 0.5)

    $p = Get-NippPunkt -Eintrag $Eintrag -Anteil $Anteil
    [NippMaus]::Bewege($p.X, $p.Y)
    Start-Sleep -Milliseconds 80
    return $p
}

<#
    Das nipp-Fenster nach vorn holen — <b>und das ist keine Hoeflichkeit,
    sondern die Voraussetzung jedes Mausklicks.</b>

    <b>Gemessen am 24.09.2026, und es ist derselbe Befund wie damals bei
    T311:</b> das Fenster der Konsole, aus der gemessen wird, steht bei jedem
    Befehl selbst im Vordergrund. Ein Klick auf die errechnete Stelle landet
    dann dort statt in nipp — er kommt an, nur woanders. Von aussen sieht das
    aus, als reagiere nipp nicht: der Reiter wird angeklickt, die Seite
    wechselt nicht, und man sucht den Fehler im Programm.

    Der Rueckgabewert sagt, ob nipp danach wirklich vorn steht. Ist er
    falsch, ist jede folgende Messung wertlos.
#>
function Set-NippVordergrund {
    param([switch]$Leise)

    $fenster = Get-NippWindow
    $griff = [IntPtr]$fenster.Current.NativeWindowHandle

    for ($versuch = 1; $versuch -le 3; $versuch++) {
        [void][NippFenster]::ShowWindow($griff, 9)        # SW_RESTORE
        [void][NippFenster]::SetForegroundWindow($griff)
        Start-Sleep -Milliseconds 250

        if ([NippFenster]::GetForegroundWindow() -eq $griff) { return $true }

        # Zweiter und dritter Versuch mit Alt-Impuls, siehe NippFenster.
        [NippFenster]::AltImpuls()
        Start-Sleep -Milliseconds 120
    }

    if (-not $Leise) {
        Write-Warning 'nipp steht nicht im Vordergrund — Mausklicks landen in einem anderen Fenster.'
    }

    return $false
}

<#
    Ein echter Mausklick auf ein Element — mit allem, was daran haengt:
    Fokuswechsel vor dem Click-Ereignis, Zeigerereignisse, verlorener Fokus
    des vorherigen Elements.

    <b>Wann ihn nehmen und wann Invoke-NippElement:</b> geht es nur darum,
    dass etwas passiert, reicht das Muster und stoert niemanden. Geht es um
    den Fokus oder darum, was beim Schliessen einer Liste geschieht, muss es
    die Maus sein.
#>
function Invoke-NippKlick {
    param(
        [Parameter(Mandatory = $true)]$Eintrag,
        [double]$Anteil = 0.5,
        [switch]$OhneVordergrund)

    if (-not $OhneVordergrund) { [void](Set-NippVordergrund) }

    $p = Move-NippMaus -Eintrag $Eintrag -Anteil $Anteil
    [NippMaus]::Taste([NippMaus]::LinksAb)
    Start-Sleep -Milliseconds 60
    [NippMaus]::Taste([NippMaus]::LinksAuf)
    Start-Sleep -Milliseconds 250
    return $p
}

<#
    Mit dem Mausrad scrollen — dort, wo der Zeiger gerade steht, oder ueber
    einem Element.

    Eine gedeckelte Liste scrollt, wenn der Zeiger ueber IHR steht, und die
    Seite darunter, wenn er daneben steht. Das ist kein Detail: seit dem
    23.09.2026 sind die Listen der Einstellungsseite gedeckelt, und wer die
    Seite bewegen will, muss den Zeiger neben die Liste halten.
#>
function Invoke-NippRad {
    param(
        $Ueber,
        [int]$Rasten = 3,
        [switch]$Hoch)

    if ($Ueber) { Move-NippMaus -Eintrag $Ueber | Out-Null }

    $delta = if ($Hoch) { [NippMaus]::RadHoch } else { [NippMaus]::RadRunter }

    for ($i = 1; $i -le $Rasten; $i++) {
        [NippMaus]::mouse_event([NippMaus]::Rad, 0, 0, $delta, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 220
    }

    Start-Sleep -Milliseconds 400
}

<#
    Ein Zug von einem Element auf ein anderes.

    <b>Warum in Schritten und nicht in einem Sprung.</b> Zwei Schwellen
    liegen dazwischen, beide gewollt (ADR-065, ADR-066): die ersten acht
    Pixel sind noch ein Klick und starten den Zug ueberhaupt erst, und ein
    Vorschauschritt braucht eine ZEIGERBEWEGUNG — ohne sie zittert es an der
    Gruppengrenze, weil das Layout unter dem stehenden Zeiger wandert. Ein
    SetCursorPos direkt aufs Ziel erzeugt eine einzige Bewegung und kommt
    unter beiden Schwellen durch.

    <b>-Anteil am Ziel entscheidet ueber davor oder dahinter</b>: in der
    unteren Haelfte einer Zeile (bei Kacheln: der rechten) landet das
    Gezogene dahinter.

    <b>Und das Wichtigste zuerst: Team-Zeilen lassen sich nur im
    UMSORTIER-MODUS ziehen.</b> `OnTeamPointerPressed` liest
    `IsTeamReorderMode`, und ohne den Modus bleibt jeder Zug folgenlos --
    ohne Fehler, ohne Protokollzeile. Am 24.09.2026 sah das eine halbe
    Stunde lang wie ein kaputtes Ziehen aus. Eingeschaltet wird er ueber den
    Knopf «Reihenfolge ändern» im Gruppenkopf; laeuft er, steht ueber der
    Liste «Kacheln ziehen, um sie umzusortieren.»

    Beispiel:
        $von  = Get-NippElement -Name '601' | Select-Object -First 1
        $nach = Get-NippElement -Name '604' | Select-Object -First 1
        Invoke-NippZug -Von $von -Nach $nach -Anteil 0.75
#>
function Invoke-NippZug {
    param(
        [Parameter(Mandatory = $true)]$Von,
        [Parameter(Mandatory = $true)]$Nach,
        [double]$Anteil = 0.5,
        [int]$Schritte = 14,
        [int]$PauseMs = 45)

    [void](Set-NippVordergrund)

    $start = Get-NippPunkt -Eintrag $Von
    $ziel = Get-NippPunkt -Eintrag $Nach -Anteil $Anteil

    "Zug: ($($start.X),$($start.Y)) -> ($($ziel.X),$($ziel.Y)) in $Schritte Schritten"

    [NippMaus]::Bewege($start.X, $start.Y)
    Start-Sleep -Milliseconds 200
    [NippMaus]::Taste([NippMaus]::LinksAb)
    Start-Sleep -Milliseconds 200

    for ($i = 1; $i -le $Schritte; $i++) {
        $x = [int]($start.X + ($ziel.X - $start.X) * $i / $Schritte)
        $y = [int]($start.Y + ($ziel.Y - $start.Y) * $i / $Schritte)
        [NippMaus]::Bewege($x, $y)
        Start-Sleep -Milliseconds $PauseMs
    }

    # Am Ziel stehen bleiben, bevor losgelassen wird: die Vorschau haengt
    # am Ueberfahren, und ein Loslassen im selben Atemzug misst das
    # Aufraeumen statt des Ablegens.
    Start-Sleep -Milliseconds 500
    [NippMaus]::Taste([NippMaus]::LinksAuf)
    Start-Sleep -Milliseconds 700
}
