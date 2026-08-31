<#
.SYNOPSIS
    Copies the built files into the game and into SimHub, checking the prerequisites first.

.EXAMPLE
    .\install.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\CarX Drift Racing Online 2"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GamePath,
    [string] $SimHubPath = 'C:\Program Files (x86)\SimHub',
    [switch] $ModOnly,
    [switch] $SimHubOnly
)

$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'

function Write-Step  ($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok    ($m) { Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2 ($m) { Write-Host "    $m" -ForegroundColor Yellow }

if (-not (Test-Path -LiteralPath $dist)) { throw "no dist folder -- run .\build.ps1 first" }

# --- Game side ---------------------------------------------------------------------

if (-not $SimHubOnly) {
    $modSource = Join-Path $dist 'mod'
    if (-not (Test-Path -LiteralPath $modSource)) { throw "dist\mod is missing -- run .\build.ps1 without -SimHubOnly" }

    if (-not (Test-Path -LiteralPath $GamePath)) { throw "game path not found: $GamePath" }

    $pluginsDir = Join-Path $GamePath 'BepInEx\plugins'
    if (-not (Test-Path -LiteralPath $pluginsDir)) {
        Write-Warn2 "BepInEx\plugins does not exist under the game."
        Write-Warn2 "Install BepInEx into the game folder and launch the game once so it creates its"
        Write-Warn2 "directories, then run this again. Use BepInEx 6 (IL2CPP x64) if the game folder"
        Write-Warn2 "has a GameAssembly.dll, otherwise BepInEx 5 (x64)."
        throw "BepInEx is not installed yet"
    }

    $target = Join-Path $pluginsDir 'CarXTelemetry'
    New-Item -ItemType Directory -Path $target -Force | Out-Null

    Write-Step "installing the mod"
    Copy-Item -Path (Join-Path $modSource '*.dll') -Destination $target -Force
    Write-Ok "-> $target"

    $configDir = Join-Path $GamePath 'BepInEx\config\CarXTelemetry\profiles'
    if (Test-Path -LiteralPath $configDir) {
        Write-Ok "profiles already seeded at $configDir (they are yours now; the mod will not overwrite them)"
    }
    else {
        Write-Ok "profiles will be seeded to BepInEx\config\CarXTelemetry\profiles on first launch"
    }
}

# --- SimHub side -------------------------------------------------------------------

if (-not $ModOnly) {
    $simhubSource = Join-Path $dist 'simhub'
    if (-not (Test-Path -LiteralPath $simhubSource)) {
        Write-Warn2 "dist\simhub is missing -- skipping the SimHub plugin."
        Write-Warn2 "That is fine if you are using the UDPConnector plugin instead."
    }
    elseif (-not (@('SimHubWPF.exe', 'SimHub.exe') | Where-Object { Test-Path -LiteralPath (Join-Path $SimHubPath $_) })) {
        Write-Warn2 "No SimHub executable found under $SimHubPath -- skipping. Pass -SimHubPath."
    }
    else {
        if (Get-Process -Name 'SimHubWPF', 'SimHub' -ErrorAction SilentlyContinue) {
            throw "SimHub is running -- close it first, it holds its plugin DLLs open"
        }

        Write-Step "installing the SimHub plugin"
        Copy-Item -Path (Join-Path $simhubSource '*.dll') -Destination $SimHubPath -Force
        Write-Ok "-> $SimHubPath"
        Write-Ok "start SimHub and enable 'CarX Telemetry' when it asks about the new plugin"
    }
}

Write-Host ""
Write-Step "done. Now:"
Write-Host "    1. Launch the game, get into a car."
Write-Host "    2. Check BepInEx\LogOutput.log for 'located vehicle' -- that means it found your car."
Write-Host "    3. python tools\monitor.py --port 20777   (optional, shows the live stream)"
Write-Host "    4. Press F9 in-car to dump field names, then fill in the profile."
Write-Host "       See docs\FINDING-FIELDS.md"
