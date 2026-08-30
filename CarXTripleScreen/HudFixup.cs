using System.Collections.Generic;
using UnityEngine;

namespace CarXTripleScreen
{
    /// <summary>
    /// A ScreenSpaceOverlay canvas ignores camera viewports entirely and paints
    /// itself across the whole surround target, so the minimap and tach end up
    /// smeared across all three panels. Re-homing those canvases onto the centre
    /// camera as ScreenSpaceCamera confines them to the centre third.
    /// </summary>
    public static class HudFixup
    {
        private class Saved
        {
            public Canvas Canvas;
            public RenderMode Mode;
            public Camera WorldCamera;
            public float PlaneDistance;
        }

        private static readonly List<Saved> _saved = new List<Saved>();

        /// <summary>
        /// Layers of every canvas we converted. A ScreenSpaceCamera canvas obeys
        /// culling masks where an overlay canvas did not, so these layers have to
        /// be switched on for the centre camera and off for the side ones - which
        /// is also what keeps the HUD from being drawn three times.
        /// </summary>
        public static int ConvertedLayerMask { get; private set; }

        public static void Apply(Camera centerCamera)
        {
            if (centerCamera == null) return;

            int mask = 0;
            string filter = Cfg.HudCanvasNames.Value;
            string[] wanted = string.IsNullOrEmpty(filter) ? null : filter.Split(',');

            Canvas[] all = UnityEngine.Object.FindObjectsOfType<Canvas>();
            for (int i = 0; i < all.Length; i++)
            {
                Canvas c = all[i];
                if (c == null || !c.isRootCanvas) continue;

                if (wanted != null && !Matches(c.name, wanted)) continue;

                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    Remember(c);
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = centerCamera;
                    c.planeDistance = ClampPlane(centerCamera, Cfg.HudPlaneDistance.Value);
                    mask |= 1 << c.gameObject.layer;
                    if (Cfg.VerboseLogging.Value)
                        Plugin.Log.LogInfo("HUD: '" + c.name + "' overlay -> centre camera (layer " +
                                           LayerMask.LayerToName(c.gameObject.layer) + ")");
                }
                else if (c.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    // Already camera-space, but possibly pointed at a camera we
                    // just repurposed, or at one the game re-assigned.
                    if (c.worldCamera == null || c.worldCamera == centerCamera)
                    {
                        Remember(c);
                        c.worldCamera = centerCamera;
                        c.planeDistance = ClampPlane(centerCamera, Cfg.HudPlaneDistance.Value);
                        mask |= 1 << c.gameObject.layer;
                    }
                }
            }

            ConvertedLayerMask = mask;
        }

        private static float ClampPlane(Camera cam, float requested)
        {
            // Outside the clip range the canvas silently disappears, which reads
            // as "the mod deleted my HUD".
            float lo = cam.nearClipPlane * 1.01f;
            float hi = cam.farClipPlane * 0.99f;
            return Mathf.Clamp(requested, lo, hi);
        }

        private static bool Matches(string name, string[] wanted)
        {
            for (int i = 0; i < wanted.Length; i++)
            {
                string w = wanted[i].Trim();
                if (w.Length > 0 && name == w) return true;
            }
            return false;
        }

        private static void Remember(Canvas c)
        {
            for (int i = 0; i < _saved.Count; i++)
                if (_saved[i].Canvas == c) return;

            Saved s = new Saved();
            s.Canvas = c;
            s.Mode = c.renderMode;
            s.WorldCamera = c.worldCamera;
            s.PlaneDistance = c.planeDistance;
            _saved.Add(s);
        }

        public static void Restore()
        {
            for (int i = 0; i < _saved.Count; i++)
            {
                Saved s = _saved[i];
                if (s.Canvas == null) continue;
                s.Canvas.renderMode = s.Mode;
                s.Canvas.worldCamera = s.WorldCamera;
                s.Canvas.planeDistance = s.PlaneDistance;
            }
            _saved.Clear();
            ConvertedLayerMask = 0;
        }
    }
}
