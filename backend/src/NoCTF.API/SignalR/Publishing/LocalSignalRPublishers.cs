using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.API.SignalR.Publishing;

public sealed class LocalLeaderboardRefreshPublisher(
    IHubContext<CompetitionHub> hub) : ILeaderboardRefreshPublisher
{
    public async Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await hub.Clients.Group($"competition:{projection.Snapshot.CompetitionId:N}").SendAsync(
                "scoreboardUpdated",
                ScoreboardUpdated.From(projection),
                cancellationToken);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordSignalRPublish(
                "leaderboard",
                outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
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
