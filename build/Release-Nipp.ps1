<#
.SYNOPSIS
    Baut ein Velopack-Setup von nipp und laedt es optional zu GitHub (ADR-038).

.DESCRIPTION
    Der Auslieferungsweg seit dem 07.09.2026. Vier Schritte:

      1. publish, self-contained und unpackaged — die App bringt .NET und das
         Windows App SDK mit, der Zielrechner braucht keine Voraussetzungen.
      2. Pruefen, dass die native Linphone-Kette vollstaendig im Ergebnis
         liegt. Paragraph 14.2: eine fehlende DLL faellt erst beim ersten Start
         auf dem Zielrechner auf, mit einer Meldung, die nicht sagt, welche.
         Beim ersten Lauf am 07.09.2026 fehlte die GANZE Kette — das
         publish-Ziel kannte sie nicht (siehe build\Linphone.Sdk.targets).
      3. vpk pack: Setup.exe, Voll- und Delta-Paket, releases.<kanal>.json.
      4. Optional vpk upload github.

    MSIX bleibt daneben bestehen (Pack-Nipp.ps1) und wartet auf T110 und ein
    Zertifikat. Ausgeliefert wird, was hier herauskommt.

.PARAMETER Version
    Dreiteilig nach SemVer, z. B. 0.9.0. Fuer den Beta-Kanal mit Suffix:
    0.9.0-beta.3. MSIX-Vierteiligkeit gilt hier NICHT.

.PARAMETER Channel
    stable oder beta. Bestimmt den Feed und — bei beta — das
    Vorabversions-Kennzeichen auf GitHub. Die Namen stehen gleichlautend in
    UpdateChannels.NameOf; wer sie hier aendert, aendert sie dort mit.

.PARAMETER Publish
    Nach dem Bauen zu GitHub laden. Braucht ein Token in GITHUB_TOKEN oder
    -Token.

.PARAMETER Token
    GitHub-Token mit Schreibrecht auf Releases. Ohne Angabe wird
    $env:GITHUB_TOKEN benutzt.

.PARAMETER CertificateThumbprint
    Zertifikat aus Cert:\CurrentUser\My zum Signieren (AP9.2). Ohne Angabe
    bleibt das Setup UNSIGNIERT — dann zeigt Windows beim ersten Start
    "Unbekannter Herausgeber". Fuer den internen Gebrauch tragbar, fuer eine
    Abgabe ausser Haus nicht.

.EXAMPLE
    .\build\Release-Nipp.ps1 -Version 0.9.0 -Channel stable

.EXAMPLE
    .\build\Release-Nipp.ps1 -Version 0.9.0-beta.1 -Channel beta -Publish
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('stable', 'beta')]
    [string]$Channel = 'stable',

    [switch]$Publish,

    [string]$Token,

    [string]$CertificateThumbprint,

    [switch]$SkipBuild,

    # Wohin das Paket hochgeladen wird. Standard ist das neue, oeffentliche
    # Repo; fuer die Bruecke (0.9.5) wird hier einmalig das alte angegeben.
    [string]$UploadRepo = 'https://github.com/bv2-GmbH/nipp-softphone'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\Nipp.App\Nipp.App.csproj'
$publishDirectory = Join-Path $repoRoot 'dist\publish'
$releaseDirectory = Join-Path $repoRoot 'dist\releases'

# ADR-001: das x64-SDK ist auf der ARM64-Maschine nicht das im PATH.
$dotnet = Join-Path $env:ProgramFiles 'dotnet\x64\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# Der Kanalname, den die App sucht (UpdateChannels.NameOf). "win-" davor, weil
# Velopack Kanaele je Betriebssystem und Architektur trennt.
$velopackChannel = "win-$Channel"

function Write-Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Problem($text) { Write-Host "  $text" -ForegroundColor Red }
function Write-Note($text) { Write-Host "  $text" -ForegroundColor DarkGray }

# ── Version pruefen ──────────────────────────────────────────────────────────

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Die Version muss dreiteilig sein (z. B. 0.9.0 oder 0.9.0-beta.1), war aber '$Version'."
}

if ($Channel -eq 'beta' -and $Version -notmatch '-') {
    Write-Host 'Hinweis: eine Beta-Fassung ohne Suffix (z. B. -beta.1) ist schwer von stable' -ForegroundColor Yellow
    Write-Host '         zu unterscheiden, sobald jemand zwei Versionen nebeneinander sieht.' -ForegroundColor Yellow
}

# ── Bauen ────────────────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Step "nipp $Version bauen (self-contained, unpackaged)"

    if (Test-Path $publishDirectory) {
        Remove-Item -Recurse -Force $publishDirectory
    }

    & $dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:Version=$($Version -replace '-.*$', '') `
        -p:InformationalVersion=$Version `
        -o $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw 'Der Build ist gescheitert.'
    }
}

if (-not (Test-Path (Join-Path $publishDirectory 'Nipp.App.exe'))) {
    throw "Unter '$publishDirectory' liegt keine Nipp.App.exe."
}

# ── Vollstaendigkeit der nativen Kette pruefen (Paragraph 14.2) ──────────────

Write-Step 'Native Bibliotheken pruefen'

# Dieselbe Liste wie in Pack-Nipp.ps1 — sie gilt fuer jedes Paketformat.
# liblinphone braucht alle sechs; fehlt eine, startet die App und wirft beim
# ersten Core-Zugriff eine DllNotFoundException, die nicht sagt, welche.
$requiredNative = @(
    'liblinphone.dll',
    'bctoolbox.dll',
    'belle-sip.dll',
    'belr.dll',
    'ortp.dll',
    'mediastreamer2.dll'
)

$missing = @()

foreach ($dll in $requiredNative) {
    if (-not (Test-Path (Join-Path $publishDirectory $dll))) { $missing += $dll }
}

# belr laedt acht Grammatiken zur Laufzeit nach. Fehlen sie, stirbt der Start
# mit "bctbx-fatal: Unable to load VCARD grammar" — eine Meldung, die nicht
# erkennen laesst, dass eine DATEI fehlt.
$grammarDirectory = Join-Path $publishDirectory 'share\belr\grammars'
$grammarCount = if (Test-Path $grammarDirectory) {
    (Get-ChildItem $grammarDirectory -Filter '*.belr').Count
} else { 0 }

if ($grammarCount -lt 8) {
    $missing += "share\belr\grammars ($grammarCount von 8)"
}

# Das WASAPI-Plugin: ohne es gibt es keine Audioausgabe, und zwar lautlos —
# die Filterkette endet dann in MSVoidSink statt MSWASAPIWrite.
if (-not (Test-Path (Join-Path $publishDirectory 'lib\mediastreamer\plugins\libmswasapi.dll'))) {
    $missing += 'lib\mediastreamer\plugins\libmswasapi.dll'
}

if ($missing.Count -gt 0) {
    Write-Problem 'Im Ergebnis fehlen:'
    $missing | ForEach-Object { Write-Problem "  - $_" }
    Write-Problem ''
    Write-Problem 'Das Setup wuerde auf dem Zielrechner mit einer Meldung scheitern, die'
    Write-Problem 'nicht sagt, was fehlt. Siehe build\Linphone.Sdk.targets, Target'
    Write-Problem 'AddLinphoneToPublish.'
    throw 'Die native Kette ist unvollstaendig.'
}

$size = (Get-ChildItem $publishDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Note ('{0} DLLs, {1} Grammatiken, {2:N0} MB' -f `
    (Get-ChildItem $publishDirectory -Filter '*.dll').Count, $grammarCount, ($size / 1MB))
Write-Host '  Die native Kette ist vollstaendig.' -ForegroundColor Green

# ── Paketieren ───────────────────────────────────────────────────────────────

Write-Step "Setup bauen ($velopackChannel)"

$vpkArgs = @(
    'vpk', 'pack',
    '--packId', 'nipp',
    '--packVersion', $Version,
    '--packDir', $publishDirectory,
    '--mainExe', 'Nipp.App.exe',
    '--packTitle', 'nipp',
    '--packAuthors', 'bv2 GmbH',
    '--channel', $velopackChannel,
    '--outputDir', $releaseDirectory,
    '--icon', (Join-Path $repoRoot 'src\Nipp.App\Assets\AppIcon.ico'),
    '--shortcuts', 'Desktop,StartMenuRoot'
)

if ($CertificateThumbprint) {
    # signtool-Parameter, wie sie vpk durchreicht.
    $vpkArgs += @('--signParams', "/sha1 $CertificateThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256")
} else {
    Write-Host '  Ohne Zertifikat: das Setup bleibt UNSIGNIERT (AP9.2 offen).' -ForegroundColor Yellow
    Write-Host '  Windows zeigt beim ersten Start "Unbekannter Herausgeber" — siehe T130.' -ForegroundColor Yellow
}

& $dotnet @vpkArgs

if ($LASTEXITCODE -ne 0) {
    throw 'vpk pack ist gescheitert.'
}

$setup = Get-ChildItem $releaseDirectory -Filter '*Setup.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($setup) {
    Write-Note ('{0} ({1:N1} MB)' -f $setup.Name, ($setup.Length / 1MB))
}

# ── Veroeffentlichen ─────────────────────────────────────────────────────────

if (-not $Publish) {
    Write-Step 'Fertig'
    Write-Note "Ergebnis: $releaseDirectory"
    Write-Note 'Zum Veroeffentlichen dasselbe Kommando mit -Publish.'
    return
}

$effectiveToken = if ($Token) { $Token } else { $env:GITHUB_TOKEN }

if (-not $effectiveToken) {
    throw 'Kein Token. Entweder -Token angeben oder GITHUB_TOKEN setzen. Das Repo ist privat (docs/licensing.md), ohne Token geht kein Upload und kein Abruf.'
}

Write-Step "Nach GitHub laden ($velopackChannel)"

# <b>Wohin hochgeladen wird, ist NICHT dasselbe wie das, wonach die App
# sucht.</b> Die App sucht dort, wo VelopackUpdateGateway.RepositoryUrl
# hinzeigt — seit dem 14.09.2026 im neuen, oeffentlichen Repo.
#
# Fuer die BRUECKE (Fassung 0.9.5) muessen die beiden auseinanderfallen: das
# Paket zeigt schon aufs neue Repo, hochgeladen wird es aber noch ins ALTE,
# weil die installierten Arbeitsplaetze nur dort suchen. Danach steht -UploadRepo
# wieder auf dem Standard.
$uploadArgs = @(
    'vpk', 'upload', 'github',
    '--repoUrl', $UploadRepo,
    '--token', $effectiveToken,
    '--channel', $velopackChannel,
    '--outputDir', $releaseDirectory,
    '--tag', "v$Version",
    '--releaseName', "nipp $Version",
    '--publish'
)

if ($Channel -eq 'beta') {
    # Auf GitHub als Vorabversion kennzeichnen. Die App sucht im Beta-Kanal
    # ausdruecklich danach (GithubSource mit prerelease: true); ohne das Flag
    # findet sie das Release nicht.
    $uploadArgs += '--pre'
}

& $dotnet @uploadArgs

if ($LASTEXITCODE -ne 0) {
    throw 'vpk upload ist gescheitert.'
}

Write-Step 'Fertig'
Write-Note "Release v$Version im Kanal $velopackChannel veroeffentlicht."
Write-Note 'Der Tag gehoert zum Setup: er ist die Zuordnung "dieser Installer, dieser Quellstand"'
Write-Note 'und laesst sich nachtraeglich nicht herstellen (docs/plans/RELEASE-PLAN.md R10).'
