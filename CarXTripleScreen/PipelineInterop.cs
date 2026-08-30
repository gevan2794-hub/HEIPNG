using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarXTripleScreen
{
    /// <summary>
    /// Everything URP-shaped, reached by reflection.
    ///
    /// Deliberately no compile-time reference to
    /// Unity.RenderPipelines.Universal.Runtime: the mod has to build and load
    /// without knowing which pipeline the game shipped with, and a hard
    /// reference to a URP version the game does not have is an instant
    /// TypeLoadException on a pipeline we were not even going to touch.
    /// </summary>
    public static class PipelineInterop
    {
        public static bool Detected { get; private set; }
        public static bool IsUrp { get; private set; }
        public static bool IsScriptable { get; private set; }
        public static string PipelineName { get; private set; }

        private static Type _addDataType;
        private static PropertyInfo _renderType, _renderPost, _renderShadows, _cameraStack,
                                    _volumeLayerMask, _volumeTrigger, _antialiasing,
                                    _antialiasingQuality, _clearDepth;

        /// <summary>Cheap and re-runnable: the pipeline asset can still be null
        /// when plugins wake up, so this is re-checked on every rebuild rather
        /// than latched once at startup.</summary>
        public static void Detect()
        {
            Detected = true;
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            IsScriptable = asset != null;
            PipelineName = asset != null ? asset.GetType().FullName : "Built-in (no SRP asset)";
            IsUrp = asset != null && asset.GetType().FullName.IndexOf("Universal", StringComparison.Ordinal) >= 0;

            if (!IsUrp || _addDataType != null) return;

            _addDataType = FindType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (_addDataType == null)
            {
                Plugin.Log.LogWarning("URP asset is active but UniversalAdditionalCameraData was not found. " +
                                      "Per-camera URP settings will be left alone.");
                return;
            }

            _renderType = Prop("renderType");
            _renderPost = Prop("renderPostProcessing");
            _renderShadows = Prop("renderShadows");
            _cameraStack = Prop("cameraStack");
            _volumeLayerMask = Prop("volumeLayerMask");
            _volumeTrigger = Prop("volumeTrigger");
            _antialiasing = Prop("antialiasing");
            _antialiasingQuality = Prop("antialiasingQuality");
            _clearDepth = Prop("clearDepth");
        }

        private static PropertyInfo Prop(string name)
        {
            return _addDataType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        }

        private static Type FindType(string fullName)
        {
            Type t = Type.GetType(fullName);
            if (t != null) return t;
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                try
                {
                    t = asms[i].GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* dynamic or unloadable assembly */ }
            }
            return null;
        }

        /// <summary>Mirror the source camera's URP settings onto a clone, as a Base camera.</summary>
        public static void CloneCameraData(Camera source, Camera dest, bool postProcessing, bool shadows, bool copyStack)
        {
            if (!IsUrp || _addDataType == null) return;
            try
            {
                Component src = source.GetComponent(_addDataType);
                Component dst = dest.GetComponent(_addDataType);
                if (dst == null) dst = dest.gameObject.AddComponent(_addDataType);
                if (dst == null) return;

                if (src != null)
                {
                    Copy(_volumeLayerMask, src, dst);
                    Copy(_volumeTrigger, src, dst);
                    Copy(_antialiasing, src, dst);
                    Copy(_antialiasingQuality, src, dst);
                    Copy(_clearDepth, src, dst);
                }

                // Three Base cameras with different viewportRects, never a Base +
                // Overlay stack: URP overlays composite over the base camera's
                // whole target and would fight the viewport split.
                if (_renderType != null && _renderType.CanWrite)
                    _renderType.SetValue(dst, Enum.ToObject(_renderType.PropertyType, 0), null);

                if (_renderPost != null && _renderPost.CanWrite)
                    _renderPost.SetValue(dst, postProcessing, null);
                if (_renderShadows != null && _renderShadows.CanWrite)
                    _renderShadows.SetValue(dst, shadows, null);

                if (_cameraStack != null)
                {
                    IList stack = _cameraStack.GetValue(dst, null) as IList;
                    if (stack != null)
                    {
                        stack.Clear();
                        if (copyStack && src != null)
                        {
                            IList srcStack = _cameraStack.GetValue(src, null) as IList;
                            if (srcStack != null)
                                foreach (object o in srcStack) stack.Add(o);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not mirror URP camera data onto " + dest.name + ": " + e.Message);
            }
        }

        public static void SetPostProcessing(Camera cam, bool on)
        {
            if (!IsUrp || _addDataType == null || _renderPost == null || !_renderPost.CanWrite) return;
            try
            {
                Component d = cam.GetComponent(_addDataType);
                if (d != null) _renderPost.SetValue(d, on, null);
            }
            catch { }
        }

        /// <summary>Describe a camera's URP data for the recon report.</summary>
        public static string Describe(Camera cam)
        {
            if (!IsUrp || _addDataType == null) return "";
            try
            {
                Component d = cam.GetComponent(_addDataType);
                if (d == null) return " urp=<none>";
                string s = " urp[";
                if (_renderType != null) s += "type=" + _renderType.GetValue(d, null);
                if (_renderPost != null) s += " post=" + _renderPost.GetValue(d, null);
                if (_renderShadows != null) s += " shadows=" + _renderShadows.GetValue(d, null);
                if (_cameraStack != null)
                {
                    IList st = _cameraStack.GetValue(d, null) as IList;
                    s += " stack=" + (st == null ? "null" : st.Count.ToString());
                }
                return s + "]";
            }
            catch { return " urp=<error>"; }
        }

        private static void Copy(PropertyInfo p, object src, object dst)
        {
            if (p == null || !p.CanRead || !p.CanWrite) return;
            try { p.SetValue(dst, p.GetValue(src, null), null); } catch { }
        }
    }
}
