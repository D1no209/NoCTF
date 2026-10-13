using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.SignalR.Hubs;

public interface ISpectatorHubClient
{
    [HubMethodName("spectatorChanged")]
    Task Changed(Guid competitionId, CancellationToken ct);
}

/// <summary>Public invalidations only. This hub has no user/staff groups or fact payloads.</summary>
[AllowAnonymous]
public sealed class SpectatorHub(ICompetitionVisibilityAccess access, TimeProvider clock) : Hub<ISpectatorHubClient>
{
    private static readonly object JoinedCompetitionKey = new();
    public static string Group(Guid competitionId) => $"spectator:{competitionId:N}";

    public async Task JoinSpectator(Guid competitionId)
    {
        var permission = await access.ResolveAsync(Guid.Empty, competitionId, clock.GetUtcNow(), Context.ConnectionAborted);
        if (permission?.GameMode is not (GameMode.Awd or GameMode.Koh))
            throw new HubException("This public spectator screen is unavailable.");
        if (Context.Items.TryGetValue(JoinedCompetitionKey, out var previous) && previous is Guid previousId)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(previousId), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(competitionId), Context.ConnectionAborted);
        Context.Items[JoinedCompetitionKey] = competitionId;
    }
}

/// <summary>Fan out only the competition identity to the anonymous screen.</summary>
internal sealed class SpectatorInvalidatingClient(ICompetitionHubClient audience, ISpectatorHubClient spectators)
    : ICompetitionHubClient
{
    public async Task ScoreboardUpdated(NoCTF.Application.Notifications.ScoreboardUpdated notification, CancellationToken ct)
    {
        await spectators.Changed(notification.CompetitionId, ct);
        await audience.ScoreboardUpdated(notification, ct);
    }
    public async Task CompetitionLifecycleChanged(CompetitionLifecycleChangedNotification notification, CancellationToken ct)
    {
        await spectators.Changed(notification.CompetitionId, ct);
        await audience.CompetitionLifecycleChanged(notification, ct);
    }
    public async Task CompetitionEventChanged(CompetitionEventChangedNotification notification, CancellationToken ct)
    {
        await spectators.Changed(notification.CompetitionId, ct);
        await audience.CompetitionEventChanged(notification, ct);
    }
    public Task GameplayFactStateChanged(GameplayFactStatusResponse notification, CancellationToken ct) =>
        audience.GameplayFactStateChanged(notification, ct);
}
