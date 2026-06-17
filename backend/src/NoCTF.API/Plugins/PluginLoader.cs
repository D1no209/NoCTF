using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;

namespace NoCTF.API.Plugins;

public static class PluginLoader
{
    public static void LoadAndRegisterAll(IServiceCollection services, IConfiguration configuration)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            Console.WriteLine($"[Plugins] Directory not found: {pluginsDir}");
            return;
        }

        var dlls = Directory.GetFiles(pluginsDir, "NoCTF.Plugins.*.dll");
        foreach (var dll in dlls)
        {
            try
            {
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
