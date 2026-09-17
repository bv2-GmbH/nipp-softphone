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
        [switch]$Enthaelt)

    # Ohne Ternary: diese Datei muss auch in Windows PowerShell 5.1 laufen,
    # und dort ist "? :" ein Parserfehler.
    Get-NippTree | Where-Object {
        $passtName = $true

        if ($Name) {
            if ($Enthaelt) { $passtName = $_.Name -like "*$Name*" }
            else { $passtName = $_.Name -eq $Name }
        }

        $passtName -and (-not $Typ -or $_.Typ -eq $Typ)
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
