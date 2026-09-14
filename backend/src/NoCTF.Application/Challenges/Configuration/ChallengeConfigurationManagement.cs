using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Configuration;

public sealed record ChallengeConfigurationView(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    string Json,
    string CompetitionConfigurationJson,
    CompetitionStatus CompetitionStatus,
    int EligibleTeamCount,
    DateTimeOffset UpdatedAt,
    string DefinitionJson = "{}");

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

    IReadOnlyList<string> ValidateDefinitionForStart(
        GameMode mode,
        string json) =>
        ValidateDefinition(mode, json);

    IReadOnlyList<string> ValidateRulesForDefinition(
        GameMode mode,
        string rulesJson,
        string definitionJson,
        string competitionConfigurationJson,
        int eligibleTeamCount) =>
        ValidateRules(mode, rulesJson, competitionConfigurationJson, eligibleTeamCount);
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
        string json,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}

public enum ChallengeConfigurationUpdateFailure
{
    CompetitionNotFound,
    ConfigurationLocked,
    ChallengeNotFound
}

public enum ChallengeConfigurationFailureCode
{
    InvalidConfiguration,
    CompetitionNotFound,
    ConfigurationLocked,
    ChallengeNotFound
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
    IChallengeConfigurationCatalog catalog)
{
    public async Task<OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        string json,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Failure(
                ChallengeConfigurationFailureCode.InvalidConfiguration,
                "Challenge configuration is required.");

        var current = await store.FindAsync(competitionId, competitionChallengeId, ct);
        if (current is null)
            return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Failure(
                ChallengeConfigurationFailureCode.ChallengeNotFound,
                "Challenge was not found.");
        var errors = catalog.ValidateRulesForDefinition(
            current.Mode,
            json,
            current.DefinitionJson,
            current.CompetitionConfigurationJson,
            current.EligibleTeamCount);
        if (errors.Count > 0)
            return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Failure(
                ChallengeConfigurationFailureCode.InvalidConfiguration,
                string.Join(" ", errors));

        var result = await store.TryUpdateAsync(
            competitionId,
            competitionChallengeId,
            json,
            updatedAt,
            ct);
        if (result.Configuration is null)
        {
            var failure = result.Failure ?? ChallengeConfigurationUpdateFailure.ChallengeNotFound;
            return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Failure(failure switch
            {
                ChallengeConfigurationUpdateFailure.CompetitionNotFound => ChallengeConfigurationFailureCode.CompetitionNotFound,
                ChallengeConfigurationUpdateFailure.ConfigurationLocked => ChallengeConfigurationFailureCode.ConfigurationLocked,
                _ => ChallengeConfigurationFailureCode.ChallengeNotFound
            }, failure switch
            {
                ChallengeConfigurationUpdateFailure.CompetitionNotFound => "Competition was not found.",
                ChallengeConfigurationUpdateFailure.ConfigurationLocked => "Active or finished challenge configuration is read-only.",
                _ => "Challenge was not found."
            });
        }

        return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Success(result.Configuration);
    }
}
