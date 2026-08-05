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
using NoCTF.Infrastructure.Submissions;
using NoCTF.Infrastructure.Teams;
using NoCTF.Infrastructure.DataExports;

namespace NoCTF.Infrastructure;

public static class ServiceRegistration
{
    public static async Task MigrateNoCtfAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AdministratorBootstrapper>()
            .SeedAsync(cancellationToken);
    }

    public static IServiceCollection AddNoCtfInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var exporting = configuration.GetValue<bool>("OpenApi:Exporting");

        services.AddNoCtfPersistence(configuration, exporting);
        services.AddNoCtfMessaging(exporting);
        services.AddNoCtfRuntime(configuration);
        services.AddNoCtfSubmissions();
        services.AddNoCtfScoring();
        services.AddNoCtfNotifications();
        services.AddNoCtfTeams();
        services.AddNoCtfChallenges();
        services.AddNoCtfStorage(configuration);
        services.AddNoCtfCompetitions();
        services.AddNoCtfAuthentication();
        services.AddNoCtfAdministration(configuration, exporting);
        services.AddNoCtfDataExports();

        return services;
    }
}
