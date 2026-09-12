using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRCompetitionLifecyclePublisher(
    ICompetitionHubAudienceRouter audiences)
    : ICompetitionLifecycleNotificationPublisher
{
    public async Task PublishAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var clients = await audiences.CurrentAsync(competitionId, cancellationToken);
        if (clients is null)
            return;
        await clients.CompetitionLifecycleChanged(
            new CompetitionLifecycleChangedNotification(
                competitionId,
                CompetitionProtocolMapper.ToProtocol(from),
                CompetitionProtocolMapper.ToProtocol(to),
                occurredAt),
            cancellationToken);
    }
}
