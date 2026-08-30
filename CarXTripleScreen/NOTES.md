# Phase 0 — Recon notes

The M0 deliverable. Four facts, and everything after Phase 0 depends on them.

**These are not filled in.** Phase 0 requires the installed game, which was not
available when this code was written, so every value below is `UNKNOWN`. Nothing
here should be treated as confirmed until you have replaced it.

Filling it in is one hotkey: launch the game with the plugin installed, get into
a race, press **F11**. That writes `BepInEx/config/carx-triplescreen-recon.txt`
with each section below already answered; copy the answers across. The section
tags (`[M0.1]` … `[M0.6]`) match between the two files.

---

## `[M0.1]` Runtime — Mono or IL2CPP?

| | |
|---|---|
| `CarX Street_Data/Managed/Assembly-CSharp.dll` present | **UNKNOWN** |
| Unity version | **UNKNOWN** |
| Verdict | **UNKNOWN** |

If `Managed/` exists with readable assemblies it is Mono and BepInEx 5 is
correct. If instead there is a `GameAssembly.dll`, **stop** — the whole approach
changes and you need BepInEx 6 with Il2CppInterop.

The recon report answers this from the loaded assembly list, and the plugin logs
a warning at startup if `Assembly-CSharp` is missing.

## `[M0.2]` Render pipeline — URP or built-in?

| | |
|---|---|
| `Unity.RenderPipelines.Universal.Runtime.dll` in `Managed/` | **UNKNOWN** |
| Live `GraphicsSettings.currentRenderPipeline` | **UNKNOWN** |
| Verdict | **UNKNOWN** |

- **URP** — the clones need `UniversalAdditionalCameraData`. Three **Base**
  cameras with different `viewportRect`s, never a Base + Overlay stack: overlays
  composite over the base camera's whole target and would fight the viewport split.
- **Built-in** — viewport rects apply directly.

The mod detects this at runtime and takes the right path either way, so this
entry is for your understanding rather than for configuring anything.

## `[M0.3]` The gameplay camera

| | |
|---|---|
| GameObject name | **UNKNOWN** |
| Scene path | **UNKNOWN** |
| Tagged `MainCamera` | **UNKNOWN** |
| Other cameras (mirrors / reflections / UI) | **UNKNOWN** |

Any camera with a `targetTexture` is a mirror or a reflection probe. Those render
to a RenderTexture and are unaffected by the viewport split — but check whether
they copy FOV from the main camera. If they do, pin their FOV or they will follow
the centre panel's and the mirrors will go wrong.

→ config: `MainCameraName` (leave empty to use `Camera.main`)

## `[M0.4]` The camera controller

| | |
|---|---|
| Full type name | **UNKNOWN** |
| Assembly | **UNKNOWN** |
| Update method (`Update` / `LateUpdate` / `FixedUpdate`) | **UNKNOWN** |
| Writes `fieldOfView` every frame? | **UNKNOWN** |
| Writes the transform every frame? | **UNKNOWN** |

The last two are what the `[M0.6]` live probe measures. If the game writes FOV
every frame — it almost certainly does — the per-frame re-apply is mandatory and
without it the side cameras snap back to the game's defaults.

Get the exact namespace and signature right. Harmony fails half-silently on a
wrong signature; this plugin logs loudly instead, but it still will not patch.

→ config: `CameraControllerType`, `CameraControllerMethod`

## `[M0.5]` The HUD canvas

| | |
|---|---|
| Canvas GameObject name(s) | **UNKNOWN** |
| Render mode | **UNKNOWN** |
| Layer | **UNKNOWN** |

A `ScreenSpaceOverlay` canvas ignores camera viewports and stretches across all
three panels. Confirm the minimap, tach and race prompts all land on the centre
panel only once the fixup is on.

→ config: `HudCanvasNames` (leave empty to convert every overlay canvas)

## `[M0.6]` Live probe

| | |
|---|---|
| `fieldOfView` overwritten | **UNKNOWN** / 20 frames |
| `localRotation` overwritten | **UNKNOWN** / 20 frames |
| Harmony target actually patched | **UNKNOWN** |

---

## Rig measurements

Not part of Phase 0, but nothing renders correctly without them. Measure the
physical panels; the mod cannot be more accurate than these numbers. All in
millimetres, all in the `1. Geometry` config section.

| Key | Value | Note |
|---|---|---|
| `ScreenWidthMm` | | visible glass, not the frame |
| `ScreenHeightMm` | | visible glass |
| `BezelWidthMm` | | both bezels at one seam, combined |
| `EyeDistanceMm` | | along the centre panel's normal |
| `SideAngleDeg` | | measured, not guessed |
| `EyeOffsetXMm` / `EyeOffsetYMm` | 0 / 0 | tier 2 only |
| `PerScreenWidthPx` / `PerScreenHeightPx` | 0 / 0 | 0 derives from the window |

On startup the mod logs the **ideal side angle** for whatever else you measured —
the angle at which tier 1 becomes exact. Compare it against your real
`SideAngleDeg`: close means stop at M3, far means move the panels or go tier 2.
