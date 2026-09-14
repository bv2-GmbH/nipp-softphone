<#
.SYNOPSIS
    Bringt Windows dazu, die Symbole von nipp neu zu lesen.

.DESCRIPTION
    Ein neues Logo wird nicht von selbst sichtbar, und der Grund liegt nie im
    Code. Belegt am 07.09.2026: nach „Neues Logo in allen sechzehn Rollen"
    zeigte die angepinnte App weiter das alte Symbol — obwohl die gebaute EXE
    alle sieben Stufen des neuen `AppIcon.ico` byteidentisch enthielt und die
    Shell-API sie auch so ausliefert.

    Festgehalten wird es an drei Stellen, und dieses Skript raeumt die zwei
    auf, die sich raeumen lassen:

      1. Der SYMBOLCACHE des Explorers
         (%LocalAppData%\Microsoft\Windows\Explorer\iconcache_*.db). Ein
         angepinnter Verweis traegt kein Bild, sondern zeigt auf Symbol 0 der
         EXE — nur liest Explorer es nicht neu, solange der Pfad gleich
         bleibt. Der Cache laesst sich nur bei beendetem Explorer loeschen.

      2. Ein VERWAISTES REGISTRIERTES PAKET. `Add-AppxPackage -Register` zeigt
         auf das Ausgabeverzeichnis; der naechste unpackaged Build entfernt
         dort das `AppxManifest.xml`. Windows behaelt dann seine Kopie der
         Kachelbilder aus der Zeit der Registrierung — und die stammte
         moeglicherweise aus einem Build, in dem `Assets\` leer war.

    Die dritte Stelle sind FEHLENDE ZIELGROESSEN. Fuer eine angepinnte
    packaged App liest Windows
    `Square44x44Logo.targetsize-<N>_altform-unplated.png`, nicht
    `Square44x44Logo.png`. Die erzeugt `tools\Build-Icons.py` vollstaendig;
    dieses Skript kann daran nichts richten.

.PARAMETER KeepPackage
    Die Paketregistrierung stehen lassen. Ohne diesen Schalter wird sie
    entfernt, wenn ihr Manifest fehlt — der Alltag laeuft unpackaged
    (ADR-008), und packaged bekommt ausserdem keine Toasts.

.PARAMETER WhatIf
    Nur sagen, was geschehen wuerde.

.EXAMPLE
    .\tools\Reset-IconCache.ps1
    # Danach: den Pin in der Taskleiste loesen und neu anheften.

.NOTES
    Der Explorer wird beendet und neu gestartet. Offene Ordnerfenster gehen
    dabei zu; Taskleiste und laufende Programme bleiben.

    **nipp danach neu starten.** Sein Symbol im Infobereich kommt nach einem
    Explorer-Neustart nicht von selbst zurueck — beobachtet am 07.09.2026,
    und weil nipp im Infobereich lebt (Paragraph 10), ist es sonst nicht mehr
    erreichbar.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$KeepPackage
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ---- 1. Das verwaiste Paket ----

$paket = Get-AppxPackage -Name 'bv2.nipp' -ErrorAction SilentlyContinue

if ($paket) {
    $manifest = Join-Path $paket.InstallLocation 'AppxManifest.xml'
    $verwaist = -not (Test-Path $manifest)

    Write-Host "Paket registriert: $($paket.PackageFullName)" -ForegroundColor DarkGray
    Write-Host "  Ort:      $($paket.InstallLocation)" -ForegroundColor DarkGray
    Write-Host "  Manifest: $(if ($verwaist) { 'FEHLT' } else { 'da' })" `
        -ForegroundColor $(if ($verwaist) { 'Yellow' } else { 'DarkGray' })

    if ($verwaist -and -not $KeepPackage) {
        if ($PSCmdlet.ShouldProcess($paket.PackageFullName, 'Registrierung entfernen')) {
            $paket | Remove-AppxPackage
            Write-Host '  -> Registrierung entfernt. Die Dateien bleiben liegen.' -ForegroundColor Green
        }
    } elseif ($verwaist) {
        Write-Host '  -> bleibt stehen (-KeepPackage). Es haelt weiter alte Kachelbilder.' -ForegroundColor Yellow
    }
} else {
    Write-Host 'Kein Paket registriert — der unpackaged Weg gilt.' -ForegroundColor DarkGray
}

Write-Host ''

# ---- 2. Der Symbolcache ----

$dir = Join-Path $env:LocalAppData 'Microsoft\Windows\Explorer'
$dateien = @(Get-ChildItem $dir -Filter 'iconcache*.db' -Force -ErrorAction SilentlyContinue)

Write-Host "Symbolcache: $($dateien.Count) Dateien in $dir" -ForegroundColor DarkGray

if (-not $PSCmdlet.ShouldProcess('Explorer', 'beenden, Symbolcache loeschen, neu starten')) {
    return
}

Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$weg = 0

foreach ($datei in $dateien) {
    try {
        Remove-Item $datei.FullName -Force -ErrorAction Stop
        $weg++
    } catch {
        # Eine Datei, die Windows noch haelt, ist kein Grund abzubrechen: der
        # Rest zu loeschen genuegt in der Praxis.
    }
}

Write-Host "  -> $weg von $($dateien.Count) geloescht" -ForegroundColor Green

& "$env:WINDIR\System32\ie4uinit.exe" -show
Start-Sleep -Seconds 1

if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
    Start-Process explorer.exe
}

Start-Sleep -Seconds 3
Write-Host "  -> Explorer laeuft wieder: $([bool](Get-Process -Name explorer -ErrorAction SilentlyContinue))" -ForegroundColor Green

Write-Host ''
Write-Host 'Noch zu tun:' -ForegroundColor Cyan
Write-Host '  1. nipp neu starten — das Symbol im Infobereich kommt nach einem'
Write-Host '     Explorer-Neustart nicht von selbst zurueck.'
Write-Host '  2. Den Pin in der Taskleiste loesen und neu anheften, falls er'
Write-Host '     immer noch alt aussieht.'
