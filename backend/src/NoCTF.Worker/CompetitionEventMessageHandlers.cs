using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Observability;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using Wolverine.Attributes;
using NoCTF.Application.Competitions.Webhooks;
using Wolverine;

namespace NoCTF.Worker;

[StickyHandler(CompetitionEventFanoutQueueNames.Realtime)]
public sealed class CompetitionEventRealtimeMessageHandler(
    ICompetitionEventRefreshPublisher publisher,
    GameplayFactProgressionReconciler progression)
{
    public async Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        await progression.ReconcileAsync(message, cancellationToken);
        await publisher.PublishAsync(message, cancellationToken);
    }
}

[StickyHandler(CompetitionEventFanoutQueueNames.Leaderboard)]
public sealed class CompetitionEventLeaderboardMessageHandler(
    ILeaderboardCache leaderboard,
    LeaderboardProjectionMergeQueue mergeQueue,
    TimeProvider timeProvider, ICompetitionEventApplicabilityReader applicability)
{
    public async Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        if (!AffectsLeaderboard(message.Kind))
            return;
        if (!await applicability.HasOrdinaryScoreboardAsync(message.CompetitionId, cancellationToken)) return;

        if (ScoreboardChangePolicy.ChangesAccess(message.Kind))
            await leaderboard.InvalidateAccessAsync(message.CompetitionId, cancellationToken);
        else await leaderboard.InvalidateAsync(message.CompetitionId, cancellationToken);
        var startedWindow = mergeQueue.Enqueue(
            message.CompetitionId,
            timeProvider.GetUtcNow());
        NoCtfTelemetry.RecordLeaderboardMergeEvent(startedWindow);
    }

    internal static bool AffectsLeaderboard(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.CompetitionCreated
            or CompetitionEventKind.CompetitionUpdated
            or CompetitionEventKind.CompetitionDeleted
            or CompetitionEventKind.CompetitionLifecycleChanged
            or CompetitionEventKind.LeaderboardVisibilityChanged
            or CompetitionEventKind.ChallengeCreated
            or CompetitionEventKind.ChallengeUpdated
            or CompetitionEventKind.ChallengePublished
            or CompetitionEventKind.ChallengeUnpublished
            or CompetitionEventKind.ChallengeDeleted
            or CompetitionEventKind.HintUnlocked
            or CompetitionEventKind.ChallengeWriteUpUnlocked
            or CompetitionEventKind.TeamRegistered
            or CompetitionEventKind.TeamRegistrationChanged
            or CompetitionEventKind.TeamUpdated
            or CompetitionEventKind.TeamDeleted
            or CompetitionEventKind.TeamBanned
            or CompetitionEventKind.TeamUnbanned
            or CompetitionEventKind.GameplayFactReceived
            or CompetitionEventKind.AwdScoringRulesChanged
            or CompetitionEventKind.GameplayFactAdjudicated
            or CompetitionEventKind.ScoringRecorded
            or CompetitionEventKind.CheatIncidentConfirmed
            or CompetitionEventKind.CheatIncidentSuperseded
            or CompetitionEventKind.CheatIncidentCorrected
            or CompetitionEventKind.TeamBanAppealUpheld
            or CompetitionEventKind.TeamBanAppealAccepted
            or CompetitionEventKind.TeamBanCorrectionPublished
            or CompetitionEventKind.TrackConfigurationUpdated
            or CompetitionEventKind.TeamTrackChanged
            or CompetitionEventKind.AwdpBreakAttempted
            or CompetitionEventKind.AwdpFixAttempted
            or CompetitionEventKind.AwdpBreakResolved
            or CompetitionEventKind.AwdpFixResolved => true,
        // Blood announcements are derived after publication. Refresh once more so
        // webhook readiness can observe their newly committed outbox sequence.
        CompetitionEventKind.FirstBloodAwarded
            or CompetitionEventKind.SecondBloodAwarded
            or CompetitionEventKind.ThirdBloodAwarded => true,
        _ => false
    };
}

[StickyHandler(CompetitionEventFanoutQueueNames.Webhook)]
public sealed class CompetitionEventWebhookMessageHandler(IMessageBus bus)
{
    public ValueTask Handle(CompetitionEventCommitted message) =>
        // Only public, webhook-mapped events have an outbox row. Events are immutable, so the others
        // never deliver; skipping them avoids a dispatch message, an event read and a no-op outbox update.
        message.Visibility != NoCTF.Domain.Competitions.Events.CompetitionEventVisibility.Public
            || NoCTF.Infrastructure.Competitions.Webhooks.CompetitionWebhookEventTypes.From(message.Kind) is null
            ? ValueTask.CompletedTask
            : bus.PublishAsync(new DispatchCompetitionWebhooks(
                message.CompetitionId,
                message.EventId));
}
