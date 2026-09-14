<#
.SYNOPSIS
  Baut nipp mit dem richtigen dotnet-Host.

.DESCRIPTION
  nipp ist x64 only (NIPP-BUILD.md Paragraph 4), weil das linphone-sdk fuer
  Windows nur win64-Binaries liefert. Die Entwicklungsmaschine ist ARM64
  (ADR-001): dort liegt das x64-SDK unter "C:\Program Files\dotnet\x64" und
  ist vom arm64-Host im PATH nicht sichtbar. Ein blankes "dotnet build"
  scheitert deshalb mit "No .NET SDKs were found" oder baut - schlimmer -
  gegen die falsche Architektur.

  Dieses Skript loest den x64-Host auf und reicht alle Argumente weiter.
  Auf einer echten x64-Maschine findet es den normalen Host und funktioniert
  unveraendert.

.EXAMPLE
  .\build.ps1 build -c Debug
  .\build.ps1 test
  .\build.ps1 format

.EXAMPLE
  # Argumente, die mit einem Bindestrich beginnen und wie ein Parameter dieses
  # Skripts aussehen (-p:, -t:), brauchen den Stopp-Parser --% :
  .\build.ps1 --% build src\Nipp.App -c Debug -p:WindowsPackageType=None -t:Rebuild

.NOTES
  Ohne --% bindet PowerShell "-p:..." an einen eigenen Parameter und bricht ab:
  "parameter name 'p' is ambiguous. Possible matches include: -ProgressAction
  -PipelineVariable". ValueFromRemainingArguments faengt das nicht ab, weil die
  Bindung vorher geschieht. Der in README und CLAUDE.md genannte
  unpackaged-Befehl war deshalb nicht ausfuehrbar.
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @('build', '-c', 'Debug')
)

$ErrorActionPreference = 'Stop'

function Resolve-DotnetX64 {
    # 1. Bevorzugt: der als x64 registrierte Host (ARM64-Maschine mit x64-SDK)
    $sideBySide = Join-Path $env:ProgramFiles 'dotnet\x64\dotnet.exe'
    if (Test-Path $sideBySide) {
        return $sideBySide
    }

    # 2. Der Standardhost, falls er selbst x64 ist (echte x64-Maschine)
    $default = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $default) {
        $arch = (& $default --info 2>$null | Select-String -Pattern '^\s*Architecture:\s*(\S+)').Matches.Groups[1].Value
        if ($arch -eq 'x64') {
            return $default
        }
        throw "Der dotnet-Host unter '$default' ist '$arch', nicht x64, und ein x64-SDK unter '$sideBySide' fehlt. nipp ist x64 only (Paragraph 4). Installation: winget install --id Microsoft.DotNet.SDK.8 --architecture x64"
    }

    throw 'Kein dotnet-Host gefunden. Installation: winget install --id Microsoft.DotNet.SDK.8 --architecture x64'
}

# Warnen, wenn unpackaged ohne Rebuild gebaut wird — das erzeugt zuverlaessig
# eine App, die beim Start wortlos stirbt (siehe docs/packaging.md).
$argText = $Arguments -join ' '
if ($argText -match 'WindowsPackageType=None' -and $argText -notmatch '-t:Rebuild') {
    Write-Host 'Hinweis: unpackaged ohne -t:Rebuild. Die App stirbt dann beim Start mit' -ForegroundColor Yellow
    Write-Host '         REGDB_E_CLASSNOTREG, bevor eigener Code laeuft. Siehe docs/packaging.md.' -ForegroundColor Yellow
    Write-Host ''
}

$dotnet = Resolve-DotnetX64
Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray
Write-Host "Argumente: $($Arguments -join ' ')" -ForegroundColor DarkGray
Write-Host ''

& $dotnet @Arguments
exit $LASTEXITCODE
