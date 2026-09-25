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
    ICompetitionEventRefreshPublisher publisher)
{
    public Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken) =>
        publisher.PublishAsync(message, cancellationToken);
}

[StickyHandler(CompetitionEventFanoutQueueNames.Leaderboard)]
public sealed class CompetitionEventLeaderboardMessageHandler(
    ILeaderboardCache leaderboard,
    LeaderboardProjectionMergeQueue mergeQueue,
    TimeProvider timeProvider)
{
    public async Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        if (!AffectsLeaderboard(message.Kind))
            return;

        await leaderboard.InvalidateAsync(message.CompetitionId, cancellationToken);
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
            or CompetitionEventKind.TeamRegistered
            or CompetitionEventKind.TeamRegistrationChanged
            or CompetitionEventKind.TeamUpdated
            or CompetitionEventKind.TeamDeleted
            or CompetitionEventKind.TeamBanned
            or CompetitionEventKind.TeamUnbanned
            or CompetitionEventKind.GameplayFactReceived
            or CompetitionEventKind.GameplayFactAdjudicated
            or CompetitionEventKind.ScoringRecorded
            or CompetitionEventKind.FirstBloodAwarded
            or CompetitionEventKind.SecondBloodAwarded
            or CompetitionEventKind.ThirdBloodAwarded
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
        _ => false
    };
}

[StickyHandler(CompetitionEventFanoutQueueNames.Webhook)]
public sealed class CompetitionEventWebhookMessageHandler(IMessageBus bus)
{
    public ValueTask Handle(CompetitionEventCommitted message) =>
        bus.PublishAsync(new DispatchCompetitionWebhooks(
            message.CompetitionId,
            message.EventId));
}
