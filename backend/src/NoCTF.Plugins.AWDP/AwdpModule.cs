using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWD;

namespace NoCTF.Plugins.AWDP;

/// <summary>
/// Plugin module for the AWDP game mode. Registers all AWDP-related services.
/// </summary>
public class AwdpModule : IPluginModule
{
    public string Name => "NoCTF.Plugins.AWDP";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        // AWD base services (AwdpGameMode delegates to AwdGameMode)
        services.AddScoped<AwdGameMode>();

        // AWDP game mode
        services.AddScoped<AwdpGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<AwdpGameMode>());

        // Patch service
        services.AddScoped<AwdpPatchService>();
        services.AddScoped<IAwdpPatchService>(sp => sp.GetRequiredService<AwdpPatchService>());
    }
}
