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
    Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(Guid competitionId, int expectedRevision, string json, DateTimeOffset now, CancellationToken cancellationToken);
}

public enum CompetitionConfigurationUpdateFailure
{
    CompetitionNotFound,
    ConfigurationLocked,
    RevisionConflict
}
public sealed record CompetitionConfigurationUpdateResult(
    CompetitionConfigurationView? Configuration,
    CompetitionConfigurationUpdateFailure? Failure = null);

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
        var result = await store.TryUpdateAsync(competitionId, expectedRevision, json, now, ct);
        if (result.Configuration is null)
        {
            var failure = result.Failure ?? CompetitionConfigurationUpdateFailure.RevisionConflict;
            return OperationResult<CompetitionConfigurationView>.Failure(failure switch
            {
                CompetitionConfigurationUpdateFailure.CompetitionNotFound => "competition_not_found",
                CompetitionConfigurationUpdateFailure.ConfigurationLocked => "configuration_locked",
                _ => "configuration_conflict"
            }, failure switch
            {
                CompetitionConfigurationUpdateFailure.CompetitionNotFound => "Competition was not found.",
                CompetitionConfigurationUpdateFailure.ConfigurationLocked => "Active or finished competition configuration is read-only.",
                _ => "Configuration revision changed concurrently."
            });
        }
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult<CompetitionConfigurationView>.Success(result.Configuration);
    }
}
