using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Competitions.Configuration;

public sealed record CompetitionChallengeConfigurationSnapshot(
    Guid Id,
    CompetitionChallengeRules Rules,
    NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset? ScorePreset = null);

public sealed record ChallengeConfigurationSections(
    CompetitionChallengeRules Rules,
    ChallengeDefinition Definition);

public sealed record CompetitionConfigurationView(
    Guid CompetitionId,
    GameMode Mode,
    CompetitionModeConfiguration Configuration,
    CompetitionStatus CompetitionStatus,
    int EligibleTeamCount,
    IReadOnlyList<CompetitionChallengeConfigurationSnapshot> ChallengeConfigurations,
    DateTimeOffset UpdatedAt);

public interface ICompetitionConfigurationValidator
{
    IReadOnlyList<string> Validate(
        GameMode mode,
        CompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<CompetitionChallengeRules> challengeRules);

    IReadOnlyList<string> ValidateForStart(
        GameMode mode,
        CompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<CompetitionChallengeRules> challengeRules) =>
        Validate(mode, configuration, eligibleTeamCount, challengeRules);

    IReadOnlyList<string> ValidateForStart(
        GameMode mode,
        CompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<ChallengeConfigurationSections> challenges) =>
        ValidateForStart(
            mode,
            configuration,
            eligibleTeamCount,
            challenges.Select(challenge => challenge.Rules).ToArray());
}

public interface ICompetitionConfigurationStore
{
    Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        CompetitionModeConfiguration configuration,
        bool allowWhileRunning,
        DateTimeOffset now,
        CancellationToken cancellationToken, AwdConfigurationChangeAudit? audit = null);
}

public enum CompetitionConfigurationUpdateFailure
{
    CompetitionNotFound,
    ConfigurationLocked,
    RuleChangeReasonRequired
}

public enum CompetitionConfigurationFailureCode
{
    CompetitionNotFound,
    InvalidConfiguration,
    ConfigurationLocked,
    RuleChangeReasonRequired
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
        Guid competitionId,
        CompetitionModeConfiguration configuration,
        DateTimeOffset now,
        CancellationToken ct = default, AwdConfigurationChangeAudit? audit = null)
    {
        var current = await store.FindAsync(competitionId, ct);
        if (current is null)
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(
                CompetitionConfigurationFailureCode.CompetitionNotFound,
                "Competition was not found.");
        if (current.Mode == GameMode.Awd
            && current.CompetitionStatus is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished
            && audit?.HasValidReason != true)
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(
                CompetitionConfigurationFailureCode.RuleChangeReasonRequired, "Provide a reason for changing active AWD rules.");
        var errors = validator.Validate(
            current.Mode,
            configuration,
            current.EligibleTeamCount,
            current.ChallengeConfigurations.Select(challenge => NoCTF.Domain.Competitions.Scoring.CtfScoreCurveResolver
                .ForValidation(challenge.Rules, challenge.ScorePreset)).ToArray());
        if (errors.Count > 0)
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(
                CompetitionConfigurationFailureCode.InvalidConfiguration,
                string.Join(" ", errors));
        const bool allowWhileRunning = true;
        var result = await store.TryUpdateAsync(
            competitionId,
            configuration,
            allowWhileRunning,
            now,
            ct, audit);
        if (result.Configuration is null)
        {
            var failure = result.Failure ?? CompetitionConfigurationUpdateFailure.CompetitionNotFound;
            return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Failure(failure switch
            {
                CompetitionConfigurationUpdateFailure.RuleChangeReasonRequired => CompetitionConfigurationFailureCode.RuleChangeReasonRequired,
                CompetitionConfigurationUpdateFailure.CompetitionNotFound => CompetitionConfigurationFailureCode.CompetitionNotFound,
                _ => CompetitionConfigurationFailureCode.ConfigurationLocked
            }, failure switch
            {
                CompetitionConfigurationUpdateFailure.RuleChangeReasonRequired => "Provide a reason for changing active AWD rules.",
                CompetitionConfigurationUpdateFailure.CompetitionNotFound => "Competition was not found.",
                _ => "The configuration change is not allowed in the current competition state."
            });
        }
        return OperationResult<CompetitionConfigurationView, CompetitionConfigurationFailureCode>.Success(result.Configuration);
    }
}
