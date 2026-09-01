# CarX Triple Screen

True triple-screen rendering for the Mono-runtime CarX titles: three cameras,
correct per-screen projection, bezel compensation, configurable monitor angles.
The game only stretches a single flat frustum across a surround resolution, which
distorts the outer thirds badly; this replaces that with one correctly-projected
frustum per physical panel.

**Targets:**

| Game | Unity | Runtime | Supported |
|---|---|---|---|
| CarX Drift Racing Online (1) | 6000.3-era `2023.2.22f1` | Mono | yes |
| CarX Street | 2021.3 | Mono | yes |
| CarX Drift Racing Online 2 | 6000.3.19f1 | IL2CPP | **no** — see below |

Nothing in the C# is specific to a title: every game type is reached by
reflection and named in config, so supporting another Mono CarX game is a build
flag, not a code change.

**DRO2 is not supported and cannot be.** It is an IL2CPP build shipping an
encrypted `global-metadata.dat`, so Cpp2IL cannot dump it, Il2CppInterop cannot
generate interop assemblies, and BepInEx 6 fails during first-launch generation
before any plugin runs. DRO2 also gained native triple-screen support from CarX
in 2026, which makes the point moot there.

**Loader:** BepInEx 5.4.21+ (Mono, x64) · **Patching:** HarmonyX · **Language:** C#, `net472`

> Single-player display mod. Do not run it in online modes — injecting a DLL into
> a multiplayer title is a ban risk regardless of what the DLL does.

---

## Status

Read this before you build anything.

| | |
|---|---|
| Projection math | **Verified.** `tools/validate_projection.py` checks it against ray-traced ground truth; tier 2 is exact to floating point. |
| Plugin code | **Compiles clean** (.NET SDK 9.0.305, `dotnet build -c Release`, zero warnings). Still **never run** — no Mono CarX title was installed to load it into. |
| Phase 0 recon | **Not done** — it requires the installed game. The plugin performs it for you instead; see below. |

So: the build is no longer the risk — treat the first in-game run as the start of
M1, not the end of it. Everything the spec's Phase 0
was meant to establish is a runtime setting here rather than a compile-time fact,
which is what makes the code deliverable without the game in hand.

---

## Build

```bash
dotnet build -c Release
```

Reference assemblies come from NuGet (`BepInEx.Core`, `UnityEngine.Modules`), so
this builds on any machine with the .NET SDK and no copy of the game.

To build against the real install and deploy in one step:

```bash
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\CarX Drift Racing Online"
```

The managed-assembly folder is named after the product, so the csproj carries a
candidate per supported title. For anything else, name it directly with
`-p:GameManagedDir="...\Whatever_Data\Managed\"`.

There is deliberately **no reference to `Assembly-CSharp.dll` or to URP**. Every
game-specific and pipeline-specific type is reached by reflection, so a game
update that reshuffles them cannot stop the assembly from loading, and a URP
version mismatch cannot become a `TypeLoadException` on a pipeline the mod was
not going to touch anyway.

Install: drop `CarXTripleScreen.dll` into `BepInEx/plugins/`. Run the game once
to generate `BepInEx/config/com.local.carxtriplescreen.cfg`.

---

## Phase 0, done by the plugin

The spec budgets an hour of UnityExplorer and ILSpy to establish four facts.
Press **F11** in a gameplay scene instead. That writes
`BepInEx/config/carx-triplescreen-recon.txt` containing:

- **Runtime** — whether `Assembly-CSharp` is loaded, i.e. Mono (this plugin) vs
  IL2CPP (which would need BepInEx 6 + Il2CppInterop and a different design).
- **Render pipeline** — the live `GraphicsSettings.currentRenderPipeline`, and
  which code path the mod took as a result.
- **Every camera** — name, scene path, depth, clear flags, culling mask, target
  texture, and URP per-camera data. Cameras with a `targetTexture` are your
  mirrors and reflections.
- **Every canvas** — with `ScreenSpaceOverlay` ones flagged, because those are
  the ones that will stretch across all three panels.
- **Camera controller candidates** — every `MonoBehaviour` on the gameplay camera
  and its ancestors that declares `Update`/`LateUpdate`/`FixedUpdate`, with the
  ones holding `Camera` references marked as strong candidates.
- **A live probe** — 20 frames of stamping a sentinel `fieldOfView` and rotation
  and checking whether the game overwrote them. This answers "does it write FOV
  every frame?" empirically, which is the question that silently undoes the mod
  and the one that decompiled code answers least reliably.

Put whatever it names into `CameraControllerType` / `CameraControllerMethod` /
`HudCanvasNames` and reload with **F10**. Auto-detection usually gets the
controller right on its own; the config key exists for when it does not.

Record the answers in `NOTES.md` — that file is the M0 deliverable.

**Hotkeys:** F9 toggle · F10 reload config and rebuild · F11 recon dump.

---

## Dialling in the rig (M3 and M4)

Measure the panels. Do not guess — every formula here is driven by the numbers in
the `1. Geometry` config section, and the mod cannot be more accurate than they are.

1. `ScreenWidthMm` / `ScreenHeightMm` — **visible glass**, not the outside of the frame.
2. `EyeDistanceMm` — eye to the centre of the centre panel, along the panel's normal.
3. `BezelWidthMm` — the combined dead strip at one seam: right bezel of the left
   panel plus left bezel of the centre panel.
4. `SideAngleDeg` — the inward angle of the side panels. Measure it.

Then start the game and read the log. It prints the yaw, FOV and off-axis
asymmetry it derived for each panel, plus **the exact side angle for your other
measurements** — the angle at which tier 1 becomes perfect. If your panels are
close to it, tier 1 is all you need. If they are not, either move the panels or
switch `Tier` to `Tier2OffAxis`.

For M4, park next to a long straight guardrail and pan until it crosses both
seams. It should stay straight and unbroken. If the world *duplicates* across a
seam, `BezelWidthMm` is too small; if it *jumps ahead*, too large. Adjust and
press F10 — no rebuild.

---

## Where this departs from the spec, and why

Each of these is a place the build spec's stated approach would have produced a
visible defect. The numbers come from `tools/validate_projection.py`, run against
a 597×336mm rig at 700mm with 20mm seams.

**1. Tier 1's yaw is derived from geometry, not `SideAngleDeg + atan(B/D)`.**
The spec uses the panel's physical tilt angle directly as the camera's yaw. Those
are only equal in the ideal cylindrical arrangement where each panel happens to
face the eye. On the test rig the ideal angle is 47.84°; set the panels to 47.5°
and the spec's formula yaws the side camera 1.3° too far — about **16mm of world
slip at the seam**, which is exactly the misalignment M4 is trying to remove.

**2. The bezel is modelled as surface, not as an angle at the eye.**
`atan(BezelWidthMm / EyeDistanceMm)` treats the bezel as if it sat straight ahead
at the eye distance. It sits at the panel's edge — further away and viewed
obliquely. For a 20mm seam at 700mm the strip actually hides **1.386°**, where
the spec's formula says 1.637°, a 15% over-compensation that shows up as world
being skipped at the seam. Its *incremental* response to bezel width is close to
right, which is why dialling it in by hand converges anyway; it just converges on
a wrong base angle.

**3. Clones are built from scratch, not `Instantiate`d.**
`Instantiate` on the camera GameObject deep-copies its children — including the
other clone, on the second rebuild — and every gameplay `MonoBehaviour` riding on
it. `new GameObject` + `Camera.CopyFrom` copies exactly the camera settings and
nothing else. The clones are also force-tagged `Untagged`, or `Camera.main` can
start returning a clone and the game's own lookups follow it.

**4. The HUD fix needs culling masks, which the spec omits.**
A `ScreenSpaceOverlay` canvas ignores culling masks; a `ScreenSpaceCamera` one
obeys them. So switching the render mode is only half the job — the HUD's layer
has to be *added* to the centre camera and *removed* from the side cameras.
Without that the HUD either vanishes or is drawn three times, once per panel.

**5. A pre-render hook backs up the Harmony postfix.**
`Camera.onPreCull` (built-in) and `RenderPipelineManager.beginCameraRendering`
(SRP) both fire after every script has written the camera, and both live in
`UnityEngine.CoreModule` — no URP reference needed. This is a direct answer to
the spec's own "game updates break Harmony patches" risk: when the patch target
moves, the patch failure is logged loudly and the mod keeps working.

**6. Per-camera shadow distance and LOD bias do not exist.**
`QualitySettings.shadowDistance` and `QualitySettings.lodBias` are global, so the
spec's suggested mitigation cannot be implemented as written. What is genuinely
per-camera is URP's `renderShadows` flag and `Camera.layerCullDistances`; both
are wired up, along with per-layer culling-mask exclusions.

**7. Tier 1 matches horizontal extent exactly and takes the error vertically.**
A symmetric frustum cannot match a physically non-square-pixel panel in both
axes. Horizontal continuity across the seams is what the eye notices, so that is
matched exactly and any residual is reported as an explicit aspect-mismatch
warning telling you to re-measure.

**8. One geometry model feeds both tiers.**
`BuildRig` produces the panel corners once; tier 1 fits a symmetric frustum to
them and tier 2 runs Kooima on them. The tiers cannot disagree about the rig.

On the spec's handedness warning: the resolution is `vn = cross(vu, vr)` rather
than `cross(vr, vu)`. That makes `d` positive and makes the view matrix collapse
to `Matrix4x4.Scale(1, 1, -1)` for a centred axis-aligned panel — the known-good
Unity idiom, and the cheapest possible check that the sign is right. It is
asserted in the validation script.

---

## Verifying the math

```bash
python3 tools/validate_projection.py
```

No dependencies. It ports `ProjectionMath.cs` to Python and checks it against
ray-traced ground truth: for a grid of world points it compares where the
projection matrix draws them against where the light ray from the eye actually
pierces the glass. Tier 2 is exact to ~1e-13 mm, including with the eye up to
200mm off-centre. It also prints the tier-1 error curve that the "stop at M3"
advice depends on:

| Side angle | Off-axis | Error on the glass | Error at the seam |
|---|---|---|---|
| 40° | 2.14° | 30.5 mm | 1.6 mm |
| 45° | 0.85° | 12.1 mm | 0.3 mm |
| **47.8° (ideal)** | **0.00°** | **0.1 mm** | **0.1 mm** |
| 50° | 0.71° | 9.8 mm | 9.8 mm |
| 55° | 2.55° | 33.2 mm | 33.2 mm |
| 60° | 4.69° | 57.3 mm | 57.3 mm |

Note the asymmetry: below the ideal angle tier 1's error is pushed to the *outer*
edges, where nothing lines up against anything. Above it, the error lands on the
*seam*, where it is most visible. Tier 1 degrades gracefully when your panels are
angled too shallow and badly when they are angled too steep.

---

## Milestones

| # | Gate | State |
|---|---|---|
| M0 | Recon: pipeline, camera class, update method, HUD canvas | Tooling shipped (F11); the facts need the game |
| M1 | Plugin loads, logs the camera list on a hotkey | Written, unverified |
| M2 | Three cameras rendering three viewport thirds | Written, unverified |
| M3 | Tier 1 rotated cameras; lines continuous across seams | Written, math verified |
| M4 | Bezel compensation dialled in against a real straight edge | Written, math verified |
| M5 | HUD confined to centre panel; mirrors correct | Written, unverified |
| M6 | Config hot-reload; effects flags; perf pass | Written, unverified |
| M7 | Tier 2 off-axis projection for off-centre seating | Written, math verified exact |

## Performance

Three culling passes and three render passes: budget 2.5–3× the GPU cost and a
significant CPU hit from culling. The levers, cheapest first, all in the
`5. Effects and performance` config section: `SideCullingMaskExclude` to drop
small props from the side cameras, `SideCullDistanceScale` to pull their draw
distance in, `SideShadows` off, and post-processing off on the sides — which is
also the fix for the seams that SSAO, SSR, motion blur and vignette leave at the
panel edges. A clean seam beats a slightly prettier stretched one.

## Files

| | |
|---|---|
| `ProjectionMath.cs` | Rig geometry and both projection tiers. Pure math, no Unity state. |
| `TripleScreenManager.cs` | Owns the three cameras; rebuilds and re-applies per frame. |
| `Patches/CameraControllerPatch.cs` | Resolves and postfixes the camera controller. |
| `PipelineInterop.cs` | URP access by reflection. |
| `HudFixup.cs` | Confines overlay canvases to the centre panel. |
| `ReconDump.cs` | Phase 0, performed in-game. |
| `Config.cs` · `Plugin.cs` | Settings and entry point. |
| `tools/validate_projection.py` | Ground-truth check on the geometry. |
