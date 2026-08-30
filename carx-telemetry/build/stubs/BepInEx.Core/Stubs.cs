// Compile-only stubs for the BepInEx 5 API surface used by CarX.Telemetry.Mod.
// See ../README.md. Every member throws; nothing here ever runs.

using System;
using UnityEngine;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version) { }
    }

    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        protected Logging.ManualLogSource Logger => throw new NotImplementedException();
        public Configuration.ConfigFile Config => throw new NotImplementedException();
    }
}

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogInfo(object data) => throw new NotImplementedException();
        public void LogWarning(object data) => throw new NotImplementedException();
        public void LogError(object data) => throw new NotImplementedException();
    }
}

namespace BepInEx.Configuration
{
    public abstract class AcceptableValueBase
    {
        protected AcceptableValueBase(Type valueType) { }
    }

    public class AcceptableValueRange<T> : AcceptableValueBase where T : IComparable
    {
        public AcceptableValueRange(T minValue, T maxValue) : base(typeof(T)) { }
    }

    public class ConfigDescription
    {
        public ConfigDescription(string description, AcceptableValueBase acceptableValues = null,
            params object[] tags) { }
    }

    public sealed class ConfigEntry<T>
    {
        public T Value
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
    }

    public class ConfigFile
    {
        public string ConfigFilePath => throw new NotImplementedException();

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = null)
            => throw new NotImplementedException();

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription configDescription)
            => throw new NotImplementedException();
    }
}
