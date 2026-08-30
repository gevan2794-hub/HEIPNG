using BepInEx.Configuration;
using UnityEngine;

namespace CarXTripleScreen
{
    public enum ProjectionTier
    {
        /// <summary>Rotated cameras with per-screen FOV. Exact for a centred eye.</summary>
        Tier1Rotated = 1,
        /// <summary>Generalised off-axis (Kooima). Exact for any eye position.</summary>
        Tier2OffAxis = 2,
    }

    public enum DynamicFovMode
    {
        /// <summary>Pin the geometric FOV. Kills the game's speed-based FOV ramp.</summary>
        Off = 0,
        /// <summary>
        /// Track how far the game moved its own FOV from the value it had when
        /// we attached, and add that delta to all three cameras. Keeps the speed
        /// ramp; requires the Harmony patch (the render-hook fallback cannot see
        /// the game's value before we overwrite it).
        /// </summary>
        Relative = 1,
    }

    /// <summary>
    /// Every value the mod reads. Lives in
    /// BepInEx/config/com.local.carxtriplescreen.cfg and hot-reloads on ReloadKey,
    /// because dialling in SideAngleDeg and BezelWidthMm takes dozens of passes
    /// and none of them should need a rebuild.
    /// </summary>
    public static class Cfg
    {
        // --- Geometry (millimetres, measured off the real rig) ---
        public static ConfigEntry<float> ScreenWidthMm;
        public static ConfigEntry<float> ScreenHeightMm;
        public static ConfigEntry<float> BezelWidthMm;
        public static ConfigEntry<float> EyeDistanceMm;
        public static ConfigEntry<float> SideAngleDeg;
        public static ConfigEntry<float> EyeOffsetXMm;
        public static ConfigEntry<float> EyeOffsetYMm;
        public static ConfigEntry<int> PerScreenWidthPx;
        public static ConfigEntry<int> PerScreenHeightPx;

        // --- Behaviour ---
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<ProjectionTier> Tier;
        public static ConfigEntry<DynamicFovMode> DynamicFov;
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<KeyboardShortcut> ReloadKey;
        public static ConfigEntry<KeyboardShortcut> DumpKey;

        // --- Discovery (the Phase 0 unknowns, as runtime settings) ---
        public static ConfigEntry<string> CameraControllerType;
        public static ConfigEntry<string> CameraControllerMethod;
        public static ConfigEntry<string> MainCameraName;
        public static ConfigEntry<bool> UseHarmonyPatch;
        public static ConfigEntry<bool> UseRenderHook;

        // --- HUD ---
        public static ConfigEntry<bool> HudFixupEnabled;
        public static ConfigEntry<string> HudCanvasNames;
        public static ConfigEntry<float> HudPlaneDistance;
        public static ConfigEntry<float> HudRecheckSeconds;

        // --- Effects and performance ---
        public static ConfigEntry<bool> SidePostProcessing;
        public static ConfigEntry<bool> CenterPostProcessing;
        public static ConfigEntry<bool> SideShadows;
        public static ConfigEntry<string> SideCullingMaskExclude;
        public static ConfigEntry<float> SideCullDistanceScale;
        public static ConfigEntry<bool> CopyUrpCameraStack;

        // --- Diagnostics ---
        public static ConfigEntry<bool> VerboseLogging;

        public static void Bind(ConfigFile c)
        {
            const string G = "1. Geometry";
            ScreenWidthMm = c.Bind(G, "ScreenWidthMm", 597f,
                "Visible glass width of ONE panel, in mm. Measure the glass, not the outside of the frame.");
            ScreenHeightMm = c.Bind(G, "ScreenHeightMm", 336f,
                "Visible glass height of one panel, in mm.");
            BezelWidthMm = c.Bind(G, "BezelWidthMm", 20f,
                "Combined dead strip at ONE seam: right bezel of the left panel + left bezel of the centre panel. " +
                "This is what makes the world continue behind the seam instead of duplicating across it.");
            EyeDistanceMm = c.Bind(G, "EyeDistanceMm", 700f,
                "Eye to the centre of the centre panel, measured along the panel's normal (straight out from the glass).");
            SideAngleDeg = c.Bind(G, "SideAngleDeg", 50f,
                "Inward angle of each side panel from the centre panel's plane. Measure the panels; do not guess. " +
                "The log reports the 'ideal' angle for your other measurements - the angle at which Tier1 becomes exact.");
            EyeOffsetXMm = c.Bind(G, "EyeOffsetXMm", 0f,
                "Eye offset right(+)/left(-) from the centre of the centre panel. Only Tier2 uses this fully.");
            EyeOffsetYMm = c.Bind(G, "EyeOffsetYMm", 0f,
                "Eye offset up(+)/down(-) from the centre of the centre panel. Only Tier2 uses this fully.");
            PerScreenWidthPx = c.Bind(G, "PerScreenWidthPx", 0,
                "Horizontal pixels of ONE panel. 0 = derive from the actual window (Screen.width / 3), which is usually right.");
            PerScreenHeightPx = c.Bind(G, "PerScreenHeightPx", 0,
                "Vertical pixels of one panel. 0 = derive from the actual window (Screen.height).");

            const string B = "2. Behaviour";
            Enabled = c.Bind(B, "Enabled", true, "Master switch. Turning this off restores the game's single camera.");
            Tier = c.Bind(B, "Tier", ProjectionTier.Tier1Rotated,
                "Tier1Rotated: rotated cameras with per-screen FOV. Correct for a centred eye, and the right default. " +
                "Tier2OffAxis: full off-axis projection. Only worth it if you sit off-centre or your panels are not symmetric.");
            DynamicFov = c.Bind(B, "DynamicFov", DynamicFovMode.Off,
                "Off pins the geometric FOV, which is what a triple setup wants. Relative preserves the game's " +
                "speed-based FOV ramp by applying its deviation to all three cameras together. Relative needs the Harmony " +
                "patch, and applies to Tier1Rotated only - Tier2OffAxis drives explicit matrices that no FOV value feeds into.");
            ToggleKey = c.Bind(B, "ToggleKey", new KeyboardShortcut(KeyCode.F9), "Toggle triple-screen on/off in game.");
            ReloadKey = c.Bind(B, "ReloadKey", new KeyboardShortcut(KeyCode.F10), "Re-read this file and rebuild the cameras.");
            DumpKey = c.Bind(B, "DumpKey", new KeyboardShortcut(KeyCode.F11),
                "Write a recon report (cameras, canvases, camera-controller candidates, pipeline) to the config folder.");

            const string D = "3. Discovery";
            CameraControllerType = c.Bind(D, "CameraControllerType", "",
                "Full name of the MonoBehaviour that drives the gameplay camera, e.g. 'CarX.Camera.GameCameraController'. " +
                "Empty = auto-detect from the components on the camera and its parents. Press DumpKey in game to find it.");
            CameraControllerMethod = c.Bind(D, "CameraControllerMethod", "LateUpdate",
                "The method on that type to postfix. Usually LateUpdate.");
            MainCameraName = c.Bind(D, "MainCameraName", "",
                "GameObject name of the gameplay camera. Empty = use Camera.main / the highest-depth screen camera.");
            UseHarmonyPatch = c.Bind(D, "UseHarmonyPatch", true,
                "Re-apply projections from a postfix on the camera controller. Preferred: it runs before culling.");
            UseRenderHook = c.Bind(D, "UseRenderHook", true,
                "Also re-apply from Camera.onPreCull / RenderPipelineManager.beginCameraRendering. This is the safety net " +
                "that keeps the mod working when a game update breaks the Harmony patch. Leave it on.");

            const string H = "4. HUD";
            HudFixupEnabled = c.Bind(H, "HudFixupEnabled", true,
                "Move ScreenSpaceOverlay canvases onto the centre camera so the HUD does not stretch across all three panels.");
            HudCanvasNames = c.Bind(H, "HudCanvasNames", "",
                "Comma-separated GameObject names to confine to the centre panel. Empty = every ScreenSpaceOverlay canvas.");
            HudPlaneDistance = c.Bind(H, "HudPlaneDistance", 1f,
                "planeDistance for the converted canvases. Must sit between the camera's near and far clip planes.");
            HudRecheckSeconds = c.Bind(H, "HudRecheckSeconds", 2f,
                "How often to re-scan for canvases the game created or reset. 0 disables re-scanning.");

            const string E = "5. Effects and performance";
            SidePostProcessing = c.Bind(E, "SidePostProcessing", false,
                "Post-processing on the side cameras. Off by default: anything sampling the screen buffer (SSAO, SSR, " +
                "motion blur, vignette) seams visibly at the panel edges. A clean seam beats a prettier stretched one.");
            CenterPostProcessing = c.Bind(E, "CenterPostProcessing", true,
                "Post-processing on the centre camera. Turn off too if effects still seam.");
            SideShadows = c.Bind(E, "SideShadows", true,
                "Render shadows on the side cameras (URP only; the built-in pipeline has no per-camera equivalent).");
            SideCullingMaskExclude = c.Bind(E, "SideCullingMaskExclude", "",
                "Comma-separated layer names to drop from the side cameras, e.g. 'SmallProps,Decals'. Cheapest real win.");
            SideCullDistanceScale = c.Bind(E, "SideCullDistanceScale", 1f,
                "Scales per-layer cull distances on the side cameras (Camera.layerCullDistances). " +
                "0.5 halves the draw distance out there. 1 = unchanged.");
            CopyUrpCameraStack = c.Bind(E, "CopyUrpCameraStack", false,
                "Copy the source camera's URP overlay stack onto the clones. Off by default: a URP overlay camera may only " +
                "belong to one stack, and overlays composite against the base camera's target.");

            const string X = "6. Diagnostics";
            VerboseLogging = c.Bind(X, "VerboseLogging", false, "Log every rebuild in full detail.");
        }

        /// <summary>Geometry as the math layer wants it.</summary>
        public static RigGeometry Geometry()
        {
            RigGeometry g;
            g.ScreenWidthMm = ScreenWidthMm.Value;
            g.ScreenHeightMm = ScreenHeightMm.Value;
            g.BezelWidthMm = BezelWidthMm.Value;
            g.EyeDistanceMm = EyeDistanceMm.Value;
            g.SideAngleDeg = SideAngleDeg.Value;
            g.EyeOffsetXMm = EyeOffsetXMm.Value;
            g.EyeOffsetYMm = EyeOffsetYMm.Value;
            return g;
        }
    }
}
