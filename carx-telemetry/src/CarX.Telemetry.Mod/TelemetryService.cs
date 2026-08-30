using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// The pump. Locates the car, samples it, and pushes one JSON datagram per tick.
    ///
    /// Everything in here is written to fail soft: if the profile is wrong, or the game
    /// patched a field away, or no car exists because you are in a menu, the result is
    /// missing channels and an idle socket -- never an exception on Unity's main thread.
    /// </summary>
    public sealed class TelemetryService : MonoBehaviour
    {
        private TelemetrySettings _settings;
        private GameProfile _profile;
        private List<GameProfile> _profiles = new List<GameProfile>();

        private VehicleLocator _locator;
        private readonly PhysicsSampler _sampler = new PhysicsSampler();
        private readonly TelemetryFrame _frame = new TelemetryFrame();
        private UdpJsonSender _sender;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private float _nextSendTime;
        private long _sequence;

        private readonly HashSet<string> _reportedUnresolved = new HashSet<string>(StringComparer.Ordinal);
        private bool _dumpRequested;

#if IL2CPP
        // Il2CppInterop constructs injected MonoBehaviours from the native side and
        // requires this constructor to exist.
        public TelemetryService(IntPtr pointer) : base(pointer) { }

        public TelemetryService() : base(
            Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<TelemetryService>())
        {
            Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);
        }
#endif

        // MonoBehaviours are constructed by Unity, so configuration arrives via Initialize
        // rather than a constructor.
        public void Initialize(TelemetrySettings settings, IEnumerable<GameProfile> profiles)
        {
            _settings = settings ?? new TelemetrySettings();
            _profiles = new List<GameProfile>(profiles);
            _locator = new VehicleLocator(message => _settings.LogInfo(message));
            _sampler.AccelerationSmoothingSeconds = _settings.AccelerationSmoothingSeconds;

            SelectProfile();

            _sender = new UdpJsonSender(_settings.Host, _settings.Port);
            if (_sender.Faulted)
                _settings.LogWarning($"UDP sender failed to start for {_settings.Host}:{_settings.Port}: {_sender.LastError}");
            else
                _settings.LogInfo($"streaming telemetry to {_settings.Host}:{_settings.Port} at {_settings.SendRateHz:0.#}Hz");
        }

        private void SelectProfile()
        {
            var productName = SafeProductName();
            var processName = SafeProcessName();

            if (!string.IsNullOrEmpty(_settings.ForceProfile))
            {
                _profile = _profiles.Find(p => string.Equals(p.Name, _settings.ForceProfile, StringComparison.OrdinalIgnoreCase));
                if (_profile == null)
                    _settings.LogWarning($"forceProfile '{_settings.ForceProfile}' not found; auto-selecting instead");
            }

            _profile = _profile ?? GameProfile.Select(_profiles, productName, processName);

            if (_profile == null)
            {
                _profile = new GameProfile { Name = "fallback (physics only)" };
                _settings.LogWarning("no profiles loaded; falling back to engine-derived channels only");
            }

            _settings.LogInfo($"game '{productName}' / process '{processName}' -> profile '{_profile.Name}' " +
                              $"({_profile.Channels.Count} channels, {_profile.WheelChannels.Count} wheel channels)");

            foreach (var warning in _profile.Warnings) _settings.LogWarning(warning);
        }

        private static string SafeProductName()
        {
            try { return Application.productName ?? ""; } catch { return ""; }
        }

        private static string SafeProcessName()
        {
            try { return Process.GetCurrentProcess().ProcessName ?? ""; } catch { return ""; }
        }

        public void FixedUpdate()
        {
            if (_settings == null || !_settings.Enabled || !_locator.HasVehicle) return;
            _sampler.SampleFixed(_locator.Body, _locator.Transform, Time.fixedDeltaTime);
        }

        public void Update()
        {
            if (_settings == null || !_settings.Enabled) return;

            var now = Time.unscaledTime;
            var hadVehicle = _locator.HasVehicle;

            _locator.Update(_profile, now, _settings.VehicleSearchIntervalSeconds);

            // A new car means the acceleration filter's history belongs to the old one.
            if (_locator.HasVehicle && !hadVehicle) _sampler.Reset();

            PollDumpKey();
            if (_dumpRequested)
            {
                _dumpRequested = false;
                DumpVehicle();
            }

            if (now < _nextSendTime) return;
            var period = _settings.SendRateHz > 0f ? 1f / _settings.SendRateHz : 1f / 60f;
            _nextSendTime = now + period;

            SendFrame();
        }

        private void PollDumpKey()
        {
            if (string.IsNullOrEmpty(_settings.DumpKey)) return;

            try
            {
                if (Enum.TryParse<KeyCode>(_settings.DumpKey, true, out var key) && Input.GetKeyDown(key))
                    _dumpRequested = true;
            }
            catch
            {
                // Games on the new Input System can throw from legacy Input. Stop asking.
                _settings.DumpKey = "";
            }
        }

        private void SendFrame()
        {
            _frame.Reset(++_sequence, _clock.Elapsed.TotalMilliseconds, _locator.HasVehicle);
            _frame.Set(Channels.GameName, SafeProductName());
            _frame.Set(Channels.ProfileName, _profile.Name);

            if (_locator.HasVehicle)
            {
                _sampler.Fill(_frame, _locator.Body, _locator.Transform);
                FillProfileChannels();
            }

            _sender.Send(_frame);
        }

        private void FillProfileChannels()
        {
            foreach (var entry in _profile.Channels)
            {
                var root = _locator.GetComponentByTypeName(entry.Value.ComponentTypeName);
                if (root != null && entry.Value.TryEvaluate(root, out var value))
                {
                    _frame.Set(entry.Key, value);
                    continue;
                }

                ReportUnresolved(entry.Key, entry.Value, root == null);
            }

            foreach (var entry in _profile.WheelChannels)
            {
                var root = _locator.GetComponentByTypeName(entry.Value.ComponentTypeName);
                if (root == null)
                {
                    ReportUnresolved(entry.Key + "[]", entry.Value, true);
                    continue;
                }

                var resolvedAny = false;
                for (var wheel = 0; wheel < Channels.WheelSuffixes.Length; wheel++)
                {
                    if (!entry.Value.TryEvaluateAt(root, wheel, out var value)) continue;
                    _frame.Set(entry.Key + Channels.WheelSuffixes[wheel], value);
                    resolvedAny = true;
                }

                if (!resolvedAny) ReportUnresolved(entry.Key + "[]", entry.Value, false);
            }
        }

        private void ReportUnresolved(string channel, MemberPath path, bool missingComponent)
        {
            if (!_settings.LogUnresolvedChannels) return;
            if (!_reportedUnresolved.Add(channel)) return;

            var reason = missingComponent
                ? $"component '{path.ComponentTypeName}' not found on the car"
                : "path did not resolve to a number";

            _settings.LogWarning($"channel '{channel}' unresolved: {reason} (path: {path.Raw}). " +
                                 $"Press {_settings.DumpKey} in-game to list what is actually there.");
        }

        /// <summary>
        /// Prints every component on the located car and each of its numeric members with
        /// its current value. This is how you write a profile for a new CarX build: sit in
        /// a car at a known state (say, 3000rpm in 2nd), press the key, and read off the
        /// fields whose values match what the HUD says.
        /// </summary>
        public void DumpVehicle()
        {
            if (!_locator.HasVehicle)
            {
                _settings.LogInfo("dump requested but no vehicle is currently located");
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var lines = new List<string> { $"=== vehicle dump: {_locator.Body.name} (matched via {_locator.MatchedComponent}) ===" };

            foreach (var component in _locator.Body.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;

                var type = component.GetType();
                // Engine types are already covered by PhysicsSampler and only add noise.
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;

                var members = new List<string>();

                foreach (var field in type.GetFields(flags))
                {
                    if (!IsInterestingType(field.FieldType)) continue;
                    try { members.Add($"    {type.Name}.{field.Name} = {field.GetValue(component)}"); }
                    catch { /* a field that throws on read is not a telemetry source */ }
                }

                foreach (var property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    if (!IsInterestingType(property.PropertyType)) continue;
                    try { members.Add($"    {type.Name}.{property.Name} = {property.GetValue(component, null)}"); }
                    catch { /* likewise */ }
                }

                if (members.Count == 0) continue;

                lines.Add($"  [{type.FullName}]");
                lines.AddRange(members);
            }

            lines.Add("=== end dump ===");
            foreach (var line in lines) _settings.LogInfo(line);
        }

        private static bool IsInterestingType(Type type)
        {
            if (type == typeof(float) || type == typeof(double) || type == typeof(int) ||
                type == typeof(bool) || type == typeof(long) || type == typeof(short) ||
                type == typeof(byte) || type == typeof(uint)) return true;
            return type.IsEnum;
        }

        public void OnDestroy()
        {
            _sender?.Dispose();
            _sender = null;
        }
    }
}
