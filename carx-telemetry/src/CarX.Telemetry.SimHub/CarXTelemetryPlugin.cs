using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GameReaderCommon;
using SimHub.Plugins;

namespace CarX.Telemetry.Receiver
{
    /// <summary>
    /// SimHub side of the bridge. Listens for the mod's UDP frames and republishes every
    /// channel as a SimHub property, so dashboards, ShakeIt effects and motion profiles
    /// can bind to them.
    ///
    /// Properties appear as <c>CarXTelemetryPlugin.&lt;Channel&gt;</c> -- for example
    /// <c>CarXTelemetryPlugin.SpeedKph</c> or <c>CarXTelemetryPlugin.SlipAngle</c>.
    /// Channels are published as they are first seen, so adding one to a game profile
    /// needs no change here.
    /// </summary>
    [PluginDescription("Receives CarX telemetry over UDP from the CarX Telemetry Bridge mod.")]
    [PluginAuthor("CarX Telemetry Bridge")]
    [PluginName("CarX Telemetry")]
    public class CarXTelemetryPlugin : IPlugin, IDataPlugin
    {
        private const int DefaultPort = 20777;

        /// <summary>How long the last frame stays valid before the plugin reports no game.</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

        private readonly UdpJsonReceiver _receiver = new UdpJsonReceiver();

        /// <summary>
        /// Channels that have been declared to SimHub. Properties must be added before
        /// they can be set, and re-adding one on every frame is wasteful, so this tracks
        /// what already exists.
        /// </summary>
        private readonly HashSet<string> _publishedNumbers = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _publishedStrings = new HashSet<string>(StringComparer.Ordinal);

        private long _lastPublishedSequence = -1;
        private bool _wasConnected;

        public PluginManager PluginManager { get; set; }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;

            var port = ReadConfiguredPort();

            pluginManager.AddProperty("Connected", GetType(), false);
            pluginManager.AddProperty("ListenPort", GetType(), port);
            pluginManager.AddProperty("PacketsReceived", GetType(), 0L);
            pluginManager.AddProperty("PacketsLost", GetType(), 0L);
            pluginManager.AddProperty("PacketsRejected", GetType(), 0L);
            pluginManager.AddProperty("Status", GetType(), "starting");

            _receiver.Start(port);

            if (_receiver.LastError != null)
                SimHub.Logging.Current.Warn("[CarX Telemetry] " + _receiver.LastError);
            else
                SimHub.Logging.Current.Info($"[CarX Telemetry] listening on UDP {port}");
        }

        /// <summary>
        /// Reads the listen port from CarXTelemetry.config next to the plugin DLL. A plain
        /// text file rather than a settings UI so that the plugin has no WPF dependency and
        /// stays loadable on any SimHub build.
        /// </summary>
        private int ReadConfiguredPort()
        {
            try
            {
                var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (directory == null) return DefaultPort;

                var path = Path.Combine(directory, "CarXTelemetry.config");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "# UDP port the CarX Telemetry Bridge mod sends to." + Environment.NewLine +
                        "# Must match the Port setting in the game's BepInEx config." + Environment.NewLine +
                        "port = " + DefaultPort + Environment.NewLine);
                    return DefaultPort;
                }

                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    var equals = line.IndexOf('=');
                    if (equals <= 0) continue;
                    if (!line.Substring(0, equals).Trim().Equals("port", StringComparison.OrdinalIgnoreCase)) continue;

                    if (int.TryParse(line.Substring(equals + 1).Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var port) && port > 0 && port <= 65535)
                        return port;
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[CarX Telemetry] could not read config, using default port: " + ex.Message);
            }

            return DefaultPort;
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            var snapshot = _receiver.Latest;
            var connected = snapshot != null && (DateTime.UtcNow - snapshot.ReceivedUtc) < StaleAfter;

            pluginManager.SetPropertyValue("Connected", GetType(), connected);
            pluginManager.SetPropertyValue("PacketsReceived", GetType(), _receiver.PacketsReceived);
            pluginManager.SetPropertyValue("PacketsLost", GetType(), _receiver.PacketsLost);
            pluginManager.SetPropertyValue("PacketsRejected", GetType(), _receiver.PacketsRejected);

            // Publish into SimHub's OWN telemetry fields, not just plugin properties.
            // This is what makes CarX behave like a supported game: built-in ShakeIt
            // effects, motion profiles and dashboards all read StatusDataBase, so binding
            // them by hand to CarXTelemetryPlugin.* properties should not be necessary.
            // Done every tick rather than only on a new frame, because SimHub repopulates
            // this structure continuously and a stale write would be overwritten.
            if (connected) PublishAsGame(data, snapshot);

            if (connected != _wasConnected)
            {
                _wasConnected = connected;
                pluginManager.SetPropertyValue("Status", GetType(),
                    connected ? "receiving" : (_receiver.LastError ?? "waiting for game"));
                SimHub.Logging.Current.Info("[CarX Telemetry] " + (connected ? "stream started" : "stream stopped"));
            }

            if (!connected)
            {
                // Zero the channels rather than leaving the last value frozen on screen --
                // a dash stuck at 6000rpm after you quit the game looks like a live reading.
                foreach (var name in _publishedNumbers) pluginManager.SetPropertyValue(name, GetType(), 0d);
                _lastPublishedSequence = -1;
                return;
            }

            // SimHub calls this at 60Hz and the game sends at its own rate; skip the work
            // when the same frame comes round twice.
            if (snapshot.Sequence == _lastPublishedSequence) return;
            _lastPublishedSequence = snapshot.Sequence;

            foreach (var entry in snapshot.Numbers)
            {
                if (_publishedNumbers.Add(entry.Key)) pluginManager.AddProperty(entry.Key, GetType(), 0d);
                pluginManager.SetPropertyValue(entry.Key, GetType(), entry.Value);
            }

            foreach (var entry in snapshot.Strings)
            {
                if (_publishedStrings.Add(entry.Key)) pluginManager.AddProperty(entry.Key, GetType(), "");
                pluginManager.SetPropertyValue(entry.Key, GetType(), entry.Value);
            }
        }

        /// <summary>
        /// Maps our channels onto SimHub's standard telemetry so that anything reading
        /// normal game data -- built-in ShakeIt effects, motion profiles, dashboards --
        /// sees CarX as a running game and needs no hand-bound properties.
        ///
        /// SimHub declares these setters <c>internal</c>, so they are reached through
        /// cached delegates rather than direct assignment. That is deliberately the only
        /// non-public API this plugin touches, and it is the reason a SimHub update could
        /// break this path: if a name changes, the setter simply resolves to null and the
        /// field is skipped rather than throwing. The plugin's own
        /// CarXTelemetryPlugin.* properties keep working regardless.
        /// </summary>
        private static class Native
        {
            private static Action<T1, T2> Setter<T1, T2>(string property)
            {
                var info = typeof(T1).GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                var setter = info?.GetSetMethod(true);
                if (setter == null) return null;
                return (Action<T1, T2>)Delegate.CreateDelegate(typeof(Action<T1, T2>), setter, false);
            }

            internal static readonly Action<GameData, bool> GameRunning = Setter<GameData, bool>("GameRunning");
            internal static readonly Action<GameData, string> GameName = Setter<GameData, string>("GameName");

            internal static readonly Action<StatusDataBase, double> Rpms = Setter<StatusDataBase, double>("Rpms");
            internal static readonly Action<StatusDataBase, double> MaxRpm = Setter<StatusDataBase, double>("MaxRpm");
            internal static readonly Action<StatusDataBase, double> MaxRpmSetting = Setter<StatusDataBase, double>("CarSettings_MaxRPM");
            internal static readonly Action<StatusDataBase, double> RedLine = Setter<StatusDataBase, double>("CarSettings_RedLineRPM");
            internal static readonly Action<StatusDataBase, double> SpeedKmh = Setter<StatusDataBase, double>("SpeedKmh");
            internal static readonly Action<StatusDataBase, double> SpeedMph = Setter<StatusDataBase, double>("SpeedMph");
            internal static readonly Action<StatusDataBase, double> SpeedLocal = Setter<StatusDataBase, double>("SpeedLocal");
            internal static readonly Action<StatusDataBase, double> Throttle = Setter<StatusDataBase, double>("Throttle");
            internal static readonly Action<StatusDataBase, double> Brake = Setter<StatusDataBase, double>("Brake");
            internal static readonly Action<StatusDataBase, double> Clutch = Setter<StatusDataBase, double>("Clutch");
            internal static readonly Action<StatusDataBase, double> Handbrake = Setter<StatusDataBase, double>("Handbrake");
            internal static readonly Action<StatusDataBase, string> Gear = Setter<StatusDataBase, string>("Gear");
            internal static readonly Action<StatusDataBase, string> CarModel = Setter<StatusDataBase, string>("CarModel");
            internal static readonly Action<StatusDataBase, string> TrackName = Setter<StatusDataBase, string>("TrackName");
            internal static readonly Action<StatusDataBase, double?> Sway = Setter<StatusDataBase, double?>("AccelerationSway");
            internal static readonly Action<StatusDataBase, double?> Surge = Setter<StatusDataBase, double?>("AccelerationSurge");
            internal static readonly Action<StatusDataBase, double?> Heave = Setter<StatusDataBase, double?>("AccelerationHeave");
        }

        private void PublishAsGame(GameData data, Snapshot snapshot)
        {
            Native.GameRunning?.Invoke(data, true);

            var gameName = snapshot.Strings.TryGetValue("GameName", out var reported) && !string.IsNullOrEmpty(reported)
                ? reported
                : "CarX Drift Racing Online";
            Native.GameName?.Invoke(data, gameName);

            var s = data.NewData;
            if (s == null) return;

            double Value(string key) => snapshot.Numbers.TryGetValue(key, out var v) ? v : 0d;
            bool Has(string key) => snapshot.Numbers.ContainsKey(key);

            if (Has("Rpm")) Native.Rpms?.Invoke(s, Value("Rpm"));

            if (Has("MaxRpm"))
            {
                var max = Value("MaxRpm");
                Native.MaxRpm?.Invoke(s, max);
                Native.MaxRpmSetting?.Invoke(s, max);
                Native.RedLine?.Invoke(s, max);
            }

            if (Has("SpeedKph"))
            {
                var kmh = Value("SpeedKph");
                Native.SpeedKmh?.Invoke(s, kmh);
                Native.SpeedLocal?.Invoke(s, kmh);
                Native.SpeedMph?.Invoke(s, Has("SpeedMph") ? Value("SpeedMph") : kmh * 0.621371);
            }

            // SimHub expresses pedals as 0-100, the wire protocol as 0-1.
            if (Has("Throttle")) Native.Throttle?.Invoke(s, Value("Throttle") * 100d);
            if (Has("Brake")) Native.Brake?.Invoke(s, Value("Brake") * 100d);
            if (Has("Clutch")) Native.Clutch?.Invoke(s, Value("Clutch") * 100d);
            if (Has("Handbrake")) Native.Handbrake?.Invoke(s, Value("Handbrake") * 100d);

            // Gear is text in SimHub: R, N, then the number.
            if (Has("Gear"))
            {
                var g = (int)Math.Round(Value("Gear"));
                Native.Gear?.Invoke(s, g < 0 ? "R" : g == 0 ? "N" : g.ToString(CultureInfo.InvariantCulture));
            }

            // The channels ShakeIt and every motion rig actually consume.
            if (Has("AccelSwayG")) Native.Sway?.Invoke(s, Value("AccelSwayG"));
            if (Has("AccelSurgeG")) Native.Surge?.Invoke(s, Value("AccelSurgeG"));
            if (Has("AccelHeaveG")) Native.Heave?.Invoke(s, Value("AccelHeaveG"));

            if (snapshot.Strings.TryGetValue("CarName", out var car) && !string.IsNullOrEmpty(car))
                Native.CarModel?.Invoke(s, car);
            if (snapshot.Strings.TryGetValue("TrackName", out var track) && !string.IsNullOrEmpty(track))
                Native.TrackName?.Invoke(s, track);
        }

        public void End(PluginManager pluginManager)
        {
            _receiver.Dispose();
        }
    }
}
