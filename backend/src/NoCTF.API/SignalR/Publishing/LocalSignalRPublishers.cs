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
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.API.SignalR.Publishing;

public sealed class LocalLeaderboardRefreshPublisher(
    ICompetitionHubAudienceRouter audiences) : ILeaderboardRefreshPublisher
{
    public async Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            var clients = await audiences.CurrentAsync(
                projection.Snapshot.CompetitionId,
                cancellationToken);
            if (clients is null)
                return;
            await clients.ScoreboardUpdated(
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
    IHubContext<CompetitionHub, ICompetitionHubClient> hub, MfaConnectionGuard guard) : IGameplayFactStateChangedNotification
{
    public async Task PublishAsync(
        GameplayFactStateChangedNotification notification,
        CancellationToken cancellationToken) =>
        await hub.Clients.Clients(await guard.EligibleAsync(MfaHubKind.Competition, new HashSet<Guid> { notification.UserId }, cancellationToken)).GameplayFactStateChanged(
            GameplayFactMapper.ToStatusResponse(notification.Result),
            cancellationToken);
}

public sealed class LocalCompetitionEventMessageHandler(
    ICompetitionHubAudienceRouter audiences,
    ICompetitionHubAudienceCoordinator coordinator)
{
    public async Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        var clients = message.Kind == CompetitionEventKind.CompetitionAudienceChanged
            ? await audiences.AllKnownAsync(message.CompetitionId, cancellationToken)
            : await audiences.CurrentAsync(message.CompetitionId, cancellationToken);
        if (clients is null)
            return;
        await clients.CompetitionEventChanged(
            new CompetitionEventChangedNotification(
                message.CompetitionId,
                message.EventId,
                CompetitionEventProtocolMapper.ToProtocol(message.Kind),
                CompetitionEventProtocolMapper.ToProtocol(message.Level),
                message.OccurredAt),
            cancellationToken);
        if (message.Kind == CompetitionEventKind.CompetitionAudienceChanged)
            await coordinator.ApplyAsync(message, cancellationToken);
    }
}
