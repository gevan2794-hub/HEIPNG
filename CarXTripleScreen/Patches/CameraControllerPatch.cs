using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarXTripleScreen
{
    /// <summary>
    /// Postfix on whichever MonoBehaviour drives the gameplay camera, so our
    /// projections are re-applied after the game has written its own transform
    /// and FOV. Without it the side cameras snap back to the game's defaults.
    ///
    /// Patched lazily rather than through PatchAll, for two reasons: the target
    /// type is not known until the gameplay camera exists, and manual patching
    /// is the only way to report a broken target loudly instead of failing
    /// half-silently the way an attribute patch does.
    /// </summary>
    public static class CameraControllerPatch
    {
        public static bool Patched { get; private set; }
        public static MethodBase Target { get; private set; }

        /// <summary>True only while the game's own update has just run. Lets
        /// DynamicFovMode.Relative read the game's FOV before we overwrite it.</summary>
        public static bool InPostfix { get; private set; }

        private static Harmony _harmony;
        private static bool _attempted;

        public static void Init(Harmony harmony) { _harmony = harmony; }

        /// <summary>Allow one more attempt after a config change. A successful
        /// patch is never retried - patching the same method twice would apply
        /// the projections twice per frame.</summary>
        public static void ResetAttempt() { if (!Patched) _attempted = false; }

        /// <summary>Resolve and patch the camera controller. Safe to call every rebuild.</summary>
        public static void EnsurePatched(Camera source)
        {
            if (Patched || _attempted || _harmony == null || source == null) return;
            if (!Cfg.UseHarmonyPatch.Value) return;
            _attempted = true;

            string methodName = Cfg.CameraControllerMethod.Value;
            if (string.IsNullOrEmpty(methodName)) methodName = "LateUpdate";

            Type type = ResolveType(source, methodName);
            if (type == null)
            {
                Fail("Could not identify the camera controller type. " +
                     "Set CameraControllerType in the config, or press DumpKey in game to list the candidates.");
                return;
            }

            MethodInfo method = AccessTools.Method(type, methodName);
            if (method == null)
            {
                Fail("Type '" + type.FullName + "' has no method '" + methodName +
                     "'. Set CameraControllerMethod to the right one (the recon dump lists what each candidate declares).");
                return;
            }

            try
            {
                MethodInfo postfix = typeof(CameraControllerPatch)
                    .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(method, null, new HarmonyMethod(postfix));
                Target = method;
                Patched = true;
                Plugin.Log.LogInfo("Patched " + type.FullName + "." + methodName + " (postfix).");
            }
            catch (Exception e)
            {
                Fail("Harmony refused to patch " + type.FullName + "." + methodName + ": " + e.Message);
            }
        }

        private static void Fail(string why)
        {
            // Loud, because the alternative is a mod that quietly renders one
            // camera and looks broken for no visible reason.
            Plugin.Log.LogError("=====================================================================");
            Plugin.Log.LogError(" CarX Triple Screen: camera controller patch FAILED");
            Plugin.Log.LogError(" " + why);
            Plugin.Log.LogError(" Falling back to the pre-render hook" +
                                (Cfg.UseRenderHook.Value ? ", which is enabled - the mod should still work."
                                                         : " - but UseRenderHook is OFF, so it will NOT work. Turn it on."));
            Plugin.Log.LogError("=====================================================================");
        }

        /// <summary>
        /// Config wins; otherwise look for a MonoBehaviour on the camera or one
        /// of its ancestors that declares the update method itself. Nearest to
        /// the camera wins, and game assemblies are preferred over engine ones.
        /// </summary>
        private static Type ResolveType(Camera source, string methodName)
        {
            string configured = Cfg.CameraControllerType.Value;
            if (!string.IsNullOrEmpty(configured))
            {
                Type t = AccessTools.TypeByName(configured);
                if (t != null) return t;
                Plugin.Log.LogWarning("CameraControllerType '" + configured + "' did not resolve; auto-detecting instead.");
            }

            List<Type> candidates = FindCandidates(source, methodName);
            if (candidates.Count == 0) return null;

            Plugin.Log.LogInfo("Camera controller auto-detect found " + candidates.Count + " candidate(s); using '" +
                               candidates[0].FullName + "'. If the side cameras drift, set CameraControllerType explicitly.");
            for (int i = 1; i < candidates.Count && i < 6; i++)
                Plugin.Log.LogInfo("  other candidate: " + candidates[i].FullName);
            return candidates[0];
        }

        public static List<Type> FindCandidates(Camera source, string methodName)
        {
            List<Type> ordered = new List<Type>();
            if (source == null) return ordered;

            Transform t = source.transform;
            while (t != null)
            {
                MonoBehaviour[] behaviours = t.GetComponents<MonoBehaviour>();
                List<Type> here = new List<Type>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour mb = behaviours[i];
                    if (mb == null) continue;
                    Type ty = mb.GetType();
                    if (ty.Namespace != null && ty.Namespace.StartsWith("CarXTripleScreen", StringComparison.Ordinal)) continue;
                    if (ty.Assembly == typeof(TripleScreenManager).Assembly) continue;
                    if (ordered.Contains(ty) || here.Contains(ty)) continue;
                    if (DeclaresMethod(ty, methodName)) here.Add(ty);
                }
                // Game code before engine/plugin code at the same level.
                here.Sort(delegate (Type a, Type b)
                {
                    int sa = IsGameAssembly(a) ? 0 : 1, sb = IsGameAssembly(b) ? 0 : 1;
                    return sa != sb ? sa.CompareTo(sb) : string.CompareOrdinal(a.FullName, b.FullName);
                });
                ordered.AddRange(here);
                t = t.parent;
            }
            return ordered;
        }

        private static bool IsGameAssembly(Type t)
        {
            string n = t.Assembly.GetName().Name;
            return n.StartsWith("Assembly-CSharp", StringComparison.Ordinal)
                || (!n.StartsWith("Unity", StringComparison.Ordinal)
                    && !n.StartsWith("System", StringComparison.Ordinal)
                    && !n.StartsWith("mscorlib", StringComparison.Ordinal)
                    && !n.StartsWith("BepInEx", StringComparison.Ordinal)
                    && !n.StartsWith("0Harmony", StringComparison.Ordinal));
        }

        /// <summary>Declared on the type itself or on a base type, but not on MonoBehaviour.</summary>
        public static bool DeclaresMethod(Type t, string methodName)
        {
            Type cur = t;
            while (cur != null && cur != typeof(MonoBehaviour) && cur != typeof(object))
            {
                if (cur.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
                    return true;
                cur = cur.BaseType;
            }
            return false;
        }

        private static void Postfix()
        {
            TripleScreenManager mgr = TripleScreenManager.Instance;
            if (mgr == null) return;
            InPostfix = true;
            try { mgr.ApplyProjections(); }
            catch (Exception e) { Plugin.Log.LogError("ApplyProjections threw: " + e); }
            finally { InPostfix = false; }
        }
    }
}
