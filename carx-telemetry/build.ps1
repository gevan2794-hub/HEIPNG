<#
.SYNOPSIS
    Builds the CarX Telemetry Bridge and stages the files you need to copy.

.DESCRIPTION
    Works out on its own whether the game is IL2CPP or Mono (which decides the BepInEx
    version and the mod build flavour), builds everything, and drops the results in
    dist\mod and dist\simhub ready for install.ps1.

.EXAMPLE
    .\build.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\CarX Drift Racing Online 2"

.EXAMPLE
    .\build.ps1 -Flavor IL2CPP        # skip detection, build for IL2CPP
#>
[CmdletBinding()]
param(
    # Game install folder, the one containing the .exe. Used only to detect the flavour.
    [string] $GamePath,

    # Force a flavour instead of detecting: Mono (BepInEx 5) or IL2CPP (BepInEx 6).
    [ValidateSet('Auto', 'Mono', 'IL2CPP')]
    [string] $Flavor = 'Auto',

    [string] $SimHubPath = 'C:\Program Files (x86)\SimHub',

    # Build only the game mod, or only the SimHub plugin.
    [switch] $ModOnly,
    [switch] $SimHubOnly
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

function Write-Step  ($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok    ($m) { Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2 ($m) { Write-Host "    $m" -ForegroundColor Yellow }

# --- Prerequisites -----------------------------------------------------------------

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is not installed (no 'dotnet' on PATH). Get it from https://dotnet.microsoft.com/download"
}

Write-Step "using $(dotnet --version) SDK"

# --- Detect the game flavour -------------------------------------------------------
#
# IL2CPP builds compile the game's C# to native code and ship GameAssembly.dll.
# Mono builds keep it as a managed Assembly-CSharp.dll. The two need different BepInEx
# major versions, so this is the first thing to get right.

function Get-GameFlavor([string] $path) {
    if (-not $path) { return $null }
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warn2 "game path not found: $path"
        return $null
    }

    if (Test-Path -LiteralPath (Join-Path $path 'GameAssembly.dll')) { return 'IL2CPP' }

    $managed = Get-ChildItem -LiteralPath $path -Filter '*_Data' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'Managed\Assembly-CSharp.dll' } |
        Where-Object { Test-Path -LiteralPath $_ }

    if ($managed) { return 'Mono' }

    Write-Warn2 "found neither GameAssembly.dll nor Assembly-CSharp.dll under $path"
    return $null
}

if ($Flavor -eq 'Auto') {
    $detected = Get-GameFlavor $GamePath
    if ($detected) {
        $Flavor = $detected
        Write-Step "detected $Flavor game -> you need BepInEx $(if ($Flavor -eq 'IL2CPP') { '6 (IL2CPP, x64)' } else { '5 (x64)' })"
    }
    else {
        $Flavor = 'Mono'
        Write-Warn2 "could not detect the game flavour; defaulting to Mono."
        Write-Warn2 "Pass -GamePath, or -Flavor IL2CPP if the game folder has a GameAssembly.dll."
    }
}
else {
    Write-Step "building for $Flavor (forced)"
}

# --- Build -------------------------------------------------------------------------

if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist -Force | Out-Null

if (-not $SimHubOnly) {
    Write-Step "building the game mod ($Flavor)"
    $modOut = Join-Path $dist 'mod'
    $modProject = Join-Path $root 'src\CarX.Telemetry.Mod'

    # BepInEx publishes to its own feed rather than nuget.org, so the mod restores with
    # an extra config. It is kept out of the default NuGet.config because NuGet contacts
    # every configured source on every restore, which would break the other projects on
    # a machine that cannot reach that host.
    dotnet restore $modProject -p:GameFlavor=$Flavor --configfile (Join-Path $root 'build\nuget.bepinex.config')
    if ($LASTEXITCODE -ne 0) {
        Write-Warn2 "restore failed. If nuget.bepinex.dev is unreachable, you can type-check"
        Write-Warn2 "with -p:UseStubs=true, but the result will not run. See build\stubs\README.md."
        throw "mod restore failed"
    }

    dotnet build $modProject -c Release -p:GameFlavor=$Flavor -o $modOut --no-restore
    if ($LASTEXITCODE -ne 0) { throw "mod build failed" }

    # BepInEx only needs the plugin and its own dependency; the rest is build noise.
    Get-ChildItem -LiteralPath $modOut -File |
        Where-Object { $_.Name -notin @('CarX.Telemetry.Mod.dll', 'CarX.Telemetry.Shared.dll') } |
        Remove-Item -Force
    Write-Ok "-> $modOut"
}

if (-not $ModOnly) {
    Write-Step "building the SimHub plugin"

    if (-not (Test-Path -LiteralPath (Join-Path $SimHubPath 'SimHub.Plugins.dll'))) {
        Write-Warn2 "SimHub.Plugins.dll not found under $SimHubPath"
        Write-Warn2 "Pass -SimHubPath, or use -ModOnly and drive SimHub with the UDPConnector plugin instead."
        throw "SimHub assemblies not found"
    }

    $simhubOut = Join-Path $dist 'simhub'
    dotnet build (Join-Path $root 'src\CarX.Telemetry.SimHub') `
        -c Release -p:SimHubPath=$SimHubPath -o $simhubOut
    if ($LASTEXITCODE -ne 0) { throw "SimHub plugin build failed" }

    Get-ChildItem -LiteralPath $simhubOut -File |
        Where-Object { $_.Name -notin @('CarX.Telemetry.SimHub.dll', 'CarX.Telemetry.Shared.dll') } |
        Remove-Item -Force
    Write-Ok "-> $simhubOut"
}

Write-Host ""
Write-Step "build complete. Next:"
Write-Host "    .\install.ps1 -GamePath `"$GamePath`" -SimHubPath `"$SimHubPath`""
