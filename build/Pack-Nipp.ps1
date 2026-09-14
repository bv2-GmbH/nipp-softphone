<#
.SYNOPSIS
    Baut ein installierbares MSIX-Paket von nipp (AP9.1, AP9.2, AP9.4).

.DESCRIPTION
    Erzeugt ein signiertes MSIX unter dist\. Ohne -CertificateThumbprint wird
    selbstsigniert — das reicht zum Entwickeln und für einen Testrechner mit
    importiertem Zertifikat, nicht für die Auslieferung.

    Danach wird geprüft, dass die native Linphone-Kette vollständig im Paket
    liegt. §14.2 nennt genau diese Bruchstelle: eine fehlende DLL fällt erst
    beim ersten Start auf dem Zielrechner auf, mit einer Fehlermeldung, die
    nicht sagt, welche fehlt.

.PARAMETER Version
    Vierteilige Version für das Manifest. Ohne Angabe bleibt die aus dem
    Manifest stehen.

.PARAMETER CertificateThumbprint
    Fingerabdruck eines Zertifikats aus Cert:\CurrentUser\My. Ohne Angabe
    wird ein selbstsigniertes angelegt oder wiederverwendet.

.PARAMETER SkipBuild
    Nur paketieren, nicht neu bauen. Für einen zweiten Durchlauf.

.EXAMPLE
    .\build\Pack-Nipp.ps1 -Version 1.0.0.0

.EXAMPLE
    .\build\Pack-Nipp.ps1 -Version 1.0.0.0 -CertificateThumbprint A1B2C3...
#>

[CmdletBinding()]
param(
    [string]$Version,
    [string]$CertificateThumbprint,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\Nipp.App\Nipp.App.csproj'
$manifestPath = Join-Path $repoRoot 'src\Nipp.App\Package.appxmanifest'
$distDirectory = Join-Path $repoRoot 'dist'

# ADR-001: das x64-SDK ist auf der ARM64-Maschine nicht das im PATH.
$dotnet = Join-Path $env:ProgramFiles 'dotnet\x64\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

function Write-Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Problem($text) { Write-Host "  $text" -ForegroundColor Red }
function Write-Note($text) { Write-Host "  $text" -ForegroundColor DarkGray }

# ── Version ins Manifest ─────────────────────────────────────────────────────

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "Die Version muss vierteilig sein (z. B. 1.0.0.0), war aber '$Version'."
    }

    Write-Step "Version $Version ins Manifest schreiben"

    # Als XML, nicht per Textersetzung: eine Regex über ein Manifest trifft
    # früher oder später das falsche Attribut.
    $manifest = [xml](Get-Content $manifestPath -Raw)
    $manifest.Package.Identity.Version = $Version
    $manifest.Save($manifestPath)

    Write-Note "Package.appxmanifest aktualisiert"
}
else {
    $manifest = [xml](Get-Content $manifestPath -Raw)
    $Version = $manifest.Package.Identity.Version
    Write-Note "Version aus dem Manifest: $Version"
}

# ── Bauen ────────────────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Step 'Release bauen und paketieren'

    # §14.4: RuntimeIdentifier fest auf win-x64, sonst leitet das Template ihn
    # aus der Prozessarchitektur der Baumaschine ab.
    & $dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -p:Platform=x64 `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageDir="$distDirectory\" `
        -p:AppxBundle=Never `
        -p:UapAppxPackageBuildMode=SideloadOnly `
        -p:AppxPackageSigningEnabled=false `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw "Der Build ist fehlgeschlagen (Exit-Code $LASTEXITCODE)." }
}

# ── Das erzeugte Paket finden ────────────────────────────────────────────────

Write-Step 'Paket suchen'

$package = Get-ChildItem -Path $distDirectory -Filter '*.msix' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "Unter $distDirectory liegt kein .msix. Der Build hat keines erzeugt — die Ausgabe oben sagt warum."
}

Write-Note $package.FullName
Write-Note ('{0:N1} MB' -f ($package.Length / 1MB))

# ── Vollständigkeit der nativen Kette prüfen (§14.2, AP9.1) ─────────────────

Write-Step 'Native Bibliotheken im Paket prüfen'

# Die Kette, an der es hängt. liblinphone braucht sie alle; fehlt eine, startet
# die App und wirft beim ersten Core-Zugriff eine DllNotFoundException, die
# nicht sagt, welche Abhängigkeit fehlt.
$requiredNative = @(
    'liblinphone.dll',
    'bctoolbox.dll',
    'belle-sip.dll',
    'belr.dll',
    'ortp.dll',
    'mediastreamer2.dll'
)

# belr lädt acht Grammatiken zur Laufzeit nach. Fehlen sie, stirbt der Start
# mit „bctbx-fatal: Unable to load VCARD grammar" — eine Meldung, die nicht
# erkennen lässt, dass eine Datei fehlt und nicht der Code kaputt ist. Genau
# das ist in AP2.4 passiert; §14.2 kennt nur die DLLs und ist unvollständig.
$requiredResources = @('share/belr/grammars')

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

try {
    $entries = $archive.Entries | ForEach-Object { $_.FullName }

    $missing = @()

    foreach ($dll in $requiredNative) {
        if (-not ($entries | Where-Object { $_ -like "*$dll" })) { $missing += $dll }
    }

    foreach ($resource in $requiredResources) {
        if (-not ($entries | Where-Object { $_ -replace '\\', '/' -like "*$resource*" })) {
            $missing += $resource
        }
    }

    $nativeCount = ($entries | Where-Object { $_ -like '*.dll' }).Count
    Write-Note "$nativeCount DLLs im Paket, $($entries.Count) Einträge insgesamt"

    if ($missing.Count -gt 0) {
        Write-Problem 'Im Paket fehlen:'
        $missing | ForEach-Object { Write-Problem "  - $_" }
        Write-Problem ''
        Write-Problem 'Das Paket würde auf dem Zielrechner mit einer Meldung scheitern, die'
        Write-Problem 'nicht sagt, was fehlt. Siehe build\Linphone.Sdk.targets.'

        throw 'Die native Kette ist unvollständig.'
    }

    Write-Host '  Die native Kette ist vollständig.' -ForegroundColor Green
}
finally {
    $archive.Dispose()
}

# ── Signieren (AP9.2) ────────────────────────────────────────────────────────

Write-Step 'Signieren'

$signTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signTool) {
    Write-Problem 'signtool.exe nicht gefunden. Das Paket bleibt unsigniert und lässt sich nicht installieren.'
    Write-Problem 'Abhilfe: Windows SDK installieren (Komponente „Windows SDK Signing Tools").'
    exit 1
}

if (-not $CertificateThumbprint) {
    $publisher = ([xml](Get-Content $manifestPath -Raw)).Package.Identity.Publisher

    # Der Antragsteller muss buchstabengleich zum Publisher im Manifest sein,
    # sonst lehnt Windows die Installation ab — mit einer Meldung, die den
    # Grund nicht nennt.
    $existing = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if ($existing) {
        Write-Note "Vorhandenes Entwicklungszertifikat: $($existing.Thumbprint)"
        $CertificateThumbprint = $existing.Thumbprint
    }
    else {
        Write-Note "Selbstsigniertes Zertifikat für $publisher anlegen"

        $created = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $publisher `
            -KeyUsage DigitalSignature `
            -FriendlyName 'nipp Entwicklungssignatur' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

        $CertificateThumbprint = $created.Thumbprint
        Write-Note "Fingerabdruck: $CertificateThumbprint"
    }

    Write-Host ''
    Write-Host '  Achtung: selbstsigniert. Zum Installieren muss das Zertifikat auf dem' -ForegroundColor Yellow
    Write-Host '  Zielrechner unter "Vertrauenswürdige Personen" liegen. Für die' -ForegroundColor Yellow
    Write-Host '  Auslieferung braucht es ein echtes Code-Signing-Zertifikat (§16.2).' -ForegroundColor Yellow
}

& $signTool.FullName sign /fd SHA256 /sha1 $CertificateThumbprint /t http://timestamp.digicert.com $package.FullName

if ($LASTEXITCODE -ne 0) {
    throw "Das Signieren ist fehlgeschlagen (Exit-Code $LASTEXITCODE)."
}

Write-Host '  Signiert.' -ForegroundColor Green

# ── Ergebnis ─────────────────────────────────────────────────────────────────

Write-Step 'Fertig'

Write-Host "  Paket:   $($package.FullName)"
Write-Host "  Version: $Version"
Write-Host ''
Write-Host '  Installieren auf einem Testrechner:'
Write-Host "    Add-AppxPackage -Path `"$($package.Name)`""
Write-Host ''
Write-Host '  Unbeaufsichtigt verteilen: siehe docs\packaging.md'
