using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Plugin module for the KoH game mode. Registers all KoH-related services.
/// </summary>
public class KohModule : IHostAwarePluginModule
{
    public string Name => "NoCTF.Plugins.KoH";
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
        services.AddScoped<KohGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<KohGameMode>());
        services.AddScoped<ICompetitionModeProvider, KohModeProvider>();
    }

    private static void ConfigureWorkerHost(IServiceCollection services)
    {
        services.AddScoped<KohGameMode>();
        services.AddSingleton<KohPollEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<KohPollEngine>());
    }

    private static void ConfigureRuntimeServices(IServiceCollection services)
    {
        services.AddScoped<KohScoreEngine>();
        services.AddHttpClient<KohAgentClient>();
    }
}
