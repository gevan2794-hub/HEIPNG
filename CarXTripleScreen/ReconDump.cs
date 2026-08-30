using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using BepInEx;

namespace CarXTripleScreen
{
    /// <summary>
    /// The Phase 0 recon, performed by the plugin itself.
    ///
    /// The four facts the whole design hinges on - pipeline type, gameplay
    /// camera, the class that drives it and its update method, and the HUD
    /// canvas name - are all readable from the live scene. Answering them from
    /// inside the running game is faster and less error-prone than reading them
    /// out of a decompiler, and it stays correct across game updates.
    /// </summary>
    public static class ReconDump
    {
        public const string FileName = "carx-triplescreen-recon.txt";

        public static string OutputPath
        {
            get { return Path.Combine(Paths.ConfigPath, FileName); }
        }

        public static void Write(Camera source, Camera left, Camera right)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Header(sb);
                Cameras(sb, source, left, right);
                Canvases(sb);
                Controllers(sb, source);
                Footer(sb);

                File.WriteAllText(OutputPath, sb.ToString());
                Plugin.Log.LogInfo("Recon report written to " + OutputPath);
                Plugin.Log.LogInfo(sb.ToString());

                if (source != null && Plugin.Instance != null)
                    Plugin.Instance.StartCoroutine(Probe(source));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Recon dump failed: " + e);
            }
        }

        private static void Header(StringBuilder sb)
        {
            PipelineInterop.Detect();
            sb.AppendLine("CarX Triple Screen - recon report");
            sb.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("[M0.1] Runtime");
            sb.AppendLine("  Unity version    : " + Application.unityVersion);
            sb.AppendLine("  Product          : " + Application.productName);
            sb.AppendLine("  Assembly-CSharp  : " + (FindAssembly("Assembly-CSharp") ? "present  -> Mono runtime, BepInEx 5 is correct"
                                                                                     : "NOT FOUND -> possibly IL2CPP; BepInEx 6 + Il2CppInterop would be required"));
            sb.AppendLine("  Screen           : " + Screen.width + "x" + Screen.height +
                          " (fullscreen=" + Screen.fullScreen + ", mode=" + Screen.fullScreenMode + ")");
            sb.AppendLine("  Per-panel guess  : " + (Screen.width / 3) + "x" + Screen.height +
                          "  aspect " + ((Screen.width / 3f) / Mathf.Max(1, Screen.height)).ToString("0.0000"));
            sb.AppendLine();
            sb.AppendLine("[M0.2] Render pipeline");
            sb.AppendLine("  " + PipelineInterop.PipelineName);
            sb.AppendLine("  scriptable=" + PipelineInterop.IsScriptable + "  urp=" + PipelineInterop.IsUrp);
            sb.AppendLine(PipelineInterop.IsUrp
                ? "  -> URP: clones get UniversalAdditionalCameraData, all three as Base cameras with viewport rects."
                : "  -> Built-in: viewport rects apply directly, no additional camera data needed.");
            sb.AppendLine();
        }

        private static bool FindAssembly(string name)
        {
            Assembly[] a = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < a.Length; i++)
                if (a[i].GetName().Name.StartsWith(name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Cameras(StringBuilder sb, Camera source, Camera left, Camera right)
        {
            sb.AppendLine("[M0.3] Cameras  (* = the one this mod is driving)");
            Camera[] all = UnityEngine.Object.FindObjectsOfType<Camera>();
            if (all.Length == 0) sb.AppendLine("  none - are you in a gameplay scene?");

            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                string flag = c == source ? "* " : (c == left || c == right ? "+ " : "  ");
                sb.AppendLine(flag + ScenePath(c.transform));
                sb.AppendLine("      enabled=" + c.enabled + " tag=" + SafeTag(c) + " depth=" + c.depth +
                              " fov=" + c.fieldOfView.ToString("0.00") +
                              " near=" + c.nearClipPlane.ToString("0.###") + " far=" + c.farClipPlane.ToString("0.#"));
                sb.AppendLine("      clear=" + c.clearFlags + " rect=" + c.rect +
                              " cullingMask=0x" + c.cullingMask.ToString("X8") +
                              " targetTexture=" + (c.targetTexture == null ? "none" : c.targetTexture.name + " (RenderTexture -> mirror/reflection)") +
                              PipelineInterop.Describe(c));
            }
            sb.AppendLine();
            sb.AppendLine("  Any camera with a targetTexture is a mirror or reflection probe. Those render to a");
            sb.AppendLine("  RenderTexture and are unaffected by the viewport split, but check whether they copy FOV");
            sb.AppendLine("  from the main camera - if they do, pin their FOV or they will follow the centre panel's.");
            sb.AppendLine();
        }

        private static string SafeTag(Component c)
        {
            try { return c.tag; } catch { return "<untagged>"; }
        }

        private static void Canvases(StringBuilder sb)
        {
            sb.AppendLine("[M0.4] Canvases  (HudCanvasNames takes the names below)");
            Canvas[] all = UnityEngine.Object.FindObjectsOfType<Canvas>();
            if (all.Length == 0) sb.AppendLine("  none");
            for (int i = 0; i < all.Length; i++)
            {
                Canvas c = all[i];
                if (!c.isRootCanvas) continue;
                sb.AppendLine("  " + c.name + "   mode=" + c.renderMode +
                              " layer=" + LayerMask.LayerToName(c.gameObject.layer) +
                              " sortOrder=" + c.sortingOrder +
                              " worldCamera=" + (c.worldCamera == null ? "none" : c.worldCamera.name));
                sb.AppendLine("      path: " + ScenePath(c.transform));
                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                    sb.AppendLine("      -> ScreenSpaceOverlay: this one WILL stretch across all three panels.");
            }
            sb.AppendLine();
        }

        private static void Controllers(StringBuilder sb, Camera source)
        {
            sb.AppendLine("[M0.5] Camera controller candidates");
            if (source == null)
            {
                sb.AppendLine("  no gameplay camera found - run this again while driving.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("  Walking up from " + ScenePath(source.transform) + ".");
            sb.AppendLine("  Set CameraControllerType to the full name of whichever writes the camera each frame,");
            sb.AppendLine("  and CameraControllerMethod to the update method marked below.");
            sb.AppendLine();

            Transform t = source.transform;
            int depth = 0;
            while (t != null)
            {
                MonoBehaviour[] mbs = t.GetComponents<MonoBehaviour>();
                for (int i = 0; i < mbs.Length; i++)
                {
                    MonoBehaviour mb = mbs[i];
                    if (mb == null) continue;
                    Type ty = mb.GetType();
                    if (ty.Assembly == typeof(ReconDump).Assembly) continue;

                    List<string> methods = new List<string>();
                    if (CameraControllerPatch.DeclaresMethod(ty, "LateUpdate")) methods.Add("LateUpdate");
                    if (CameraControllerPatch.DeclaresMethod(ty, "Update")) methods.Add("Update");
                    if (CameraControllerPatch.DeclaresMethod(ty, "FixedUpdate")) methods.Add("FixedUpdate");
                    if (methods.Count == 0) continue;

                    sb.AppendLine("  [" + depth + "] " + ty.FullName);
                    sb.AppendLine("        assembly : " + ty.Assembly.GetName().Name);
                    sb.AppendLine("        declares : " + string.Join(", ", methods.ToArray()));
                    string camFields = CameraFields(ty);
                    if (camFields.Length > 0)
                        sb.AppendLine("        camera-ish members: " + camFields + "   <-- strong candidate");
                }
                t = t.parent;
                depth++;
            }
            sb.AppendLine();
        }

        /// <summary>Fields or properties that reference a Camera or a Transform - the tell for a camera driver.</summary>
        private static string CameraFields(Type ty)
        {
            List<string> hits = new List<string>();
            BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                FieldInfo[] fields = ty.GetFields(f);
                for (int i = 0; i < fields.Length && hits.Count < 8; i++)
                    if (typeof(Camera).IsAssignableFrom(fields[i].FieldType)) hits.Add(fields[i].Name + ":Camera");
                PropertyInfo[] props = ty.GetProperties(f);
                for (int i = 0; i < props.Length && hits.Count < 8; i++)
                    if (typeof(Camera).IsAssignableFrom(props[i].PropertyType)) hits.Add(props[i].Name + ":Camera");
            }
            catch { }
            return string.Join(", ", hits.ToArray());
        }

        private static void Footer(StringBuilder sb)
        {
            sb.AppendLine("[M0.6] Live-probe results are appended a few frames after this file is written.");
            sb.AppendLine();
        }

        /// <summary>
        /// Answers "does the game write fieldOfView / the camera rotation every
        /// frame?" empirically: stamp a sentinel, wait for the frame to end, see
        /// whether anything overwrote it. That is the bullet in Phase 0 that
        /// silently undoes the mod, and it is not reliably answerable by reading
        /// decompiled code.
        /// </summary>
        public static IEnumerator Probe(Camera source)
        {
            if (source == null) yield break;

            TripleScreenManager.ProbeSuspended = true;
            float origFov = source.fieldOfView;
            Quaternion origRot = source.transform.localRotation;

            int fovWrites = 0, rotWrites = 0;
            const int frames = 20;

            for (int i = 0; i < frames; i++)
            {
                // Coroutines resume after Update and before LateUpdate, so a
                // sentinel set here is overwritten by anything running in
                // LateUpdate or later - which is where camera rigs live.
                float sentinelFov = 33.3f + i * 0.01f;
                Quaternion sentinelRot = Quaternion.Euler(11.1f, 22.2f, 0f);
                source.fieldOfView = sentinelFov;
                source.transform.localRotation = sentinelRot;

                yield return new WaitForEndOfFrame();
                if (source == null) break;

                if (Mathf.Abs(source.fieldOfView - sentinelFov) > 0.001f) fovWrites++;
                if (Quaternion.Angle(source.transform.localRotation, sentinelRot) > 0.01f) rotWrites++;
            }

            if (source != null)
            {
                source.fieldOfView = origFov;
                source.transform.localRotation = origRot;
            }
            TripleScreenManager.ProbeSuspended = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[M0.6] Live probe over " + frames + " frames");
            sb.AppendLine("  fieldOfView overwritten on " + fovWrites + "/" + frames + " frames -> " +
                          (fovWrites > frames / 2
                              ? "the game writes FOV every frame. The per-frame re-apply is REQUIRED."
                              : "the game does not appear to write FOV each frame."));
            sb.AppendLine("  localRotation overwritten on " + rotWrites + "/" + frames + " frames -> " +
                          (rotWrites > frames / 2
                              ? "the game drives the camera transform every frame, as expected for a chase cam."
                              : "the camera transform looks static here - are you in a menu rather than driving?"));
            if (CameraControllerPatch.Patched)
                sb.AppendLine("  Harmony target: " + CameraControllerPatch.Target.DeclaringType.FullName +
                              "." + CameraControllerPatch.Target.Name);
            else
                sb.AppendLine("  Harmony target: NOT PATCHED - running on the pre-render hook only.");
            sb.AppendLine();

            try
            {
                File.AppendAllText(OutputPath, sb.ToString());
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Log.LogError("Could not append probe results: " + e.Message); }
        }

        private static string ScenePath(Transform t)
        {
            string p = t.name;
            Transform cur = t.parent;
            while (cur != null) { p = cur.name + "/" + p; cur = cur.parent; }
            return p;
        }
    }
}
