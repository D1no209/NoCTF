using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;
using System.Security.Cryptography;

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
            throw new InvalidOperationException($"Required plugins directory was not found: {pluginsDir}");

        var dlls = Directory.GetFiles(pluginsDir, "NoCTF.Plugins.*.dll");
        EnsureBuiltInPluginsPresent(dlls);
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

                if (!BuiltInPluginAssemblies.Contains(fileName))
                {
                    var expectedDigest = configuration[$"Plugins:AssemblySha256:{fileName}"];
                    if (string.IsNullOrWhiteSpace(expectedDigest) || !HasExpectedDigest(dll, expectedDigest))
                    {
                        Console.WriteLine($"[Plugins] Skipped external plugin {fileName}: SHA-256 digest is missing or invalid.");
                        continue;
                    }
                }

                var context = new PluginLoadContext(dll);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));

                var moduleType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPluginModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (moduleType == null)
                {
                    if (BuiltInPluginAssemblies.Contains(fileName))
                        throw new InvalidOperationException($"Built-in plugin {fileName} does not expose an IPluginModule.");
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
                if (BuiltInPluginAssemblies.Contains(Path.GetFileName(dll)))
                    throw new InvalidOperationException(
                        $"Required built-in plugin {Path.GetFileName(dll)} failed to load.", ex);
            }
        }
    }

    internal static void EnsureBuiltInPluginsPresent(IEnumerable<string> pluginPaths)
    {
        var available = pluginPaths
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = BuiltInPluginAssemblies
            .Where(fileName => !available.Contains(fileName))
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Required built-in plugins are missing: {string.Join(", ", missing)}");
        }
    }

    private static bool HasExpectedDigest(string path, string expectedDigest)
    {
        var expected = expectedDigest.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (expected.Length != 64)
            return false;

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected.ToUpperInvariant());
        var actualBytes = System.Text.Encoding.ASCII.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
