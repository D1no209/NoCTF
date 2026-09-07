using Microsoft.Extensions.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Hosting.Messaging;
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
        Route<GameplayFactStateChanged>(options, WorkerQueue.Gameplay);
        Route<ProjectLeaderboard>(options, WorkerQueue.Projection);
        Route<ApplyCompetitionVisibility>(options, WorkerQueue.Control);
        Route<CleanupCompetitionRuntimes>(options, WorkerQueue.Control);
        Route<ProvisionCompetitionRuntimes>(options, WorkerQueue.Control);
        Route<AdvanceCompetitionLifecycle>(options, WorkerQueue.Control);
        Route<AdvanceAwdRound>(options, WorkerQueue.Control);
        Route<GenerateAwdFlags>(options, WorkerQueue.Control);
        Route<AwdFlagInjectionFailed>(options, WorkerQueue.Gameplay);
        Route<DispatchAwdCheckers>(options, WorkerQueue.Control);
        Route<AwdCheckerCallbackMissing>(options, WorkerQueue.Control);
        Route<PollKohChallenge>(options, WorkerQueue.Control);
        Route<RecordKohObservation>(options, WorkerQueue.Gameplay);
        Route<DispatchRuntime>(options, WorkerQueue.Control);
        Route<StopRuntime>(options, WorkerQueue.Control);
        Route<DrainGameplayFactEvaluation>(options, WorkerQueue.Gameplay);
        Route<DrainGameplayFactRejudge>(options, WorkerQueue.Gameplay);
        Route<SendEmailVerification>(options, WorkerQueue.Background);
        Route<SendPasswordReset>(options, WorkerQueue.Background);
        Route<SendPasswordChangedNotification>(options, WorkerQueue.Background);
        Route<CleanupFile>(options, WorkerQueue.Background, durableOutbox: true);
        Route<ExpireAccountSourceAddresses>(options, WorkerQueue.Background, durableOutbox: true);
        Route<InvalidateDeletedCompetitionReadModels>(options, WorkerQueue.Background, durableOutbox: true);
        Route<ChallengePublished>(options, WorkerQueue.Background);
        Route<PublishHintNotification>(options, WorkerQueue.Background);
        Route<TeamBanned>(options, WorkerQueue.Background);
        Route<ForeignTeamFlagDetected>(options, WorkerQueue.Gameplay);
        Route<TeamBanCorrected>(options, WorkerQueue.Background);
        Route<DeliverCompetitionQuestionNotification>(options, WorkerQueue.Background);
        FanOutCompetitionEvents(options);
        Route<ReconcileRunnerAssignments>(options, WorkerQueue.Control);
        Route<BloodAwarded>(options, WorkerQueue.Background);
        Route<StartAwdpFixVerification>(options, WorkerQueue.Gameplay);
        Route<AwdpFixResult>(options, WorkerQueue.Gameplay);
        Route<CompleteAwdpFixRecovery>(options, WorkerQueue.Control);
        Route<ExpireAwdpFixVerification>(options, WorkerQueue.Control);
    }

    private static void Route<TMessage>(WolverineOptions options, WorkerQueue queue, bool durableOutbox = true)
    {
        var queueName = WorkerQueues.GetName(queue);
        var route = options.PublishMessage<TMessage>().ToNatsSubject(NatsSubjects.Subject(queue));
        // Business queues must be durable senders or EF SaveChanges cannot persist their envelopes.
        if (durableOutbox)
            route.UseJetStream(NatsSubjects.Stream(queue)).UseDurableOutbox();
        options.ConfigureNoCtfInfrastructureRetriesFor<TMessage>(queue, queueName);
    }

    private static void FanOutCompetitionEvents(WolverineOptions options)
    {
        var route = options.PublishMessage<CompetitionEventCommitted>();
        route.ToNatsSubject(NatsSubjects.RealtimeEvents).UseJetStream(NatsSubjects.EventsStream).UseDurableOutbox();
        route.ToNatsSubject(NatsSubjects.LeaderboardEvents).UseJetStream(NatsSubjects.EventsStream).UseDurableOutbox();
        options.ConfigureNoCtfInfrastructureRetriesFor<CompetitionEventCommitted>(
            WorkerQueue.Background,
            CompetitionEventFanoutQueueNames.Realtime);
        options.ConfigureNoCtfInfrastructureRetriesFor<CompetitionEventCommitted>(
            WorkerQueue.Projection,
            CompetitionEventFanoutQueueNames.Leaderboard);
    }
}
