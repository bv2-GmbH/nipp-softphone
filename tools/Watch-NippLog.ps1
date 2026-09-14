<#
.SYNOPSIS
  Liest das Log von nipp live mit — Prüfhilfe für die manuelle Testmatrix.

.DESCRIPTION
  Für die Testfälle aus docs/test-matrix.md, die einen Menschen am Gerät
  brauchen. Zeigt nur die Zeilen, die für die Abnahme zählen, und hebt
  Zustandswechsel hervor. Vollständige Logs stehen in der Datei.

  Nebenher offen lassen, während getestet wird. Beenden mit Strg+C.

.EXAMPLE
  .\tools\Watch-NippLog.ps1
  .\tools\Watch-NippLog.ps1 -All        # auch Debug-Zeilen
#>
[CmdletBinding()]
param(
    [switch]$All
)

$logDir = Join-Path $env:LOCALAPPDATA 'nipp\logs'

if (-not (Test-Path $logDir)) {
    Write-Host "Noch kein Log unter $logDir — nipp mindestens einmal starten." -ForegroundColor Yellow
    exit 1
}

$log = Get-ChildItem $logDir -Filter *.log |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1

Write-Host "Lese mit: $($log.FullName)" -ForegroundColor DarkGray
Write-Host "Beenden mit Strg+C.`n" -ForegroundColor DarkGray

# Muster, die für die Abnahme zählen
$interesting = 'Registrierung|Anruf|Gespraech|Aufnahme|Weitergeleitet|weitergeleitet|Mailbox|Netzwerk|Standby|aufgewacht|Audiogeraete|Ueberlauf|Überlauf|Konto'

Get-Content $log.FullName -Wait -Tail 5 | ForEach-Object {
    $line = $_

    if (-not $All -and $line -notmatch $interesting) {
        return
    }

    $colour = switch -Regex ($line) {
        '\[ERR\]|\[FTL\]'                      { 'Red' }
        '\[WRN\]'                              { 'Yellow' }
        'Registrierung Registered'             { 'Green' }
        'StreamsRunning|Connected'             { 'Green' }
        'Aufnahme'                             { 'Magenta' }
        'Registrierung Failed|Unregistered'    { 'Red' }
        default                                { 'Gray' }
    }

    # Zeitstempel und Logger-Name kürzen, damit die Meldung lesbar bleibt
    $short = $line -replace '^(\d{4}-\d{2}-\d{2} )', '' -replace 'Nipp\.(Core|App)\.[\w\.]+\.', ''
    Write-Host $short -ForegroundColor $colour
}
