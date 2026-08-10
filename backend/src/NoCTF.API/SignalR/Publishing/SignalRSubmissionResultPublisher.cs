using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Notifications;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.API.SignalR.Hubs;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRGameplayFactStatePublisher(
    IHubContext<CompetitionHub> hub) : IGameplayFactStatePublisher
{
    public Task PublishAsync(Guid userId, GameplayFactStatusView result, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync("gameplayFactStateChanged", result, cancellationToken);
}
