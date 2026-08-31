# Handover to a machine that has the game

> ## Status after the first run on a real PC (2026-08-31)
>
> Windows 11, .NET SDK 9.0.305, SimHub at `C:\Program Files (x86)\SimHub`,
> CarX Drift Racing Online 2 (Steam app 1826420), Unity **6000.3.19f1**, IL2CPP.
>
> **The SimHub half works and is installed.** The plugin builds against the real
> assemblies, loads the right interfaces, and the wire protocol carries all 59 channels
> at 60Hz from `fake_game.py`.
>
> **The game half is blocked on DRO2, and not by anything in this repo.** The game ships
> an *encrypted* `global-metadata.dat`:
>
> ```
> magic   : 0xCD756523      (IL2CPP metadata must start with 0xFAB11BAF)
> entropy : 7.861 bits/byte over the first 4KB, all 256 byte values present
> scan    : 0xFAB11BAF appears nowhere in the 31MB file
> ```
>
> Cpp2IL cannot read encrypted metadata, so Il2CppInterop cannot generate the interop
> assemblies, so BepInEx 6 cannot produce the managed layer an IL2CPP plugin needs. This
> fails on the game's first launch, before any of our code runs. Installing BepInEx and
> launching the game does not get past it — that was tried; BepInEx was removed again and
> the game folder is back to stock.
>
> This is a deliberate anti-tamper measure (there is no EasyAntiCheat or BattlEye in the
> folder — the encryption *is* the protection). Treat it as a clear signal from CarX that
> they do not want the process touched, which is worth weighing before going further.
>
> **What is still open:** whether DRO2 exposes telemetry natively. Nothing was found in
> the binary, but that search is inconclusive — IL2CPP string literals live inside the
> encrypted metadata, so absence of evidence is not evidence of absence. Checking the
> in-game settings for a telemetry or motion output option is the cheap next step, and it
> would make this entire mod unnecessary.
>
> **A Mono CarX title remains fully viable.** The Mono flavour builds clean against real
> BepInEx 5, and Mono games are decompilable, which makes step 5 far easier. DRO1 is Mono,
> but the copy on this machine is a leftover `_Data` folder with no executable, so it
> would need reinstalling to try.

Everything in this repo was written and tested in a cloud container with no CarX, no
SimHub and no Windows. The remaining work needs the actual PC. This file is the handover.

## Fastest path: run one script

On the Windows PC, with the game installed:

```powershell
git clone https://github.com/gevan2794-hub/HEIPNG
cd HEIPNG\carx-telemetry
.\setup.ps1
```

It finds the game in your Steam libraries by itself. If it cannot (non-Steam install,
an unusual location), pass the folder: `.\setup.ps1 -GamePath "<folder with the .exe>"`.

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

**Verified on a real PC, no need to re-litigate:**

- The stub build and the 102 logic tests still pass (`./verify.sh`).
- **The SimHub API was inferred correctly.** Reflection over the real `SimHub.Plugins.dll`
  confirms `AddProperty<T>(name, type, value, description = "")`,
  `SetPropertyValue(name, type, value)`, `IDataPlugin.DataUpdate(PluginManager, ref GameData)`
  and `IPlugin`. The one wrong guess was that `SimHub.Logging` lives in `SimHub.Plugins.dll`
  — it is its own assembly and returns a log4net `ILog`, so both DLLs are referenced now.
  The plugin builds clean and is installed.
- The Mono mod flavour builds clean against real BepInEx 5.
- `fake_game.py` → `monitor.py` carries all 59 channels at 60Hz.

**Genuinely unverified, in rough order of risk:**

1. **The IL2CPP reference set.** `CarX.Telemetry.Mod.csproj` now compiles IL2CPP builds
   against `BepInEx\interop` instead of the stock `UnityEngine.Modules` package, because
   only the Il2CppInterop `MonoBehaviour` has the `IntPtr` constructor `TelemetryService`
   needs. **This change has never been compiled** — it needs an interop folder, and no
   IL2CPP CarX title on this machine can produce one (see the status block above). Without
   `-p:GamePath` it now fails with an explicit message rather than a confusing `CS1729`.
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
