using System.Reflection;

namespace DwgShadow.Core
{
    /// <summary>Plug-in version taken from the assembly (set by &lt;Version&gt; in the project file).</summary>
    public static class PluginInfo
    {
        public static string Version { get; } =
            typeof(PluginInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(PluginInfo).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }
}
