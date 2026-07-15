using System.Reflection;
using System.Runtime.Loader;

namespace NoCTF.Application.Plugins;

/// <summary>
/// Loads a plugin and its private dependencies while sharing host contracts
/// from the default context. Sharing the contracts prevents otherwise
/// identical interfaces from becoming incompatible across load contexts.
/// </summary>
public class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: false)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var existing = Default.Assemblies.FirstOrDefault(
            assembly => assembly.FullName == assemblyName.FullName);
        if (existing is not null)
            return existing;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
