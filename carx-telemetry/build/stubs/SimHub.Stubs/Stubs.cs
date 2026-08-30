// Compile-only stubs for the SimHub plugin API used by CarX.Telemetry.SimHub.
// See ../README.md. Every member throws.
//
// SimHub publishes no plugin documentation and ships no NuGet package -- these
// signatures were written from the shapes the real plugin SDK is known to use, so treat
// a green stub build here as "my code is consistent", not "the API matches". The real
// build against SimHub.Plugins.dll is the authority.

using System;

namespace GameReaderCommon
{
    public class GameData
    {
        public string GameName => throw new NotImplementedException();
        public bool GameRunning => throw new NotImplementedException();
    }
}

namespace SimHub
{
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Debug(string message);
    }

    public static class Logging
    {
        public static ILogger Current => throw new NotImplementedException();
    }
}

namespace SimHub.Plugins
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PluginNameAttribute : Attribute
    {
        public PluginNameAttribute(string name) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PluginAuthorAttribute : Attribute
    {
        public PluginAuthorAttribute(string author) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PluginDescriptionAttribute : Attribute
    {
        public PluginDescriptionAttribute(string description) { }
    }

    public class PluginManager
    {
        public void AddProperty(string propertyName, Type pluginType, object defaultValue)
            => throw new NotImplementedException();

        public void SetPropertyValue(string propertyName, Type pluginType, object value)
            => throw new NotImplementedException();
    }

    public interface IPlugin
    {
        PluginManager PluginManager { get; set; }
        void Init(PluginManager pluginManager);
        void End(PluginManager pluginManager);
    }

    public interface IDataPlugin : IPlugin
    {
        void DataUpdate(PluginManager pluginManager, ref GameReaderCommon.GameData data);
    }
}
