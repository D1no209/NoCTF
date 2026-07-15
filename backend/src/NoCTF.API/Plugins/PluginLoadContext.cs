namespace NoCTF.API.Plugins;

/// <summary>
/// Compatibility type for consumers of the former API-local load context.
/// </summary>
public class PluginLoadContext(string pluginPath)
    : NoCTF.Application.Plugins.PluginLoadContext(pluginPath);
