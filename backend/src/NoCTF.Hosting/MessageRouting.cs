using Microsoft.Extensions.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.GameplayFacts.Processing;
using Wolverine;
using Wolverine.Postgresql;

namespace NoCTF.Hosting;

public static class MessageRouting
{
    public static void ConfigureNoCtfMessageRouting(
        this WolverineOptions options,
        IConfiguration configuration,
        HostRoles roles)
    {
        options.PublishMessage<EvaluateGameplayFact>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<GameplayFactStateChanged>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<RefreshDirtyLeaderboards>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ProjectLeaderboard>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ApplyCompetitionVisibility>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CleanupCompetitionRuntimes>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ProvisionCompetitionRuntimes>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AdvanceCompetitionLifecycle>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AdvanceAwdRound>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<GenerateAwdFlags>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AwdFlagInjectionFailed>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DispatchAwdCheckers>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AwdCheckerCallbackMissing>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<PollKohChallenge>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<RecordKohObservation>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DispatchRuntime>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<StopRuntime>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DrainGameplayFactEvaluation>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DrainGameplayFactRejudge>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendEmailVerification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendPasswordReset>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendPasswordChangedNotification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CleanupFile>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ChallengePublished>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<PublishHintNotification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<TeamBanned>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ForeignTeamFlagDetected>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<TeamBanCorrected>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DeliverCompetitionQuestionNotification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CompetitionEventCommitted>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<GenerateDataExport>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ExpireDataExport>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<PurgeDataExport>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ReconcileRunnerAssignments>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ReleaseRunnerCapacity>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<BloodAwarded>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AwdpFixResult>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ReplayAwdpFixVerification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ExpireAwdpFixVerification>().ToPostgresqlQueue("noctf-worker");

        if (!roles.Has(HostRole.Runner))
            return;
        var runnerPool = configuration["Runner:Pool"] ?? "default";
        var poolQueueName = RunnerQueueName.FromPool(runnerPool);
        options.PublishMessage<ClaimContainerRuntime>().ToPostgresqlQueue(poolQueueName.Value);
    }
}
