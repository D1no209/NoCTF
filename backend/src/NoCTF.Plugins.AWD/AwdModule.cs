using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Plugin module for the AWD game mode. Registers all AWD-related services.
/// </summary>
public class AwdModule : IPluginModule
{
    public string Name => "NoCTF.Plugins.AWD";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        // Game mode (scoped to match DbContext lifetime)
        services.AddScoped<AwdGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<AwdGameMode>());

        // Flag service (scoped — uses DbContext)
        services.AddScoped<AwdFlagService>();

        // Checker service (scoped — uses DbContext)
        services.AddScoped<AwdCheckerService>();

        // Score engine (scoped — uses DbContext)
        services.AddScoped<AwdScoreEngine>();

        // Round engine (singleton background service)
        services.AddSingleton<AwdRoundEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<AwdRoundEngine>());
    }
}
