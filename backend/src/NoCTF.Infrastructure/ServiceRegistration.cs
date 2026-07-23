using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Notifications;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Runtime.Instances;
using NoCTF.GameModes.Submission;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;
using Amazon.S3;
using NoCTF.Application.Storage;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Application.Submissions.Management;
using NoCTF.Infrastructure.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using Wolverine.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using NoCTF.Domain.Identity;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication;
using NoCTF.Application.Administration;
using NoCTF.Application.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;

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
        var exporting = configuration.GetValue<bool>("OpenApi:Exporting");
        if (exporting)
        {
            services.AddDbContext<NoCtfDbContext>(options =>
                options.UseInMemoryDatabase("noctf-openapi"));
            services.AddScoped<ITransactionalMessageOutbox, OpenApiTransactionalMessageOutbox>();
        }
        else
        {
            var postgres = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
                options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
            services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        }
        services.AddScoped<IBackendMessagePublisher, WolverineBackendMessagePublisher>();
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();

        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
            throw new InvalidOperationException("ConnectionStrings:Redis is required for the API host.");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisOptions = ConfigurationOptions.Parse(redis);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddScoped<IRunnerCapacityGate, RedisRunnerCapacityGate>();

        services.AddScoped<ISubmissionIntakeStore, EfSubmissionIntakeStore>();
        services.AddScoped<IPatchUploadStore, EfPatchUploadStore>();
        services.AddScoped<CreatePatchUpload>();
        services.AddScoped<IFixArchiveReader, EfFixArchiveReader>();
        services.AddScoped<ISubmissionStatusReader, EfSubmissionStatusReader>();
        services.AddScoped<IAdminSubmissionStatusReader, EfAdminSubmissionStatusReader>();
        services.AddScoped<ISubmissionManagementStore, EfSubmissionManagementStore>();
        services.AddScoped<ListSubmissions>();
        services.AddScoped<QueueSubmissionWork>();
        services.AddScoped<ICompetitionHubAccess, EfCompetitionHubAccess>();
        services.AddScoped<INotificationReader, EfNotificationReader>();
        services.AddScoped<ListNotifications>();
        services.AddScoped<ISubmissionProcessor, EfSubmissionProcessor>();
        services.AddScoped<IInternalResultStore, EfInternalResultStore>();
        services.AddScoped<RecordInternalResult>();
        services.AddSingleton<ISubmissionEvaluatorCatalog, GameModeSubmissionEvaluatorCatalog>();
        services.AddSingleton<IChallengeConfigurationCatalog, GameModeChallengeConfigurationCatalog>();
        services.AddSingleton<IChallengeRuntimeTemplateCatalog, ChallengeRuntimeTemplateCatalog>();
        services.AddScoped<IRuntimeInstanceStore, EfRuntimeInstanceStore>();
        services.AddScoped<GetPlayerRuntime>();
        services.AddScoped<MutatePlayerRuntime>();
        services.AddScoped<IRuntimeTargetReader, EfRuntimeTargetReader>();
        services.AddScoped<ListRuntimeTargets>();
        services.AddScoped<IAdminRuntimeStore, EfAdminRuntimeStore>();
        services.AddScoped<ManageAdminRuntimes>();
        services.AddSingleton<ISubmissionAdmissionModePolicy, GameModeSubmissionAdmissionPolicy>();
        services.AddSingleton<ILeaderboardProjectorCatalog, LeaderboardProjectorCatalog>();
        services.AddSingleton<ILeaderboardProjectionEngine, LeaderboardProjectionEngine>();
        services.AddSingleton<AwdpCheckExitCodeMapper>();
        services.AddScoped<ILeaderboardCache, RedisLeaderboardCache>();
        services.AddSingleton<ISubmissionResultNotification, RedisSubmissionResultNotification>();
        services.AddScoped<ITeamModerationStore, EfTeamModerationStore>();
        services.AddScoped<ICompetitionModerationAuthorizer, EfCompetitionModerationAuthorizer>();
        services.AddScoped<ITeamRegistrationStore, EfTeamRegistrationStore>();
        services.AddScoped<CreateTeam>();
        services.AddScoped<ListCompetitionTeams>();
        services.AddScoped<ReviewTeamRegistration>();
        services.AddScoped<ResubmitTeamRegistration>();
        services.AddScoped<GetTeam>();
        services.AddScoped<GetMyTeam>();
        services.AddScoped<UpdateTeam>();
        services.AddScoped<DeleteTeam>();
        services.AddScoped<ITeamMembershipStore, EfTeamMembershipStore>();
        services.AddScoped<JoinTeamByInvitation>();
        services.AddScoped<RotateTeamInvitation>();
        services.AddScoped<RemoveTeamMember>();
        services.AddScoped<LeaveTeam>();
        services.AddScoped<TransferTeamCaptain>();
        services.AddScoped<IChallengeManagementStore, EfChallengeManagementStore>();
        services.AddScoped<IChallengeBankStore, EfChallengeBankStore>();
        services.AddScoped<CreateChallengeTemplate>();
        services.AddScoped<ListChallengeTemplates>();
        services.AddScoped<GetChallengeTemplate>();
        services.AddScoped<UpdateChallengeTemplate>();
        services.AddScoped<DeleteChallengeTemplate>();
        services.AddScoped<UpdateChallengeTemplatePermissions>();
        services.AddScoped<TransferChallengeTemplateOwner>();
        services.AddScoped<IChallengeAttachmentStore, EfChallengeAttachmentStore>();
        services.AddScoped<ManageChallengeAttachments>();
        services.AddScoped<GetChallengeAttachments>();
        services.AddScoped<IChallengeFlagStore, EfChallengeFlagManagementStore>();
        services.AddScoped<ManageChallengeFlags>();
        services.AddScoped<IMissingFlagGenerator, PostgresMissingFlagGenerator>();
        services.AddScoped<GenerateMissingFlags>();
        services.AddScoped<IChallengeHintStore, EfChallengeHintStore>();
        services.AddScoped<ManageChallengeHints>();
        services.AddScoped<UnlockChallengeHint>();
        services.AddScoped<CreateChallenge>();
        services.AddScoped<GetChallenge>();
        services.AddScoped<ListChallenges>();
        services.AddScoped<UpdateChallenge>();
        services.AddScoped<DeleteChallenge>();
        services.AddScoped<IChallengeConfigurationStore, EfChallengeConfigurationStore>();
        services.AddScoped<GetChallengeConfiguration>();
        services.AddScoped<UpdateChallengeConfiguration>();
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
        services.AddScoped<IAwdRoundCoordinator, PostgresAwdRoundCoordinator>();
        services.AddSingleton<AwdRoundConfigurationCatalog>();
        services.AddSingleton<AwdCheckerConfigurationCatalog>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICompetitionStartGateStore, EfCompetitionStartGateStore>();
        services.AddScoped<CompetitionStartGate>();
        services.AddScoped<ICompetitionManagementStore, EfCompetitionManagementStore>();
        services.AddScoped<CreateCompetition>();
        services.AddScoped<GetCompetition>();
        services.AddScoped<ListCompetitions>();
        services.AddScoped<UpdateCompetition>();
        services.AddScoped<DeleteCompetition>();
        services.AddScoped<IAdminCompetitionStore, EfAdminCompetitionStore>();
        services.AddScoped<ListAdminCompetitions>();
        services.AddScoped<GetAdminCompetition>();
        services.AddScoped<RestoreCompetition>();
        services.AddScoped<HardDeleteCompetition>();
        services.AddScoped<TransferCompetitionOwner>();
        services.AddScoped<ICompetitionConfigurationStore, EfCompetitionConfigurationStore>();
        services.AddSingleton<ICompetitionConfigurationValidator, GameModeCompetitionConfigurationValidator>();
        services.AddScoped<GetCompetitionConfiguration>();
        services.AddScoped<UpdateCompetitionConfiguration>();
        services.AddScoped<ICompetitionPermissionStore, EfCompetitionPermissionStore>();
        services.AddScoped<UpdateCompetitionPermissions>();
        services.AddScoped<NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycle>();
        services.AddScoped<TransitionCompetitionLifecycle>();
        services.AddScoped<IUserAuthenticationStore, EfAuthenticationStore>();
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 210_000);
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<RegisterUser>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<ChangePassword>();
        services.AddScoped<LogoutAll>();
        services.AddScoped<IEmailVerificationStore, EfEmailVerificationStore>();
        services.AddScoped<ResendEmailVerification>();
        services.AddScoped<VerifyEmail>();
        services.AddScoped<IAccessTokenVersionReader, EfAccessTokenVersionReader>();
        if (exporting)
            services.AddScoped<IPlatformAdministrationStore, OpenApiPlatformAdministrationStore>();
        else
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
        services.AddScoped<ManagePlatform>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        return services;
    }
}
