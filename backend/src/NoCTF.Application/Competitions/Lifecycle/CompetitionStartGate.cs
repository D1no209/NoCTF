using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record StartGateChallenge(
    Guid CompetitionChallengeId,
    string ConfigurationJson,
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
        foreach (var message in competitionConfigurations.Validate(
                     snapshot.Mode, snapshot.ConfigurationJson))
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
            foreach (var message in challengeConfigurations.Validate(
                         snapshot.Mode, challenge.ConfigurationJson))
                errors.Add(new(
                    "challenge_configuration_invalid",
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
