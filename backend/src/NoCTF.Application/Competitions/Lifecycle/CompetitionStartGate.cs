using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Challenges.Images;

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
    int ApprovedTeamCount,
    int MaxConcurrentRuntimeInstancesPerTeam);

public enum StartGateFailureCode
{
    CompetitionNotPublished,
    CompetitionConfigurationInvalid,
    PublishedChallengeRequired,
    ApprovedTeamRequired,
    RuntimeQuotaInsufficient,
    ChallengeModeMismatch,
    ChallengeRulesInvalid,
    RuntimeDefinitionInvalid,
    RuntimeImageNotPinned
}

public sealed record StartGateError(
    StartGateFailureCode Code,
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
    IChallengeConfigurationCatalog challengeConfigurations,
    IChallengeImageDefinitionCatalog? imageDefinitions = null)
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
                StartGateFailureCode.CompetitionNotPublished,
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
            errors.Add(new(StartGateFailureCode.CompetitionConfigurationInvalid, null, message));
        if (!snapshot.Challenges.Any(challenge => challenge.Published))
            errors.Add(new(
                StartGateFailureCode.PublishedChallengeRequired,
                null,
                "At least one CompetitionChallenge must be published."));
        if (snapshot.ApprovedTeamCount == 0)
            errors.Add(new(
                StartGateFailureCode.ApprovedTeamRequired,
                null,
                "At least one approved team is required."));
        var publishedChallengeCount = snapshot.Challenges.Count(challenge => challenge.Published);
        if (snapshot.Mode == GameMode.Awd
            && snapshot.MaxConcurrentRuntimeInstancesPerTeam > 0
            && publishedChallengeCount > snapshot.MaxConcurrentRuntimeInstancesPerTeam)
        {
            errors.Add(new(
                StartGateFailureCode.RuntimeQuotaInsufficient,
                null,
                $"MaxConcurrentRuntimeInstancesPerTeam must be at least {publishedChallengeCount} for the published AWD challenges."));
        }
        foreach (var challenge in snapshot.Challenges.Where(item => item.Published))
        {
            if (challenge.ChallengeMode != snapshot.Mode)
            {
                errors.Add(new(
                    StartGateFailureCode.ChallengeModeMismatch,
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
                    StartGateFailureCode.ChallengeRulesInvalid,
                    challenge.CompetitionChallengeId,
                    message));
            foreach (var message in challengeConfigurations.ValidateDefinitionForStart(
                         snapshot.Mode,
                         challenge.DefinitionJson))
                errors.Add(new(
                    StartGateFailureCode.RuntimeDefinitionInvalid,
                    challenge.CompetitionChallengeId,
                    message));
            if (imageDefinitions is not null)
            {
                var definition = imageDefinitions.Read(
                    snapshot.Mode,
                    challenge.DefinitionJson);
                if (definition.Succeeded)
                {
                    foreach (var image in definition.Images!.Where(item =>
                                 !ContainerImageReference.TryParse(item.Image, out var parsed)
                                 || !parsed.IsDigest))
                    {
                        errors.Add(new(
                            StartGateFailureCode.RuntimeImageNotPinned,
                            challenge.CompetitionChallengeId,
                            "Every Runtime and Checker image must be pinned to a sha256 digest before the competition can start."));
                    }
                }
            }
        }
        return errors
            .DistinctBy(error => (
                error.Code,
                error.CompetitionChallengeId,
                error.Message))
            .OrderBy(error => error.Code)
            .ThenBy(error => error.CompetitionChallengeId)
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToArray();
    }
}
