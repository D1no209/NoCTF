using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Security;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

/// <summary>
/// Plugin module for the AWDP game mode. Registers all AWDP-related services.
/// </summary>
public class AwdpModule : IHostAwarePluginModule
{
    public string Name => "NoCTF.Plugins.AWDP";
    public string Version => "1.0.1";

    public void ConfigureServices(IServiceCollection services)
    {
        ConfigureRuntimeServices(services);
        ConfigureApiSurface(services);
        ConfigureWorkerHost(services);
    }

    public void ConfigureServices(IServiceCollection services, PluginHostRole hostRole)
    {
        if (hostRole == PluginHostRole.Api)
        {
            ConfigureRuntimeServices(services);
            ConfigureApiSurface(services);
        }
        else
        {
            ConfigureRuntimeServices(services);
            ConfigureWorkerHost(services);
        }
    }

    private static void ConfigureApiSurface(IServiceCollection services)
    {
        services.AddScoped<AwdpGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<AwdpGameMode>());
        services.AddScoped<ICompetitionModeProvider, AwdpModeProvider>();
        services.AddScoped<ICompetitionFileActionProvider, AwdpModeProvider>();
    }

    private static void ConfigureWorkerHost(IServiceCollection services)
    {
        services.AddSingleton<AwdpRoundEngine>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AwdpRoundEngine>());
        services.AddScoped<ICompetitionJobHandler, AwdpPatchValidationJobHandler>();
        services.AddScoped<ICompetitionJobHandler, AwdpContainerCleanupJobHandler>();
    }

    private static void ConfigureRuntimeServices(IServiceCollection services)
    {
        services.AddScoped<IPatchArchiveValidator, PatchArchiveValidator>();
        services.AddScoped<AwdpConfigResolver>();
        services.AddScoped<AwdpStateService>();
        services.AddScoped<AwdpScoreEngine>();
        services.AddScoped<AwdpPatchService>();
        services.AddScoped<IAwdpPatchService>(sp => sp.GetRequiredService<AwdpPatchService>());
    }
}
