using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.API.SignalR.Publishing;

public sealed class LocalLeaderboardRefreshPublisher(
    IHubContext<CompetitionHub> hub) : ILeaderboardRefreshPublisher
{
    public Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{projection.Snapshot.CompetitionId:N}").SendAsync(
            "scoreboardUpdated",
            ScoreboardUpdated.From(projection),
            cancellationToken);
}

public sealed class LocalGameplayFactStatePublisher(
    IHubContext<CompetitionHub> hub) : IGameplayFactStateChangedNotification
{
    public Task PublishAsync(
        GameplayFactStateChangedNotification notification,
        CancellationToken cancellationToken) =>
        hub.Clients.User(notification.UserId.ToString()).SendAsync(
            "gameplayFactStateChanged",
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
