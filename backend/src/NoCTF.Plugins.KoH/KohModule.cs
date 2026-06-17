using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Plugin module for the KoH game mode. Registers all KoH-related services.
/// </summary>
public class KohModule : IPluginModule
{
    public string Name => "NoCTF.Plugins.KoH";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        // Game mode (scoped to match DbContext lifetime)
        services.AddScoped<KohGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<KohGameMode>());
        services.AddScoped<ICompetitionModeProvider, KohModeProvider>();

        // Score engine (scoped — uses DbContext)
        services.AddScoped<KohScoreEngine>();

        // HTTP client for agent polling
        services.AddHttpClient<KohAgentClient>();

        // Poll engine (singleton background service)
        services.AddSingleton<KohPollEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<KohPollEngine>());
    }
}
