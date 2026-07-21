using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Configuration;

public sealed record ChallengeConfigurationView(
    Guid CompetitionId,
    Guid ChallengeId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset UpdatedAt);

public interface IChallengeConfigurationCatalog
{
    string GetDefaultJson(GameMode mode);
    IReadOnlyList<string> Validate(GameMode mode, string json);
}

public interface IChallengeConfigurationStore
{
    Task<ChallengeConfigurationView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken cancellationToken);

    Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        int expectedRevision,
        string json,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}

public enum ChallengeConfigurationUpdateFailure
{
    CompetitionNotFound,
    ConfigurationLocked,
    ChallengeNotFound,
    RevisionConflict
}
public sealed record ChallengeConfigurationUpdateResult(
    ChallengeConfigurationView? Configuration,
    ChallengeConfigurationUpdateFailure? Failure = null);

public sealed class GetChallengeConfiguration(IChallengeConfigurationStore store)
{
    public Task<ChallengeConfigurationView?> ExecuteAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, challengeId, ct);
}

public sealed class UpdateChallengeConfiguration(
    IChallengeConfigurationStore store,
    IChallengeConfigurationCatalog catalog,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult<ChallengeConfigurationView>> ExecuteAsync(
        Guid competitionId,
        Guid challengeId,
        int expectedRevision,
        string json,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        if (expectedRevision < 0)
            return OperationResult<ChallengeConfigurationView>.Failure(
                "invalid_revision",
                "Expected revision cannot be negative.");
        if (string.IsNullOrWhiteSpace(json))
            return OperationResult<ChallengeConfigurationView>.Failure(
                "invalid_configuration",
                "Challenge configuration is required.");

        var current = await store.FindAsync(competitionId, challengeId, ct);
        if (current is null)
            return OperationResult<ChallengeConfigurationView>.Failure(
                "challenge_not_found",
                "Challenge was not found.");
        if (current.CompetitionStatus is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return OperationResult<ChallengeConfigurationView>.Failure(
                "configuration_locked",
                "Active or finished challenge configuration is read-only.");

        var errors = catalog.Validate(current.Mode, json);
        if (errors.Count > 0)
            return OperationResult<ChallengeConfigurationView>.Failure(
                "invalid_configuration",
                string.Join(" ", errors));

        var result = await store.TryUpdateAsync(
            competitionId,
            challengeId,
            expectedRevision,
            json,
            updatedAt,
            ct);
        if (result.Configuration is null)
        {
            var failure = result.Failure ?? ChallengeConfigurationUpdateFailure.RevisionConflict;
            return OperationResult<ChallengeConfigurationView>.Failure(failure switch
            {
                ChallengeConfigurationUpdateFailure.CompetitionNotFound => "competition_not_found",
                ChallengeConfigurationUpdateFailure.ConfigurationLocked => "configuration_locked",
                ChallengeConfigurationUpdateFailure.ChallengeNotFound => "challenge_not_found",
                _ => "configuration_conflict"
            }, failure switch
            {
                ChallengeConfigurationUpdateFailure.CompetitionNotFound => "Competition was not found.",
                ChallengeConfigurationUpdateFailure.ConfigurationLocked => "Active or finished challenge configuration is read-only.",
                ChallengeConfigurationUpdateFailure.ChallengeNotFound => "Challenge was not found.",
                _ => "Challenge configuration revision changed concurrently."
            });
        }

        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult<ChallengeConfigurationView>.Success(result.Configuration);
    }
}
