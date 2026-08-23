using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Notifications;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.API.Endpoints.GameplayFacts;

namespace NoCTF.API.SignalR.Publishing;

public sealed class LocalLeaderboardRefreshPublisher(
    IHubContext<CompetitionHub, ICompetitionHubClient> hub) : ILeaderboardRefreshPublisher
{
    public async Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await hub.Clients.Group($"competition:{projection.Snapshot.CompetitionId:N}").ScoreboardUpdated(
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
    IHubContext<CompetitionHub, ICompetitionHubClient> hub) : IGameplayFactStateChangedNotification
{
    public Task PublishAsync(
        GameplayFactStateChangedNotification notification,
        CancellationToken cancellationToken) =>
        hub.Clients.User(notification.UserId.ToString()).GameplayFactStateChanged(
            GameplayFactMapper.ToStatusResponse(notification.Result),
            cancellationToken);
}

public sealed class LocalCompetitionEventMessageHandler(
    IHubContext<CompetitionHub, ICompetitionHubClient> hub)
{
    public Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken) =>
        hub.Clients.Group($"competition:{message.CompetitionId:N}").CompetitionEventChanged(
            new CompetitionEventChangedNotification(
                message.CompetitionId,
                message.EventId,
                CompetitionEventProtocolMapper.ToProtocol(message.Kind),
                CompetitionEventProtocolMapper.ToProtocol(message.Level),
                message.OccurredAt),
            cancellationToken);
}
