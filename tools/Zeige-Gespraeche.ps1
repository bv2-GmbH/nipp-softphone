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

    [string]$Verzeichnis = "$env:LOCALAPPDATA\nipp\logs"
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

        [pscustomobject]@{
            Zeit        = $m.Groups['zeit'].Value
            Id          = $m.Groups['id'].Value
            Dauer       = $dauerText
            Codec       = Feld 'Codec' $rest
            Echo        = Feld 'Echo' $rest
            Verwuerfe   = $verwuerfe
            MaxVerwurf  = $maxVerwurf
            Spaet       = $spaet
            MaxSpaet    = $maxSpaet
            Pufferfehl  = $pufferfehler
            ProMinute   = if ($minuten -gt 0) { [math]::Round($verwuerfe / $minuten, 2) } else { 0 }
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

$zeilen | Format-Table -AutoSize

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
