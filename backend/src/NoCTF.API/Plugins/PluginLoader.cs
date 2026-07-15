using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;

namespace NoCTF.API.Plugins;

/// <summary>
/// Compatibility facade for callers that referenced the original API-local
/// loader. Host implementations use the shared Application loader.
/// </summary>
public static class PluginLoader
{
    public static NoCTF.Application.Plugins.PluginCatalog LoadAndRegisterAll(
        IServiceCollection services,
        IConfiguration configuration,
        string? pluginsDirectory = null)
        => NoCTF.Application.Plugins.PluginLoader.LoadAndRegisterAll(
            services,
            configuration,
            pluginsDirectory);

    public static NoCTF.Application.Plugins.PluginCatalog LoadAndRegisterAll(
        IServiceCollection services,
        IConfiguration configuration,
        PluginHostRole hostRole,
        string? pluginsDirectory = null)
        => NoCTF.Application.Plugins.PluginLoader.LoadAndRegisterAll(
            services,
            configuration,
            hostRole,
            pluginsDirectory);

    internal static void EnsureBuiltInPluginsPresent(IEnumerable<string> pluginPaths)
        => NoCTF.Application.Plugins.PluginLoader.EnsureBuiltInPluginsPresent(pluginPaths);
}
