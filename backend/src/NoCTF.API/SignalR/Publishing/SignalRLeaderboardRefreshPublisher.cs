using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRLeaderboardRefreshPublisher(IHubContext<CompetitionHub> hub)
    : ILeaderboardRefreshPublisher
{
    public Task PublishAsync(Guid competitionId, DateTimeOffset generatedAt, CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{competitionId:N}")
            .SendAsync("leaderboardRefreshed", new LeaderboardRefreshNotification(competitionId, generatedAt), cancellationToken);
}

public sealed record LeaderboardRefreshNotification(Guid CompetitionId, DateTimeOffset GeneratedAt);
