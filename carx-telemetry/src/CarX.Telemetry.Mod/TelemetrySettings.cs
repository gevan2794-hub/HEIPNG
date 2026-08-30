using System;

namespace CarX.Telemetry.Mod
{
    /// <summary>Runtime knobs, populated from the BepInEx config file.</summary>
    public sealed class TelemetrySettings
    {
        public bool Enabled = true;
        public string Host = "127.0.0.1";
        public int Port = 20777;
        public float SendRateHz = 60f;
        public float VehicleSearchIntervalSeconds = 1.0f;
        public float AccelerationSmoothingSeconds = 0.04f;

        /// <summary>Profile name to force, or empty to auto-select by game.</summary>
        public string ForceProfile = "";

        /// <summary>Directory holding .profile files. Created and seeded on first run.</summary>
        public string ProfileDirectory = "";

        /// <summary>Key that dumps the located car's components and numeric fields to the log.</summary>
        public string DumpKey = "F9";

        /// <summary>Log each channel the profile could not resolve, once, after the car is found.</summary>
        public bool LogUnresolvedChannels = true;

        public Action<string> LogInfo = _ => { };
        public Action<string> LogWarning = _ => { };
    }
}
