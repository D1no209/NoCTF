using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// Plugin module for the CTF game mode. Registers all CTF-related services.
/// </summary>
public class CtfModule : IPluginModule
{
    public string Name => "NoCTF.Plugins.CTF";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        // Core CTF services
        services.AddSingleton<DynamicScoringCalculator>();

        // Game mode (scoped to match DbContext lifetime)
        services.AddScoped<CtfGameMode>();
        services.AddScoped<IGameMode>(sp => sp.GetRequiredService<CtfGameMode>());
        services.AddScoped<ICompetitionModeProvider, CtfModeProvider>();

        // Challenge types (singleton — stateless)
        services.AddSingleton<IChallengeType, StaticFlagChallengeType>();
        services.AddSingleton<IChallengeType, DynamicFlagChallengeType>();
    }
}
