#Requires -Version 5.1
<#
.SYNOPSIS
  Rollt nipp auf einem Arbeitsplatz aus — Setup und Auslieferungszustand in
  einem Schritt.

.DESCRIPTION
  Bis zum 13.09.2026 kostete jeder Arbeitsplatz vier Handgriffe: Setup
  anklicken, SmartScreen wegklicken, die Factory-Datei nach %PROGRAMDATA%
  kopieren und das Passwort eintippen. Der dritte Schritt stand nur in der
  Dokumentation — kein Skript und kein Installationsschritt legte die Datei ab
  (Befund E6). Ein Arbeitsplatz ohne sie nimmt kein Kundenprofil entgegen, weil
  die Provisioning-Adresse dort steht; wer das nicht wusste, suchte den Fehler
  beim Server.

  Dieses Skript macht beides und ist absichtlich klein: es kopiert die
  Factory-Datei und startet das Setup. Mehr ist es nicht, und mehr soll es
  nicht sein — was es tut, muss ein Administrator in zwei Minuten nachlesen
  können.

  <b>Was es NICHT loest:</b> das Setup ist unsigniert (AP9.2). SmartScreen
  haelt den ersten Start weiterhin auf, wenn ein Mensch doppelklickt. Ueber ein
  GPO-Startskript oder Intune laeuft es als SYSTEM und die Frage stellt sich
  nicht — genau dafuer ist -Silent gedacht.

.PARAMETER Setup
  Pfad zu `nipp-win-<kanal>-Setup.exe`. Ohne Angabe wird die neueste aus
  `dist\releases\` genommen.

.PARAMETER Factory
  Pfad zur Auslieferungskonfiguration. Ohne Angabe `build\nipp-factory.xml`
  neben diesem Skript.

.PARAMETER Profile
  Optional: ein kundenspezifisches Profil (`nippprov neu`), das zusaetzlich
  abgelegt wird. Es gewinnt ueber die Factory-Datei und darf Konten enthalten.

.PARAMETER Silent
  Ohne Rueckfragen und ohne Fortschrittsfenster — fuer GPO-Startskripte und
  Intune.

.PARAMETER WhatIf
  Zeigt, was geschaehe, und aendert nichts.

.EXAMPLE
  .\build\Install-Nipp.ps1
  Interaktiv auf dem eigenen Rechner.

.EXAMPLE
  .\build\Install-Nipp.ps1 -Setup \\fileserver\nipp\nipp-win-stable-Setup.exe `
      -Factory \\fileserver\nipp\nipp-factory.xml -Silent
  Als Computerstartskript per Gruppenrichtlinie.

.NOTES
  Adminrechte braucht nur das Kopieren nach %PROGRAMDATA%. Das Setup selbst ist
  per-user (Velopack, ADR-038) und laeuft ohne. Ohne Adminrechte meldet das
  Skript das und installiert trotzdem — dann fehlt nur der
  Auslieferungszustand, und das steht dann auch da.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Setup,
    [string]$Factory,
    [string]$Profile,
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$zielOrdner = Join-Path $env:ProgramData 'bv2\nipp'

function Schreibe($text) {
    if (-not $Silent) { Write-Host $text }
}

# ---------------------------------------------------------------- Setup finden

if (-not $Setup) {
    $releases = Join-Path $repoRoot 'dist\releases'

    if (-not (Test-Path $releases)) {
        throw "Kein Setup angegeben, und '$releases' gibt es nicht. Entweder -Setup benutzen oder zuerst build\Release-Nipp.ps1 laufen lassen."
    }

    $Setup = Get-ChildItem $releases -Filter 'nipp-win-*-Setup.exe' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $Setup) {
        throw "Unter '$releases' liegt kein 'nipp-win-*-Setup.exe'. Zuerst build\Release-Nipp.ps1 laufen lassen."
    }
}

if (-not (Test-Path $Setup)) {
    throw "Das Setup '$Setup' gibt es nicht."
}

Schreibe "Setup:   $Setup"

# ------------------------------------------------- Auslieferungszustand finden

if (-not $Factory) {
    $Factory = Join-Path $PSScriptRoot 'nipp-factory.xml'
}

if (-not (Test-Path $Factory)) {
    throw "Die Auslieferungskonfiguration '$Factory' gibt es nicht."
}

# Vor dem Kopieren pruefen, nicht danach.
#
# Eine kaputte Datei nach %PROGRAMDATA% zu legen hiesse, dass nipp bei JEDEM
# Start eine Warnung schreibt und der Auslieferungszustand trotzdem fehlt —
# und der Arbeitsplatz sieht aus, als waere er eingerichtet.
try {
    [xml]$geprueft = Get-Content $Factory -Raw

    if ($geprueft.DocumentElement.Name -ne 'nipp-provisioning') {
        throw "Wurzelelement ist '$($geprueft.DocumentElement.Name)', erwartet 'nipp-provisioning'."
    }
} catch {
    throw "'$Factory' ist keine brauchbare nipp-Konfiguration: $($_.Exception.Message)"
}

Schreibe "Factory: $Factory"

if ($Profile) {
    if (-not (Test-Path $Profile)) {
        throw "Das Kundenprofil '$Profile' gibt es nicht."
    }

    Schreibe "Profil:  $Profile"
}

# --------------------------------------------------------------- Ablegen

$istAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $istAdmin) {
    Write-Warning "Ohne Adminrechte: '$zielOrdner' laesst sich nicht beschreiben. nipp wird installiert, aber OHNE Auslieferungszustand — Konten und Provisioning-Adresse fehlen dann. Das Skript in einer Administrator-Konsole erneut laufen lassen."
} elseif ($PSCmdlet.ShouldProcess($zielOrdner, 'Auslieferungszustand ablegen')) {
    if (-not (Test-Path $zielOrdner)) {
        New-Item -ItemType Directory -Path $zielOrdner -Force | Out-Null
    }

    Copy-Item $Factory (Join-Path $zielOrdner 'nipp-factory.xml') -Force
    Schreibe "  -> $zielOrdner\nipp-factory.xml"

    if ($Profile) {
        Copy-Item $Profile (Join-Path $zielOrdner 'nipp-profil.xml') -Force
        Schreibe "  -> $zielOrdner\nipp-profil.xml"
    }
}

# --------------------------------------------------------------- Installieren

if ($PSCmdlet.ShouldProcess($Setup, 'Setup ausfuehren')) {
    $argumente = if ($Silent) { @('--silent') } else { @() }

    $lauf = Start-Process -FilePath $Setup -ArgumentList $argumente -Wait -PassThru

    if ($lauf.ExitCode -ne 0) {
        throw "Das Setup ist mit Code $($lauf.ExitCode) abgebrochen."
    }

    Schreibe 'nipp ist installiert.'
}

if (-not $Silent) {
    Write-Host ''
    Write-Host 'Was jetzt noch fehlt:'
    Write-Host '  - Das SIP-Passwort. Es steht bewusst nicht im Profil (siehe docs/provisioning.md).'
    Write-Host '  - Beim ersten Start ohne Signaturzertifikat: SmartScreen wegklicken.'
}
