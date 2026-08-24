using NoCTF.Application.Messaging;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineBackendMessagePublisher(IMessageBus bus) : IBackendMessagePublisher
{
    public async ValueTask ProjectLeaderboardAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new ProjectLeaderboard(competitionId));

    public async ValueTask ApplyCompetitionVisibilityAsync(
        Guid competitionId,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new ApplyCompetitionVisibility(
            competitionId,
            scheduledAt));

    public async ValueTask RebuildCompetitionAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new ProjectLeaderboard(competitionId));

    public async ValueTask CleanupCompetitionRuntimesAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new CleanupCompetitionRuntimes(competitionId));

    public async ValueTask ProvisionCompetitionRuntimesAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        await bus.SendAsync(new ProvisionCompetitionRuntimes(competitionId));
}
