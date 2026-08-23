using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRCompetitionLifecyclePublisher(
    IHubContext<CompetitionHub, ICompetitionHubClient> hub)
    : ICompetitionLifecycleNotificationPublisher
{
    public Task PublishAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{competitionId:N}")
            .CompetitionLifecycleChanged(
                new CompetitionLifecycleChangedNotification(
                    competitionId,
                    CompetitionProtocolMapper.ToProtocol(from),
                    CompetitionProtocolMapper.ToProtocol(to),
                    occurredAt),
                cancellationToken);
}
