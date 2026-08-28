using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Infrastructure.Competitions;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime;
using NoCTF.Infrastructure.Scoring;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.GameplayFacts;
using NoCTF.Infrastructure.Teams;
using NoCTF.Infrastructure.Exports;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Infrastructure;

public static class ServiceRegistration
{
    public static async Task InitializeNoCtfAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(cancellationToken);
        else
            await db.Database.EnsureCreatedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AdministratorBootstrapper>()
            .SeedAsync(cancellationToken);
    }

    public static IServiceCollection AddNoCtfInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool development = false)
    {
        var exporting = configuration.GetValue<bool>("OpenApi:Exporting");

        services.AddNoCtfCaching(configuration, development);
        services.AddNoCtfPersistence(configuration, exporting, development);
        services.AddNoCtfMessaging(exporting, development);
        services.AddNoCtfRuntime(configuration, development);
        services.AddNoCtfSubmissions();
        services.AddNoCtfScoring(development);
        services.AddNoCtfNotifications(development);
        services.AddNoCtfTeams();
        services.AddNoCtfChallenges();
        services.AddNoCtfStorage(configuration);
        services.AddNoCtfCompetitions(development);
        services.AddNoCtfAuthentication(configuration);
        services.AddNoCtfAdministration(configuration, exporting, development);
        services.AddNoCtfSynchronousArchives(configuration);
        if (!exporting)
            services.AddHostedService<OperationalMetricsCollector>();

        return services;
    }
}
