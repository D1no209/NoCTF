using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Competitions.Events;
using Wolverine.Attributes;

namespace NoCTF.Worker;

[StickyHandler(CompetitionEventFanoutQueueNames.Realtime)]
public sealed class CompetitionEventRealtimeMessageHandler(
    RedisCompetitionEventRefreshPublisher publisher)
{
    public Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken) =>
        publisher.PublishAsync(message, cancellationToken);
}

[StickyHandler(CompetitionEventFanoutQueueNames.Leaderboard)]
public sealed class CompetitionEventLeaderboardMessageHandler
{
    public static ProjectLeaderboard Handle(CompetitionEventCommitted message) =>
        new(message.CompetitionId);
}
