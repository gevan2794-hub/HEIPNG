using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CarXTripleScreen
{
    [BepInPlugin(Guid, "CarX Triple Screen", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.local.carxtriplescreen";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Cfg.Bind(Config);

            if (!LooksLikeMono())
                Log.LogWarning("Assembly-CSharp was not found in the loaded assemblies. If this game is an IL2CPP " +
                               "build, this plugin cannot work under BepInEx 5 - it needs BepInEx 6 with Il2CppInterop.");

            _harmony = new Harmony(Guid);
            CameraControllerPatch.Init(_harmony);
            // Patched lazily rather than via PatchAll: the target type cannot be
            // identified until the gameplay camera exists.

            gameObject.AddComponent<TripleScreenManager>();

            Config.SettingChanged += OnSettingChanged;

            Log.LogInfo("Loaded. Toggle=" + Cfg.ToggleKey.Value + "  Reload=" + Cfg.ReloadKey.Value +
                        "  Recon dump=" + Cfg.DumpKey.Value);
            Log.LogInfo("Single-player display mod. Do not run it in online modes.");
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private static void OnSettingChanged(object sender, EventArgs e)
        {
            // A config edit may have supplied the controller type that
            // auto-detection could not find, so let the patch be retried.
            CameraControllerPatch.ResetAttempt();
            TripleScreenManager mgr = TripleScreenManager.Instance;
            if (mgr != null) mgr.MarkDirty();
        }

        private static bool LooksLikeMono()
        {
            System.Reflection.Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
                if (all[i].GetName().Name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
