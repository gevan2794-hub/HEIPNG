using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// A per-game binding file: which object is the player's car, and where the
    /// powertrain/input numbers live on it. Profiles are plain text loaded at runtime, so
    /// supporting a new CarX title -- or repairing an existing one after a patch renames a
    /// field -- is an edit in <c>BepInEx/config/CarXTelemetry/profiles/</c>, not a rebuild.
    /// </summary>
    public sealed class GameProfile
    {
        public string Name = "unnamed";
        public string SourceFile = "(built-in)";

        /// <summary>Regex matched against the Unity product name and the process name. "*" matches anything.</summary>
        public Regex Match = new Regex(".*", RegexOptions.Compiled);

        /// <summary>Priority for tie-breaks; the catch-all generic profile sits at 0.</summary>
        public int Priority;

        /// <summary>Regex a component's type name must match for its GameObject to count as a car.</summary>
        public Regex VehicleType = new Regex("(?i)(car|vehicle|drift)", RegexOptions.Compiled);

        /// <summary>Regex identifying a "this one is the local player" marker component, if the game has one.</summary>
        public Regex PlayerType;

        public int MinWheelColliders = 2;

        public readonly Dictionary<string, MemberPath> Channels = new Dictionary<string, MemberPath>(StringComparer.Ordinal);

        /// <summary>Channels declared as <c>Name[] = ...wheels[*]...</c>, expanded to FL/FR/RL/RR.</summary>
        public readonly Dictionary<string, MemberPath> WheelChannels = new Dictionary<string, MemberPath>(StringComparer.Ordinal);

        /// <summary>Parse diagnostics, surfaced in the log so a typo in a profile is findable.</summary>
        public readonly List<string> Warnings = new List<string>();

        public static GameProfile Parse(string text, string sourceName)
        {
            var profile = new GameProfile { SourceFile = sourceName };
            var section = "";
            var lineNumber = 0;

            foreach (var rawLine in text.Split('\n'))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    profile.Warnings.Add($"{sourceName}:{lineNumber}: expected 'key = value'");
                    continue;
                }

                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();

                switch (section)
                {
                    case "profile":
                        profile.ApplyProfileSetting(key, value, sourceName, lineNumber);
                        break;
                    case "locator":
                        profile.ApplyLocatorSetting(key, value, sourceName, lineNumber);
                        break;
                    case "channels":
                        profile.ApplyChannel(key, value, sourceName, lineNumber);
                        break;
                    default:
                        profile.Warnings.Add($"{sourceName}:{lineNumber}: setting outside a known section");
                        break;
                }
            }

            return profile;
        }

        public static GameProfile Load(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

        private void ApplyProfileSetting(string key, string value, string source, int line)
        {
            switch (key.ToLowerInvariant())
            {
                case "name": Name = value; break;
                case "match": Match = CompileRegex(value, source, line) ?? Match; break;
                case "priority":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority)) Priority = priority;
                    else Warnings.Add($"{source}:{line}: priority must be an integer");
                    break;
                default: Warnings.Add($"{source}:{line}: unknown [profile] key '{key}'"); break;
            }
        }

        private void ApplyLocatorSetting(string key, string value, string source, int line)
        {
            switch (key.ToLowerInvariant())
            {
                case "vehicletype": VehicleType = CompileRegex(value, source, line) ?? VehicleType; break;
                case "playertype": PlayerType = CompileRegex(value, source, line); break;
                case "minwheelcolliders":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) MinWheelColliders = count;
                    else Warnings.Add($"{source}:{line}: minWheelColliders must be an integer");
                    break;
                default: Warnings.Add($"{source}:{line}: unknown [locator] key '{key}'"); break;
            }
        }

        private void ApplyChannel(string key, string value, string source, int line)
        {
            var isWheelChannel = key.EndsWith("[]", StringComparison.Ordinal);
            if (isWheelChannel) key = key.Substring(0, key.Length - 2).Trim();

            if (!MemberPath.TryParse(value, out var path, out var error))
            {
                Warnings.Add($"{source}:{line}: channel '{key}': {error}");
                return;
            }

            if (isWheelChannel != path.IsWheelArray)
            {
                Warnings.Add(isWheelChannel
                    ? $"{source}:{line}: channel '{key}[]' needs a [*] wildcard in its path"
                    : $"{source}:{line}: channel '{key}' has a [*] wildcard but is not declared as '{key}[]'");
                return;
            }

            if (isWheelChannel) WheelChannels[key] = path;
            else Channels[key] = path;
        }

        private Regex CompileRegex(string pattern, string source, int line)
        {
            if (pattern == "*") pattern = ".*";
            try
            {
                return new Regex(pattern, RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                Warnings.Add($"{source}:{line}: bad regex '{pattern}': {ex.Message}");
                return null;
            }
        }

        public bool Matches(params string[] identifiers)
        {
            foreach (var identifier in identifiers)
            {
                if (!string.IsNullOrEmpty(identifier) && Match.IsMatch(identifier)) return true;
            }
            return false;
        }

        /// <summary>
        /// Picks the best profile for the running game: highest priority among those whose
        /// match expression hits, falling back to the highest-priority profile overall so
        /// there is always something to run with.
        /// </summary>
        public static GameProfile Select(IEnumerable<GameProfile> profiles, params string[] identifiers)
        {
            GameProfile best = null;
            GameProfile fallback = null;

            foreach (var profile in profiles)
            {
                if (fallback == null || profile.Priority > fallback.Priority) fallback = profile;
                if (!profile.Matches(identifiers)) continue;
                if (best == null || profile.Priority > best.Priority) best = profile;
            }

            return best ?? fallback;
        }
    }
}
