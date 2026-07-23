using NoCTF.Application.Messaging;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Configuration;

public sealed record CompetitionChallengeConfigurationSnapshot(Guid Id, int Revision, string Json);

public sealed record CompetitionConfigurationView(
    Guid CompetitionId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    int EligibleTeamCount,
    IReadOnlyList<CompetitionChallengeConfigurationSnapshot> ChallengeConfigurations,
    DateTimeOffset UpdatedAt);

public interface ICompetitionConfigurationValidator
{
    IReadOnlyList<string> Validate(
        GameMode mode,
        string json,
        int eligibleTeamCount,
        IReadOnlyList<string> challengeConfigurationJsons);
}

public interface ICompetitionConfigurationStore
{
    Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        int expectedRevision,
        string json,
        bool allowWhileRunning,
        IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
        DateTimeOffset now,
        CancellationToken cancellationToken);
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
    IBackendMessagePublisher messages)
{
    public async Task<OperationResult<CompetitionConfigurationView>> ExecuteAsync(
        Guid competitionId, int expectedRevision, string json, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await store.FindAsync(competitionId, ct);
        if (current is null) return OperationResult<CompetitionConfigurationView>.Failure("competition_not_found", "Competition was not found.");
        var errors = validator.Validate(
            current.Mode,
            json,
            current.EligibleTeamCount,
            current.ChallengeConfigurations.Select(challenge => challenge.Json).ToArray());
        if (errors.Count > 0)
            return OperationResult<CompetitionConfigurationView>.Failure("invalid_configuration", string.Join(" ", errors));
        const bool allowWhileRunning = true;
        var result = await store.TryUpdateAsync(
            competitionId,
            expectedRevision,
            json,
            allowWhileRunning,
            current.ChallengeConfigurations.ToDictionary(challenge => challenge.Id, challenge => challenge.Revision),
            now,
            ct);
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
                CompetitionConfigurationUpdateFailure.ConfigurationLocked => "The configuration change is not allowed in the current competition state.",
                _ => "Configuration revision changed concurrently."
            });
        }
        await cache.InvalidateAsync(competitionId, ct);
        await messages.RebuildCompetitionAsync(competitionId, ct);
        return OperationResult<CompetitionConfigurationView>.Success(result.Configuration);
    }
}
