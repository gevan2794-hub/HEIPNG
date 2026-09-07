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

**Answered for CarX Drift Racing Online 1 on the `moddable` Steam branch**
(buildid 24351645), read off the install:

> **The branch decides the runtime.** The default `public` branch is an **IL2CPP**
> build (`GameAssembly.dll`, no `Managed/`). The `moddable` branch is **Mono**
> (`MonoBleedingEdge/`, `Managed/Assembly-CSharp.dll`). BepInEx 5 is correct only
> on `moddable`; on `public` you would need BepInEx 6. Check before installing.

| | |
|---|---|
| Unity version | **2023.2.22f1** |
| Runtime | **Mono.** The log carries managed stack traces with Mono runtime internals (`System.RuntimeType:CreateInstanceMono`): 143 `mono` mentions, against **0** in DRO2's IL2CPP log used as a control. |
| Verdict | **BepInEx 5 (Mono, x64) is correct.** |
| `Drift Racing Online_Data/Managed/Assembly-CSharp.dll` present | unconfirmed — the game is not currently installed |

Still **UNKNOWN** for CarX Street. Same two questions, same method.

If `Managed/` exists with readable assemblies it is Mono and BepInEx 5 is
correct. If instead there is a `GameAssembly.dll`, **stop** — the whole approach
changes and you need BepInEx 6 with Il2CppInterop.

The recon report answers this from the loaded assembly list, and the plugin logs
a warning at startup if `Assembly-CSharp` is missing.

## `[M0.2]` Render pipeline — URP or built-in?

**Neither: DRO1 is HDRP.** `Drift Racing Online_Data/Managed/` ships
`Unity.RenderPipelines.HighDefinition.Runtime.dll` (and no Universal assembly), on
both the public and moddable branches.

This is a third case the mod does not yet handle. HDRP puts per-camera state on
`HDAdditionalCameraData`, which the clones must carry, and three HDRP cameras is a
markedly heavier ask than three URP ones. Treat performance as unmeasured.

## `[M0.3]` The gameplay camera

Read straight out of `Assembly-CSharp.dll` with Cecil — no launch required.

`CarX.BaseCamera : MonoBehaviour` is the base; the game switches between four
concrete cameras deriving from it, each with a thin `CARX`-prefixed subclass:

| Type | Update method |
|---|---|
| `CarX.FollowCamera` | `LateUpdate` |
| `CarX.CockpitCamera` | `LateUpdate` |
| `CarX.RearCamera` | `Update`, `LateUpdate` |
| `CarX.StaticCamera` | `LateUpdate` |

So there is no single controller — which is exactly why `UseRenderHook` matters.
Leave it on; it catches whichever camera is live.

**Mirrors exist:** `MirrorCameraHold.LateUpdate` writes `rotation`. Those render to
a RenderTexture and are unaffected by the viewport split, but check they do not
copy FOV from the main camera.

→ config: `MainCameraName` (leave empty to use `Camera.main`)

## `[M0.4]` The camera controller

| | |
|---|---|
| Full type name | **`CarX.FollowCamera`** (third-person drift view) |
| Assembly | `Assembly-CSharp` |
| Update method | **`LateUpdate`** |
| Writes the transform every frame? | **Yes** — `LateUpdate` calls `set_rotation` and `set_localRotation`. The per-frame re-apply is therefore **mandatory**. |
| Writes `fieldOfView` every frame? | **No.** Nothing writes it in gameplay — only `CameraRotation.SetFov` (event-driven) and `GarageCameraBinding` (garage only). |

That last row settles `DynamicFov`: leave it **Off**. There is no speed-based FOV
ramp to preserve, so pinning the geometric FOV costs nothing.

```ini
CameraControllerType   = CarX.FollowCamera
CameraControllerMethod = LateUpdate
```

For the cockpit view, switch the type to `CarX.CockpitCamera`. The render hook
covers both regardless.

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
| `PerScreenWidthPx` / `PerScreenHeightPx` | 0 / 0 | 0 derives from the window. Both CarX titles are stored at **7680x1440** in `HKCU\Software\CarX Technologies\...`, i.e. three 2560x1440 panels, so the derived value will be right and these can stay 0. |

On startup the mod logs the **ideal side angle** for whatever else you measured —
the angle at which tier 1 becomes exact. Compare it against your real
`SideAngleDeg`: close means stop at M3, far means move the panels or go tier 2.
