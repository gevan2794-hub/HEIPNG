# CarX Telemetry Bridge

Streams live telemetry out of the CarX games into [SimHub](https://www.simhubdash.com/),
which neither game exposes on its own.

Primary target is **CarX Drift Racing Online 2**, but nothing here is written against a
specific title. The game-specific part is a **text profile**, not code — so the same
build covers CarX Drift Racing Online 1, and stays useful across the Early Access churn
in Online 2, where a patch that renames a field costs a text edit instead of a rebuild.

> **Status: unverified against a running game.** The design is complete and the wire
> protocol is round-trip tested, but this has not been compiled against real BepInEx and
> SimHub assemblies, and no CarX field names are asserted anywhere — the shipped profiles
> are templates you fill in with the in-game dump key. Expect to fix compile errors on
> first build. See [Current state](#current-state).

## How it works

```
  CarX (Unity)                                   SimHub
 ┌──────────────────────────┐                 ┌─────────────────────────┐
 │ BepInEx                  │                 │ CarX Telemetry plugin   │
 │  └ CarX.Telemetry.Mod    │   UDP :20777    │  └ UDP listener         │
 │      ├ VehicleLocator    │ ══════════════> │      ↓                  │
 │      ├ PhysicsSampler ───┼─ engine-derived │  SimHub properties      │
 │      └ GameProfile    ───┼─ game-specific  │  CarXTelemetryPlugin.*  │
 └──────────────────────────┘   flat JSON     └─────────────────────────┘
                                                  ↓         ↓        ↓
                                              dashboards ShakeIt  motion
```

The split that makes "works for all" possible:

- **`PhysicsSampler` needs no game knowledge.** A car in any Unity game is a `Rigidbody`
  with a `Transform`. From those two you get speed, body-frame accelerations, yaw/pitch/
  roll rates, attitude, position, and chassis slip angle — which is most of what a motion
  rig and ShakeIt actually consume. This works on a CarX build nobody has ever profiled.
- **`VehicleLocator` finds the car by shape, not by name.** It scores every non-kinematic
  `Rigidbody` in the scene on wheel count, mass, whether a camera is parented to it, and
  component-name hints. Script names change between versions; "heavy thing with four
  wheels and the camera on it" does not.
- **`GameProfile` holds only what the engine cannot know** — RPM, gear, pedals, drift
  score — as late-bound reflection paths in a text file.

## Layout

| Path | What it is |
|---|---|
| `src/CarX.Telemetry.Shared/` | Frame model, hand-rolled flat-JSON codec, UDP sender. `netstandard2.0`, shared by both ends |
| `src/CarX.Telemetry.Mod/` | The BepInEx plugin that runs inside the game |
| `src/CarX.Telemetry.SimHub/` | The SimHub plugin that receives and republishes as properties |
| `profiles/` | Per-game binding files, seeded into the BepInEx config dir on first run |
| `SETUP.md` | Step-by-step: what you actually have to do, in order |
| `build.ps1` / `install.ps1` | Detect the game flavour, build, and copy everything into place |
| `tools/fake_game.py` | Sends synthetic telemetry, so the SimHub side can be built without the game |
| `tools/monitor.py` | Receives and prints frames, to check the mod before SimHub is involved |
| `PROTOCOL.md` | The wire format and the channel list |
| `docs/FINDING-FIELDS.md` | How to fill in a profile for a new build |

## Build

See **[SETUP.md](SETUP.md)** for the full step-by-step walkthrough. The short version:

Requires the .NET SDK. Two mod flavours, because the two mod loaders differ:

```bash
# Mono games (BepInEx 5) — e.g. CarX Drift Racing Online 1
dotnet build src/CarX.Telemetry.Mod -c Release -p:GameFlavor=Mono

# IL2CPP games (BepInEx 6) — likely current CarX Drift Racing Online 2
dotnet build src/CarX.Telemetry.Mod -c Release -p:GameFlavor=IL2CPP

# SimHub plugin (adjust the path if SimHub is not on C:)
dotnet build src/CarX.Telemetry.SimHub -c Release -p:SimHubPath="C:\Program Files (x86)\SimHub"
```

Which flavour a game needs is visible next to its `.exe`: a `GameAssembly.dll` means
IL2CPP, a `<Game>_Data/Managed/Assembly-CSharp.dll` means Mono.

## Install

**Game side**

1. For CarX Drift Racing Online 1, switch to the moddable build in Steam →
   Properties → Betas first. For Online 2, check whether the current build ships one.
2. Install BepInEx (5 for Mono, 6 for IL2CPP) into the game folder and launch once so it
   generates its directories.
3. Drop `CarX.Telemetry.Mod.dll` and `CarX.Telemetry.Shared.dll` into `BepInEx/plugins/`.
4. Launch. The mod seeds `BepInEx/config/CarXTelemetry/profiles/` and starts sending.

**SimHub side**

Drop `CarX.Telemetry.SimHub.dll` and `CarX.Telemetry.Shared.dll` next to `SimHub.exe`,
restart SimHub, and enable the plugin when it offers. Channels appear as properties named
`CarXTelemetryPlugin.SpeedKph`, `CarXTelemetryPlugin.SlipAngle`, and so on — bind them in
the dashboard editor like any other property.

Port defaults to 20777 on both ends; change it in `BepInEx/config/` in the game and in
`CarXTelemetry.config` next to `SimHub.exe`.

**Or skip the SimHub plugin entirely.** The stream is plain flat JSON, which is exactly
what SimHub's [UDPConnector](https://github.com/Dasde/SimHubUDPConnector) plugin already
eats — point it at the same port and every channel becomes a SimHub property with no
code from this repo on the SimHub side at all. The bundled plugin exists because it adds
connection state, packet-loss counters, and zeroing on disconnect.

## Check it without the game

```bash
python3 tools/fake_game.py --port 20777          # a car in a steady drift, 60Hz
python3 tools/monitor.py  --port 20777           # live one-line readout
python3 tools/monitor.py  --port 20777 --channels  # list every channel in one frame
```

Run `fake_game.py` with SimHub open to build and tune a dashboard before you have a
single working field binding in the game.

## Driving ShakeIt and motion

Because CarX is not a game SimHub knows, this plugin publishes **properties** rather than
filling SimHub's built-in `GameData`. Built-in ShakeIt effects that key off known games
will not light up on their own — but ShakeIt Bass/Motors **custom effects** bind to any
property expression, which is all you need:

| Effect | Bind to |
|---|---|
| Engine vibration | `CarXTelemetryPlugin.Rpm` |
| Lateral load / drift sway | `CarXTelemetryPlugin.AccelSwayG` |
| Accel / braking surge | `CarXTelemetryPlugin.AccelSurgeG` |
| Rear-end step-out | `CarXTelemetryPlugin.SlipAngle` |
| Wheel spin | `CarXTelemetryPlugin.WheelSpeedRL` vs `SpeedMs` |

Motion rigs consume `AccelSurgeG` / `AccelSwayG` / `AccelHeaveG` plus `Yaw`/`Pitch`/`Roll`
and the rate channels directly. Raise `AccelerationSmoothingSeconds` in the BepInEx
config if a rig chatters, lower it for a crisper response.

## Configuration

`BepInEx/config/com.carx.telemetry.bridge.cfg`:

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Master switch |
| `Host` / `Port` | `127.0.0.1` / `20777` | A LAN address streams to another machine |
| `SendRateHz` | `60` | SimHub consumes at 60Hz; higher just wastes packets |
| `AccelerationSmoothingSeconds` | `0.04` | Time constant for the acceleration filter |
| `ForceProfile` | *(empty)* | Pin a profile instead of auto-selecting by game |
| `DumpKey` | `F9` | Dumps the car's components and numeric fields to the log |

## Current state

Done and self-consistent:

- Wire protocol, round-trip tested with the Python tools (59 channels in a full frame).
- Reflection path resolver with member/property/method/index/wildcard support and scaling.
- Profile parser, auto-selection by game, and first-run seeding.
- Heuristic vehicle locator and the engine-derived physics channels.
- Both plugin ends, including packet-loss accounting and disconnect handling.

Not done:

- **Never compiled.** No .NET SDK, BepInEx, or SimHub assemblies were available here, so
  the C# is unbuilt. The SimHub plugin API in particular has no official documentation —
  people learn it by decompiling `SimHub.Plugins.dll` — so treat those signatures as
  needing a first-build pass.
- **No real CarX field names.** `profiles/dro1.profile` and `profiles/dro2.profile` are
  templates with their channel lines commented out. Nothing in this repo claims to know
  what CarX calls its RPM variable; use the F9 dump or a decompiler, per
  [docs/FINDING-FIELDS.md](docs/FINDING-FIELDS.md).
- No settings UI on either end (text config files instead), and no lap/sector timing
  channels wired up.

## Before you run this

- **Use it offline.** Injecting a mod into a live multiplayer session is how people get
  banned, whatever the intent. Practice, solo, and private lobbies are the sane scope.
- **Check the EULA.** CarX shipping a moddable branch for Online 1 is a good sign, not a
  blanket licence, and Online 2 may differ.
- **Early Access moves.** Profiles for Online 2 will need re-dumping after significant
  updates. That is a text edit — which is the entire point of the design.
