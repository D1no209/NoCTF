using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.Penetration;

public class PenetrationModule : IHostAwarePluginModule
{
    public string Name => "NoCTF.Plugins.Penetration";
    public string Version => "1.0.1";

    public void ConfigureServices(IServiceCollection services)
    {
        ConfigureRuntimeServices(services);
        ConfigureApiServices(services);
        ConfigureScoringServices(services);
    }

    public void ConfigureServices(IServiceCollection services, PluginHostRole hostRole)
    {
        ConfigureRuntimeServices(services);
        ConfigureScoringServices(services);
        if (hostRole == PluginHostRole.Api)
            ConfigureApiServices(services);
    }

    private static void ConfigureRuntimeServices(IServiceCollection services)
    {
        services.AddScoped<PenetrationTopologyService>();
        services.AddScoped<PenetrationFlagService>();
        services.AddScoped<PenetrationComposeBuilder>();
        services.AddScoped<PenetrationInstanceService>();
        services.AddScoped<PenetrationInstanceMaintenanceService>();
        services.AddScoped<IInstanceMaintenanceService>(sp => sp.GetRequiredService<PenetrationInstanceMaintenanceService>());
    }

    private static void ConfigureApiServices(IServiceCollection services)
    {
        services.AddScoped<IChallengeSubmissionHandler, PenetrationSubmissionHandler>();
        services.AddScoped<IChallengeFeatureProvider, PenetrationFeatureProvider>();
        services.AddScoped<IChallengeAdminFeatureProvider, PenetrationAdminFeatureProvider>();
    }

    private static void ConfigureScoringServices(IServiceCollection services)
    {
        services.AddScoped<IScoringProfileContributor, PenetrationScoringProfileContributor>();
        services.AddScoped<IScoringStrategy, PenetrationStageScoringStrategy>();
        services.AddScoped<IScoringStrategy, PenetrationBloodBonusStrategy>();
        services.AddScoped<IScoreRebuildContributor, PenetrationScoreRebuildContributor>();
    }
}
