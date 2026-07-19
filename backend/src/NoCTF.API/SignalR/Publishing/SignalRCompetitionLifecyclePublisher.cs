using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRCompetitionLifecyclePublisher(IHubContext<CompetitionHub> hub)
    : ICompetitionLifecycleNotificationPublisher
{
    public Task PublishAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{competitionId:N}")
            .SendAsync("competitionLifecycleChanged",
                new CompetitionLifecycleNotification(competitionId, from, to, occurredAt), cancellationToken);
}

public sealed record CompetitionLifecycleNotification(
    Guid CompetitionId,
    CompetitionStatus From,
    CompetitionStatus To,
    DateTimeOffset OccurredAt);
