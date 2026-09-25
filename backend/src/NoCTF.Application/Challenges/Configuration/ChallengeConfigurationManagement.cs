using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Configuration;

public sealed record ChallengeConfigurationView(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    CompetitionChallengeRules Rules,
    CompetitionModeConfiguration CompetitionConfiguration,
    CompetitionStatus CompetitionStatus,
    int EligibleTeamCount,
    DateTimeOffset UpdatedAt,
    ChallengeDefinition Definition);

public interface IChallengeConfigurationCatalog
{
    CompetitionChallengeRules CreateDefaultRules(GameMode mode, Guid competitionChallengeId);
    ChallengeDefinition CreateDefaultDefinition(GameMode mode, Guid challengeId);

    IReadOnlyList<string> Validate(
        GameMode mode,
        CompetitionChallengeRules rules,
        CompetitionModeConfiguration competitionConfiguration,
        int eligibleTeamCount);

    IReadOnlyList<string> ValidateRules(
        GameMode mode,
        CompetitionChallengeRules rules,
        CompetitionModeConfiguration competitionConfiguration,
        int eligibleTeamCount) =>
        Validate(mode, rules, competitionConfiguration, eligibleTeamCount);

    IReadOnlyList<string> ValidateDefinition(
        GameMode mode,
        ChallengeDefinition definition);

    IReadOnlyList<string> ValidateDefinitionForStart(
        GameMode mode,
        ChallengeDefinition definition) =>
        ValidateDefinition(mode, definition);

    IReadOnlyList<string> ValidateRulesForDefinition(
        GameMode mode,
        CompetitionChallengeRules rules,
        ChallengeDefinition definition,
        CompetitionModeConfiguration competitionConfiguration,
        int eligibleTeamCount) =>
        ValidateRules(mode, rules, competitionConfiguration, eligibleTeamCount);
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
        CompetitionChallengeRules rules,
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
        CompetitionChallengeRules rules,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        var current = await store.FindAsync(competitionId, competitionChallengeId, ct);
        if (current is null)
            return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Failure(
                ChallengeConfigurationFailureCode.ChallengeNotFound,
                "Challenge was not found.");
        var errors = catalog.ValidateRulesForDefinition(
            current.Mode,
            rules,
            current.Definition,
            current.CompetitionConfiguration,
            current.EligibleTeamCount);
        if (errors.Count > 0)
            return OperationResult<ChallengeConfigurationView, ChallengeConfigurationFailureCode>.Failure(
                ChallengeConfigurationFailureCode.InvalidConfiguration,
                string.Join(" ", errors));

        var result = await store.TryUpdateAsync(
            competitionId,
            competitionChallengeId,
            rules,
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
