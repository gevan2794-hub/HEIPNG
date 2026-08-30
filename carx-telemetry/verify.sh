#!/usr/bin/env bash
# Everything that can be checked without a game, a GPU, or Windows.
#
#   ./verify.sh
#
# Compiles all three assemblies against the reference stubs in build/stubs, runs the
# logic tests, and round-trips the wire protocol through the Python tools. This is what
# CI should run; it is not a substitute for a real build against BepInEx and SimHub.
set -euo pipefail

cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

step() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }

step "building the shared library"
dotnet build src/CarX.Telemetry.Shared -c Release -v quiet --nologo

step "building the mod (Mono flavour, against stubs)"
dotnet build src/CarX.Telemetry.Mod -c Release -p:GameFlavor=Mono -p:UseStubs=true -v quiet --nologo

step "building the mod (IL2CPP flavour, against stubs)"
dotnet build src/CarX.Telemetry.Mod -c Release -p:GameFlavor=IL2CPP -p:UseStubs=true -v quiet --nologo

step "building the SimHub plugin (against stubs)"
dotnet build src/CarX.Telemetry.SimHub -c Release -p:UseStubs=true -v quiet --nologo

step "logic tests"
dotnet run --project tests/CarX.Telemetry.Tests -c Release --no-build 2>/dev/null \
  || dotnet run --project tests/CarX.Telemetry.Tests -c Release

step "PowerShell syntax"
if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -Command '
    $bad = 0
    foreach ($f in @("setup.ps1","build.ps1","install.ps1")) {
      $errors = $null
      [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $f).Path, [ref]$null, [ref]$errors) | Out-Null
      if ($errors -and $errors.Count) {
        $bad++
        Write-Host "   FAIL $f"
        $errors | ForEach-Object { Write-Host ("        line {0}: {1}" -f $_.Extent.StartLineNumber, $_.Message) }
      } else {
        Write-Host "   ok   $f parses"
      }
    }
    exit $bad'
else
  echo "   skip (pwsh not installed: dotnet tool install --global PowerShell --version 7.4.6)"
fi

step "wire protocol round trip"
python3 - <<'PY'
import json, socket, subprocess, sys, time

PORT = 20791
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
sock.bind(("127.0.0.1", PORT))
sock.settimeout(10)

sender = subprocess.Popen(
    [sys.executable, "tools/fake_game.py", "--port", str(PORT), "--rate", "60", "--duration", "2"],
    stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

frames = []
try:
    deadline = time.monotonic() + 6
    while time.monotonic() < deadline and len(frames) < 60:
        try:
            frames.append(json.loads(sock.recv(65535).decode("utf-8")))
        except socket.timeout:
            break
finally:
    sender.wait(timeout=10)
    sock.close()

if len(frames) < 30:
    sys.exit(f"FAIL: expected at least 30 frames, got {len(frames)}")

required = ["Sequence", "InCar", "SpeedKph", "SlipAngle", "AccelSwayG", "YawRate", "Rpm", "Gear"]
missing = [k for k in required if k not in frames[0]]
if missing:
    sys.exit(f"FAIL: frames are missing {missing}")

sequences = [f["Sequence"] for f in frames]
if sequences != sorted(sequences) or len(set(sequences)) != len(sequences):
    sys.exit("FAIL: sequence numbers are not strictly increasing")

if not all(f["InCar"] is True for f in frames):
    sys.exit("FAIL: synthetic frames should all report InCar")

print(f"   ok   {len(frames)} frames, {len(frames[0])} channels, sequence intact")
PY

printf '\n\033[32m==> all checks passed\033[0m\n'
