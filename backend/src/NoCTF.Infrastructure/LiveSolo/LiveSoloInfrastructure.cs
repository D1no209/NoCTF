using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Infrastructure.LiveSolo.Templates;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.LiveSolo.Brackets;
using Microsoft.Extensions.Configuration;

namespace NoCTF.Infrastructure.LiveSolo;

public static class LiveSoloInfrastructure
{
    public static IServiceCollection AddNoCtfLiveSolo(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<ILiveSoloMatchStore, LiveSoloMatchStore>();
        services.AddScoped<ILiveSoloBracketStore, LiveSoloMatchStore>();
        services.AddScoped<ManageLiveSoloBracket>();
        services.AddScoped<NoCTF.Application.LiveSolo.Adjudication.ILiveSoloAdjudicationStore, LiveSoloMatchStore>();
        services.AddScoped<NoCTF.Application.LiveSolo.Adjudication.ManageLiveSoloAdjudication>();
        services.AddScoped<ManageLiveSoloMatches>();
        services.AddScoped<ManageLiveSoloConfiguration>();
        services.AddScoped<ILiveSoloPlayerPolicyReader, LiveSoloPlayerPolicyReader>();
        services.AddScoped<ILiveSoloTemplateCopyStore, LiveSoloTemplateCopyStore>();
        services.AddScoped<CopyLiveSoloTemplate>();
        services.AddScoped<IChallengeMaterialMutationGate, LiveSoloMaterialMutationGate>();
        services.RemoveAll<IExecutionScopeAccess>();
        services.AddScoped<IExecutionScopeAccess, LiveSoloExecutionAccess>();
        services.AddScoped<IScopedRuntimeControl, ScopedRuntimeControl>();
        services.AddScoped<ILiveSoloRuntimePreparation, LiveSoloRuntimePreparation>();
        services.AddScoped<ILiveSoloAttachmentStore, LiveSoloAttachmentStore>();
        services.AddScoped<AccessLiveSoloAttachments>();
        services.AddScoped<ILiveSoloHintReader, LiveSoloHintReader>();
        services.AddScoped<ILiveSoloPostgameQuestionAccess, LiveSoloPostgameQuestionAccess>();
        services.AddScoped<ManageLiveSoloWriteUps>();
        services.AddScoped<ILiveSoloRuntimeStore, LiveSoloRuntimeStore>();
        services.AddScoped<ManageLiveSoloRuntimes>();
        services.AddScoped<ILiveSoloMediaStore, LiveSoloMediaStore>();
        services.AddScoped<ManageLiveSoloMedia>();
        services.AddScoped<ILiveSoloMediaAlertReader, LiveSoloMediaAlertReader>();
        services.AddScoped<PublishLiveSoloMediaAlert>();
        services.AddScoped<ILiveSoloCaptureStore, LiveSoloCaptureStore>();
        services.AddScoped<ILiveSoloCaptureFiles, LiveSoloCaptureFiles>();
        services.AddScoped<ILiveSoloProgramReader, LiveSoloProgramReader>();
        services.AddScoped<ILiveSoloViewerStore, LiveSoloViewerStore>();
        services.AddScoped<ILiveSoloRecordingStore, LiveSoloRecordingStore>();
        services.AddScoped<ManageLiveSoloRecordings>();
        services.AddScoped<NoCTF.Application.LiveSolo.Realtime.ILiveSoloRealtimeAccess, Realtime.LiveSoloRealtimeAccess>();
        services.AddSingleton<NoCTF.Application.LiveSolo.Realtime.ILiveSoloRealtimePublisher, Realtime.NatsLiveSoloRealtimePublisher>();
        services.AddScoped<IClusterScheduleContributor, LiveSoloCaptureScheduleSource>();
        services.AddScoped<IClusterScheduleContributor, LiveSoloMediaScheduleSource>();
        services.AddScoped<IClusterScheduleContributor, LiveSoloScheduleSource>();
        var media = configuration?.GetSection(LiveKitMediaOptions.Section).Get<LiveKitMediaOptions>() ?? new();
        if (media.Enabled && (media.ApiUrl?.Scheme is not ("http" or "https") || media.ClientUrl?.Scheme is not ("ws" or "wss")
            || media.ClientUrl.Scheme == "ws" && !media.ClientUrl.IsLoopback || string.IsNullOrWhiteSpace(media.ApiKey)
            || System.Text.Encoding.UTF8.GetByteCount(media.ApiSecret) < 32 || media.RequestTimeoutSeconds is < 1 or > 60
            || media.MaximumParticipants is < 4 or > 64 || !media.EgressOutputRoot.StartsWith('/')
            || media.RecordingQuotaBytes < 1 || media.RecordingExportLimitBytes < 16L*1024*1024 || media.RecordingExportLimitBytes > long.MaxValue/2
            || media.RecordingExportLimitBytes > media.RecordingQuotaBytes
            || media.EgressOutputRoot.Contains("..", StringComparison.Ordinal)))
            throw new InvalidOperationException("LiveSolo media configuration is invalid.");
        services.AddSingleton(media);
        services.AddHttpClient(LiveKitMediaGateway.ClientName, client => client.Timeout = TimeSpan.FromSeconds(media.RequestTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        if (media.Enabled)
        {
            services.AddSingleton<ILiveSoloMediaGateway, LiveKitMediaGateway>();
            services.AddSingleton<ILiveSoloEgressGateway, LiveKitMediaGateway>();
        }
        else
        {
            services.TryAddSingleton<ILiveSoloMediaGateway, UnconfiguredLiveSoloMediaGateway>();
            services.TryAddSingleton<ILiveSoloEgressGateway, UnconfiguredLiveSoloMediaGateway>();
        }
        return services;
    }
}
