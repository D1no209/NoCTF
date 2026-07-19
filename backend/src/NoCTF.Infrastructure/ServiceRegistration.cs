using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Collaborators;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Notifications;
using NoCTF.GameModes.Submission;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
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
using NoCTF.Application.Authentication.Logout;
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
        services.AddHostedService<CompetitionLifecycleHostedService>();

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        services.AddScoped<ISubmissionIntakeStore, EfSubmissionIntakeStore>();
        services.AddScoped<ISubmissionStatusReader, EfSubmissionStatusReader>();
        services.AddScoped<ISubmissionProcessor, EfSubmissionProcessor>();
        services.AddSingleton<ISubmissionEvaluator, DefaultEfSubmissionEvaluator>();
        services.AddSingleton<ILeaderboardProjectorCatalog, LeaderboardProjectorCatalog>();
        services.AddScoped<ISubmissionRetryStore, EfSubmissionRetryStore>();
        services.AddScoped<ISystemScoringEventStore, EfSystemScoringEventStore>();
        services.AddScoped<RecordSystemScoringEvent>();
        services.AddScoped<RetrySubmission>();
        services.AddScoped<LogoutUser>();
        services.AddScoped<ILeaderboardCache, RedisLeaderboardCache>();
        services.AddScoped<IFixUploadSessionStore, EfFixUploadSessionStore>();
        services.AddSingleton<ISubmissionResultNotification, RedisSubmissionResultNotification>();
        services.AddScoped<ITeamModerationStore, EfTeamModerationStore>();
        services.AddScoped<ICompetitionModerationAuthorizer, EfCompetitionModerationAuthorizer>();
        services.AddScoped<ITeamRegistrationStore, EfTeamRegistrationStore>();
        services.AddScoped<CreateTeam>();
        services.AddScoped<ListCompetitionTeams>();
        services.AddScoped<ReviewTeamRegistration>();
        services.AddScoped<GetTeam>();
        services.AddScoped<UpdateTeam>();
        services.AddScoped<DeleteTeam>();
        services.AddScoped<ITeamMembershipStore, EfTeamMembershipStore>();
        services.AddScoped<InviteTeamMember>();
        services.AddScoped<RespondToTeamInvitation>();
        services.AddScoped<RemoveTeamMember>();
        services.AddScoped<LeaveTeam>();
        services.AddScoped<TransferTeamCaptain>();
        services.AddScoped<IChallengeManagementStore, EfChallengeManagementStore>();
        services.AddScoped<CreateChallenge>(); services.AddScoped<GetChallenge>(); services.AddScoped<ListChallenges>();
        services.AddScoped<UpdateChallenge>(); services.AddScoped<SetChallengePublished>(); services.AddScoped<DeleteChallenge>();
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
        services.AddScoped<ICompetitionManagementStore, EfCompetitionManagementStore>();
        services.AddScoped<CreateCompetition>();
        services.AddScoped<GetCompetition>();
        services.AddScoped<ListCompetitions>();
        services.AddScoped<UpdateCompetition>();
        services.AddScoped<DeleteCompetition>();
        services.AddScoped<ICompetitionConfigurationStore, EfCompetitionConfigurationStore>();
        services.AddSingleton<ICompetitionConfigurationValidator, GameModeCompetitionConfigurationValidator>();
        services.AddScoped<GetCompetitionConfiguration>();
        services.AddScoped<UpdateCompetitionConfiguration>();
        services.AddScoped<ICompetitionCollaboratorStore, EfCompetitionCollaboratorStore>();
        services.AddScoped<ListCompetitionCollaborators>();
        services.AddScoped<AddCompetitionCollaborator>();
        services.AddScoped<RemoveCompetitionCollaborator>();
        services.AddScoped<AdvanceCompetitionLifecycle>();
        services.AddScoped<TransitionCompetitionLifecycle>();
        services.AddScoped<IUserAuthenticationStore, EfAuthenticationStore>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        return services;
    }
}
