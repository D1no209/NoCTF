using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Ports;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Rebuild;
using NoCTF.GameModes.Scoring;
using NoCTF.Infrastructure.Eventing;
using NoCTF.Infrastructure.Eventing.Projections;
using NoCTF.Infrastructure.Eventing.ScoringStreams;
using NoCTF.Infrastructure.Eventing.SubmissionStreams;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;
using Wolverine;
using Amazon.S3;
using NoCTF.Application.Storage;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure;

public static class ServiceRegistration
{
    public static IHostApplicationBuilder AddNoCtfMessaging(
        this IHostApplicationBuilder builder,
        IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        builder.UseWolverine(options => options.ConfigureNoCtf(postgres));
        return builder;
    }

    public static async Task MigrateNoCtfAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.NoCtfDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        var store = scope.ServiceProvider.GetRequiredService<Marten.IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public static IServiceCollection AddNoCtfInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(postgres));
        services.AddNoCtfMarten(configuration);

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        services.AddScoped<ISubmissionIntakeStore, MartenSubmissionIntakeStore>();
        services.AddScoped<ISubmissionStatusReader, MartenSubmissionStatusReader>();
        services.AddScoped<ICompetitionInputAppender, MartenCompetitionInputAppender>();
        services.AddScoped<ISubmissionProcessor, MartenSubmissionProcessor>();
        services.AddSingleton<ISubmissionResultNotification, RedisSubmissionResultNotification>();
        services.AddScoped<ISubmissionQueue, WolverineSubmissionQueue>();
        services.AddScoped<IScoringRebuildStore, MartenScoringRebuildStore>();
        services.AddScoped<IScoringRebuildQueue, WolverineScoringRebuildQueue>();
        services.AddScoped<ILeaderboardStore, MartenLeaderboardStore>();
        services.AddScoped<ITeamModerationStore, EfTeamModerationStore>();
        services.AddScoped<ICompetitionModerationAuthorizer, EfCompetitionModerationAuthorizer>();
        services.AddScoped<IFixUploadSessionStore, EfFixUploadSessionStore>();
        if (string.Equals(configuration["Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL = configuration["Storage:S3:ServiceUrl"],
                ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", true)
            }));
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        }
        services.AddScoped<ICompetitionLifecycleStore, EfCompetitionLifecycleStore>();
        services.AddScoped<RebuildScoring>();
        services.AddScoped<AdvanceCompetitionLifecycle>();
        services.AddScoped<IUserAuthenticationStore, EfAuthenticationStore>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        services.AddSingleton<IScoringRuleEvaluator, GameModeScoringEvaluator>();
        return services;
    }
}
