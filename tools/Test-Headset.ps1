<#
.SYNOPSIS
    Wertet aus, was das Headset wirklich geschickt hat und was nipp daraus
    gemacht hat (§22.5).

.DESCRIPTION
    "Die Taste tut nichts" ist als Befund unbrauchbar — und am 09.09.2026 war
    er nicht einmal von "hier kommt gar nichts an" zu unterscheiden, weil über
    empfangene HID-Reports nie eine Zeile im Protokoll stand. Der Fehler, der
    jedes Gespräch kostete, lag zwei Tage im Log ohne Spur:

        16:01:57.797  Toast-Aktion: accept
        16:01:57.892  Anruf 5e83db16: Incoming -> Connected
        16:01:59.458  Auflegen am Headset gedrueckt        <- niemand drueckte
        16:01:59.547  Anruf 5e83db16: Connected -> Ended

    Dieses Skript liest das Protokoll und sagt vier Dinge:

      1. An welchem HID-Gerät nipp hängt — und ob es das richtige ist. Open
         nimmt das ERSTE Gerät mit Telefonieseite; an einem Rechner mit drei
         Headsets ist das keine rhetorische Frage.

      2. Ob das Gerät seine Gabeltaste überhaupt auf HookSwitch (0x20) legt.
         Fehlt sie in den Tasten, wird kein Tastendruck ankommen, und keine
         Zustandslogik ändert daran etwas.

      3. Welche Reports kamen, und was daraus wurde: Betätigung, Loslassen
         oder das Echo einer eigenen Meldung.

      4. Ob der Befund vom 09.09.2026 zurück ist: ein "Auflegen am Headset"
         kurz nach einem Übergang auf Connected, ohne dass eine Taste gemeldet
         wurde.

      5. Seit dem 10.09.2026: **welche Ausgangsberichte unterdrückt wurden und
         warum** — und die Probe auf den Fehler dahinter. „Bin ich in einem
         Teams-Meeting und es klingelt auf nipp, fliege ich aus dem Meeting":
         ein Bericht an das Gerät ist keine Lampe, sondern eine Mitteilung an
         ein Gerät, das nipp mit anderen Programmen teilt. Ein leerer Bericht,
         obwohl gar kein Anruf offen war, ist der Fehler — dreizehnmal am
         10.09.2026.

    Dafür muss die Protokollierung auf Debug stehen (Einstellungen ->
    Erweitert), sonst fehlen die Report-Zeilen.

.PARAMETER Log
    Eine bestimmte Protokolldatei. Ohne Angabe die neueste.

.PARAMETER Follow
    Mitlesen, während nipp läuft. Nützlich, um die Taste zu drücken und
    zuzusehen, ob überhaupt etwas ankommt.

.EXAMPLE
    # 1. Protokollierung auf Debug stellen, nipp neu starten
    # 2. Anrufen lassen, mit der Taste am Headset annehmen (T80)
    # 3. Dieses Skript aufrufen
    .\tools\Test-Headset.ps1
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

    # <b>Nicht nach LastWriteTime sortieren.</b> Solange nipp laeuft, haelt
    # Serilog die Datei offen, und Windows aktualisiert Zeitstempel und Groesse
    # im Verzeichniseintrag dann nicht — die gerade beschriebene Datei sieht
    # aelter aus als eine, die seit Stunden liegt.
    #
    # Und auch nicht nach dem Namen als Zeichenfolge: PowerShell vergleicht
    # kulturabhaengig und gewichtet Satzzeichen anders, weshalb
    # „nipp-20260909.log" vor „nipp-20260909_001.log" landet. Serilog rollt
    # <Datum>, dann _001, _002 — also beide Teile lesen und der Zahl nach
    # sortieren.
    $Log = Get-ChildItem $directory -Filter 'nipp-*.log' |
        ForEach-Object {
            if ($_.Name -match '^nipp-(\d{8})(?:_(\d+))?\.log$') {
                [pscustomobject]@{
                    Datei  = $_.FullName
                    Tag    = $Matches[1]
                    Nummer = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
                }
            }
        } |
        Sort-Object Tag, Nummer |
        Select-Object -Last 1 -ExpandProperty Datei

    if (-not $Log) {
        Write-Host "Keine Protokolldatei in $directory." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $Log)) {
    Write-Host "Protokolldatei $Log gibt es nicht." -ForegroundColor Red
    exit 1
}

Write-Host "Protokoll: $Log" -ForegroundColor DarkGray
Write-Host ''

if ($Follow) {
    Write-Host 'Mitlesen — Strg+C beendet. Jetzt die Taste am Headset druecken.' -ForegroundColor DarkGray
    Get-Content $Log -Tail 0 -Wait | Where-Object { $_ -match 'Headset|Gabel' }
    return
}

# <b>Ueber einen eigenen Strom lesen, nicht mit Get-Content.</b> Serilog haelt
# die Datei offen; Get-Content liest bis zur Groesse aus dem Verzeichniseintrag,
# und die hinkt bei einer offenen Datei nach. Bei laufendem nipp fehlt sonst
# genau der Teil, um den es geht — gemessen am 09.09.2026: die Datei sah
# unveraendert aus, waehrend nipp seit sechs Minuten hineinschrieb.
$stream = [System.IO.File]::Open($Log, 'Open', 'Read', 'ReadWrite')

try {
    $reader = New-Object System.IO.StreamReader($stream)

    try {
        $all = $reader.ReadToEnd() -split "`r?`n"
    } finally {
        $reader.Dispose()
    }
} finally {
    $stream.Dispose()
}

# Nur der letzte Programmstart: aeltere Laeufe hatten eine andere
# Konfiguration, und ihre Zeilen wuerden den Befund verfaelschen.
$startIndex = 0

for ($i = $all.Count - 1; $i -ge 0; $i--) {
    if ($all[$i] -match 'nipp startet') { $startIndex = $i; break }
}

$scope = $all[$startIndex..($all.Count - 1)]

if ($startIndex -gt 0) {
    $stempel = $all[$startIndex] -replace '^(\S+ \S+).*', '$1'
    Write-Host "Ausgewertet wird der Start um $stempel." -ForegroundColor DarkGray
    Write-Host ''
}

function Show-Section {
    param([string]$Title, [string[]]$Lines, [string]$Empty, [int]$Max = 25)

    Write-Host "== $Title" -ForegroundColor Cyan

    if (-not $Lines) {
        Write-Host "   $Empty" -ForegroundColor Yellow
        Write-Host ''
        return
    }

    if ($Lines.Count -gt $Max) {
        Write-Host "   ($($Lines.Count) Zeilen, die letzten $Max)" -ForegroundColor DarkGray
        $Lines = $Lines | Select-Object -Last $Max
    }

    $Lines | ForEach-Object {
        $colour = switch -Regex ($_) {
            '\[ERR\]|\[FTL\]|angenommen=False' { 'Red' }
            '\[WRN\]'                          { 'Yellow' }
            'gedrueckt'                        { 'Green' }
            default                            { 'Gray' }
        }

        Write-Host "   $($_ -replace '^\d{4}-\d{2}-\d{2} ', '')" -ForegroundColor $colour
    }

    Write-Host ''
}

# ---- 1. An welchem Geraet haengt nipp ----

$geoeffnet = @($scope | Where-Object { $_ -match 'Headset geoeffnet:' })

Show-Section 'Das geoeffnete Geraet' $geoeffnet `
    'Keine Zeile dazu. Entweder steht die Protokollierung nicht auf Debug, oder es wurde kein Geraet gefunden (siehe unten).'

if (-not $geoeffnet) {
    Show-Section 'Was die Suche sagt' `
        (@($scope | Where-Object { $_ -match 'Kein Headset mit Telefonie-Tasten|liess sich nicht anbinden' })) `
        'Auch dazu nichts. Dann lief die Suche nicht.'
}

# ---- 2. Legt das Geraet seine Gabeltaste auf 0x20? ----

Write-Host '== Die Gabeltaste (Usage 0x20 auf Seite 0x0B)' -ForegroundColor Cyan

if (-not $geoeffnet) {
    Write-Host '   Nicht zu beurteilen: kein geoeffnetes Geraet im Protokoll.' -ForegroundColor Yellow
} elseif ($geoeffnet[-1] -match 'Tasten: (.+?)(?: \| |$)') {
    $tasten = $Matches[1]

    if ($tasten -match '0x20') {
        Write-Host '   Vorhanden. Das Geraet fuehrt die Gabeltaste dort, wo nipp sie liest.' -ForegroundColor Green
    } elseif ($tasten -match '0x0B 0x([0-9A-F]+)-0x([0-9A-F]+)') {
        $von = [Convert]::ToInt32($Matches[1], 16)
        $bis = [Convert]::ToInt32($Matches[2], 16)

        if (0x20 -ge $von -and 0x20 -le $bis) {
            Write-Host '   Im gemeldeten Bereich enthalten.' -ForegroundColor Green
        } else {
            Write-Host '   NICHT enthalten. Dann kommt von diesem Geraet kein Tastendruck an,' -ForegroundColor Red
            Write-Host '   und die Ursache liegt vor jeder Zustandslogik.' -ForegroundColor Red
        }
    } else {
        Write-Host "   Unklar. Gemeldet: $tasten" -ForegroundColor Yellow
    }
}

Write-Host ''

# ---- 3. Was kam an, und was wurde daraus ----

Show-Section 'Empfangene Berichte' `
    (@($scope | Where-Object { $_ -match 'Headset-Bericht' })) `
    'Kein einziger Bericht. Entweder ist die Protokollierung nicht auf Debug, oder das Geraet schickt nichts — dann liegt es nicht an der Deutung.'

$verworfen = @($scope | Where-Object { $_ -match 'Gabelmeldung .* verworfen' })
$gedrueckt = @($scope | Where-Object { $_ -match 'am Headset gedrueckt' })

Write-Host '== Gabelmeldungen' -ForegroundColor Cyan
Write-Host "   $($gedrueckt.Count) als Tastendruck gedeutet, $($verworfen.Count) verworfen" -ForegroundColor Gray

if ($verworfen) {
    $verworfen |
        ForEach-Object { if ($_ -match 'verworfen: (\w+)') { $Matches[1] } } |
        Group-Object |
        ForEach-Object { Write-Host ("   {0,3}x  {1}" -f $_.Count, $_.Name) -ForegroundColor DarkGray }
}

Write-Host ''

Show-Section 'Ausgeloeste Aktionen' $gedrueckt `
    'Keine. Fuer T79/T80 ist damit nichts nachgewiesen — und wenn Berichte ankamen, steht oben, warum sie verworfen wurden.'

$zustaende = @($scope | Where-Object { $_ -match 'Headset-Zustand gemeldet' })

Show-Section 'Gemeldete Zustaende (Lampen und Gabel)' $zustaende `
    'Keine. Dann hat nipp dem Geraet nie etwas gemeldet — die Off-Hook-Lampe bleibt aus (T84).'

# ---- Wie lange ein Report zum Geraet braucht ----
#
# Am 09.09.2026 dauerte einer am Jabra Engage 75 <b>2,94 Sekunden</b>. Solange
# laeutet das Headset nach dem Annehmen weiter, und solange ist jede
# Gabelmeldung des Geraets eine Antwort und kein Tastendruck. Ohne diese
# Messung war das nicht zu erklaeren.

if ($zustaende) {
    $dauern = $zustaende | ForEach-Object {
        if ($_ -match 'nach (\d+) ms') { [int]$Matches[1] }
    }

    if ($dauern) {
        $langsam = @($dauern | Where-Object { $_ -ge 500 })

        Write-Host '== Dauer der Reports an das Geraet' -ForegroundColor Cyan
        Write-Host ("   {0} Reports, laengster {1} ms, Mittel {2} ms" -f
            $dauern.Count,
            ($dauern | Measure-Object -Maximum).Maximum,
            [int]($dauern | Measure-Object -Average).Average) -ForegroundColor Gray

        if ($langsam) {
            Write-Host "   $($langsam.Count) davon ueber 500 ms." -ForegroundColor Yellow
            Write-Host '   Das ist die Zeit, die das Headset nach dem Annehmen weiterlaeutet —' -ForegroundColor Yellow
            Write-Host '   und das Fenster, in dem nipp jede Gabelmeldung als Antwort des' -ForegroundColor Yellow
            Write-Host '   Geraets verwirft. Ein Druck in dieser Zeit wirkt nicht.' -ForegroundColor Yellow
        } else {
            Write-Host '   Alle unter 500 ms.' -ForegroundColor Green
        }

        Write-Host ''
    }
}

# ---- 4. Ist der Befund vom 09.09.2026 zurueck? ----
#
# Das Muster: ein Auflegen wenige Sekunden nach einem Uebergang auf Connected,
# ohne dass davor ein Bericht als Tastendruck gedeutet wurde. Das war die
# erfundene Flanke.

Write-Host '== Rueckfallprobe (der Befund vom 09.09.2026)' -ForegroundColor Cyan

$verdacht = @()
$letztesConnected = $null

foreach ($line in $scope) {
    if ($line -notmatch '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})') { continue }

    $zeit = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null)

    if ($line -match '-> Connected') {
        $letztesConnected = $zeit
        continue
    }

    if ($line -match 'Auflegen am Headset gedrueckt' -and $letztesConnected) {
        $abstand = ($zeit - $letztesConnected).TotalMilliseconds

        if ($abstand -ge 0 -and $abstand -lt 3000) {
            $verdacht += "$($zeit.ToString('HH:mm:ss.fff')) — $([int]$abstand) ms nach dem Verbinden"
        }
    }
}

if ($verdacht) {
    Write-Host '   VERDACHT: Auflegen kurz nach dem Verbinden.' -ForegroundColor Red
    $verdacht | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '   Zu pruefen: stand davor ein Bericht, der als Tastendruck gedeutet wurde?' -ForegroundColor Red
    Write-Host '   Wenn nicht, ist die erfundene Flanke zurueck — dann schreibt irgendwer' -ForegroundColor Red
    Write-Host '   den Gabelzustand wieder selbst (siehe HookWatch).' -ForegroundColor Red
} else {
    Write-Host '   Kein Auflegen innerhalb von 3 s nach einem Verbinden.' -ForegroundColor Green
}

Write-Host ''

# ---- 5. Ausgangsberichte und ihr Anlass (der Befund vom 10.09.2026) ----
#
# „Bin ich in einem Teams-Meeting und es klingelt auf nipp, fliege ich aus dem
# Meeting." Ein Ausgangsbericht ist keine Lampe, sondern eine Mitteilung an ein
# Geraet, das nipp mit anderen Programmen teilt — und Teams deutet den
# Zustandswechsel, den das Geraet daraufhin meldet, als Tastendruck.

Show-Section 'Unterdrueckte Ausgangsberichte' `
    ($scope | Where-Object { $_ -match 'Ausgangsbericht unterdrueckt' }) `
    'Keiner unterdrueckt. Ohne Teams-Gespraech ist das richtig; mit einem waere zu pruefen, ob die Fremdbelegung ueberhaupt erkannt wurde.'

Show-Section 'Geraetewechsel' `
    ($scope | Where-Object { $_ -match 'Telefoniegeraet .* unveraendert|Telefoniegeraet gewechselt' }) `
    'Kein gemeldeter Geraetewechsel in diesem Lauf.'

# Die Probe: ein Bericht, obwohl nipp keinen Anruf hatte. Bis zum 10.09.2026
# ging genau der beim Start und bei jedem Audiogeraetewechsel hinaus — am
# 10.09.2026 dreizehnmal, ohne dass ein Anruf existierte.

Write-Host '== Probe: Bericht ohne Anlass' -ForegroundColor Cyan

$ohneAnlass = @()
$anrufOffen = $false

foreach ($line in $scope) {
    if ($line -match 'neu -> Incoming|-> Dialing|-> Ringing|-> Connected|-> OnHold') {
        $anrufOffen = $true
        continue
    }

    if ($line -match '-> Ended|-> Failed') {
        $anrufOffen = $false
        continue
    }

    if (-not $anrufOffen -and
        $line -match 'Headset-Zustand gemeldet .*imGespraech=false, klingelt=false, stumm=false') {
        $ohneAnlass += ($line -replace '^\d{4}-\d{2}-\d{2} ', '')
    }
}

if ($ohneAnlass) {
    Write-Host "   VERDACHT: $($ohneAnlass.Count) leere Bericht(e), ohne dass ein Anruf offen war." -ForegroundColor Red
    $ohneAnlass | Select-Object -First 5 | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '   Jeder davon kann ein fremdes Gespraech am selben Geraet beenden.' -ForegroundColor Red
    Write-Host '   Zu pruefen: greift HeadsetSignalGate noch? Erwartet waere daneben eine' -ForegroundColor Red
    Write-Host "   Zeile 'Ausgangsbericht unterdrueckt (ohne Anlass)'." -ForegroundColor Red
} else {
    Write-Host '   Kein leerer Bericht ohne offenen Anruf. So soll es sein.' -ForegroundColor Green
}

Write-Host ''
