using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;

namespace NoCTF.Application.Plugins;

public sealed record LoadedPlugin(
    string Name,
    string Version,
    string AssemblyFileName,
    bool IsHostAware);

public sealed class PluginCatalog
{
    public PluginCatalog(IEnumerable<LoadedPlugin> plugins)
    {
        Plugins = plugins.ToArray();
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }
}

/// <summary>
/// Shared plugin discovery and registration used by every application host.
/// </summary>
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

    /// <summary>
    /// Compatibility entry point. Every module is composed through its legacy
    /// registration method, matching the loader behavior before host roles were
    /// introduced.
    /// </summary>
    public static PluginCatalog LoadAndRegisterAll(
        IServiceCollection services,
        IConfiguration configuration,
        string? pluginsDirectory = null)
        => LoadAndRegisterCore(services, configuration, null, pluginsDirectory);

    public static PluginCatalog LoadAndRegisterAll(
        IServiceCollection services,
        IConfiguration configuration,
        PluginHostRole hostRole,
        string? pluginsDirectory = null)
        => LoadAndRegisterCore(services, configuration, hostRole, pluginsDirectory);

    private static PluginCatalog LoadAndRegisterCore(
        IServiceCollection services,
        IConfiguration configuration,
        PluginHostRole? hostRole,
        string? pluginsDirectory)
    {
        var pluginsDir = ResolvePluginsDirectory(configuration, pluginsDirectory);
        if (!Directory.Exists(pluginsDir))
            throw new InvalidOperationException($"Required plugins directory was not found: {pluginsDir}");

        var dlls = Directory.GetFiles(pluginsDir, "NoCTF.Plugins.*.dll")
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EnsureBuiltInPluginsPresent(dlls);

        var allowedExternal = configuration.GetSection("Plugins:AllowedAssemblies").Get<string[]>() ?? [];
        var allowedExternalSet = allowedExternal.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowAllExternal = configuration.GetValue("Plugins:AllowUnlistedExternalPlugins", false);
        var loadedPlugins = new List<LoadedPlugin>(dlls.Length);

        foreach (var dll in dlls)
        {
            var fileName = Path.GetFileName(dll);
            try
            {
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
                var moduleTypes = assembly.GetTypes()
                    .Where(type =>
                        typeof(IPluginModule).IsAssignableFrom(type) &&
                        !type.IsInterface &&
                        !type.IsAbstract)
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();

                if (moduleTypes.Length == 0)
                {
                    if (BuiltInPluginAssemblies.Contains(fileName))
                        throw new InvalidOperationException($"Built-in plugin {fileName} does not expose an IPluginModule.");
                    Console.WriteLine($"[Plugins] No IPluginModule found in {fileName}");
                    continue;
                }
                if (moduleTypes.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Plugin {fileName} exposes multiple IPluginModule implementations; exactly one is required.");
                }

                var moduleType = moduleTypes[0];
                if (Activator.CreateInstance(moduleType) is not IPluginModule module)
                    throw new InvalidOperationException($"Plugin module {moduleType.FullName} could not be created.");

                ConfigureModule(services, module, hostRole);

                loadedPlugins.Add(new LoadedPlugin(
                    module.Name,
                    module.Version,
                    fileName,
                    module is IHostAwarePluginModule));
                Console.WriteLine($"[Plugins] Loaded module {module.Name} for {hostRole?.ToString() ?? "legacy"} host from {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugins] Failed to load {fileName}: {ex.Message}");
                if (BuiltInPluginAssemblies.Contains(fileName))
                {
                    throw new InvalidOperationException(
                        $"Required built-in plugin {fileName} failed to load.", ex);
                }
            }
        }

        var catalog = new PluginCatalog(loadedPlugins);
        services.AddSingleton(catalog);
        return catalog;
    }

    public static void EnsureBuiltInPluginsPresent(IEnumerable<string> pluginPaths)
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

    internal static void ConfigureModule(
        IServiceCollection services,
        IPluginModule module,
        PluginHostRole? hostRole)
    {
        if (hostRole.HasValue && module is IHostAwarePluginModule hostAwareModule)
            hostAwareModule.ConfigureServices(services, hostRole.Value);
        else
            module.ConfigureServices(services);
    }

    private static string ResolvePluginsDirectory(
        IConfiguration configuration,
        string? pluginsDirectory)
    {
        var configured = pluginsDirectory ?? configuration["Plugins:Directory"];
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(AppContext.BaseDirectory, "plugins");

        return Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, AppContext.BaseDirectory);
    }

    private static bool HasExpectedDigest(string path, string expectedDigest)
    {
        var expected = expectedDigest.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (expected.Length != 64)
            return false;

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected.ToUpperInvariant());
        var actualBytes = System.Text.Encoding.ASCII.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
