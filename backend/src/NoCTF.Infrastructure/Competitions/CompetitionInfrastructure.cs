using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Competitions.Koh;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Competitions.Permissions;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Infrastructure.Competitions.Visibility;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Caching;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Infrastructure.Competitions.Tracks;
using NoCTF.Application.Competitions.Access;
using NoCTF.Infrastructure.Competitions.Access;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Infrastructure.Competitions.Webhooks;

namespace NoCTF.Infrastructure.Competitions;

internal static class CompetitionInfrastructure
{
    internal static IServiceCollection AddNoCtfCompetitions(
        this IServiceCollection services,
        bool development)
    {
        services.AddScoped<ICompetitionLifecycleStore, CompetitionLifecycleStore>();
        services.AddScoped<IAwdRoundCoordinator, PostgresAwdRoundCoordinator>();
        services.AddScoped<IAwdRuntimeProvisioner, PostgresAwdRuntimeProvisioner>();
        services.AddScoped<IKohRuntimeProvisioner, PostgresKohRuntimeProvisioner>();
        services.AddScoped<IKohChallengeAccessReader, PostgresKohChallengeAccessReader>();
        services.AddSingleton<AwdRoundConfigurationCatalog>();
        services.AddSingleton<IAwdRoundConfigurationCatalog,
            FusionAwdRoundConfigurationCatalog>();
        services.AddSingleton<AwdCheckerConfigurationCatalog>();
        services.AddSingleton<KohProducerConfigurationCatalog>();
        services.AddSingleton<IKohProducerConfigurationCatalog,
            FusionKohProducerConfigurationCatalog>();
        services.AddHttpClient(HttpKohControlClient.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            .AddResilienceHandler("koh-observe", pipeline =>
            {
                var retry = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    Delay = TimeSpan.FromMilliseconds(100),
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = true
                };
                retry.DisableForUnsafeHttpMethods();
                pipeline.AddRetry(retry);
            });
        services.AddTransient<IKohControlClient, HttpKohControlClient>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICompetitionStartGateStore, CompetitionStartGateStore>();
        services.AddScoped<CompetitionStartGate>();
        services.AddScoped<ICompetitionManagementStore, CompetitionManagementStore>();
        services.AddSingleton<CompetitionReadModelCache>();
        services.AddScoped<ICompetitionAudienceReader, CompetitionAudienceReader>();
        services.AddScoped<CreateCompetition>();
        services.AddScoped<GetCompetition>();
        services.AddScoped<ListCompetitions>();
        services.AddScoped<UpdateCompetition>();
        services.AddScoped<DeleteCompetition>();
        services.AddScoped<IAdminCompetitionStore, AdminCompetitionStore>();
        services.AddScoped<ListAdminCompetitions>();
        services.AddScoped<GetAdminCompetition>();
        services.AddScoped<RestoreCompetition>();
        services.AddScoped<HardDeleteCompetition>();
        services.AddScoped<ForceDeleteCompetition>();
        services.AddScoped<PreviewCompetitionHardDelete>();
        services.AddScoped<TransferCompetitionOwner>();
        services.AddScoped<ICompetitionConfigurationStore, CompetitionConfigurationStore>();
        services.AddSingleton<ICompetitionConfigurationValidator, GameModeCompetitionConfigurationValidator>();
        services.AddScoped<GetCompetitionConfiguration>();
        services.AddScoped<UpdateCompetitionConfiguration>();
        services.AddScoped<ICompetitionTrackStore, CompetitionTrackStore>();
        services.AddScoped<GetCompetitionTracks>();
        services.AddScoped<UpdateCompetitionTracks>();
        services.AddScoped<AssignTeamTrack>();
        services.AddScoped<ICompetitionPermissionStore, CompetitionPermissionStore>();
        services.AddScoped<GetCompetitionPermissions>();
        services.AddScoped<ListCompetitionPermissionCandidates>();
        services.AddScoped<UpdateCompetitionPermissions>();
        services.AddScoped<NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase>();
        services.AddScoped<TransitionCompetitionLifecycle>();
        services.AddScoped<ICompetitionVisibilityAccess, CompetitionVisibilityAccess>();
        services.AddScoped<ICompetitionVisibilityStore, CompetitionVisibilityStore>();
        services.AddScoped<GetCompetitionVisibility>();
        services.AddScoped<UpdateCompetitionVisibility>();
        services.AddScoped<ICompetitionEventStore, CompetitionEventStore>();
        services.AddScoped<ICompetitionEventRecorder, CompetitionEventStore>();
        services.AddScoped<ListCompetitionEvents>();
        services.AddScoped<ExportCompetitionEvents>();
        services.AddScoped<AccessGameplayFactValue>();
        services.AddScoped<ICompetitionWebhookStore, CompetitionWebhookStore>();
        services.AddScoped<ListCompetitionWebhookTargets>();
        services.AddScoped<CreateCompetitionWebhookTarget>();
        services.AddScoped<UpdateCompetitionWebhookTarget>();
        services.AddScoped<RotateCompetitionWebhookSecret>();
        services.AddScoped<DeleteCompetitionWebhookTarget>();
        if (!development)
            services.AddSingleton<RedisCompetitionEventRefreshPublisher>();
        return services;
    }
}
