using Microsoft.Extensions.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Hosting.Messaging;
using NoCTF.Application.Competitions.Webhooks;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Hosting;

public static class MessageRouting
{
    public static void ConfigureNoCtfMessageRouting(
        this WolverineOptions options,
        IConfiguration configuration,
        HostRoles roles)
    {
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
        options.Policies.Add(new DurableRunnerCommandPolicy());
        Route<EvaluateGameplayFact>(options, WorkerQueue.Gameplay);
        Route<DispatchPendingGameplayFacts>(options, WorkerQueue.Control);
        Route<NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound>(options, WorkerQueue.Control);
        Route<NoCTF.Application.Challenges.Timing.AdvanceChallengeOpening>(options, WorkerQueue.Control);
        Route<NoCTF.Application.Challenges.Timing.RecalculateChallengeTiming>(options, WorkerQueue.Control);
        options.PublishMessage<NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged>()
            .ToNatsSubject(NatsSubjects.RealtimeEvents).UseJetStream(NatsSubjects.EventsStream);
        options.ConfigureNoCtfInfrastructureRetriesFor<NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged>(
            WorkerQueue.Background,CompetitionEventFanoutQueueNames.Realtime);
        Route<NoCTF.Application.LiveSolo.Media.LiveSoloMediaAlertCreated>(options, WorkerQueue.LiveSoloMedia);
        Route<NoCTF.Application.LiveSolo.Media.RefreshLiveSoloMedia>(options, WorkerQueue.LiveSoloMedia);
        Route<NoCTF.Application.LiveSolo.Media.AdvanceLiveSoloCapture>(options, WorkerQueue.LiveSoloMedia);
        Route<NoCTF.Application.LiveSolo.Media.SnapshotLiveSoloResult>(options, WorkerQueue.LiveSoloMedia);
        Route<NoCTF.Application.LiveSolo.Media.PruneLiveSoloCapture>(options, WorkerQueue.LiveSoloMedia);
        Route<NoCTF.Application.LiveSolo.Media.RemoveLiveSoloCaptureFiles>(options, WorkerQueue.LiveSoloMedia);
        Route<NoCTF.Application.LiveSolo.Media.RemoveLiveSoloRecordingRaw>(options, WorkerQueue.LiveSoloMedia);
        Route<GameplayFactStateChanged>(options, WorkerQueue.Gameplay);
        Route<ProjectLeaderboard>(options, WorkerQueue.Projection);
        Route<ApplyCompetitionVisibility>(options, WorkerQueue.Control);
        Route<CleanupCompetitionRuntimes>(options, WorkerQueue.Control);
        Route<ProvisionCompetitionRuntimes>(options, WorkerQueue.Control);
        Route<AdvanceCompetitionLifecycle>(options, WorkerQueue.Control);
        Route<AdvanceAwdRound>(options, WorkerQueue.Control);
        Route<GenerateAwdFlags>(options, WorkerQueue.Control);
        Route<AwdFlagInjectionFailed>(options, WorkerQueue.Gameplay);
        Route<DispatchAwdCheckers>(options, WorkerQueue.Gameplay);
        Route<AwdCheckerCallbackMissing>(options, WorkerQueue.Control);
        Route<PollKohChallenge>(options, WorkerQueue.Control);
        Route<RecordKohObservation>(options, WorkerQueue.Gameplay);
        Route<DispatchRuntime>(options, WorkerQueue.Control);
        Route<DispatchQueuedRuntimes>(options, WorkerQueue.Control);
        Route<ReleaseRunnerCapacity>(options, WorkerQueue.Control);
        Route<StopRuntime>(options, WorkerQueue.Control);
        Route<DrainGameplayFactEvaluation>(options, WorkerQueue.Gameplay);
        Route<DrainGameplayFactRejudge>(options, WorkerQueue.Gameplay);
        Route<SendEmailVerification>(options, WorkerQueue.Background);
        Route<SendPasswordReset>(options, WorkerQueue.Background);
        Route<SendPasswordChangedNotification>(options, WorkerQueue.Background);
        Route<SendMfaMail>(options, WorkerQueue.Background);
        Route<MfaAuthenticationChanged>(options, WorkerQueue.Background);
        Route<CleanupFile>(options, WorkerQueue.Background, durableOutbox: true);
        Route<ExpireAccountSourceAddresses>(options, WorkerQueue.Background, durableOutbox: true);
        Route<DispatchCompetitionWebhooks>(options, WorkerQueue.Webhook);
        Route<DeliverCompetitionWebhook>(options, WorkerQueue.Webhook);
        Route<NoCTF.Application.Competitions.StaffWebhooks.DeliverStaffWebhook>(options, WorkerQueue.Webhook);
        Route<TestCompetitionWebhook>(options, WorkerQueue.Webhook);
        Route<InvalidateDeletedCompetitionReadModels>(options, WorkerQueue.Background, durableOutbox: true);
        Route<ChallengePublished>(options, WorkerQueue.Background);
        Route<PublishHintNotification>(options, WorkerQueue.Background);
        Route<TeamBanned>(options, WorkerQueue.Background);
        Route<ForeignTeamFlagDetected>(options, WorkerQueue.Gameplay);
        Route<StaticFlagAcquisitionViolationDetected>(options, WorkerQueue.Gameplay);
        Route<TeamBanCorrected>(options, WorkerQueue.Background);
        Route<DeliverCompetitionQuestionNotification>(options, WorkerQueue.Background);
        FanOutCompetitionEvents(options);
        Route<ReconcileRunnerAssignments>(options, WorkerQueue.Control);
        Route<BloodAwarded>(options, WorkerQueue.Background);
        Route<StartAwdpFixVerification>(options, WorkerQueue.Gameplay);
        Route<StartPatchVerification>(options, WorkerQueue.Gameplay);
        Route<AwdpFixResult>(options, WorkerQueue.Gameplay);
        Route<CompleteAwdpFixRecovery>(options, WorkerQueue.Control);
        Route<ExpireAwdpFixVerification>(options, WorkerQueue.Control);
    }

    private static void Route<TMessage>(WolverineOptions options, WorkerQueue queue, bool durableOutbox = true)
    {
        var queueName = WorkerQueues.GetName(queue);
        var route = options.PublishMessage<TMessage>().ToNatsSubject(NatsSubjects.Subject(queue));
        // JetStream is the durable transport. Publishing happens only after the owning EF
        // transaction commits; there is intentionally no database-backed Wolverine outbox.
        if (durableOutbox)
            route.UseJetStream(NatsSubjects.Stream(queue));
        options.ConfigureNoCtfInfrastructureRetriesFor<TMessage>(queue, queueName);
    }

    private static void FanOutCompetitionEvents(WolverineOptions options)
    {
        var route = options.PublishMessage<CompetitionEventCommitted>();
        route.ToNatsSubject(NatsSubjects.RealtimeEvents).UseJetStream(NatsSubjects.EventsStream);
        route.ToNatsSubject(NatsSubjects.LeaderboardEvents).UseJetStream(NatsSubjects.EventsStream);
        route.ToNatsSubject(NatsSubjects.WebhookEvents).UseJetStream(NatsSubjects.EventsStream);
        options.ConfigureNoCtfInfrastructureRetriesFor<CompetitionEventCommitted>(
            WorkerQueue.Background,
            CompetitionEventFanoutQueueNames.Realtime);
        options.ConfigureNoCtfInfrastructureRetriesFor<CompetitionEventCommitted>(
            WorkerQueue.Projection,
            CompetitionEventFanoutQueueNames.Leaderboard);
        options.ConfigureNoCtfInfrastructureRetriesFor<CompetitionEventCommitted>(
            WorkerQueue.Webhook,
            CompetitionEventFanoutQueueNames.Webhook);
    }
}
