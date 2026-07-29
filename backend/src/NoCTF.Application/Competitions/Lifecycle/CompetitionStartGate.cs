using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record StartGateChallenge(
    Guid CompetitionChallengeId,
    GameMode ChallengeMode,
    string RulesJson,
    string DefinitionJson,
    bool Published);

public sealed record CompetitionStartGateSnapshot(
    Guid CompetitionId,
    GameMode Mode,
    CompetitionStatus Status,
    string ConfigurationJson,
    IReadOnlyList<StartGateChallenge> Challenges,
    int ApprovedTeamCount);

public sealed record StartGateError(
    string Code,
    Guid? CompetitionChallengeId,
    string Message);

public interface ICompetitionStartGateStore
{
    Task<CompetitionStartGateSnapshot?> LoadAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}

public sealed class CompetitionStartGate(
    ICompetitionStartGateStore store,
    ICompetitionConfigurationValidator competitionConfigurations,
    IChallengeConfigurationCatalog challengeConfigurations)
{
    public async Task<IReadOnlyList<StartGateError>?> ValidateAsync(
        Guid competitionId,
        CancellationToken ct = default)
    {
        var snapshot = await store.LoadAsync(competitionId, ct);
        if (snapshot is null)
            return null;
        var errors = new List<StartGateError>();
        if (snapshot.Status != CompetitionStatus.Published)
            errors.Add(new(
                "competition_not_published",
                null,
                "The competition must be Published before it can start."));
        var publishedChallengeConfigurations = snapshot.Challenges
            .Where(challenge => challenge.Published)
            .Select(challenge => new ChallengeConfigurationSections(
                challenge.RulesJson,
                challenge.DefinitionJson))
            .ToArray();
        foreach (var message in competitionConfigurations.ValidateForStart(
                     snapshot.Mode,
                     snapshot.ConfigurationJson,
                     snapshot.ApprovedTeamCount,
                     publishedChallengeConfigurations))
            errors.Add(new("competition_configuration_invalid", null, message));
        if (!snapshot.Challenges.Any(challenge => challenge.Published))
            errors.Add(new(
                "published_challenge_required",
                null,
                "At least one CompetitionChallenge must be published."));
        if (snapshot.ApprovedTeamCount == 0)
            errors.Add(new(
                "approved_team_required",
                null,
                "At least one approved team is required."));
        foreach (var challenge in snapshot.Challenges.Where(item => item.Published))
        {
            if (challenge.ChallengeMode != snapshot.Mode)
            {
                errors.Add(new(
                    "challenge_mode_mismatch",
                    challenge.CompetitionChallengeId,
                    $"Challenge mode {challenge.ChallengeMode} does not match competition mode {snapshot.Mode}."));
                continue;
            }
            foreach (var message in challengeConfigurations.ValidateRules(
                         snapshot.Mode,
                         challenge.RulesJson,
                         snapshot.ConfigurationJson,
                         snapshot.ApprovedTeamCount))
                errors.Add(new(
                    "challenge_rules_invalid",
                    challenge.CompetitionChallengeId,
                    message));
            foreach (var message in challengeConfigurations.ValidateDefinition(
                         snapshot.Mode,
                         challenge.DefinitionJson))
                errors.Add(new(
                    "challenge_definition_invalid",
                    challenge.CompetitionChallengeId,
                    message));
        }
        return errors
            .DistinctBy(error => (
                error.Code,
                error.CompetitionChallengeId,
                error.Message))
            .OrderBy(error => error.Code, StringComparer.Ordinal)
            .ThenBy(error => error.CompetitionChallengeId)
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToArray();
    }
}
