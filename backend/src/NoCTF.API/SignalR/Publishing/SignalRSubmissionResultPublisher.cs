using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Notifications;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.Endpoints.GameplayFacts;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRGameplayFactStatePublisher(
    IHubContext<CompetitionHub, ICompetitionHubClient> hub, MfaConnectionGuard guard) : IGameplayFactStatePublisher
{
    public async Task PublishAsync(Guid userId, GameplayFactStatusView result, CancellationToken cancellationToken) =>
        await hub.Clients.Clients(await guard.EligibleAsync(MfaHubKind.Competition, new HashSet<Guid> { userId }, cancellationToken)).GameplayFactStateChanged(
            GameplayFactMapper.ToStatusResponse(result),
            cancellationToken);
}
