using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// Plugin module for the CTF game mode. Registers all CTF-related services.
/// </summary>
public class CtfModule : IHostAwarePluginModule
{
    public string Name => "NoCTF.Plugins.CTF";
    public string Version => "1.0.1";

    public void ConfigureServices(IServiceCollection services)
        => ConfigureApiServices(services);

    public void ConfigureServices(IServiceCollection services, PluginHostRole hostRole)
    {
        if (hostRole == PluginHostRole.Api)
            ConfigureApiServices(services);
    }

    private static void ConfigureApiServices(IServiceCollection services)
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
