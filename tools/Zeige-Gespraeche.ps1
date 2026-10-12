<#
    Holt die Gespraechsberichte aus den Protokollen und legt sie als Tabelle
    nebeneinander (ADR-077).

    Dafuer ist die Berichtszeile gebaut: eine Zeile je Gespraech, auf
    Information, mit allen Zahlen. Dieses Skript ist nur der Leser dazu —
    es rechnet nichts aus, was nicht schon dasteht, ausser den Stoerungen
    je Minute. Genau die machen lange und kurze Gespraeche vergleichbar,
    und ohne sie sieht ein Gespraech von 50 Minuten mit 22 Verwuerfen
    schlimmer aus als eines von 9 Minuten mit 13.

    Aufruf:
        .\tools\Zeige-Gespraeche.ps1
        .\tools\Zeige-Gespraeche.ps1 -Tage 7
        .\tools\Zeige-Gespraeche.ps1 -NurAuffaellige
#>
[CmdletBinding()]
param(
    # Wie viele Tage zurueck. 0 heisst: alle Protokolle, die da sind.
    [int]$Tage = 7,

    # Nur Gespraeche mit Stoerungen zeigen.
    [switch]$NurAuffaellige,

    # Gibt die Zeilen als Objekte zurueck statt als Tabelle — zum Sortieren,
    # Filtern und Exportieren. Ohne den Schalter formatiert das Skript selbst,
    # und dann ist in der Pipeline nichts mehr zu holen.
    [switch]$AlsObjekte,

    [string]$Verzeichnis = "$env:LOCALAPPDATA\bv2\nipp\logs"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Verzeichnis)) {
    throw "Protokollverzeichnis nicht gefunden: $Verzeichnis"
}

$dateien = Get-ChildItem -Path $Verzeichnis -Filter 'nipp-*.log' |
    Where-Object { $Tage -le 0 -or $_.LastWriteTime -ge (Get-Date).AddDays(-$Tage) } |
    Sort-Object Name

if (-not $dateien) {
    Write-Warning "Keine Protokolle im gewaehlten Zeitraum."
    return
}

# Der Name haengt an TelephonyLog.CallReport. Aendert er sich dort, findet
# dieses Skript nichts mehr — und das faellt sofort auf, weil die Tabelle
# leer bleibt.
$muster = '^(?<zeit>\S+ \S+).*Gespraech (?<id>\w+) ausgewertet: (?<rest>.+)$'

$zeilen = foreach ($d in $dateien) {
    foreach ($treffer in (Select-String -Path $d.FullName -Pattern $muster -AllMatches)) {
        $m = [regex]::Match($treffer.Line, $muster)
        if (-not $m.Success) { continue }

        $rest = $m.Groups['rest'].Value
        function Feld($name, $text) {
            $t = [regex]::Match($text, "$name ([^,]+)")
            if ($t.Success) { $t.Groups[1].Value.Trim() } else { '' }
        }

        $dauerText = Feld 'Dauer' $rest
        $minuten = 0.0
        if ($dauerText -match '^(\d+):(\d+)$') {
            $minuten = [int]$Matches[1] + ([int]$Matches[2] / 60.0)
        }

        $verwuerfe = 0
        if ($rest -match 'Verwuerfe (\d+)') { $verwuerfe = [int]$Matches[1] }
        $maxVerwurf = 0
        if ($rest -match 'Verwuerfe \d+ \(max (\d+) ms\)') { $maxVerwurf = [int]$Matches[1] }
        $spaet = 0
        if ($rest -match 'Ticker spaet (\d+)') { $spaet = [int]$Matches[1] }
        $maxSpaet = 0
        if ($rest -match 'Ticker spaet \d+ \(max (\d+) ms\)') { $maxSpaet = [int]$Matches[1] }
        $pufferfehler = 0
        if ($rest -match 'Pufferfehler (\d+)') { $pufferfehler = [int]$Matches[1] }

        # Das SDK schreibt mit Punkt als Dezimaltrenner. Auf einer de-CH
        # Maschine macht [double]'2.8' daraus 28 — die Zahl waere nicht
        # falsch gerundet, sondern zehnmal zu gross, und ein stilles
        # Zehnfaches sieht aus wie ein Messwert.
        $alsZahl = {
            param($text)
            $wert = 0.0
            if ([double]::TryParse($text, [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$wert)) { $wert } else { 0.0 }
        }

        # $null und nicht 0: Protokolle vor dem 12.10.2026 tragen das Feld
        # nicht, und «nicht gemessen» ist etwas anderes als «nichts gefunden».
        # Als 0 saehe jedes alte Gespraech aus, als waere es geprueft worden.
        $hoerbar = $null
        if ($rest -match 'davon hoerbar (\d+)') { $hoerbar = [int]$Matches[1] }

        $mosMin = 0.0
        if ($rest -match 'MOS [\d.]+ \(min ([\d.]+)\)') { $mosMin = & $alsZahl $Matches[1] }
        $verlustMax = 0.0
        if ($rest -match 'Verlust max ([\d.]+)%') { $verlustMax = & $alsZahl $Matches[1] }

        [pscustomobject]@{
            Zeit        = $m.Groups['zeit'].Value
            Id          = $m.Groups['id'].Value
            Dauer       = $dauerText
            ProMinute   = if ($minuten -gt 0) { [math]::Round($verwuerfe / $minuten, 2) } else { 0 }
            Hoerbar     = $hoerbar
            MosMin      = $mosMin
            VerlustMax  = $verlustMax
            Codec       = Feld 'Codec' $rest
            Echo        = Feld 'Echo' $rest
            Verwuerfe   = $verwuerfe
            MaxVerwurf  = $maxVerwurf
            Spaet       = $spaet
            MaxSpaet    = $maxSpaet
            Pufferfehl  = $pufferfehler
            Stufe       = Feld 'SDK-Stufe' $rest
        }
    }
}

if (-not $zeilen) {
    Write-Warning @"
Keine Berichtszeilen gefunden.

Das heisst eines von dreien, und sie sind zu unterscheiden:
  - die Protokolle sind aelter als ADR-077 (08.10.2026),
  - es wurde im Zeitraum nicht telefoniert,
  - oder der Meldungstext in TelephonyLog.CallReport hat sich geaendert und
    dieses Skript sucht nach dem falschen Muster.
"@
    return
}

if ($NurAuffaellige) {
    $zeilen = $zeilen | Where-Object { $_.Verwuerfe -gt 0 -or $_.Spaet -gt 0 -or $_.Pufferfehl -gt 0 }
}

if ($AlsObjekte) {
    return $zeilen
}

# Die Spalten stehen hier ausgeschrieben, und das ist der Punkt:
# -AutoSize allein waehlt alle Eigenschaften und schneidet ab, was nicht mehr
# in die Konsole passt — ProMinute stand als vorletzte und fiel damit als
# erste heraus. Ausgerechnet die Spalte, auf die es ankommt, war unsichtbar.
$zeilen | Format-Table -AutoSize -Property Zeit, Id, Dauer,
    @{ Name = 'ProMin'; Expression = { $_.ProMinute }; FormatString = '0.00' },
    @{ Name = 'MOSmin'; Expression = { $_.MosMin }; FormatString = '0.0' },
    @{ Name = 'Verlust%'; Expression = { $_.VerlustMax }; FormatString = '0.0' },
    Codec, Echo, Verwuerfe,
    @{ Name = 'Hoerbar'; Expression = { if ($null -eq $_.Hoerbar) { '?' } else { $_.Hoerbar } } },
    @{ Name = 'MaxMs'; Expression = { $_.MaxVerwurf } },
    @{ Name = 'Spaet'; Expression = { $_.Spaet } },
    @{ Name = 'PufFhl'; Expression = { $_.Pufferfehl } }

$mitDauer = $zeilen | Where-Object { $_.ProMinute -gt 0 }

Write-Host ""
Write-Host "Gespraeche: $($zeilen.Count)" -ForegroundColor Cyan

if ($mitDauer) {
    $schnitt = [math]::Round(($mitDauer | Measure-Object -Property ProMinute -Average).Average, 2)
    $schlimmstes = $mitDauer | Sort-Object ProMinute -Descending | Select-Object -First 1
    Write-Host "Verwuerfe je Minute: Schnitt $schnitt, am meisten $($schlimmstes.ProMinute) ($($schlimmstes.Zeit), $($schlimmstes.Dauer))" -ForegroundColor Cyan
}

$ohneEcho = $zeilen | Where-Object { $_.Echo -like 'AUS*' }
if ($ohneEcho) {
    Write-Host "Ohne Echounterdrueckung: $($ohneEcho.Count) von $($zeilen.Count)" -ForegroundColor Yellow
}
