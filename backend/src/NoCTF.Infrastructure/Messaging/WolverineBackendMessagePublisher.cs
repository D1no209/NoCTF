using NoCTF.Application.Messaging;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineBackendMessagePublisher(IMessageBus bus) : IBackendMessagePublisher
{
    public async ValueTask ProjectLeaderboardAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new ProjectLeaderboard(competitionId));

    public ValueTask RebuildCompetitionAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public async ValueTask CleanupCompetitionRuntimesAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new CleanupCompetitionRuntimes(competitionId));

    public async ValueTask ProvisionCompetitionRuntimesAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new ProvisionCompetitionRuntimes(competitionId));
}
