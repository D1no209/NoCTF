using System.Reflection;
using System.Runtime.Loader;

namespace NoCTF.API.Plugins;

public class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: false)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Do NOT load NoCTF.PluginBase or other shared assemblies into separate context
        // to avoid type mismatch. Let them fall back to default context.
        var existing = Default.Assemblies.FirstOrDefault(a => a.FullName == assemblyName.FullName);
        if (existing != null) return existing;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
            return LoadFromAssemblyPath(path);

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path != null)
            return LoadUnmanagedDllFromPath(path);
        return IntPtr.Zero;
    }
}
