using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.GameplayFact;
using NoCTF.Infrastructure.GameplayFacts.Administration;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.Management;
using NoCTF.Infrastructure.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Infrastructure.GameplayFacts.PatchVerification;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.GameplayFacts.Status;
using NoCTF.Infrastructure.GameplayFacts.CheatIncidents;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Infrastructure.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Infrastructure.GameplayFacts.Awdp;

namespace NoCTF.Infrastructure.GameplayFacts;

internal static class GameplayFactInfrastructure
{
    internal static IServiceCollection AddNoCtfSubmissions(this IServiceCollection services)
    {
        services.AddScoped<GameplayFactAttemptCriticalSection>();
        services.AddScoped<IGameplayFactIntakeStore, GameplayFactIntakeStore>();
        services.AddScoped<IFlagAttemptStateReader, FlagAttemptStateReader>();
        services.AddScoped<GetFlagAttemptState>();
        services.AddScoped<IAwdpParticipantStateReader, AwdpParticipantStateReader>();
        services.AddScoped<GetAwdpParticipantState>();
        services.AddScoped<IAwdpBreakFlagJudge, AwdpBreakFlagJudge>();
        services.AddScoped<JudgeAwdpBreakFlag>();
        services.AddScoped<IAwdpDefenseTargetStore, AwdpDefenseTargetStore>();
        services.AddScoped<RequestAwdpDefenseTarget>();
        services.AddScoped<CreateManualAdjustment>();
        services.AddScoped<IPatchUploadStore, PatchUploadStore>();
        services.AddScoped<CreatePatchUpload>();
        services.AddScoped<IPatchVerificationTargetStore, PatchVerificationTargetStore>();
        services.AddScoped<RequestPatchVerificationTarget>();
        services.AddScoped<GetPatchVerificationState>();
        services.AddScoped<IFixArchiveReader, FixArchiveReader>();
        services.AddScoped<IAdminPatchDownloadStore, AdminPatchDownloadStore>();
        services.AddScoped<AccessAdminPatch>();
        services.AddScoped<IGameplayFactStatusReader, GameplayFactStatusReader>();
        services.AddScoped<IAdminGameplayFactStatusReader, AdminGameplayFactStatusReader>();
        services.AddScoped<IGameplayFactManagementStore, GameplayFactManagementStore>();
        services.AddScoped<ListGameplayFacts>();
        services.AddScoped<ReadPlayerGameplayFactValue>();
        services.AddScoped<QueueGameplayFactWork>();
        services.AddScoped<ICheatIncidentStore, CheatIncidentStore>();
        services.AddScoped<ListCheatIncidents>();
        services.AddScoped<AccessCheatIncident>();
        services.AddScoped<ResolveCheatIncident>();
        services.AddScoped<IHistoricalAdjudicationEvidenceStore, HistoricalAdjudicationPreviewStore>();
        services.AddScoped<IHistoricalAdjudicationEventStore, HistoricalAdjudicationPreviewStore>();
        services.AddScoped<ReadHistoricalAdjudicationEvents>();
        services.AddScoped<PreviewHistoricalAdjudicationDifferences>();
        services.AddScoped<GameplayFactProcessor>();
        services.AddScoped<IGameplayFactProcessor, GameplayFactProcessorRouter>();
        services.AddScoped<BloodRankCriticalSection>();
        services.AddScoped<IInternalResultStore, InternalResultStore>();
        services.AddScoped<IAwdpFixExecutionFence, AwdpFixExecutionFence>();
        services.AddScoped<RecordInternalResult>();
        services.AddSingleton<IGameplayFactEvaluatorCatalog, GameModeGameplayFactEvaluatorCatalog>();
        services.AddSingleton<IGameplayFactAdmissionModePolicy, GameModeGameplayFactAdmissionPolicy>();
        return services;
    }
}
