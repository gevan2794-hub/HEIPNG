using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarXTripleScreen
{
    /// <summary>
    /// Owns the three cameras and re-applies their projections every frame.
    ///
    /// The camera clones are parented to the gameplay camera's transform, so
    /// they inherit chase-cam motion for free. That is the single biggest
    /// simplification available here: no part of this mod tries to reimplement
    /// the game's camera logic, it only adds fixed local rotations underneath it.
    /// </summary>
    public class TripleScreenManager : MonoBehaviour
    {
        public static TripleScreenManager Instance { get; private set; }

        /// <summary>Set while the recon probe owns the camera, so the per-frame
        /// re-apply does not fight the sentinel values it is measuring.</summary>
        public static bool ProbeSuspended;

        private static readonly Rect RectLeft = new Rect(0f, 0f, 1f / 3f, 1f);
        private static readonly Rect RectCenter = new Rect(1f / 3f, 0f, 1f / 3f, 1f);
        private static readonly Rect RectRight = new Rect(2f / 3f, 0f, 1f / 3f, 1f);

        private Camera _source;
        private Camera _left, _right;
        private readonly PanelSolution[] _sol = new PanelSolution[3];

        private bool _built;
        private bool _dirty = true;
        private float _hudTimer;

        // Saved so a toggle-off or an unload puts the game's camera back exactly.
        private Rect _srcRect = new Rect(0f, 0f, 1f, 1f);
        private float _srcFov = 60f;
        private int _srcCullingMask;
        private bool _srcSaved;
        private int _hudMask;

        // DynamicFovMode.Relative bookkeeping.
        private float _fovBaseline;
        private bool _fovBaselineSet;
        private float _fovDelta;

        // What Solve() used, so a game that retunes its clip planes invalidates
        // the tier 2 matrices instead of quietly rendering with stale ones.
        private float _solvedNear, _solvedFar;

        private bool _warnedInputSystem;
        private bool _warnedNoCamera;

        private void Awake()
        {
            Instance = this;
            PipelineInterop.Detect();
            Plugin.Log.LogInfo("Render pipeline: " + PipelineInterop.PipelineName +
                               (PipelineInterop.IsUrp ? "  (URP path active)" : "  (built-in path active)"));

            if (Cfg.UseRenderHook.Value)
            {
                // Two hooks, one per pipeline: onPreCull never fires under an SRP,
                // beginCameraRendering never fires without one. Whichever is live
                // runs after every script has written the camera transform, which
                // is exactly when the projections need re-stamping. This is the
                // safety net for the day a game update breaks the Harmony patch.
                Camera.onPreCull += OnPreCullBuiltIn;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingSrp;
            }
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnPreCullBuiltIn;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingSrp;
            TearDown();
            if (Instance == this) Instance = null;
        }

        public void MarkDirty() { _dirty = true; }

        private void Update()
        {
            PollHotkeys();

            if (!Cfg.Enabled.Value)
            {
                if (_built) TearDown();
                return;
            }

            // The gameplay camera is destroyed and recreated across scene loads,
            // so validity is checked every frame rather than assumed.
            Camera cam = FindSourceCamera();
            if (cam == null)
            {
                if (_built) TearDown();
                return;
            }
            if (cam != _source) { TearDown(); _source = cam; _dirty = true; }

            if (_dirty || !_built) Rebuild();

            if (_built && Cfg.HudFixupEnabled.Value && Cfg.HudRecheckSeconds.Value > 0f)
            {
                _hudTimer += Time.unscaledDeltaTime;
                if (_hudTimer >= Cfg.HudRecheckSeconds.Value)
                {
                    _hudTimer = 0f;
                    HudFixup.Apply(_source);
                    // A canvas the game created after the last rebuild brings a
                    // new layer with it, and the culling masks have to follow.
                    if (HudFixup.ConvertedLayerMask != _hudMask) _dirty = true;
                }
            }
        }

        // LateUpdate is a backstop for the case where neither the Harmony patch
        // nor a render hook is available. It runs before rendering but its order
        // against the game's own LateUpdate is not guaranteed.
        private void LateUpdate()
        {
            if (_built && !Cfg.UseRenderHook.Value && !CameraControllerPatch.Patched) ApplyProjections();
        }

        private void PollHotkeys()
        {
            try
            {
                if (Cfg.ToggleKey.Value.IsDown())
                {
                    Cfg.Enabled.Value = !Cfg.Enabled.Value;
                    Plugin.Log.LogInfo("Triple screen " + (Cfg.Enabled.Value ? "ENABLED" : "DISABLED"));
                }
                if (Cfg.ReloadKey.Value.IsDown())
                {
                    Plugin.Instance.Config.Reload();
                    _dirty = true;
                    Plugin.Log.LogInfo("Config reloaded; rebuilding cameras.");
                }
                if (Cfg.DumpKey.Value.IsDown())
                    ReconDump.Write(_source, _left, _right);
            }
            catch (Exception e)
            {
                if (!_warnedInputSystem)
                {
                    _warnedInputSystem = true;
                    Plugin.Log.LogWarning("Hotkey polling failed - the game may use the new Input System with legacy " +
                                          "input disabled. Edit the config file directly instead. (" + e.Message + ")");
                }
            }
        }

        // --- discovery -------------------------------------------------------

        private Camera FindSourceCamera()
        {
            string want = Cfg.MainCameraName.Value;
            Camera[] all = Camera.allCameras;

            if (!string.IsNullOrEmpty(want))
            {
                for (int i = 0; i < all.Length; i++)
                    if (!IsOurs(all[i]) && all[i].name == want) return all[i];
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    Plugin.Log.LogWarning("MainCameraName '" + want + "' matched no camera; falling back to Camera.main.");
                }
            }

            Camera main = Camera.main;
            if (main != null && !IsOurs(main) && main.targetTexture == null) return main;

            // No MainCamera tag in this scene: take the screen camera that renders
            // last, which is the one whose image the player sees.
            Camera best = null;
            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                if (IsOurs(c) || c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }

        private bool IsOurs(Camera c)
        {
            return c != null && (c == _left || c == _right);
        }

        // --- build / teardown ------------------------------------------------

        private void Rebuild()
        {
            _dirty = false;
            if (_source == null) return;

            PipelineInterop.Detect();

            if (!_srcSaved)
            {
                _srcRect = _source.rect;
                _srcFov = _source.fieldOfView;
                _srcCullingMask = _source.cullingMask;
                _srcSaved = true;
            }

            if (!Solve()) { TearDown(); return; }

            // The camera is known now, which is the earliest point at which the
            // controller type can be identified.
            CameraControllerPatch.EnsurePatched(_source);

            // HUD first: it decides which layers the centre camera must draw and
            // the side cameras must not, so the culling masks depend on it.
            _hudMask = 0;
            if (Cfg.HudFixupEnabled.Value)
            {
                HudFixup.Apply(_source);
                _hudMask = HudFixup.ConvertedLayerMask;
            }

            _source.cullingMask = _srcCullingMask | _hudMask;

            _left = EnsureClone(_left, "CarXTripleScreen_Left");
            _right = EnsureClone(_right, "CarXTripleScreen_Right");
            if (_left == null || _right == null) { TearDown(); return; }

            ConfigureSide(_left, RectLeft);
            ConfigureSide(_right, RectRight);

            _source.rect = RectCenter;
            PipelineInterop.SetPostProcessing(_source, Cfg.CenterPostProcessing.Value);

            // Aspect has to follow the new viewport rect. If the game had pinned
            // a custom aspect for a full-width camera it would be wrong now.
            _source.ResetAspect();
            _left.ResetAspect();
            _right.ResetAspect();

            // Done once here rather than per frame, and needed when switching
            // back from Tier2 so the custom matrices are actually let go.
            if (Cfg.Tier.Value != ProjectionTier.Tier2OffAxis)
            {
                _source.ResetProjectionMatrix(); _source.ResetWorldToCameraMatrix();
                _left.ResetProjectionMatrix(); _left.ResetWorldToCameraMatrix();
                _right.ResetProjectionMatrix(); _right.ResetWorldToCameraMatrix();
            }

            _built = true;
            _fovBaselineSet = false;
            _fovDelta = 0f;

            ApplyProjections();
            LogSolution();
        }

        private Camera EnsureClone(Camera existing, string cloneName)
        {
            Camera cam = existing;
            if (cam == null)
            {
                // Built from scratch rather than Instantiate(): instantiating the
                // camera GameObject would deep-copy its children (including the
                // other clone) and every gameplay MonoBehaviour riding on it.
                GameObject go = new GameObject(cloneName);
                go.tag = "Untagged";
                cam = go.AddComponent<Camera>();
            }

            cam.CopyFrom(_source);

            // Must not be tagged MainCamera, or Camera.main can start returning a
            // clone and the game's own lookups follow it.
            try { cam.gameObject.tag = "Untagged"; } catch { }

            AudioListener stray = cam.GetComponent<AudioListener>();
            if (stray != null) Destroy(stray);

            cam.transform.SetParent(_source.transform, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localScale = Vector3.one;

            PipelineInterop.CloneCameraData(_source, cam, Cfg.SidePostProcessing.Value,
                                            Cfg.SideShadows.Value, Cfg.CopyUrpCameraStack.Value);
            return cam;
        }

        private void ConfigureSide(Camera cam, Rect rect)
        {
            cam.rect = rect;
            cam.depth = _source.depth;
            cam.targetTexture = null;
            // Drop the HUD layers: a ScreenSpaceCamera canvas obeys culling
            // masks, so without this the HUD would be drawn on every panel.
            cam.cullingMask = ApplyCullingExclusions(_source.cullingMask & ~_hudMask);
            cam.enabled = true;
            ApplyCullDistances(cam);
        }

        private static int ApplyCullingExclusions(int mask)
        {
            string list = Cfg.SideCullingMaskExclude.Value;
            if (string.IsNullOrEmpty(list)) return mask;
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string n = parts[i].Trim();
                if (n.Length == 0) continue;
                int layer = LayerMask.NameToLayer(n);
                if (layer < 0) { Plugin.Log.LogWarning("SideCullingMaskExclude: no layer named '" + n + "'"); continue; }
                mask &= ~(1 << layer);
            }
            return mask;
        }

        private void ApplyCullDistances(Camera cam)
        {
            float scale = Cfg.SideCullDistanceScale.Value;
            if (scale <= 0f || Mathf.Approximately(scale, 1f))
            {
                // All-zero means "use the far clip plane" - this is how the
                // setting is undone after having been turned down.
                cam.layerCullDistances = new float[32];
                return;
            }

            // A zero entry means "use the far clip plane", so it has to be
            // expanded before scaling or the scale would be a no-op.
            float[] d = new float[32];
            float far = cam.farClipPlane;
            for (int i = 0; i < 32; i++) d[i] = far * scale;
            cam.layerCullDistances = d;
        }

        private void TearDown()
        {
            if (_left != null) { Destroy(_left.gameObject); _left = null; }
            if (_right != null) { Destroy(_right.gameObject); _right = null; }

            if (_source != null && _srcSaved)
            {
                _source.rect = _srcRect;
                _source.fieldOfView = _srcFov;
                _source.cullingMask = _srcCullingMask;
                _source.ResetProjectionMatrix();
                _source.ResetWorldToCameraMatrix();
                _source.ResetAspect();
                PipelineInterop.SetPostProcessing(_source, true);
            }
            HudFixup.Restore();
            _hudMask = 0;
            _built = false;
            _srcSaved = false;
        }

        // --- solve -----------------------------------------------------------

        private float PanelAspect()
        {
            float w = Cfg.PerScreenWidthPx.Value > 0 ? Cfg.PerScreenWidthPx.Value : Screen.width / 3f;
            float h = Cfg.PerScreenHeightPx.Value > 0 ? Cfg.PerScreenHeightPx.Value : Screen.height;
            if (h <= 0f) return 16f / 9f;
            return w / h;
        }

        private bool Solve()
        {
            RigGeometry g = Cfg.Geometry();
            if (g.EyeDistanceMm <= 1f || g.ScreenWidthMm <= 1f || g.ScreenHeightMm <= 1f)
            {
                Plugin.Log.LogError("Geometry is not usable: EyeDistanceMm/ScreenWidthMm/ScreenHeightMm must all be > 1mm.");
                return false;
            }

            ScreenQuad[] quads = ProjectionMath.BuildRig(g);
            float aspect = PanelAspect();
            _solvedNear = _source.nearClipPlane;
            _solvedFar = _source.farClipPlane;
            bool tier2 = Cfg.Tier.Value == ProjectionTier.Tier2OffAxis;

            for (int i = 0; i < 3; i++)
            {
                _sol[i] = tier2
                    ? ProjectionMath.SolveTier2(quads[i], Vector3.zero, _solvedNear, _solvedFar, aspect)
                    : ProjectionMath.SolveTier1(quads[i], aspect);

                if (!_sol[i].Valid)
                {
                    Plugin.Log.LogError("Projection solve failed for panel " + i + ": " + _sol[i].Error);
                    return false;
                }
            }
            return true;
        }

        // --- per-frame apply -------------------------------------------------

        /// <summary>
        /// Re-stamp FOV and matrices after the game has written its own. Called
        /// from the Harmony postfix and from the pre-render hooks; must stay cheap
        /// and must tolerate being called several times per frame.
        /// </summary>
        public void ApplyProjections()
        {
            if (!_built || ProbeSuspended || _source == null) return;
            if (_left == null || _right == null) { _dirty = true; return; }

            bool tier2 = Cfg.Tier.Value == ProjectionTier.Tier2OffAxis;

            // Read the game's FOV before overwriting it. Only meaningful when the
            // Harmony postfix is what called us, since the render hooks would just
            // read back our own value from the previous frame.
            if (Cfg.DynamicFov.Value == DynamicFovMode.Relative && CameraControllerPatch.InPostfix)
            {
                float gameFov = _source.fieldOfView;
                if (!_fovBaselineSet) { _fovBaseline = gameFov; _fovBaselineSet = true; }
                _fovDelta = gameFov - _fovBaseline;
            }

            _source.rect = RectCenter;
            _left.rect = RectLeft;
            _right.rect = RectRight;

            _left.transform.localRotation = _sol[ProjectionMath.Left].LocalRotation;
            _right.transform.localRotation = _sol[ProjectionMath.Right].LocalRotation;

            if (tier2)
            {
                // The frustum bounds are scaled by near/d, so a clip-plane change
                // invalidates them; pick it up on the next frame rather than
                // rendering with matrices built for the old planes.
                if (!Mathf.Approximately(_source.nearClipPlane, _solvedNear) ||
                    !Mathf.Approximately(_source.farClipPlane, _solvedFar))
                {
                    _dirty = true;
                }

                // Rotation-and-position only: any scale on the camera transform
                // would otherwise leak into the view matrix.
                Matrix4x4 rigToWorld = Matrix4x4.TRS(_source.transform.position, _source.transform.rotation, Vector3.one);
                Matrix4x4 worldToRig = rigToWorld.inverse;

                ApplyTier2(_source, _sol[ProjectionMath.Center], worldToRig);
                ApplyTier2(_left, _sol[ProjectionMath.Left], worldToRig);
                ApplyTier2(_right, _sol[ProjectionMath.Right], worldToRig);
            }
            else
            {
                ApplyTier1(_source, _sol[ProjectionMath.Center]);
                ApplyTier1(_left, _sol[ProjectionMath.Left]);
                ApplyTier1(_right, _sol[ProjectionMath.Right]);
            }
        }

        private void ApplyTier1(Camera cam, PanelSolution s)
        {
            cam.fieldOfView = s.VerticalFovDeg + _fovDelta;
        }

        private void ApplyTier2(Camera cam, PanelSolution s, Matrix4x4 worldToRig)
        {
            cam.worldToCameraMatrix = s.EyeToView * worldToRig;
            cam.projectionMatrix = s.Projection;
        }

        // --- render hooks ----------------------------------------------------

        private void OnPreCullBuiltIn(Camera cam)
        {
            if (_built && cam == _source) ApplyProjections();
        }

        private void OnBeginCameraRenderingSrp(ScriptableRenderContext ctx, Camera cam)
        {
            if (_built && cam == _source) ApplyProjections();
        }

        // --- logging ---------------------------------------------------------

        private void LogSolution()
        {
            RigGeometry g = Cfg.Geometry();
            float ideal = ProjectionMath.IdealSideAngleDeg(g);

            Plugin.Log.LogInfo("--- triple screen rebuilt (" + Cfg.Tier.Value + ") ---");
            Plugin.Log.LogInfo(string.Format(
                "  panel {0:0}x{1:0}mm  eye {2:0}mm  bezel {3:0}mm  side {4:0.0}deg  aspect {5:0.0000}",
                g.ScreenWidthMm, g.ScreenHeightMm, g.EyeDistanceMm, g.BezelWidthMm, g.SideAngleDeg, PanelAspect()));

            string[] names = { "left  ", "centre", "right " };
            for (int i = 0; i < 3; i++)
            {
                PanelSolution s = _sol[i];
                Plugin.Log.LogInfo(string.Format(
                    "  {0}  yaw {1,7:0.00}deg  vFov {2,6:0.00}deg  hFov {3,6:0.00}deg  asym {4,5:0.00}deg",
                    names[i], s.YawDeg, s.VerticalFovDeg, s.HorizontalFovDeg, s.HorizontalAsymmetryDeg));
            }

            float mismatch = _sol[ProjectionMath.Center].AspectMismatch;
            if (Mathf.Abs(mismatch - 1f) > 0.01f)
                Plugin.Log.LogWarning(string.Format(
                    "  Physical panel aspect disagrees with pixel aspect by {0:0.0}%. Horizontal is matched exactly " +
                    "(seams stay continuous) and the error is taken vertically. Re-measure ScreenWidthMm/ScreenHeightMm.",
                    Mathf.Abs(mismatch - 1f) * 100f));

            if (Cfg.Tier.Value == ProjectionTier.Tier1Rotated)
            {
                float asym = Mathf.Max(_sol[ProjectionMath.Left].HorizontalAsymmetryDeg,
                                       _sol[ProjectionMath.Right].HorizontalAsymmetryDeg);
                if (asym > 1.0f)
                    Plugin.Log.LogWarning(string.Format(
                        "  Side panels are {0:0.00}deg off-axis. For your measurements the exact angle is {1:0.0}deg " +
                        "(currently {2:0.0}deg). Either set SideAngleDeg to match the panels you actually have and " +
                        "switch Tier to Tier2OffAxis, or physically angle the panels closer to {1:0.0}deg.",
                        asym, ideal, g.SideAngleDeg));
                else
                    Plugin.Log.LogInfo(string.Format("  Side panels are near-axis ({0:0.00}deg); Tier1 is accurate here " +
                                                     "(exact angle for this rig: {1:0.0}deg).", asym, ideal));

                if (Mathf.Abs(g.EyeOffsetXMm) > 1f || Mathf.Abs(g.EyeOffsetYMm) > 1f)
                    Plugin.Log.LogWarning("  EyeOffset is set but Tier1 assumes a centred eye. Switch Tier to " +
                                          "Tier2OffAxis for the offset to be applied exactly.");
            }

            if (Cfg.PerScreenWidthPx.Value > 0 && Screen.width != Cfg.PerScreenWidthPx.Value * 3)
                Plugin.Log.LogWarning(string.Format(
                    "  Window is {0}x{1} but PerScreenWidthPx*3 = {2}. Run the game at the full surround resolution.",
                    Screen.width, Screen.height, Cfg.PerScreenWidthPx.Value * 3));
        }
    }
}
