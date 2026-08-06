using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.API.SignalR.Publishing;

public sealed class LocalLeaderboardRefreshPublisher(
    IHubContext<CompetitionHub> hub) : ILeaderboardRefreshPublisher
{
    public Task PublishAsync(
        Guid competitionId,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{competitionId:N}").SendAsync(
            "leaderboardRefreshed",
            new LeaderboardRefreshNotification(competitionId, generatedAt),
            cancellationToken);
}

public sealed class LocalSubmissionResultPublisher(
    IHubContext<CompetitionHub> hub) : ISubmissionResultNotification
{
    public Task PublishAsync(
        SubmissionResultNotification notification,
        CancellationToken cancellationToken) =>
        hub.Clients.User(notification.UserId.ToString()).SendAsync(
            "submissionResult",
            notification.Result,
            cancellationToken);
}

public sealed class LocalCompetitionEventMessageHandler(
    IHubContext<CompetitionHub> hub)
{
    public Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{message.CompetitionId:N}").SendAsync(
            "competitionEventChanged",
            message,
            cancellationToken);
}
