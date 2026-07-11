using NoCTF.API.Plugins;

namespace NoCTF.Tests;

public class PluginLoaderTests
{
    private static readonly string[] BuiltInPluginPaths =
    [
        "plugins/NoCTF.Plugins.CTF.dll",
        "plugins/NoCTF.Plugins.AWD.dll",
        "plugins/NoCTF.Plugins.AWDP.dll",
        "plugins/NoCTF.Plugins.KoH.dll",
        "plugins/NoCTF.Plugins.Penetration.dll"
    ];

    [Fact]
    public void EnsureBuiltInPluginsPresent_AcceptsCompleteSet()
    {
        PluginLoader.EnsureBuiltInPluginsPresent(BuiltInPluginPaths);
    }

    [Fact]
    public void EnsureBuiltInPluginsPresent_RejectsMissingPlugin()
    {
        var paths = BuiltInPluginPaths
            .Where(path => !path.EndsWith("NoCTF.Plugins.KoH.dll", StringComparison.Ordinal))
            .ToArray();

        var exception = Assert.Throws<InvalidOperationException>(
            () => PluginLoader.EnsureBuiltInPluginsPresent(paths));

        Assert.Contains("NoCTF.Plugins.KoH.dll", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureBuiltInPluginsPresent_UsesFileNamesCaseInsensitively()
    {
        var paths = BuiltInPluginPaths
            .Select(path => path.ToUpperInvariant())
            .Concat(["plugins/NoCTF.Plugins.External.dll"]);

        PluginLoader.EnsureBuiltInPluginsPresent(paths);
    }
}
