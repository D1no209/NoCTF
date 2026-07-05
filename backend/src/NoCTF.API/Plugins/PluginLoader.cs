using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;

namespace NoCTF.API.Plugins;

public static class PluginLoader
{
    private static readonly HashSet<string> BuiltInPluginAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "NoCTF.Plugins.CTF.dll",
        "NoCTF.Plugins.AWD.dll",
        "NoCTF.Plugins.AWDP.dll",
        "NoCTF.Plugins.KoH.dll",
        "NoCTF.Plugins.Penetration.dll"
    };

    public static void LoadAndRegisterAll(IServiceCollection services, IConfiguration configuration)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            Console.WriteLine($"[Plugins] Directory not found: {pluginsDir}");
            return;
        }

        var dlls = Directory.GetFiles(pluginsDir, "NoCTF.Plugins.*.dll");
        var allowedExternal = configuration.GetSection("Plugins:AllowedAssemblies").Get<string[]>() ?? [];
        var allowedExternalSet = allowedExternal.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowAllExternal = configuration.GetValue("Plugins:AllowUnlistedExternalPlugins", false);

        foreach (var dll in dlls)
        {
            try
            {
                var fileName = Path.GetFileName(dll);
                if (!BuiltInPluginAssemblies.Contains(fileName) &&
                    !allowAllExternal &&
                    !allowedExternalSet.Contains(fileName))
                {
                    Console.WriteLine($"[Plugins] Skipped unlisted external plugin {fileName}");
                    continue;
                }

                var context = new PluginLoadContext(dll);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));

                var moduleType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPluginModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (moduleType == null)
                {
                    Console.WriteLine($"[Plugins] No IPluginModule found in {Path.GetFileName(dll)}");
                    continue;
                }

                if (Activator.CreateInstance(moduleType) is IPluginModule module)
                {
                    module.ConfigureServices(services);
                    Console.WriteLine($"[Plugins] Loaded module from {Path.GetFileName(dll)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugins] Failed to load {Path.GetFileName(dll)}: {ex.Message}");
            }
        }
    }
}
