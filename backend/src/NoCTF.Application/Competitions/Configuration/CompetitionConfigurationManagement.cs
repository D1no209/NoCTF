using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Configuration;

public sealed record CompetitionChallengeConfigurationSnapshot(Guid Id, string Json);

public sealed record ChallengeConfigurationSections(
    string RulesJson,
    string DefinitionJson);

public sealed record CompetitionConfigurationView(
    Guid CompetitionId,
    GameMode Mode,
    string Json,
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

    IReadOnlyList<string> ValidateForStart(
        GameMode mode,
        string json,
        int eligibleTeamCount,
        IReadOnlyList<string> challengeConfigurationJsons) =>
        Validate(mode, json, eligibleTeamCount, challengeConfigurationJsons);

    IReadOnlyList<string> ValidateForStart(
        GameMode mode,
        string json,
        int eligibleTeamCount,
        IReadOnlyList<ChallengeConfigurationSections> challenges) =>
        ValidateForStart(
            mode,
            json,
            eligibleTeamCount,
            challenges.Select(challenge => challenge.RulesJson).ToArray());
}

public interface ICompetitionConfigurationStore
{
    Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        string json,
        bool allowWhileRunning,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public enum CompetitionConfigurationUpdateFailure
{
    CompetitionNotFound,
    ConfigurationLocked
}

public enum CompetitionConfigurationFailureCode
{
    CompetitionNotFound,
    InvalidConfiguration,
    ConfigurationLocked
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
    ICompetitionConfigurationValidator validator)
{
    public async Task<OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>> ExecuteAsync(
        Guid competitionId, string json, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await store.FindAsync(competitionId, ct);
        if (current is null)
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(
                CompetitionConfigurationFailureCode.CompetitionNotFound,
                "Competition was not found.");
        var errors = validator.Validate(
            current.Mode,
            json,
            current.EligibleTeamCount,
            current.ChallengeConfigurations.Select(challenge => challenge.Json).ToArray());
        if (errors.Count > 0)
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(
                CompetitionConfigurationFailureCode.InvalidConfiguration,
                string.Join(" ", errors));
        const bool allowWhileRunning = true;
        var result = await store.TryUpdateAsync(
            competitionId,
            json,
            allowWhileRunning,
            now,
            ct);
        if (result.Configuration is null)
        {
            var failure = result.Failure ?? CompetitionConfigurationUpdateFailure.CompetitionNotFound;
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(failure switch
            {
                CompetitionConfigurationUpdateFailure.CompetitionNotFound => CompetitionConfigurationFailureCode.CompetitionNotFound,
                _ => CompetitionConfigurationFailureCode.ConfigurationLocked
            }, failure switch
            {
                CompetitionConfigurationUpdateFailure.CompetitionNotFound => "Competition was not found.",
                _ => "The configuration change is not allowed in the current competition state."
            });
        }
        return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Success(result.Configuration);
    }
}
