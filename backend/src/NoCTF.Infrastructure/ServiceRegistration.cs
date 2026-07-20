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
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Notifications;
using NoCTF.Application.Maintenance;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
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
using NoCTF.Infrastructure.Runtime;
using NoCTF.Application.SystemProducers;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.SystemProducers;

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
        services.AddSingleton<BackgroundWorkShutdownCoordinator>();
        services.AddSingleton<IBackgroundWorkScheduler>(serviceProvider =>
            serviceProvider.GetRequiredService<ChannelBackgroundWorkScheduler>());
        services.AddSingleton<IBackgroundWorkAdmissionGate>(serviceProvider =>
            serviceProvider.GetRequiredService<ChannelBackgroundWorkScheduler>());
        services.AddHostedService<SubmissionProcessingHostedService>();
        services.AddHostedService<LeaderboardRefreshHostedService>();
        services.AddHostedService<CompetitionRebuildHostedService>();
        // Hosted services stop in reverse registration order: producers first,
        // then the staged drain coordinator, and consumers last.
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<BackgroundWorkShutdownCoordinator>());
        services.AddHostedService<CompetitionLifecycleHostedService>();
        services.AddHostedService<FixUploadExpiryHostedService>();
        services.AddHostedService<FixArchiveCleanupHostedService>();
        services.AddHostedService<RuntimeHealthHostedService>();
        services.AddHostedService<OrphanRuntimeCleanupHostedService>();
        services.AddHostedService<KohPollingHostedService>();

        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
            throw new InvalidOperationException("ConnectionStrings:Redis is required for the API host.");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        services.AddScoped<ISubmissionIntakeStore, EfSubmissionIntakeStore>();
        services.AddScoped<ISubmissionStatusReader, EfSubmissionStatusReader>();
        services.AddScoped<IAdminSubmissionStatusReader, EfAdminSubmissionStatusReader>();
        services.AddScoped<ICompetitionHubAccess, EfCompetitionHubAccess>();
        services.AddScoped<ISubmissionProcessor, EfSubmissionProcessor>();
        services.AddScoped<IFixVerificationStore, EfFixVerificationStore>();
        services.AddScoped<UnavailableFixSubmissionVerifier>();
        services.AddScoped<IFixSubmissionVerifier, ValidatingFixSubmissionVerifier>();
        services.AddScoped<VerifyFixSubmission>();
        services.AddScoped<ExpireFixUploads>();
        services.AddScoped<IFixArchiveCleanupStore, EfFixArchiveCleanupStore>();
        services.AddScoped<CleanupFixArchives>();
        services.AddSingleton<ISubmissionEvaluatorCatalog, GameModeSubmissionEvaluatorCatalog>();
        services.AddSingleton<IChallengeConfigurationCatalog, GameModeChallengeConfigurationCatalog>();
        services.AddSingleton<IChallengeRuntimeTemplateCatalog, ChallengeRuntimeTemplateCatalog>();
        services.AddSingleton<ISubmissionAdmissionModePolicy, GameModeSubmissionAdmissionPolicy>();
        services.AddSingleton<ILeaderboardProjectorCatalog, LeaderboardProjectorCatalog>();
        services.AddSingleton<ILeaderboardProjectionEngine, LeaderboardProjectionEngine>();
        services.AddScoped<ISubmissionRetryStore, EfSubmissionRetryStore>();
        services.AddScoped<ISystemScoringEventStore, EfSystemScoringEventStore>();
        services.AddScoped<ISystemScoringEventProcessor, EfSystemScoringEventProcessor>();
        services.AddScoped<RecordSystemScoringEvent>();
        services.AddScoped<IKohProducerTargetStore, EfKohProducerTargetStore>();
        services.AddSingleton<IKohProducerConfigurationCatalog, KohProducerConfigurationCatalog>();
        services.AddHttpClient<IKohAgentClient, HttpKohAgentClient>();
        services.AddScoped<ProduceKohObservations>();
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
        services.AddScoped<GetMyTeam>();
        services.AddScoped<UpdateTeam>();
        services.AddScoped<DeleteTeam>();
        services.AddScoped<ITeamMembershipStore, EfTeamMembershipStore>();
        services.AddScoped<InviteTeamMember>();
        services.AddScoped<RespondToTeamInvitation>();
        services.AddScoped<RemoveTeamMember>();
        services.AddScoped<LeaveTeam>();
        services.AddScoped<TransferTeamCaptain>();
        services.AddScoped<IChallengeManagementStore, EfChallengeManagementStore>();
        services.AddScoped<CreateChallenge>();
        services.AddScoped<GetChallenge>();
        services.AddScoped<ListChallenges>();
        services.AddScoped<UpdateChallenge>();
        services.AddScoped<SetChallengePublished>();
        services.AddScoped<DeleteChallenge>();
        services.AddScoped<IChallengeConfigurationStore, EfChallengeConfigurationStore>();
        services.AddScoped<GetChallengeConfiguration>();
        services.AddScoped<UpdateChallengeConfiguration>();
        services.AddScoped<IChallengeFlagStore, EfChallengeFlagStore>();
        services.AddScoped<ICompetitionRebuildProcessor, EfCompetitionRebuildProcessor>();
        var runtimePolicyOptions = new RuntimeOperationPolicyOptions(
            TimeSpan.FromSeconds(configuration.GetValue("Runtime:Operation:DefaultTimeoutSeconds", 300)),
            TimeSpan.FromSeconds(configuration.GetValue("Runtime:Operation:CompensationTimeoutSeconds", 30)),
            TimeSpan.FromSeconds(configuration.GetValue("Runtime:Operation:ClaimLeaseGraceSeconds", 30)));
        runtimePolicyOptions.Validate();
        services.AddSingleton(runtimePolicyOptions);
        services.AddScoped<IRuntimeOperationStore, EfRuntimeOperationStore>();
        services.AddScoped<IRuntimeCleanupStore, EfRuntimeCleanupStore>();
        services.AddScoped<ChallengeRuntimeProvisioner>();
        services.AddScoped<ICompetitionRuntimeProvisioningStore, EfCompetitionRuntimeProvisioningStore>();
        services.AddScoped<CompetitionRuntimeProvisioner>();
        services.AddScoped<CompetitionRuntimeCleaner>();
        services.AddScoped<IOrphanRuntimeStore, EfOrphanRuntimeStore>();
        services.AddScoped<OrphanRuntimeCleaner>();
        services.AddScoped<IRuntimeHealthStore, EfRuntimeHealthStore>();
        services.AddScoped<ChallengeRuntimeHealthChecker>();
        var runnerBaseUrl = configuration["Runtime:Runner:BaseUrl"];
        if (string.IsNullOrWhiteSpace(runnerBaseUrl))
        {
            services.AddScoped<IContainerLifecycle, UnavailableContainerLifecycle>();
        }
        else
        {
            services.AddHttpClient<RunnerContainerLifecycle>(client =>
            {
                client.BaseAddress = new Uri(runnerBaseUrl.EndsWith('/') ? runnerBaseUrl : runnerBaseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Runtime:Runner:TimeoutSeconds", 30), 1, 120));
            });
            services.AddScoped<IContainerLifecycle>(provider => provider.GetRequiredService<RunnerContainerLifecycle>());
        }
        services.AddScoped<ListChallengeFlags>();
        services.AddScoped<GetChallengeFlag>();
        services.AddScoped<CreateChallengeFlag>();
        services.AddScoped<UpdateChallengeFlag>();
        services.AddScoped<DeleteChallengeFlag>();
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
        services.AddScoped<IAccessTokenVersionReader, EfAccessTokenVersionReader>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        return services;
    }
}
