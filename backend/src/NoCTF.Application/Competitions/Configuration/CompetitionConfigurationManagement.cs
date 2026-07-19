using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Configuration;

public sealed record CompetitionConfigurationView(
    Guid CompetitionId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset UpdatedAt);

public interface ICompetitionConfigurationValidator
{
    IReadOnlyList<string> Validate(GameMode mode, string json);
}

public interface ICompetitionConfigurationStore
{
    Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<CompetitionConfigurationView?> TryUpdateAsync(Guid competitionId, int expectedRevision, string json, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class GetCompetitionConfiguration(ICompetitionConfigurationStore store)
{
    public Task<CompetitionConfigurationView?> ExecuteAsync(Guid competitionId, CancellationToken ct = default) => store.FindAsync(competitionId, ct);
}

public sealed class UpdateCompetitionConfiguration(
    ICompetitionConfigurationStore store,
    ICompetitionConfigurationValidator validator,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult<CompetitionConfigurationView>> ExecuteAsync(
        Guid competitionId, int expectedRevision, string json, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await store.FindAsync(competitionId, ct);
        if (current is null) return OperationResult<CompetitionConfigurationView>.Failure("competition_not_found", "Competition was not found.");
        if (current.CompetitionStatus is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return OperationResult<CompetitionConfigurationView>.Failure("configuration_locked", "Active or finished competition configuration is read-only.");
        var errors = validator.Validate(current.Mode, json);
        if (errors.Count > 0)
            return OperationResult<CompetitionConfigurationView>.Failure("invalid_configuration", string.Join(" ", errors));
        var updated = await store.TryUpdateAsync(competitionId, expectedRevision, json, now, ct);
        if (updated is null)
            return OperationResult<CompetitionConfigurationView>.Failure("configuration_conflict", "Configuration revision changed concurrently.");
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult<CompetitionConfigurationView>.Success(updated);
    }
}
