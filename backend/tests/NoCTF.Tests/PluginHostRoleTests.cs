using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Plugins;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class PluginHostRoleTests
{
    [Fact]
    public void ApiRole_LoadsCompleteCatalogWithoutBuiltInRoundEngines()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var catalog = PluginLoader.LoadAndRegisterAll(
            services,
            configuration,
            PluginHostRole.Api);

        Assert.Equal(5, catalog.Plugins.Count);
        Assert.All(catalog.Plugins, plugin => Assert.True(plugin.IsHostAware));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void HostAwareContract_DefaultsToLegacyRegistration()
    {
        var implementation = new DefaultCompatibleModule();
        IHostAwarePluginModule module = implementation;

        module.ConfigureServices(new ServiceCollection(), PluginHostRole.Worker);

        Assert.Equal(1, implementation.ConfigureCount);
    }

    [Fact]
    public void SharedLoader_UsesLegacyRegistrationForExternalModules()
    {
        var module = new LegacyModule();

        PluginLoader.ConfigureModule(
            new ServiceCollection(),
            module,
            PluginHostRole.Worker);

        Assert.Equal(1, module.ConfigureCount);
    }

    private sealed class DefaultCompatibleModule : IHostAwarePluginModule
    {
        public string Name => "test";
        public string Version => "1.0.0";
        public int ConfigureCount { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            ConfigureCount++;
        }
    }

    private sealed class LegacyModule : IPluginModule
    {
        public string Name => "legacy-test";
        public string Version => "1.0.0";
        public int ConfigureCount { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            ConfigureCount++;
        }
    }
}
