using NoCTF.Application.Messaging;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Configuration;

public sealed record ChallengeConfigurationView(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    string Json,
    string CompetitionConfigurationJson,
    int CompetitionConfigurationRevision,
    int Revision,
    CompetitionStatus CompetitionStatus,
    int EligibleTeamCount,
    DateTimeOffset UpdatedAt);

public interface IChallengeConfigurationCatalog
{
    string GetDefaultJson(GameMode mode);
    string GetDefaultDefinitionJson(GameMode mode) => GetDefaultJson(mode);

    IReadOnlyList<string> Validate(
        GameMode mode,
        string json,
        string competitionConfigurationJson,
        int eligibleTeamCount);

    IReadOnlyList<string> ValidateRules(
        GameMode mode,
        string json,
        string competitionConfigurationJson,
        int eligibleTeamCount) =>
        Validate(mode, json, competitionConfigurationJson, eligibleTeamCount);

    IReadOnlyList<string> ValidateDefinition(
        GameMode mode,
        string json) =>
        Validate(mode, json, "{}", 1);
}

public interface IChallengeConfigurationStore
{
    Task<ChallengeConfigurationView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken);

    Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        int expectedCompetitionConfigurationRevision,
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
        Guid competitionChallengeId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, competitionChallengeId, ct);
}

public sealed class UpdateChallengeConfiguration(
    IChallengeConfigurationStore store,
    IChallengeConfigurationCatalog catalog,
    ILeaderboardCache cache,
    IBackendMessagePublisher messages)
{
    public async Task<OperationResult<ChallengeConfigurationView>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
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

        var current = await store.FindAsync(competitionId, competitionChallengeId, ct);
        if (current is null)
            return OperationResult<ChallengeConfigurationView>.Failure(
                "challenge_not_found",
                "Challenge was not found.");
        var errors = catalog.ValidateRules(
            current.Mode,
            json,
            current.CompetitionConfigurationJson,
            current.EligibleTeamCount);
        if (errors.Count > 0)
            return OperationResult<ChallengeConfigurationView>.Failure(
                "invalid_configuration",
                string.Join(" ", errors));

        var result = await store.TryUpdateAsync(
            competitionId,
            competitionChallengeId,
            expectedRevision,
            current.CompetitionConfigurationRevision,
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
        await messages.RebuildCompetitionAsync(competitionId, ct);
        return OperationResult<ChallengeConfigurationView>.Success(result.Configuration);
    }
}
