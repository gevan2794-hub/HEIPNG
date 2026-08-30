<#
.SYNOPSIS
    One-command setup on the machine that actually has the game: installs BepInEx,
    builds the bridge, and copies everything into place.

.DESCRIPTION
    Run this on the Windows PC with CarX installed. It works out whether the game is
    IL2CPP or Mono, fetches the matching BepInEx release, installs it, builds the mod and
    the SimHub plugin, and puts them where they belong.

    It stops at each point where it needs you: launching the game once so BepInEx
    generates its folders, and closing SimHub before its DLLs can be replaced.

.EXAMPLE
    .\setup.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\CarX Drift Racing Online 2"

.EXAMPLE
    .\setup.ps1 -GamePath "..." -SkipBepInEx      # BepInEx is already installed
#>
[CmdletBinding()]
param(
    # Game folder. Omit it and Steam's own library index is searched for a CarX install.
    [string] $GamePath,

    [string] $SimHubPath = 'C:\Program Files (x86)\SimHub',
    [switch] $SkipBepInEx,
    [switch] $SkipSimHub
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Write-Step  ($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Ok    ($m) { Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2 ($m) { Write-Host "    $m" -ForegroundColor Yellow }
function Write-Ask   ($m) { Write-Host "    $m" -ForegroundColor Magenta }


# --- 0. Find the game --------------------------------------------------------------
#
# Steam records every library folder in steamapps\libraryfolders.vdf, including ones on
# other drives, so this finds the game wherever it was installed rather than assuming C:.

function Find-CarXInstall {
    $steamRoots = @(
        "${env:ProgramFiles(x86)}\Steam",
        "$env:ProgramFiles\Steam",
        "$env:SystemDrive\Steam"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    try {
        $registered = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
        if ($registered) { $steamRoots = @($registered.Replace('/', '\')) + $steamRoots }
    }
    catch {
        # No Steam registry key: fall back to the well-known locations above.
    }

    $libraries = New-Object System.Collections.Generic.List[string]

    foreach ($steam in ($steamRoots | Select-Object -Unique)) {
        $libraries.Add((Join-Path $steam 'steamapps\common'))

        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf)) { continue }

        # Entries look like:   "path"   "D:\\SteamLibrary"
        $text = Get-Content -LiteralPath $vdf -Raw
        foreach ($match in [regex]::Matches($text, '"path"\s*"([^"]+)"')) {
            $libraries.Add((Join-Path $match.Groups[1].Value.Replace('\\', '\') 'steamapps\common'))
        }
    }

    $found = foreach ($library in ($libraries | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $library)) { continue }
        Get-ChildItem -LiteralPath $library -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)carx' }
    }

    # Prefer the newest title when several CarX games are installed.
    $found | Sort-Object Name -Descending | Select-Object -ExpandProperty FullName -Unique
}

if (-not $GamePath) {
    Write-Step "looking for a CarX install"
    $candidates = @(Find-CarXInstall)

    if ($candidates.Count -eq 0) {
        Write-Warn2 "No CarX install found in your Steam libraries."
        Write-Warn2 "Pass the folder containing the game's .exe yourself:"
        Write-Warn2 "  .\setup.ps1 -GamePath 'D:\Games\CarX Drift Racing Online 2'"
        throw "could not locate the game"
    }

    if ($candidates.Count -eq 1) {
        $GamePath = $candidates[0]
        Write-Ok "found $GamePath"
    }
    else {
        Write-Host ""
        Write-Ask "Several CarX games are installed. Which one?"
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            Write-Host ("      [{0}] {1}" -f ($i + 1), $candidates[$i])
        }
        Write-Host ""
        $choice = Read-Host "    number"
        $index = 0
        if (-not [int]::TryParse($choice, [ref] $index) -or $index -lt 1 -or $index -gt $candidates.Count) {
            throw "'$choice' is not one of the listed numbers"
        }
        $GamePath = $candidates[$index - 1]
        Write-Ok "using $GamePath"
    }
}

if (-not (Test-Path -LiteralPath $GamePath)) { throw "game path not found: $GamePath" }

# --- 1. Which flavour? -------------------------------------------------------------
#
# IL2CPP builds compile the game's C# to native code and ship GameAssembly.dll; Mono
# builds keep it managed in Assembly-CSharp.dll. They need different BepInEx major
# versions, and installing the wrong one produces a plugin that silently never loads.

Write-Step "checking the game"

$isIl2Cpp = Test-Path -LiteralPath (Join-Path $GamePath 'GameAssembly.dll')
$managedDll = Get-ChildItem -LiteralPath $GamePath -Filter '*_Data' -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName 'Managed\Assembly-CSharp.dll' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if ($isIl2Cpp) {
    $flavor = 'IL2CPP'
    Write-Ok "IL2CPP (found GameAssembly.dll) -> BepInEx 6"
}
elseif ($managedDll) {
    $flavor = 'Mono'
    Write-Ok "Mono (found $(Split-Path $managedDll -Leaf)) -> BepInEx 5"
    Write-Ok "tip: this build is decompilable -- opening that DLL in dnSpy is the fast way to find field names"
}
else {
    throw "found neither GameAssembly.dll nor Assembly-CSharp.dll under $GamePath -- is this the right folder?"
}

# --- 2. BepInEx --------------------------------------------------------------------

if (-not $SkipBepInEx) {
    if (Test-Path -LiteralPath (Join-Path $GamePath 'BepInEx')) {
        Write-Step "BepInEx already present, leaving it alone (pass -SkipBepInEx to silence this)"
    }
    else {
        Write-Step "fetching BepInEx for $flavor"

        # Asset names have changed across releases, so match loosely on the shape rather
        # than pinning an exact filename.
        $pattern = if ($flavor -eq 'IL2CPP') { '^BepInEx-Unity\.IL2CPP-win-x64-6\..*\.zip$' }
                   else                      { '^BepInEx_(win_)?x64_5\..*\.zip$' }

        try {
            $releases = Invoke-RestMethod -Uri 'https://api.github.com/repos/BepInEx/BepInEx/releases' `
                -Headers @{ 'User-Agent' = 'carx-telemetry-setup' } -TimeoutSec 60
        }
        catch {
            throw "could not reach the GitHub releases API: $($_.Exception.Message)"
        }

        $asset = $releases |
            Sort-Object { [datetime] $_.published_at } -Descending |
            ForEach-Object { $_.assets } |
            Where-Object { $_.name -match $pattern } |
            Select-Object -First 1

        if (-not $asset) {
            throw "no BepInEx asset matched '$pattern'. Download it manually from https://github.com/BepInEx/BepInEx/releases and extract into $GamePath, then re-run with -SkipBepInEx."
        }

        $zip = Join-Path ([System.IO.Path]::GetTempPath()) $asset.name
        Write-Ok "downloading $($asset.name)"
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -TimeoutSec 600

        Write-Ok "extracting into the game folder"
        Expand-Archive -LiteralPath $zip -DestinationPath $GamePath -Force
        Remove-Item -LiteralPath $zip -Force

        Write-Ok "BepInEx installed"
    }

    # BepInEx only creates config/ and plugins/ on its first run, and the IL2CPP build
    # also generates interop assemblies then, which takes a while.
    if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'BepInEx\plugins'))) {
        Write-Host ""
        Write-Ask "ACTION NEEDED: launch the game once, wait for the main menu, then quit."
        Write-Ask "This lets BepInEx create its folders. On IL2CPP the first launch is slow"
        Write-Ask "(it generates interop assemblies) -- give it a few minutes."
        Write-Host ""
        Read-Host "    press Enter once you have done that"

        if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'BepInEx\plugins'))) {
            throw "BepInEx\plugins still does not exist, so BepInEx is not loading. Check BepInEx\LogOutput.log, and confirm you installed the x64 build matching the game's flavour ($flavor)."
        }
        Write-Ok "BepInEx is loading"
    }
}

# --- 3. Build ----------------------------------------------------------------------

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is not installed (no 'dotnet' on PATH). Get it from https://dotnet.microsoft.com/download and re-run."
}

$buildArgs = @{ GamePath = $GamePath; SimHubPath = $SimHubPath; Flavor = $flavor }
if ($SkipSimHub -or -not (Test-Path -LiteralPath (Join-Path $SimHubPath 'SimHub.Plugins.dll'))) {
    if (-not $SkipSimHub) {
        Write-Warn2 "SimHub not found at $SimHubPath -- building the game mod only."
        Write-Warn2 "You can still receive telemetry via SimHub's UDPConnector plugin."
    }
    $buildArgs.ModOnly = $true
}

Write-Step "building"
& (Join-Path $root 'build.ps1') @buildArgs

# --- 4. Install --------------------------------------------------------------------

$installArgs = @{ GamePath = $GamePath; SimHubPath = $SimHubPath }
if ($buildArgs.ModOnly) { $installArgs.ModOnly = $true }

& (Join-Path $root 'install.ps1') @installArgs

# --- 5. What is left for a human ---------------------------------------------------

Write-Step "setup done. Two things left, and both need you in the driver's seat:"
Write-Host ""
Write-Host "  1. Launch the game and get into a car. Then check:"
Write-Host "       Get-Content '$GamePath\BepInEx\LogOutput.log' -Tail 40 | Select-String 'CarX Telemetry'"
Write-Host "     You want a line saying 'located vehicle'. That means speed, slip angle,"
Write-Host "     accelerations and yaw rate are already streaming."
Write-Host ""
Write-Host "     Watch them live with:  python tools\monitor.py --port 20777"
Write-Host ""
Write-Host "  2. For RPM and gear, hold a known state (idle, then ~3000rpm in 2nd) and"
Write-Host "     press F9. The log fills with your car's live field values. Match them to"
Write-Host "     the HUD and write them into:"
Write-Host "       $GamePath\BepInEx\config\CarXTelemetry\profiles\"
Write-Host "     Then restart the game. See docs\FINDING-FIELDS.md."
Write-Host ""
Write-Warn2 "Use this offline -- practice, solo, private lobbies. Not live multiplayer."
