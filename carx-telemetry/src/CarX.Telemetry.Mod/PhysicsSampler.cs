using UnityEngine;

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// Derives the motion channels from the car's Rigidbody and Transform alone.
    ///
    /// This is the part that works on every CarX title -- and on any other Unity racing
    /// game -- without a single game-specific field name, because it only touches engine
    /// types. Speed, body-frame accelerations, rotation rates, attitude and chassis slip
    /// angle all come from here; the profile is only needed for things the engine cannot
    /// know, like RPM and pedal positions.
    /// </summary>
    public sealed class PhysicsSampler
    {
        private const float Gravity = 9.80665f;

        /// <summary>Below this speed the velocity vector is mostly noise, so slip angle is forced to zero.</summary>
        private const float SlipAngleMinSpeed = 1.0f;

        private Vector3 _previousVelocity;
        private bool _hasPrevious;

        private Vector3 _filteredLocalAcceleration;

        /// <summary>
        /// Exponential smoothing constant for acceleration, in seconds. Raw frame-to-frame
        /// differencing of velocity is spiky enough to make a motion rig chatter; ~40ms
        /// takes the edge off without adding lag a driver can feel.
        /// </summary>
        public float AccelerationSmoothingSeconds = 0.04f;

        public Vector3 Velocity { get; private set; }
        public Vector3 LocalVelocity { get; private set; }
        public Vector3 LocalAngularVelocity { get; private set; }
        public float SlipAngleDegrees { get; private set; }

        public void Reset()
        {
            _hasPrevious = false;
            _previousVelocity = Vector3.zero;
            _filteredLocalAcceleration = Vector3.zero;
            Velocity = Vector3.zero;
            LocalVelocity = Vector3.zero;
            LocalAngularVelocity = Vector3.zero;
            SlipAngleDegrees = 0f;
        }

        /// <summary>
        /// Call from FixedUpdate. Physics runs on a fixed clock, so differencing velocity
        /// there gives a stable dt -- doing it in Update would fold the frame rate into
        /// the acceleration signal.
        /// </summary>
        public void SampleFixed(Rigidbody body, Transform transform, float deltaTime)
        {
            if (body == null || transform == null || deltaTime <= 0f) return;

            var velocity = UnityCompat.GetVelocity(body);
            Velocity = velocity;
            LocalVelocity = transform.InverseTransformDirection(velocity);
            LocalAngularVelocity = transform.InverseTransformDirection(UnityCompat.GetAngularVelocity(body));

            var planarSpeed = new Vector2(LocalVelocity.x, LocalVelocity.z).magnitude;
            SlipAngleDegrees = planarSpeed < SlipAngleMinSpeed
                ? 0f
                : Mathf.Atan2(LocalVelocity.x, Mathf.Abs(LocalVelocity.z)) * Mathf.Rad2Deg;

            if (!_hasPrevious)
            {
                _previousVelocity = velocity;
                _hasPrevious = true;
                return;
            }

            var worldAcceleration = (velocity - _previousVelocity) / deltaTime;
            _previousVelocity = velocity;

            // Heave should read ~1g at rest, the way a real accelerometer in the car would,
            // so gravity is added back before rotating into the body frame.
            var localAcceleration = transform.InverseTransformDirection(worldAcceleration - Physics.gravity);

            var alpha = AccelerationSmoothingSeconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-deltaTime / AccelerationSmoothingSeconds);

            _filteredLocalAcceleration = Vector3.Lerp(_filteredLocalAcceleration, localAcceleration, alpha);
        }

        public void Fill(TelemetryFrame frame, Rigidbody body, Transform transform)
        {
            if (body == null || transform == null) return;

            var speed = Velocity.magnitude;
            frame.Set(Channels.SpeedMs, speed);
            frame.Set(Channels.SpeedKph, speed * 3.6);
            frame.Set(Channels.SpeedMph, speed * 2.2369362920544);

            frame.Set(Channels.VelocitySurge, LocalVelocity.z);
            frame.Set(Channels.VelocitySway, LocalVelocity.x);
            frame.Set(Channels.VelocityHeave, LocalVelocity.y);

            frame.Set(Channels.AccelSurgeG, _filteredLocalAcceleration.z / Gravity);
            frame.Set(Channels.AccelSwayG, _filteredLocalAcceleration.x / Gravity);
            frame.Set(Channels.AccelHeaveG, _filteredLocalAcceleration.y / Gravity);

            frame.Set(Channels.YawRate, LocalAngularVelocity.y * Mathf.Rad2Deg);
            frame.Set(Channels.PitchRate, LocalAngularVelocity.x * Mathf.Rad2Deg);
            frame.Set(Channels.RollRate, LocalAngularVelocity.z * Mathf.Rad2Deg);

            var euler = transform.rotation.eulerAngles;
            frame.Set(Channels.Yaw, UnityCompat.WrapDegrees(euler.y));
            frame.Set(Channels.Pitch, UnityCompat.WrapDegrees(euler.x));
            frame.Set(Channels.Roll, UnityCompat.WrapDegrees(euler.z));

            frame.Set(Channels.SlipAngle, SlipAngleDegrees);

            var position = transform.position;
            frame.Set(Channels.PositionX, position.x);
            frame.Set(Channels.PositionY, position.y);
            frame.Set(Channels.PositionZ, position.z);
        }
    }
}
