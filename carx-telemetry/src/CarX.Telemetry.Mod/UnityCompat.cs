using System;
using System.Reflection;
using UnityEngine;

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// Shims for the Unity API differences between the engine versions the CarX titles
    /// are built on. CarX Drift Racing Online 1 is Unity 2019 (Rigidbody.velocity);
    /// Online 2 is a much newer build, and Unity 6 renamed that to linearVelocity while
    /// leaving the old name as an obsolete alias that may or may not survive stripping.
    /// Resolved once by reflection so one assembly works against both.
    /// </summary>
    internal static class UnityCompat
    {
        private static readonly PropertyInfo LinearVelocity =
            typeof(Rigidbody).GetProperty("linearVelocity", BindingFlags.Instance | BindingFlags.Public)
            ?? typeof(Rigidbody).GetProperty("velocity", BindingFlags.Instance | BindingFlags.Public);

        private static readonly PropertyInfo AngularVelocity =
            typeof(Rigidbody).GetProperty("angularVelocity", BindingFlags.Instance | BindingFlags.Public);

        public static bool VelocityAvailable => LinearVelocity != null;

        public static Vector3 GetVelocity(Rigidbody body)
        {
            if (LinearVelocity == null || body == null) return Vector3.zero;
            try { return (Vector3)LinearVelocity.GetValue(body, null); }
            catch { return Vector3.zero; }
        }

        public static Vector3 GetAngularVelocity(Rigidbody body)
        {
            if (AngularVelocity == null || body == null) return Vector3.zero;
            try { return (Vector3)AngularVelocity.GetValue(body, null); }
            catch { return Vector3.zero; }
        }

        /// <summary>Wraps an angle to (-180, 180] so yaw does not jump by 360 between frames.</summary>
        public static float WrapDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }
    }
}
