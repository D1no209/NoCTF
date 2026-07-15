using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Plugin module for the AWD game mode. Registers all AWD-related services.
/// </summary>
public class AwdModule : IHostAwarePluginModule
{
    public string Name => "NoCTF.Plugins.AWD";
    public string Version => "1.0.0";

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
        services.AddScoped<AwdGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<AwdGameMode>());
        services.AddScoped<ICompetitionModeProvider, AwdModeProvider>();
    }

    private static void ConfigureWorkerHost(IServiceCollection services)
    {
        services.AddSingleton<AwdRoundEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<AwdRoundEngine>());
    }

    private static void ConfigureRuntimeServices(IServiceCollection services)
    {
        services.AddScoped<AwdFlagService>();
        services.AddScoped<AwdChallengeRuntimeConfigProvider>();
        services.AddScoped<AwdCheckerService>();
        services.AddScoped<AwdScoreEngine>();
    }
}
