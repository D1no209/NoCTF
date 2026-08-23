using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.Application.Notifications;

namespace NoCTF.API.SignalR.Hubs;

public sealed record CompetitionLifecycleChangedNotification(
    Guid CompetitionId,
    CompetitionStatusProtocol From,
    CompetitionStatusProtocol To,
    DateTimeOffset OccurredAt);

public sealed record CompetitionEventChangedNotification(
    Guid CompetitionId,
    Guid EventId,
    CompetitionEventKindProtocol Kind,
    CompetitionEventLevelProtocol Level,
    DateTimeOffset OccurredAt);

public interface ICompetitionHubClient
{
    [HubMethodName("scoreboardUpdated")]
    Task ScoreboardUpdated(
        ScoreboardUpdated notification,
        CancellationToken cancellationToken);

    [HubMethodName("competitionLifecycleChanged")]
    Task CompetitionLifecycleChanged(
        CompetitionLifecycleChangedNotification notification,
        CancellationToken cancellationToken);

    [HubMethodName("competitionEventChanged")]
    Task CompetitionEventChanged(
        CompetitionEventChangedNotification notification,
        CancellationToken cancellationToken);

    [HubMethodName("gameplayFactStateChanged")]
    Task GameplayFactStateChanged(
        GameplayFactStatusResponse notification,
        CancellationToken cancellationToken);
}
