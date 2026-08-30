# Handover to a machine that has the game

Everything in this repo was written and tested in a cloud container with no CarX, no
SimHub and no Windows. The remaining work needs the actual PC. This file is the handover.

## Fastest path: run one script

On the Windows PC, with the game installed:

```powershell
git clone https://github.com/gevan2794-hub/HEIPNG
cd HEIPNG\carx-telemetry
.\setup.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\CarX Drift Racing Online 2"
```

`setup.ps1` detects IL2CPP vs Mono, downloads the matching BepInEx release, installs it,
builds the mod and the SimHub plugin against the real libraries, and copies everything
into place. It pauses where it genuinely needs you — launching the game once so BepInEx
creates its folders, and closing SimHub before replacing its DLLs.

Prerequisites: the [.NET SDK](https://dotnet.microsoft.com/download), and
[Python](https://www.python.org/downloads/) if you want the monitor tools.

## Handing it to Claude Code running on that PC

A Claude Code session started **on the PC** has your filesystem and can do the install,
the build, and the log-reading itself. This is not the same as a session started from
the phone app or from claude.ai, which runs in Anthropic's cloud and can only reach this
repo through git.

Install Claude Code on the PC, open this folder, and paste:

> I want to get the CarX telemetry bridge in `carx-telemetry/` working on this machine.
> Read `carx-telemetry/SETUP.md` and `carx-telemetry/README.md` first.
>
> My game is at `<paste the game folder path>`. SimHub is at `<path, or say "not installed">`.
>
> Please:
> 1. Run `carx-telemetry\setup.ps1` with those paths, and fix whatever it hits. Expect
>    real compile errors in `src/CarX.Telemetry.SimHub` — that project has only ever been
>    built against reference stubs, because SimHub publishes no plugin documentation and
>    its assemblies were unavailable where this was written. Decompile
>    `SimHub.Plugins.dll` if you need the true signatures.
> 2. Tell me when to launch the game, then read `BepInEx\LogOutput.log` and confirm you
>    see a `located vehicle` line. If it never appears, tune `minWheelColliders` and
>    `vehicleType` in `BepInEx\config\CarXTelemetry\profiles\generic.profile`.
> 3. I'll drive and press F9. Read the dump out of the log, work out which fields are
>    RPM, gear, throttle, brake and steering, and write them into the dro2 profile using
>    the syntax in `carx-telemetry/docs/FINDING-FIELDS.md`.
> 4. Commit and push to `claude/carx-telemetry-simhub-api-hf5ifl`.
>
> Don't run the game online — practice and private lobbies only.

## What that session should expect

**Already verified, no need to re-litigate:** all three assemblies compile clean against
stubs, and 102 logic tests pass over the JSON codec, the reflection path resolver and the
profile parser. Run `./verify.sh` (bash) or the equivalent `dotnet build`/`dotnet run`
commands to reproduce.

**Genuinely unverified, in rough order of risk:**

1. **`src/CarX.Telemetry.SimHub`** — the SimHub API signatures are inferred, not read off
   the real DLL. Most likely thing to fail on first build. `PluginManager.AddProperty` /
   `SetPropertyValue` and the `IDataPlugin.DataUpdate` signature are the ones to check.
2. **`VehicleLocator`** — the scoring heuristic that picks the player's car out of the
   scene has never run against a real Unity scene. If it picks nothing, or picks an AI
   car, that is the thing to tune.
3. **`PhysicsSampler`** — the maths is straightforward but the sign conventions
   (especially `SlipAngle` and `AccelSwayG`) have never been eyeballed against a car
   actually drifting. Check that a right-hand drift gives a consistent sign.
4. **BepInEx integration under IL2CPP** — the `ClassInjector` registration and the
   `IntPtr` constructors in `TelemetryService` are excluded from stub builds, so they
   have never been compiled at all. See `build/stubs/README.md`.
5. **The profiles** — `dro1.profile` and `dro2.profile` have every channel commented out.
   Nothing here knows what CarX calls its RPM field, and nothing should pretend to.

## If you have no PC time right now

The one thing that works with nothing but Python and SimHub, on any machine:

```
python tools\fake_game.py --port 20777
```

Synthetic car in a steady drift, 60Hz, 59 channels. Point SimHub's UDPConnector at that
port and build your dashboard against it. When the real stream arrives it is the same
channel names, so the dashboard just starts showing real numbers.
