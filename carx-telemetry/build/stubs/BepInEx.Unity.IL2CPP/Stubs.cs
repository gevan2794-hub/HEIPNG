// Compile-only stubs for the BepInEx 6 (IL2CPP) API surface, plus the Il2CppInterop
// class-injection entry points. See ../README.md. Every member throws.
//
// This project also carries the BepInEx 5 types, because the IL2CPP flavour of the mod
// still uses BepInPlugin, ConfigFile and friends -- in a real IL2CPP build those come
// from BepInEx.Core alongside BepInEx.Unity.IL2CPP, but a single stub assembly is
// simpler here and the mod source cannot tell the difference.

using System;
using UnityEngine;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version) { }
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

namespace BepInEx.Unity.IL2CPP
{
    public abstract class BasePlugin
    {
        public Logging.ManualLogSource Log => throw new NotImplementedException();
        public Configuration.ConfigFile Config => throw new NotImplementedException();
        public abstract void Load();
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static void RegisterTypeInIl2Cpp<T>() where T : class => throw new NotImplementedException();
        public static IntPtr DerivedConstructorPointer<T>() => throw new NotImplementedException();
        public static void DerivedConstructorBody(object objectBase) => throw new NotImplementedException();
    }
}
