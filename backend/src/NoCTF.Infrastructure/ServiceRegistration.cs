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
using NoCTF.GameModes.Submission;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;
using Amazon.S3;
using NoCTF.Application.Storage;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.BackgroundWork;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Retry;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Caching;

namespace NoCTF.Infrastructure;

public static class ServiceRegistration
{
    public static async Task MigrateNoCtfAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.NoCtfDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public static IServiceCollection AddNoCtfInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(postgres));
        services.Configure<BackgroundQueueOptions>(configuration.GetSection(BackgroundQueueOptions.SectionName));
        var queueOptions = configuration.GetSection(BackgroundQueueOptions.SectionName).Get<BackgroundQueueOptions>()
            ?? new BackgroundQueueOptions();
        services.AddSingleton(queueOptions);
        services.AddSingleton<ChannelBackgroundWorkScheduler>();
        services.AddSingleton<IBackgroundWorkScheduler>(serviceProvider =>
            serviceProvider.GetRequiredService<ChannelBackgroundWorkScheduler>());
        services.AddHostedService<SubmissionProcessingHostedService>();
        services.AddHostedService<LeaderboardRefreshHostedService>();

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        services.AddScoped<ISubmissionIntakeStore, EfSubmissionIntakeStore>();
        services.AddScoped<ISubmissionStatusReader, EfSubmissionStatusReader>();
        services.AddScoped<ISubmissionProcessor, EfSubmissionProcessor>();
        services.AddSingleton<ISubmissionEvaluator, DefaultEfSubmissionEvaluator>();
        services.AddScoped<ISubmissionRetryStore, EfSubmissionRetryStore>();
        services.AddScoped<RetrySubmission>();
        services.AddScoped<ILeaderboardCache, RedisLeaderboardCache>();
        services.AddScoped<IFixUploadSessionStore, EfFixUploadSessionStore>();
        services.AddSingleton<ISubmissionResultNotification, RedisSubmissionResultNotification>();
        services.AddScoped<ITeamModerationStore, EfTeamModerationStore>();
        services.AddScoped<ICompetitionModerationAuthorizer, EfCompetitionModerationAuthorizer>();
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
        services.AddScoped<AdvanceCompetitionLifecycle>();
        services.AddScoped<IUserAuthenticationStore, EfAuthenticationStore>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        services.AddSingleton<IScoringRuleEvaluator, GameModeScoringEvaluator>();
        return services;
    }
}
