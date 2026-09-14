<#
.SYNOPSIS
    Wertet aus, ob das Besetztlampenfeld mit der Anlage funktioniert (§8.4).

.DESCRIPTION
    nipp abonniert die Präsenz über Linphones FriendList, und die sendet
    SUBSCRIBE mit `Event: presence` (RFC 3856, SIMPLE). Ein klassisches
    Besetztlampenfeld an einem Tischtelefon verwendet dagegen `Event: dialog`
    (RFC 4235). Das ist nicht dasselbe:

        presence  sagt, was ein Client über sich selbst veröffentlicht
        dialog    sagt, was die Anlage über eine Nebenstelle weiss

    Ob die Anlage von bv2 das eine oder das andere kann, war bis jetzt
    ungeprüft. Dieses Skript liest das Protokoll und sagt, was herausgekommen
    ist.

.PARAMETER Log
    Eine bestimmte Protokolldatei. Ohne Angabe die neueste.

.PARAMETER Follow
    Mitlesen, während nipp läuft.

.EXAMPLE
    # 1. In nipp unter Einstellungen -> Kontakte eine Team-Nebenstelle
    #    eintragen, mit SIP-Adresse (sip:153@pbx.example.ch), speichern
    # 2. nipp neu starten
    # 3. Dieses Skript aufrufen
    .\tools\Test-Blf.ps1
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
        Write-Host "Kein Protokollordner unter $directory. Läuft nipp schon einmal gelaufen?" -ForegroundColor Red
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

# Die Zeilen, auf die es ankommt
# Mit Protokollierung auf Debug stehen auch die SIP-Nachrichten des SDK im
# Protokoll — dann sieht man die Antwort der Anlage im Klartext.
$pattern = 'Besetztlampenfeld|Abonnement|NOTIFY|Praesenz geaendert|Nebenstellenadresse|SUBSCRIBE|SIP/2.0 [2-6][0-9][0-9]'

if ($Follow) {
    Write-Host 'Mitlesen — Strg+C beendet.' -ForegroundColor DarkGray
    Get-Content $Log -Tail 0 -Wait | Where-Object { $_ -match $pattern }
    return
}

# Nur der letzte Programmstart: aeltere Laeufe hatten eine andere
# Konfiguration, und ihre Zeilen wuerden den Befund verfaelschen.
$all = Get-Content $Log
$startIndex = 0

for ($i = $all.Count - 1; $i -ge 0; $i--) {
    if ($all[$i] -match 'nipp startet') { $startIndex = $i; break }
}

$lines = $all[$startIndex..($all.Count - 1)] | Where-Object { $_ -match $pattern }

if ($startIndex -gt 0) {
    Write-Host "Ausgewertet wird der Start um $($all[$startIndex] -replace '^(\S+ \S+).*', '$1')." -ForegroundColor DarkGray
    Write-Host ''
}

if (-not $lines) {
    Write-Host 'Keine Spur vom Besetztlampenfeld im Protokoll.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Wahrscheinlich ist keine Team-Nebenstelle eingetragen. In nipp:'
    Write-Host '  Einstellungen -> Kontakte -> Team-Nebenstellen. Wichtig ist die'
    Write-Host '  SIP-Adresse (sip:153@pbx.example.ch) — ohne sie wird nicht abonniert.'
    exit 0
}

$lines | ForEach-Object {
    $colour = switch -Regex ($_) {
        '\[ERR\]|\[FTL\]' { 'Red' }
        '\[WRN\]'         { 'Yellow' }
        default           { 'Gray' }
    }

    Write-Host $_ -ForegroundColor $colour
}

# ── Auswertung ───────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '--- Befund ---------------------------------------------' -ForegroundColor Cyan

$subscribed = $lines | Where-Object { $_ -match 'Besetztlampenfeld: (\d+) Nebenstellen' } |
    Select-Object -Last 1

$count = 0
if ($subscribed -match 'Besetztlampenfeld: (\d+) Nebenstellen') { $count = [int]$Matches[1] }

if ($count -eq 0) {
    Write-Host '  Es wurde nichts abonniert.' -ForegroundColor Yellow
    Write-Host '  Eine Team-Nebenstelle MIT SIP-Adresse eintragen, dann nipp neu starten.'
    exit 0
}

Write-Host "  $count Nebenstelle(n) abonniert." -ForegroundColor Gray

$active   = $lines | Where-Object { $_ -match 'Abonnement .*: Active' }
$badEvent = $lines | Where-Object { $_ -match 'BadEvent|SIP 489|SIP/2.0 489' }
$rejected = $lines | Where-Object { $_ -match 'abgelehnt' }
$notify   = $lines | Where-Object { $_ -match 'NOTIFY' }
$presence = $lines | Where-Object { $_ -match 'Praesenz geaendert' }

if ($badEvent) {
    Write-Host ''
    Write-Host '  ERGEBNIS: Die Anlage kennt das Ereignis "presence" nicht.' -ForegroundColor Red
    Write-Host ''
    Write-Host '  Für ein Besetztlampenfeld will sie "dialog" (RFC 4235) — den Weg, den'
    Write-Host '  auch ein Tischtelefon geht. Das ist mit diesem SDK machbar'
    Write-Host '  (Core.Subscribe mit "dialog" plus eigenes Auswerten der NOTIFY-Bodies),'
    Write-Host '  aber ein Umbau von etwa einem Tag. Als ADR festhalten, bevor es losgeht.'
    exit 0
}

if ($rejected) {
    Write-Host ''
    Write-Host '  ERGEBNIS: Die Anlage hat das Abonnement abgelehnt.' -ForegroundColor Red
    Write-Host '  Der Grund steht oben in der Zeile. Meist fehlt dem Konto die'
    Write-Host '  Berechtigung, fremde Nebenstellen zu beobachten — das klärt die'
    Write-Host '  Anlagenadministration.'
    exit 0
}

if (-not $active) {
    Write-Host ''
    Write-Host '  ERGEBNIS: Kein Zustand vom Abonnement im Protokoll.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Entweder laeuft noch der alte Build (die Zustandspruefung kam am 05.09.'
    Write-Host '  abends dazu), oder die Anlage hat nie geantwortet. Fuer den Klartext:'
    Write-Host '  Einstellungen -> Erweitert -> Protokollierung auf Debug, nipp neu starten,'
    Write-Host '  dann stehen SUBSCRIBE und die SIP-Antwort hier.'
    exit 0
}

Write-Host '  Der SUBSCRIBE wurde angenommen (Active).' -ForegroundColor Green

if ($presence) {
    Write-Host ''
    Write-Host '  ERGEBNIS: Das Besetztlampenfeld funktioniert.' -ForegroundColor Green
    Write-Host '  Die Anlage meldet Präsenz. Zur Gegenprobe: die beobachtete Nebenstelle'
    Write-Host '  telefonieren lassen und zusehen, ob die Lampe in nipp umschlägt.'
    Write-Host ''
    Write-Host '  Achtung beim Zustand "klingelt": über das presence-Ereignis kommt der'
    Write-Host '  nie — das SDK kennt dort nur Online, Busy, DoNotDisturb und Offline.'
    Write-Host '  Wer "klingelt" braucht, braucht trotzdem den dialog-Weg.' -ForegroundColor Yellow
    exit 0
}

if ($notify) {
    Write-Host ''
    Write-Host '  ERGEBNIS: Abonnement steht, NOTIFY kommt an — aber keine Präsenz daraus.' -ForegroundColor Yellow
    Write-Host '  Der Inhalt des NOTIFY passt vermutlich nicht zu dem, was das SDK erwartet.'
    Write-Host '  Die Grösse in Bytes steht oben; ein leeres NOTIFY heisst "nichts zu melden".'
    exit 0
}

Write-Host ''
Write-Host '  ERGEBNIS: Abonnement steht, aber es kommt kein NOTIFY.' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Die Anlage stimmt zu, hat aber nichts zu melden — typisch, wenn die'
Write-Host '  beobachtete Nebenstelle ihre Präsenz nicht veröffentlicht. Ein'
Write-Host '  Tischtelefon tut das normalerweise nicht.'
Write-Host ''
Write-Host '  Damit ist die Frage beantwortet: für ein echtes Besetztlampenfeld'
Write-Host '  braucht es den dialog-Weg (RFC 4235).'
