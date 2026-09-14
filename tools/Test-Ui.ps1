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

function Get-NippWindow {
    $p = Get-Process Nipp.App -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $p) {
        throw 'nipp laeuft nicht.'
    }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)

    $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)

    if (-not $w) {
        throw 'Kein Fenster ueber UI Automation gefunden.'
    }

    return $w
}

function Get-NippTree {
    param([System.Windows.Automation.AutomationElement]$Wurzel)

    if (-not $Wurzel) { $Wurzel = Get-NippWindow }

    $alle = $Wurzel.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)

    foreach ($e in $alle) {
        [pscustomobject]@{
            Typ     = $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', '')
            Name    = $e.Current.Name
            Id      = $e.Current.AutomationId
            Aktiv   = $e.Current.IsEnabled
            Element = $e
        }
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
