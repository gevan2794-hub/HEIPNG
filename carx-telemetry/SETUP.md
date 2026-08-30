# Setup

Ordered so that each step proves something before the next one depends on it. Roughly
30–60 minutes end to end, most of it waiting on downloads.

---

## Step 0 — See it working before you build anything (10 min)

Do this first. It proves your SimHub side works and lets you build a dashboard while the
rest is still unbuilt.

1. Install [Python](https://www.python.org/downloads/) if you don't have it.
2. Install SimHub's [UDPConnector](https://github.com/Dasde/SimHubUDPConnector) plugin and
   point it at port **20777**.
3. Run the fake game:

   ```
   python tools\fake_game.py --port 20777
   ```

That's a synthetic car in a steady drift at 60Hz. SimHub properties should start moving.
Build and lay out your dashboard now, against fake data — then the real thing is just a
matter of swapping the source.

If nothing shows up, the problem is between UDPConnector and SimHub, and it is much
easier to solve here than with a game and a mod loader in the mix.

---

## Step 1 — Work out which BepInEx you need (2 min)

Open the game's install folder (Steam → right-click the game → Manage → Browse local files).

| What you see | What it means | What you need |
|---|---|---|
| `GameAssembly.dll` | IL2CPP build | **BepInEx 6** (IL2CPP, x64) — the bleeding-edge branch |
| `<Game>_Data\Managed\Assembly-CSharp.dll` | Mono build | **BepInEx 5** (x64) — the stable release |

CarX Drift Racing Online 1 is Mono. Online 2 is a newer engine and is more likely IL2CPP,
but check rather than assume — it decides everything downstream.

For **CarX Drift Racing Online 1**, also switch to the moddable build first:
Steam → right-click the game → Properties → Betas → pick the entry marked *(moddable)*.
Check whether Online 2 offers the same.

---

## Step 2 — Install BepInEx and prove it loads (10 min)

1. Download the right version from [BepInEx releases](https://github.com/BepInEx/BepInEx/releases)
   (x64; the IL2CPP builds are under the pre-releases).
2. Extract it **into the game folder** — `winhttp.dll` and the `BepInEx` folder should sit
   next to the game's `.exe`.
3. Launch the game once, then quit.
4. Confirm `BepInEx\LogOutput.log` now exists and mentions your Unity version.

**Don't skip step 4.** If BepInEx isn't loading, no mod you write will ever run, and you
want to find that out now rather than blaming your own code later. IL2CPP first launches
are slow (it generates interop assemblies) — give it a few minutes.

---

## Step 3 — Build (10 min, expect turbulence)

Install the [.NET SDK](https://dotnet.microsoft.com/download), then from this folder:

```powershell
.\build.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\CarX Drift Racing Online 2"
```

It detects IL2CPP vs Mono from the game folder, builds the right flavour, and stages
everything into `dist\`.

> **This compiles, but against reference stubs** — BepInEx's NuGet feed and SimHub's
> assemblies were both unreachable where this was written, so the API surfaces were
> stubbed to type-check the code. Everything builds clean that way, and 102 logic tests
> pass over the JSON codec, path resolver and profile parser.
>
> Your build is the first one against the *real* libraries. The likely failure point is
> the SimHub plugin: SimHub publishes no plugin documentation, so its signatures here are
> written from the shapes that SDK is known to use, and a method name could be wrong.
> That is a small fix, not a broken approach — paste the errors back.
>
> If you want to see the pre-flight checks for yourself first, `./verify.sh` (bash) runs
> the whole stub build and test suite in about 20 seconds.

If you only want the game side for now (using UDPConnector on the SimHub end):

```powershell
.\build.ps1 -GamePath "..." -ModOnly
```

---

## Step 4 — Install and confirm it finds your car (5 min)

```powershell
.\install.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\CarX Drift Racing Online 2"
```

Launch the game, get into a car, then check `BepInEx\LogOutput.log` for:

```
[Info : CarX Telemetry Bridge] located vehicle 'PlayerCar' via CarController (score 340, 47 components visible)
```

That line is the whole ball game. It means the heuristic locator found your car without
knowing anything about CarX, and every physics-derived channel is now live.

With the game running, in another window:

```
python tools\monitor.py --port 20777
```

You should see speed, slip angle, yaw rate and accelerations moving as you drive.
`Rpm` and `Gear` will read `--` — that's step 5.

**If it never finds a car:** lower `minWheelColliders` to `2` in
`BepInEx\config\CarXTelemetry\profiles\generic.profile`, or widen `vehicleType`. If it
locks onto the *wrong* car (an AI or a showroom prop), find the game's own local-player
marker component in a dump and set `playerType`.

---

## Step 5 — Fill in RPM, gear and pedals (20 min, once per game version)

Everything so far needed zero knowledge of CarX. This step is the only part that does.

1. In-car, hold a state you can read off the HUD — sit at idle, then hold a steady
   ~3000rpm in 2nd.
2. Press **F9**. The BepInEx log fills with every component on your car and every numeric
   field with its live value.
3. Find the fields matching the HUD, and write them into
   `BepInEx\config\CarXTelemetry\profiles\dro2.profile`:

   ```ini
   [channels]
   Rpm      = CarController.engineRpm
   Throttle = CarController.gasInput
   Gear     = CarController.currentGear
   ```

4. Restart the game. `monitor.py` should now show real RPM and gear.

Dump twice in different states and diff them — the fields that changed the way you expect
are the ones you want. Full syntax and the decompiler route (faster, if the build allows
it) are in [docs/FINDING-FIELDS.md](docs/FINDING-FIELDS.md).

This is also the step you redo after a big Early Access patch. It's a text edit — no
rebuild, no reinstall.

---

## Step 6 — Wire up SimHub

Channels arrive as properties named `CarXTelemetryPlugin.SpeedKph`,
`CarXTelemetryPlugin.SlipAngle`, and so on (or without the prefix, if you went the
UDPConnector route). Bind them in the dashboard editor like any other property.

For ShakeIt, use **custom** effects — the built-in ones key off games SimHub knows, but
custom effects bind to any property expression:

| Effect | Bind to |
|---|---|
| Engine vibration | `Rpm` |
| Drift sway | `AccelSwayG` |
| Accel / braking | `AccelSurgeG` |
| Rear stepping out | `SlipAngle` |
| Wheelspin | `WheelSpeedRL` vs `SpeedMs` |

Motion rigs take `AccelSurgeG` / `AccelSwayG` / `AccelHeaveG` plus `Yaw`/`Pitch`/`Roll`
and the rate channels directly. If a rig chatters, raise `AccelerationSmoothingSeconds`
in `BepInEx\config\com.carx.telemetry.bridge.cfg`.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| No `BepInEx\LogOutput.log` | BepInEx isn't loading. Wrong version (5 vs 6), or 32-bit build on a 64-bit game |
| Log exists, no `CarX Telemetry Bridge` lines | DLLs in the wrong place, or a flavour mismatch — a Mono-flavour build won't load under BepInEx 6 |
| `located vehicle` never appears | Heuristic missed. Lower `minWheelColliders`, widen `vehicleType` |
| Physics channels move, RPM stays 0 | Normal until step 5 is done |
| `channel 'Rpm' unresolved` in the log | Field renamed by a patch. Dump again, edit the profile |
| Values in `monitor.py` but not SimHub | Port mismatch. Game config vs `CarXTelemetry.config` next to `SimHub.exe` |
| Dash frozen on the last value after quitting | Shouldn't happen — the plugin zeroes channels after 2s. If it does, the plugin isn't running |

## Before you run it

Use it offline — practice, solo, private lobbies. Injecting a mod into a live multiplayer
session is how people get banned, whatever the intent. And check the EULA: CarX shipping
a moddable branch is a good sign, not a blanket licence.
