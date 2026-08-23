namespace NoCTF.Application.Messaging;

/// <summary>
/// Publishes durable commands whose handlers run in the Worker process.
/// Transactional persistence adapters should prefer the EF Core outbox directly.
/// </summary>
public interface IBackendMessagePublisher
{
    ValueTask ProjectLeaderboardAsync(Guid competitionId, CancellationToken cancellationToken);
    ValueTask ApplyCompetitionVisibilityAsync(
        Guid competitionId,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
    ValueTask RebuildCompetitionAsync(Guid competitionId, CancellationToken cancellationToken);
    ValueTask CleanupCompetitionRuntimesAsync(
        Guid competitionId,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
    ValueTask ProvisionCompetitionRuntimesAsync(
        Guid competitionId,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
