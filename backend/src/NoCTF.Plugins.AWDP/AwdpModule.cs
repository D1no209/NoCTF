using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

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
        services.AddScoped<AwdpConfigResolver>();
        services.AddScoped<AwdpStateService>();
        services.AddScoped<AwdpScoreEngine>();
        services.AddScoped<AwdpGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<AwdpGameMode>());
        services.AddScoped<ICompetitionModeProvider, AwdpModeProvider>();
        services.AddScoped<ICompetitionFileActionProvider, AwdpModeProvider>();
        services.AddSingleton<AwdpRoundEngine>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AwdpRoundEngine>());

        services.AddScoped<AwdpPatchService>();
        services.AddScoped<IAwdpPatchService>(sp => sp.GetRequiredService<AwdpPatchService>());
        services.AddScoped<ICompetitionJobHandler, AwdpPatchValidationJobHandler>();
    }
}
