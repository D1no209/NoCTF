using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.Penetration;

public class PenetrationModule : IPluginModule
{
    public string Name => "NoCTF.Plugins.Penetration";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<PenetrationTopologyService>();
        services.AddScoped<PenetrationFlagService>();
        services.AddScoped<PenetrationComposeBuilder>();
        services.AddScoped<PenetrationInstanceService>();
        services.AddScoped<PenetrationInstanceMaintenanceService>();
        services.AddScoped<IInstanceMaintenanceService>(sp => sp.GetRequiredService<PenetrationInstanceMaintenanceService>());

        services.AddScoped<IChallengeSubmissionHandler, PenetrationSubmissionHandler>();
        services.AddScoped<IChallengeFeatureProvider, PenetrationFeatureProvider>();
        services.AddScoped<IChallengeAdminFeatureProvider, PenetrationAdminFeatureProvider>();
        services.AddScoped<IScoringProfileContributor, PenetrationScoringProfileContributor>();
        services.AddScoped<IScoringStrategy, PenetrationStageScoringStrategy>();
        services.AddScoped<IScoringStrategy, PenetrationBloodBonusStrategy>();
    }
}
