<#
    Summiert, wie lange ein Konto je Tag NICHT angemeldet («Registered») war
    (LOGBEFUNDE-PLAN.md L5, R3.1/R3.2).

    Anlass: in vierzehn Tagen wurden 2 126 Netzwerkwechsel erkannt und 979
    davon ans SDK gemeldet, bei zwei Sekunden Entprellung in
    ConnectivityMonitor — am 29.10. zwoelf Meldungen in 26 Sekunden. Was
    daraus folgt, war bisher nicht gemessen: ob nipp danach Sekunden oder
    Minuten nicht erreichbar war. Dieses Skript misst genau das, und nur das
    — es aendert nichts an der Entprellung ("Erst messen. ... Dann
    entscheiden.").

    Gelesen wird die Protokollzeile aus TelephonyLog.RegistrationChanged
    ("Registrierung {Status} fuer {Identity}"), eine Zeile je
    Zustandswechsel. Eine Luecke beginnt beim Wechsel WEG von "Registered"
    und endet beim naechsten Wechsel DORTHIN. "InProgress", "None" und
    "Failed" zaehlen alle als nicht angemeldet — waehrend keinem davon kommt
    ein Anruf durch.

    Jede Protokolldatei ist ein Kalendertag; verarbeitet wird je Datei fuer
    sich, nicht ueber Dateigrenzen hinweg. Eine Luecke, die beim ersten
    Ereignis des Tages schon besteht (wir kennen den Beginn nicht — er kann
    vom Vortag stammen oder vorher einfach nicht geloggt worden sein), und
    eine, die beim letzten Ereignis des Tages noch offen ist (nipp beendet,
    oder der Protokollwechsel um Mitternacht trennt sie von ihrem Ende),
    bekommen deshalb KEINE erfundene Dauer, sondern eine eigene Spalte mit
    "mindestens so lange" — siehe OffenAnfang/OffenEnde unten. Sie zaehlen
    nicht in Luecken/Dauer/Laengste hinein, sonst saehe eine Vermutung wie
    eine Messung aus.

    Aufruf:
        .\tools\Zeige-Anmeldeluecken.ps1
        .\tools\Zeige-Anmeldeluecken.ps1 -Tage 14
        .\tools\Zeige-Anmeldeluecken.ps1 -Tage 14 -AlsObjekte
#>
[CmdletBinding()]
param(
    # Wie viele Tage zurueck. 0 heisst: alle Protokolle, die da sind.
    [int]$Tage = 7,

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

# Der Name haengt an TelephonyLog.RegistrationChanged. Aendert er sich dort,
# findet dieses Skript nichts mehr — und das faellt sofort auf, weil die
# Tabelle leer bleibt.
$musterZeile = '^(?<zeit>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[\w+\] \S+ ' +
    'Registrierung (?<status>\S+) fuer (?<identity>.+)$'

# Fuer die Dateigrenze reicht der Zeitstempel irgendeiner Zeile — die
# Registrierung ist nur eine von vielen Meldungen.
$musterZeit = '^(?<zeit>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})'

$kultur = [Globalization.CultureInfo]::InvariantCulture

function ZeitVon($text) {
    $m = [regex]::Match($text, $musterZeit)
    if ($m.Success) { [datetime]::ParseExact($m.Groups['zeit'].Value, 'yyyy-MM-dd HH:mm:ss.fff', $kultur) }
    else { $null }
}

# Erste und letzte Zeile mit Zeitstempel in einer Datei — der Rahmen, in dem
# ueberhaupt etwas ueber ein Konto bekannt sein kann. Es wird in den ersten
# bzw. letzten paar Zeilen gesucht statt stur Zeile 1 und Zeile n zu nehmen:
# eine abgeschnittene oder mehrzeilige Meldung (Stacktrace) traegt selbst
# keinen eigenen Zeitstempel.
function DateiRahmen($pfad) {
    $anfang = $null
    foreach ($zeile in (Get-Content -Path $pfad -TotalCount 25)) {
        $anfang = ZeitVon $zeile
        if ($anfang) { break }
    }

    $ende = $null
    $letzte = Get-Content -Path $pfad -Tail 25
    for ($i = $letzte.Count - 1; $i -ge 0; $i--) {
        $ende = ZeitVon $letzte[$i]
        if ($ende) { break }
    }

    [pscustomobject]@{ Anfang = $anfang; Ende = $ende }
}

$zeilen = foreach ($d in $dateien) {
    $datum = $d.Name -replace '^nipp-(\d{8})\.log$', '$1'
    $rahmen = DateiRahmen $d.FullName

    $treffer = Select-String -Path $d.FullName -Pattern $musterZeile -AllMatches
    if (-not $treffer) { continue }

    $ereignisse = foreach ($t in $treffer) {
        $m = $t.Matches[0]
        [pscustomobject]@{
            Zeit     = [datetime]::ParseExact($m.Groups['zeit'].Value, 'yyyy-MM-dd HH:mm:ss.fff', $kultur)
            Status   = $m.Groups['status'].Value
            Identity = $m.Groups['identity'].Value.Trim()
        }
    }

    foreach ($gruppe in ($ereignisse | Group-Object Identity)) {
        $konto = $gruppe.Name
        $namensTreffer = [regex]::Match($konto, '^"(?<name>[^"]*)"')
        $kontoName = if ($namensTreffer.Success) { $namensTreffer.Groups['name'].Value } else { $konto }

        $sortiert = $gruppe.Group | Sort-Object Zeit

        $luecken = [System.Collections.Generic.List[double]]::new()
        $registriert = $null   # $null: noch keine Beobachtung in dieser Datei
        $luckeSeit = $null
        $offenAnfang = $false
        $offenAnfangBis = $null

        foreach ($e in $sortiert) {
            $istRegistriert = $e.Status -eq 'Registered'

            if ($null -eq $registriert) {
                if (-not $istRegistriert) {
                    $offenAnfang = $true
                    $luckeSeit = $e.Zeit
                }
                $registriert = $istRegistriert
                continue
            }

            if ($registriert -and -not $istRegistriert) {
                $luckeSeit = $e.Zeit
                $registriert = $false
            }
            elseif ((-not $registriert) -and $istRegistriert) {
                if ($offenAnfang -and $null -eq $offenAnfangBis -and $luckeSeit -eq $sortiert[0].Zeit) {
                    # Schliesst die am Dateianfang schon offene Luecke — ihr
                    # wahrer Beginn bleibt unbekannt, darum eigene Spalte statt
                    # eigener Zahl in "Luecken"/"Dauer".
                    $offenAnfangBis = $e.Zeit
                }
                else {
                    $luecken.Add((New-TimeSpan -Start $luckeSeit -End $e.Zeit).TotalMinutes)
                }
                $registriert = $true
                $luckeSeit = $null
            }
        }

        $offenEnde = $false
        $offenEndeMindestens = $null
        if ($registriert -eq $false) {
            $offenEnde = $true
            $bis = if ($rahmen.Ende -and $rahmen.Ende -gt $sortiert[-1].Zeit) { $rahmen.Ende } else { $sortiert[-1].Zeit }
            $offenEndeMindestens = (New-TimeSpan -Start $luckeSeit -End $bis).TotalMinutes
        }

        $offenAnfangMindestens = $null
        if ($offenAnfang -and $null -ne $offenAnfangBis) {
            # Beobachtet ist nur der Rest ab der ersten Protokollzeile der
            # Datei bis zur ersten Anmeldung — "mindestens", nicht "genau".
            $start = if ($rahmen.Anfang -and $rahmen.Anfang -lt $sortiert[0].Zeit) { $rahmen.Anfang } else { $sortiert[0].Zeit }
            $offenAnfangMindestens = (New-TimeSpan -Start $start -End $offenAnfangBis).TotalMinutes
        }
        elseif ($offenAnfang -and $null -eq $offenAnfangBis) {
            # Nie wieder angemeldet an diesem Tag — das ist dieselbe Luecke
            # wie "offen am Ende", nicht eine zweite. OffenAnfang bleibt
            # wahr, aber ohne eigene Mindestdauer, um sie nicht doppelt zu
            # zaehlen; die Dauer steht komplett bei OffenEndeMindestens.
            $start = if ($rahmen.Anfang -and $rahmen.Anfang -lt $sortiert[0].Zeit) { $rahmen.Anfang } else { $sortiert[0].Zeit }
            if ($offenEnde) {
                $offenEndeMindestens = (New-TimeSpan -Start $start -End $(if ($rahmen.Ende -and $rahmen.Ende -gt $sortiert[-1].Zeit) { $rahmen.Ende } else { $sortiert[-1].Zeit })).TotalMinutes
            }
        }

        [pscustomobject]@{
            Datum               = $datum
            Konto               = $kontoName
            Luecken             = $luecken.Count
            DauerMin            = if ($luecken.Count -gt 0) { [math]::Round(($luecken | Measure-Object -Sum).Sum, 1) } else { 0 }
            LaengsteMin         = if ($luecken.Count -gt 0) { [math]::Round(($luecken | Measure-Object -Maximum).Maximum, 1) } else { 0 }
            OffenAnfang         = $offenAnfang
            OffenAnfangMinMin   = if ($null -ne $offenAnfangMindestens) { [math]::Round($offenAnfangMindestens, 1) } else { $null }
            OffenEnde           = $offenEnde
            OffenEndeMinMin     = if ($null -ne $offenEndeMindestens) { [math]::Round($offenEndeMindestens, 1) } else { $null }
            Ereignisse          = $sortiert.Count
        }
    }
}

if (-not $zeilen) {
    Write-Warning @"
Keine Registrierungszeilen gefunden.

Das heisst eines von dreien, und sie sind zu unterscheiden:
  - die Protokolle sind aelter als die Registrierungszeile selbst (EventId
    2021, seit den ersten Fassungen von nipp vorhanden — eher unwahrscheinlich),
  - im Zeitraum war kein Konto eingerichtet bzw. nie angemeldet,
  - oder der Meldungstext in TelephonyLog.RegistrationChanged hat sich
    geaendert und dieses Skript sucht nach dem falschen Muster.
"@
    return
}

if ($AlsObjekte) {
    return $zeilen
}

$zeilen | Format-Table -AutoSize -Property Datum, Konto,
    @{ Name = 'Luecken'; Expression = { $_.Luecken } },
    @{ Name = 'DauerMin'; Expression = { $_.DauerMin }; FormatString = '0.0' },
    @{ Name = 'LaengsteMin'; Expression = { $_.LaengsteMin }; FormatString = '0.0' },
    @{ Name = 'OffenAnfang'; Expression = {
        if (-not $_.OffenAnfang) { '' }
        elseif ($null -ne $_.OffenAnfangMinMin) { "ja, >= $($_.OffenAnfangMinMin) min" }
        else { 'ja (siehe OffenEnde)' }
    } },
    @{ Name = 'OffenEnde'; Expression = {
        if (-not $_.OffenEnde) { '' }
        else { "ja, >= $($_.OffenEndeMinMin) min" }
    } }

$beobachtet = $zeilen | Where-Object { $_.Luecken -gt 0 }

Write-Host ""
Write-Host "Tage x Konten ausgewertet: $($zeilen.Count)" -ForegroundColor Cyan

if ($beobachtet) {
    $summeMin = [math]::Round(($beobachtet | Measure-Object -Property DauerMin -Sum).Sum, 1)
    $anzahlLuecken = ($beobachtet | Measure-Object -Property Luecken -Sum).Sum
    $schlimmster = $zeilen | Sort-Object LaengsteMin -Descending | Select-Object -First 1
    Write-Host "Vollstaendig beobachtete Luecken: $anzahlLuecken, zusammen $summeMin Minuten" -ForegroundColor Cyan
    Write-Host "Laengste einzelne Luecke: $($schlimmster.LaengsteMin) min ($($schlimmster.Datum), $($schlimmster.Konto))" -ForegroundColor Cyan
}
else {
    Write-Host "Keine vollstaendig beobachtete Luecke (Beginn UND Ende im selben Protokoll) im Zeitraum." -ForegroundColor Cyan
}

$anfangOffen = ($zeilen | Where-Object { $_.OffenAnfang }).Count
$endeOffen = ($zeilen | Where-Object { $_.OffenEnde }).Count
if ($anfangOffen -gt 0 -or $endeOffen -gt 0) {
    Write-Host "Zusaetzlich unvollstaendig (Dateigrenze, keine erfundene Zahl): " -NoNewline -ForegroundColor Yellow
    Write-Host "$anfangOffen x offen am Dateianfang, $endeOffen x offen am Dateiende." -ForegroundColor Yellow
}
