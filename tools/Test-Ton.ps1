<#
.SYNOPSIS
    Wertet aus, welchen Weg die Töne von nipp genommen haben (§9.4).

.DESCRIPTION
    „Ich höre nichts" ist als Befund unbrauchbar, und die Abnahme von T78 hat
    genau daran gescheitert: das Freizeichen galt am 07.09.2026 als bestätigt,
    obwohl die dafür geprüften Anrufe intern waren und nie den Rufzustand
    erreichten — es gab also überhaupt keinen Rufton zu hören.

    Dieses Skript liest das Protokoll und sagt drei Dinge:

      1. Welche Soundkarte die Töne des SDK bekommen haben. Das SDK hat zwei
         Geräte-APIs; der Tonspieler liest die alte (`play_sndcard`), der Rest
         von nipp die neue. War die alte leer, endete die Filterkette in
         `MSVoidSink` — berechnet und verworfen.

      2. Wie die ausgehenden Anrufe verlaufen sind. `OutgoingRinging` heisst:
         das SDK spielt seinen Rufton selbst. `OutgoingEarlyMedia` heisst: die
         Anlage schickt `183` mit SDP, das SDK schweigt und spielt, was
         hereinkommt — und wenn nichts hereinkommt, ist es still. Genau das
         war der Befund vom 07.09.2026.

      3. Wo die Töne tatsächlich gelandet sind: `MSWASAPIWrite` ist hörbar,
         `MSVoidSink` nicht. Das ist der Unterschied, den man im Protokoll
         sehen kann, ohne etwas zu hören.

      4. Seit dem 10.09.2026: **wann der Strom der Gegenstelle einsetzte** und
         ob nipp trotzdem angefangen hat. Der Befund war „beim Wählen höre ich
         einen Ton, aber nicht den der Anlage" — nipp legte sich 168 und
         193 ms über deren Rufton, weil es nach 800 ms auf ein Sekundenmittel
         schaute, das noch auf 0 stand. Nennt die Startzeile eine Paketzahl
         über 0, ist der Fehler zurück.

      5. Ob der Anfang des Anlagentons beschädigt ankommt (Jitter-Puffer). Das
         ist eine eigene Spur, T146 — und der zweite Kandidat dafür, dass ein
         Ton „nicht nach unserer Anlage" klingt.

    Dafür muss die Protokollierung auf Debug stehen (Einstellungen ->
    Erweitert), sonst fehlen die Zeilen des SDK.

.PARAMETER Log
    Eine bestimmte Protokolldatei. Ohne Angabe die neueste.

.PARAMETER Follow
    Mitlesen, während nipp läuft.

.EXAMPLE
    # 1. Protokollierung auf Debug stellen, nipp neu starten
    # 2. Eine EXTERNE Nummer anrufen und läuten lassen (T78b)
    # 3. Dieses Skript aufrufen
    .\tools\Test-Ton.ps1
#>

[CmdletBinding()]
param(
    [string]$Log,
    [switch]$Follow
)

$ErrorActionPreference = 'Stop'

# Die Windows-PowerShell-Konsole zeigt Umlaute in ihrer Standardcodepage als
# Fragezeichen — und die Ausgabe hier erklaert Dinge, die man lesen muss.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not $Log) {
    $directory = Join-Path $env:LOCALAPPDATA 'nipp\logs'

    if (-not (Test-Path $directory)) {
        Write-Host "Kein Protokollordner unter $directory. Ist nipp schon einmal gelaufen?" -ForegroundColor Red
        exit 1
    }

    $Log = Get-ChildItem $directory -Filter 'nipp-*.log' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not (Test-Path $Log)) {
    Write-Host "Protokolldatei $Log gibt es nicht." -ForegroundColor Red
    exit 1
}

Write-Host "Protokoll: $Log" -ForegroundColor DarkGray
Write-Host ''

$pattern = 'Klingelton|Rufton|Soundkarte fuer Toene|startRingbackTone|stopRingbackTone|' +
    'startRingtone|MSFilePlayer|MSVoidSink|MSWASAPIWrite|LinphoneCallOutgoing|' +
    'Eigener Rufton|Klingelton-Probe|ring_sndcard|play_sndcard|' +
    'Audiostrom der Gegenstelle|Rufton beim Laeuten|Rufton-Takt'

if ($Follow) {
    Write-Host 'Mitlesen — Strg+C beendet.' -ForegroundColor DarkGray
    Get-Content $Log -Tail 0 -Wait | Where-Object { $_ -match $pattern }
    return
}

# Nur der letzte Programmstart: aeltere Laeufe hatten eine andere
# Konfiguration, und ihre Zeilen wuerden den Befund verfaelschen. Genau dieser
# Fehler steckte in der Abnahme von T78 — die belegenden Zeilen stammten von
# vor der Korrektur.
$all = Get-Content $Log
$startIndex = 0

for ($i = $all.Count - 1; $i -ge 0; $i--) {
    if ($all[$i] -match 'nipp startet') { $startIndex = $i; break }
}

$scope = $all[$startIndex..($all.Count - 1)]

if ($startIndex -gt 0) {
    Write-Host "Ausgewertet wird der Start um $($all[$startIndex] -replace '^(\S+ \S+).*', '$1')." -ForegroundColor DarkGray
    Write-Host ''
}

function Show-Section {
    param([string]$Title, [string[]]$Lines, [string]$Empty)

    Write-Host "== $Title" -ForegroundColor Cyan

    if (-not $Lines) {
        Write-Host "   $Empty" -ForegroundColor Yellow
        Write-Host ''
        return
    }

    $Lines | ForEach-Object {
        $colour = switch -Regex ($_) {
            '\[ERR\]|\[FTL\]|MSVoidSink' { 'Red' }
            '\[WRN\]'                    { 'Yellow' }
            'MSWASAPIWrite'              { 'Green' }
            default                      { 'Gray' }
        }

        Write-Host "   $($_ -replace '^\d{4}-\d{2}-\d{2} ', '')" -ForegroundColor $colour
    }

    Write-Host ''
}

# ---- 1. Die Karten und Dateien ----

Show-Section 'Karten und Dateien' `
    ($scope | Where-Object { $_ -match 'Soundkarte fuer Toene|Klingelton:|Rufton beim Waehlen:|Kein Klingelton|Kein Rufton' }) `
    'Keine Zeile dazu. Ist die Protokollierung auf Debug und nipp danach neu gestartet?'

# ---- 2. Der Verlauf der ausgehenden Anrufe ----

$verlauf = $scope | Where-Object { $_ -match 'moving from state LinphoneCallOutgoing' } | ForEach-Object {
    if ($_ -match 'moving from state (\S+) to (\S+)') { "$($Matches[1]) -> $($Matches[2])" }
}

Write-Host '== Verlauf der ausgehenden Anrufe' -ForegroundColor Cyan

if (-not $verlauf) {
    Write-Host '   Kein ausgehender Anruf im Protokoll. Ohne einen ist nichts zu beurteilen.' -ForegroundColor Yellow
} else {
    $verlauf | Group-Object | Sort-Object Count -Descending | ForEach-Object {
        Write-Host ("   {0,3}x  {1}" -f $_.Count, $_.Name) -ForegroundColor Gray
    }

    $earlyMedia = @($verlauf | Where-Object { $_ -match 'EarlyMedia$' }).Count
    $ringing = @($verlauf | Where-Object { $_ -match 'OutgoingRinging$' }).Count
    $direkt = @($verlauf | Where-Object { $_ -match 'OutgoingProgress -> LinphoneCallConnected' }).Count

    Write-Host ''

    if ($direkt -gt 0 -and $ringing -eq 0 -and $earlyMedia -eq 0) {
        Write-Host '   Befund: die Anrufe gingen ohne Rufzustand direkt auf verbunden.' -ForegroundColor Yellow
        Write-Host '   Es gab also gar keinen Rufton zu hoeren. Fuer T78 ist das kein' -ForegroundColor Yellow
        Write-Host '   Nachweis — dafuer braucht es eine EXTERNE Nummer, die laeutet.' -ForegroundColor Yellow
    } elseif ($earlyMedia -gt 0) {
        Write-Host '   Befund: Early Media. Die Anlage schickt 183 mit SDP; das SDK' -ForegroundColor Gray
        Write-Host '   spielt dann keinen eigenen Rufton, sondern was hereinkommt.' -ForegroundColor Gray
        Write-Host '   Weiter unten steht, ob nipp uebernommen hat.' -ForegroundColor Gray
    } elseif ($ringing -gt 0) {
        Write-Host '   Befund: normaler Rufzustand, das SDK spielt selbst.' -ForegroundColor Gray
    }
}

Write-Host ''

# ---- 3. Kam RTP an, und hat nipp uebernommen? ----

Show-Section 'Welcher Weg spielt' `
    ($scope | Where-Object { $_ -match 'Rufton beim Laeuten:' }) `
    'Keine Zeile dazu — laeuft eine Fassung vor dem 10.09.2026? Ohne sie ist nicht zu sagen, ob das SDK gespielt hat oder die Anlage zu hoeren war.'

Show-Section 'Audiostrom der Gegenstelle' `
    ($scope | Where-Object { $_ -match 'Audiostrom der Gegenstelle beginnt' }) `
    'Kein Strom der Gegenstelle gemessen. Bei Early Media ist genau das der Fall aus ADR-029 — dann darf nipp selbst spielen.'

Show-Section 'Eigener Rufton (RingbackWatch)' `
    ($scope | Where-Object { $_ -match 'Eigener Rufton' }) `
    'Nicht angesprungen. Bei Early Media OHNE Strom waere das der Fehler; kam ein Strom, ist es das gewuenschte Verhalten.'

# ---- 3a. Die Probe auf den Befund vom 10.09.2026 ----
#
# nipp darf nicht anfangen, wenn der Strom der Anlage schon laeuft. Am
# 10.09.2026 tat es das zweimal, 168 und 193 ms lang, weil es auf ein
# Sekundenmittel schaute, das noch auf 0 stand.

$stromZeilen = @($scope | Where-Object { $_ -match 'Audiostrom der Gegenstelle beginnt' })
$startZeilen = @($scope | Where-Object { $_ -match 'Eigener Rufton gestartet' })

if ($startZeilen.Count -gt 0) {
    Write-Host '== Probe: hat nipp blind entschieden?' -ForegroundColor Cyan

    foreach ($start in $startZeilen) {
        if ($start -match 'Pakete (\d+)') {
            $pakete = [int]$Matches[1]

            if ($pakete -gt 0) {
                Write-Host '   Der eigene Rufton begann, obwohl schon Pakete angekommen waren' -ForegroundColor Red
                Write-Host "   ($pakete Stueck). Genau das war der Befund vom 10.09.2026." -ForegroundColor Red
                Write-Host "   $($start -replace '^\d{4}-\d{2}-\d{2} ', '')" -ForegroundColor Red
            } else {
                Write-Host '   Pakete 0 beim Start — die Gegenstelle schickte nichts, also richtig.' -ForegroundColor Green
            }
        } else {
            Write-Host '   Die Startzeile nennt keine Paketzahl: eine Fassung vor dem 10.09.2026.' -ForegroundColor Yellow
        }
    }

    Write-Host ''
} elseif ($stromZeilen.Count -gt 0) {
    Write-Host '== Befund' -ForegroundColor Cyan
    Write-Host '   Die Anlage schickte einen Strom, und nipp hat geschwiegen. So soll es sein.' -ForegroundColor Green
    Write-Host ''
}

# ---- 3b. Kommt der Ton der Anlage unbeschaedigt an? ----

$unconverged = @($scope | Where-Object { $_ -match 'Jitter buffer stays unconverged' }).Count
$zuAlt = @($scope | Where-Object { $_ -match 'discarding too old packet|received too late' }).Count

if ($unconverged -gt 0 -or $zuAlt -gt 0) {
    Write-Host '== Hinweis: der Anfang des Anlagentons' -ForegroundColor Cyan
    Write-Host "   $unconverged x 'Jitter buffer stays unconverged', $zuAlt x Pakete zu spaet." -ForegroundColor Yellow
    Write-Host '   Das heisst NICHT, dass kein RTP ankam — am 10.09.2026 kamen 252 Pakete an,' -ForegroundColor Yellow
    Write-Host '   und der Puffer konvergierte trotzdem eine Sekunde lang nicht. Der Anfang' -ForegroundColor Yellow
    Write-Host '   des Anlagentons kommt dann beschaedigt an. Eigene Spur, T146.' -ForegroundColor Yellow
    Write-Host ''
}

# ---- 4. Wo die Toene gelandet sind ----

$senken = $scope | Where-Object { $_ -match 'ms_filter_link.*(MSVoidSink|MSWASAPIWrite)' }

Show-Section 'Filterketten (die letzte Zeile entscheidet)' `
    ($senken | Select-Object -Last 20) `
    'Keine Filterketten im Protokoll — ohne Debug-Stufe fehlen sie.'

$void = @($senken | Where-Object { $_ -match 'MSVoidSink' }).Count

if ($void -gt 0) {
    Write-Host '== Befund' -ForegroundColor Cyan
    Write-Host "   $void Kette(n) enden in MSVoidSink: dort wird gerechnet und verworfen." -ForegroundColor Red
    Write-Host '   Wenn eine davon zu einem Ton gehoert, ist er nicht zu hoeren.' -ForegroundColor Red
    Write-Host '   Zu pruefen: die Soundkarte oben (play_sndcard) gegen die eines Tons,' -ForegroundColor Red
    Write-Host '   der funktioniert — der Unterschied steht in der letzten Zeile.' -ForegroundColor Red
    Write-Host ''
}
