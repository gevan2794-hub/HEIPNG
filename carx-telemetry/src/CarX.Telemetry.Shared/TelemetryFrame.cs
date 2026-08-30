using System.Collections.Generic;

namespace CarX.Telemetry
{
    /// <summary>
    /// One sample of car state. Deliberately a flat bag of named doubles plus a few
    /// strings: the mod fills in whatever it can resolve for the running game, and the
    /// consumer treats every channel as optional. Adding a channel never requires a
    /// change on the SimHub side -- an unknown key just becomes a new property.
    /// </summary>
    public sealed class TelemetryFrame
    {
        /// <summary>Monotonic frame counter, used to detect drops.</summary>
        public long Sequence;

        /// <summary>Milliseconds since the mod started. Consumers use this for staleness.</summary>
        public double TimestampMs;

        /// <summary>False while in menus / no car spawned. Consumers should idle.</summary>
        public bool InCar;

        public readonly Dictionary<string, double> Numbers = new Dictionary<string, double>(64);
        public readonly Dictionary<string, string> Strings = new Dictionary<string, string>(8);

        public void Set(string key, double value) => Numbers[key] = value;

        public void Set(string key, string value)
        {
            if (!string.IsNullOrEmpty(value)) Strings[key] = value;
        }

        public void Reset(long sequence, double timestampMs, bool inCar)
        {
            Sequence = sequence;
            TimestampMs = timestampMs;
            InCar = inCar;
            Numbers.Clear();
            Strings.Clear();
        }
    }

    /// <summary>
    /// Canonical channel names. These are the keys SimHub dashboards and the bundled
    /// SimHub plugin bind to, so keep them stable -- add new ones rather than renaming.
    /// Anything a given game cannot supply is simply absent from the frame.
    /// </summary>
    public static class Channels
    {
        // --- Derived from Unity Rigidbody/Transform: available in every CarX title ---
        public const string SpeedMs = "SpeedMs";
        public const string SpeedKph = "SpeedKph";
        public const string SpeedMph = "SpeedMph";

        /// <summary>Body-frame velocity. Z forward, X right, Y up.</summary>
        public const string VelocitySurge = "VelocitySurge";
        public const string VelocitySway = "VelocitySway";
        public const string VelocityHeave = "VelocityHeave";

        /// <summary>Body-frame acceleration in g. The primary motion-rig inputs.</summary>
        public const string AccelSurgeG = "AccelSurgeG";
        public const string AccelSwayG = "AccelSwayG";
        public const string AccelHeaveG = "AccelHeaveG";

        /// <summary>Body-frame angular rate, degrees/second.</summary>
        public const string YawRate = "YawRate";
        public const string PitchRate = "PitchRate";
        public const string RollRate = "RollRate";

        /// <summary>Attitude in degrees.</summary>
        public const string Yaw = "Yaw";
        public const string Pitch = "Pitch";
        public const string Roll = "Roll";

        /// <summary>
        /// Chassis slip angle in degrees: the angle between where the car points and
        /// where it is actually going. This is the number that matters for drifting.
        /// </summary>
        public const string SlipAngle = "SlipAngle";

        public const string PositionX = "PositionX";
        public const string PositionY = "PositionY";
        public const string PositionZ = "PositionZ";

        // --- Powertrain / inputs: resolved per game via a profile ---
        public const string Rpm = "Rpm";
        public const string MaxRpm = "MaxRpm";
        public const string IdleRpm = "IdleRpm";
        public const string Gear = "Gear";
        public const string Throttle = "Throttle";     // 0..1
        public const string Brake = "Brake";           // 0..1
        public const string Clutch = "Clutch";         // 0..1
        public const string Handbrake = "Handbrake";   // 0..1
        public const string SteerAngle = "SteerAngle"; // degrees, negative = left
        public const string TurboBoost = "TurboBoost";
        public const string Fuel = "Fuel";

        // --- Per-wheel, indexed FL/FR/RL/RR ---
        public static readonly string[] WheelSuffixes = { "FL", "FR", "RL", "RR" };
        public const string WheelSpeedPrefix = "WheelSpeed";
        public const string TireSlipPrefix = "TireSlip";
        public const string TireTempPrefix = "TireTemp";
        public const string SuspTravelPrefix = "SuspTravel";
        public const string WheelGroundedPrefix = "WheelGrounded";

        // --- Session / scoring ---
        public const string DriftScore = "DriftScore";
        public const string DriftCombo = "DriftCombo";
        public const string LapNumber = "LapNumber";
        public const string CurrentLapTime = "CurrentLapTime";
        public const string LastLapTime = "LastLapTime";
        public const string BestLapTime = "BestLapTime";

        // --- Strings ---
        public const string CarName = "CarName";
        public const string TrackName = "TrackName";
        public const string GameName = "GameName";
        public const string ProfileName = "ProfileName";
    }
}
