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

        public void End(PluginManager pluginManager)
        {
            _receiver.Dispose();
        }
    }
}
