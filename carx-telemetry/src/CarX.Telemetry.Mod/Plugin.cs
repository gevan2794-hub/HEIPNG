using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;

#if IL2CPP
using BepInEx.Unity.IL2CPP;
#endif

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// BepInEx entry point. Two mod-loader flavours, one payload: BepInEx 5 (Mono games
    /// such as CarX Drift Racing Online 1) derives from BaseUnityPlugin, BepInEx 6
    /// (IL2CPP games such as recent CarX Drift Racing Online 2 builds) from BasePlugin.
    /// Both do the same thing -- read config, load profiles, attach TelemetryService to a
    /// GameObject that survives scene loads.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
#if IL2CPP
    public sealed class Plugin : BasePlugin
#else
    public sealed class Plugin : BaseUnityPlugin
#endif
    {
        public const string PluginGuid = "com.carx.telemetry.bridge";
        public const string PluginName = "CarX Telemetry Bridge";
        public const string PluginVersion = "0.1.0";

        private const string HostObjectName = "CarXTelemetryBridge";

        private static GameObject _host;

#if IL2CPP
        public override void Load()
        {
            Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<TelemetryService>();
            Start(Config, message => Log.LogInfo(message), message => Log.LogWarning(message));
        }
#else
        private void Awake()
        {
            Start(Config, message => Logger.LogInfo(message), message => Logger.LogWarning(message));
        }
#endif

        private static void Start(ConfigFile config, Action<string> logInfo, Action<string> logWarning)
        {
            try
            {
                var settings = ReadSettings(config, logInfo, logWarning);
                if (!settings.Enabled)
                {
                    logInfo("disabled by config; not starting");
                    return;
                }

                var profiles = ProfileStore.LoadAll(settings.ProfileDirectory, logInfo, logWarning);

                _host = new GameObject(HostObjectName);
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.hideFlags = HideFlags.HideAndDontSave;

                var service = _host.AddComponent<TelemetryService>();
                service.Initialize(settings, profiles);
            }
            catch (Exception ex)
            {
                // A mod that throws in Awake can take the game's whole plugin chain down.
                logWarning("failed to start: " + ex);
            }
        }

        private static TelemetrySettings ReadSettings(ConfigFile config, Action<string> logInfo, Action<string> logWarning)
        {
            var defaults = new TelemetrySettings();

            var enabled = config.Bind("General", "Enabled", true,
                "Master switch. Turn off to stop all telemetry output without uninstalling.");

            var host = config.Bind("Output", "Host", defaults.Host,
                "Where to send telemetry. 127.0.0.1 for SimHub on this PC; a LAN address to stream to another machine.");

            var port = config.Bind("Output", "Port", defaults.Port,
                "UDP port. Must match the port configured in the SimHub plugin (or in UDPConnector).");

            var rate = config.Bind("Output", "SendRateHz", defaults.SendRateHz,
                new ConfigDescription("Frames per second to send. SimHub consumes at 60Hz; higher just wastes packets.",
                    new AcceptableValueRange<float>(1f, 250f)));

            var smoothing = config.Bind("Physics", "AccelerationSmoothingSeconds", defaults.AccelerationSmoothingSeconds,
                new ConfigDescription("Time constant for acceleration smoothing. Raise it if a motion rig chatters, lower it for a crisper response.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            var searchInterval = config.Bind("Physics", "VehicleSearchIntervalSeconds", defaults.VehicleSearchIntervalSeconds,
                new ConfigDescription("How often to scan the scene for the player's car while none is held.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            var forceProfile = config.Bind("Profiles", "ForceProfile", "",
                "Name of a profile to force. Leave empty to auto-select by game.");

            var dumpKey = config.Bind("Profiles", "DumpKey", defaults.DumpKey,
                "Key that dumps the located car's components and numeric fields to the BepInEx log. Used for authoring profiles. Empty to disable.");

            var logUnresolved = config.Bind("Profiles", "LogUnresolvedChannels", true,
                "Log each profile channel that could not be resolved (once per channel).");

            var profileDirectory = Path.Combine(Path.GetDirectoryName(config.ConfigFilePath) ?? ".", "CarXTelemetry", "profiles");

            return new TelemetrySettings
            {
                Enabled = enabled.Value,
                Host = host.Value,
                Port = port.Value,
                SendRateHz = rate.Value,
                AccelerationSmoothingSeconds = smoothing.Value,
                VehicleSearchIntervalSeconds = searchInterval.Value,
                ForceProfile = forceProfile.Value,
                DumpKey = dumpKey.Value,
                LogUnresolvedChannels = logUnresolved.Value,
                ProfileDirectory = profileDirectory,
                LogInfo = logInfo,
                LogWarning = logWarning
            };
        }
    }

    /// <summary>
    /// Loads .profile files from disk, seeding the directory with the built-in ones on
    /// first run. Shipped profiles are written out rather than kept internal so they can
    /// be edited in place when a game update moves a field.
    /// </summary>
    internal static class ProfileStore
    {
        private const string SeedMarker = ".seeded";

        public static List<GameProfile> LoadAll(string directory, Action<string> logInfo, Action<string> logWarning)
        {
            var profiles = new List<GameProfile>();

            try
            {
                Directory.CreateDirectory(directory);
                SeedBuiltIns(directory, logInfo, logWarning);

                foreach (var file in Directory.GetFiles(directory, "*.profile"))
                {
                    try
                    {
                        profiles.Add(GameProfile.Load(file));
                    }
                    catch (Exception ex)
                    {
                        logWarning($"could not read profile '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logWarning("profile directory unavailable, continuing with engine-derived channels only: " + ex.Message);
            }

            logInfo($"loaded {profiles.Count} profile(s) from {directory}");
            return profiles;
        }

        private static void SeedBuiltIns(string directory, Action<string> logInfo, Action<string> logWarning)
        {
            // Seed once. After that the files on disk are the user's, and overwriting them
            // on every launch would silently undo their field fixes.
            var marker = Path.Combine(directory, SeedMarker);
            if (File.Exists(marker)) return;

            var assembly = Assembly.GetExecutingAssembly();
            var written = 0;

            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.EndsWith(".profile", StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = resource.Substring(resource.LastIndexOf('.', resource.Length - ".profile".Length - 1) + 1);
                var target = Path.Combine(directory, fileName);
                if (File.Exists(target)) continue;

                try
                {
                    using (var stream = assembly.GetManifestResourceStream(resource))
                    {
                        if (stream == null) continue;
                        using (var reader = new StreamReader(stream))
                        using (var writer = new StreamWriter(target, false))
                        {
                            writer.Write(reader.ReadToEnd());
                        }
                    }
                    written++;
                }
                catch (Exception ex)
                {
                    logWarning($"could not write built-in profile '{fileName}': {ex.Message}");
                }
            }

            try { File.WriteAllText(marker, "Delete this file to restore the built-in profiles on next launch.\n"); }
            catch { /* seeding worked; the marker is only an optimisation */ }

            if (written > 0) logInfo($"seeded {written} built-in profile(s) into {directory}");
        }
    }
}
