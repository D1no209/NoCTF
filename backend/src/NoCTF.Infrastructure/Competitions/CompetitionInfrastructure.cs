using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
using NoCTF.Application.Competitions.Progression;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.Competitions;

internal static class CompetitionInfrastructure
{
    internal static IServiceCollection AddNoCtfCompetitions(
        this IServiceCollection services,
        IConfiguration configuration,
        bool development)
    {
        services.AddScoped<NoCTF.Application.Competitions.Directions.ICompetitionDirectionStore, Directions.CompetitionDirectionStore>();
        services.AddScoped<NoCTF.Application.Competitions.Directions.ManageCompetitionDirections>();
        services.AddScoped<ICompetitionLifecycleStore, CompetitionLifecycleStore>();
        services.AddScoped<ICompetitionProgressionStore, CompetitionProgressionStore>();
        services.AddSingleton<ProgressionGraphReadCache>();
        services.AddScoped<GetCompetitionProgression>();
        services.AddScoped<SaveCompetitionProgression>();
        services.AddScoped<ProgressionReconciler>();
        services.AddScoped<IProgressionChallengeAccess, ProgressionChallengeAccess>();
        services.AddScoped<IProgressionChallengeStarter, ProgressionChallengeStarter>();
        services.AddScoped<ICompetitionBadgeStore, CompetitionBadgeStore>();
        services.AddScoped<ManageCompetitionBadges>();
        services.AddScoped<IProgressionPlayerReader, ProgressionPlayerReader>();
        services.AddScoped<IAwdRoundCoordinator, AwdRoundCoordinator>();
        services.AddScoped<IAwdRuntimeProvisioner, AwdRuntimeProvisioner>();
        services.AddScoped<IKohRuntimeProvisioner, KohRuntimeProvisioner>();
        services.AddScoped<IKohChallengeAccessReader, KohChallengeAccessReader>();
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
        services.AddScoped<NoCTF.Application.Competitions.StaffWebhooks.IStaffWebhookStore,
            NoCTF.Infrastructure.Competitions.StaffWebhooks.StaffWebhookStore>();
        services.AddScoped<NoCTF.Application.Competitions.StaffWebhooks.ManageStaffWebhooks>();
        services.AddScoped<ListCompetitionWebhookTargets>();
        services.AddScoped<CreateCompetitionWebhookTarget>();
        services.AddScoped<UpdateCompetitionWebhookTarget>();
        services.AddScoped<RotateCompetitionWebhookSecret>();
        services.AddScoped<DeleteCompetitionWebhookTarget>();
        var publicBaseUrlValue = configuration["Webhooks:PublicBaseUrl"]
            ?? (development ? "http://localhost:5000" : "https://localhost");
        if (!Uri.TryCreate(publicBaseUrlValue, UriKind.Absolute, out var webhookPublicBaseUrl)
            || webhookPublicBaseUrl.Scheme is not ("https" or "http")
            || webhookPublicBaseUrl.UserInfo.Length > 0
            || webhookPublicBaseUrl.AbsolutePath != "/"
            || webhookPublicBaseUrl.Query.Length > 0
            || webhookPublicBaseUrl.Fragment.Length > 0
            || !development && webhookPublicBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Webhooks:PublicBaseUrl must be an absolute HTTPS origin in production.");
        }
        var webhookTimeoutSeconds = configuration.GetValue("Webhooks:TimeoutSeconds", 10);
        if (webhookTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException("Webhooks:TimeoutSeconds must be between 1 and 120.");
        services.AddSingleton(new CompetitionWebhookOptions(
            new Uri(webhookPublicBaseUrl.AbsoluteUri.TrimEnd('/') + "/"),
            webhookTimeoutSeconds,
            ReadAllowList(configuration.GetSection(
                "Webhooks:PrivateNetworkAllowList").Get<string[]>()),
            ReadAllowList(configuration.GetSection(
                "Webhooks:InsecureHttpHostAllowList").Get<string[]>())));
        services.AddScoped<ICompetitionWebhookDeliveryStore, CompetitionWebhookDeliveryStore>();
        services.AddSingleton<ICompetitionWebhookSender, CompetitionWebhookSender>();
        services.AddSingleton<ICompetitionWebhookTestStatusStore,
            FusionCompetitionWebhookTestStatusStore>();
        if (development)
            services.AddSingleton<ICompetitionEventRefreshPublisher,
                NoOpCompetitionEventRefreshPublisher>();
        else
            services.AddSingleton<ICompetitionEventRefreshPublisher,
                NatsCompetitionEventRefreshPublisher>();
        return services;
    }

    private static IReadOnlySet<string> ReadAllowList(string[]? values) =>
        new HashSet<string>(
            (values ?? []).Select(value => value.Trim().TrimEnd('.'))
                .Where(value => value.Length > 0),
            StringComparer.OrdinalIgnoreCase);
}
