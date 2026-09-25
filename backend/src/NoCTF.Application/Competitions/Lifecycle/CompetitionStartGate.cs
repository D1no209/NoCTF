using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Scoring;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record StartGateChallenge(
    Guid CompetitionChallengeId,
    GameMode ChallengeMode,
    CompetitionChallengeRules Rules,
    ChallengeDefinition Definition,
    bool Published,
    IReadOnlyList<long> HintCosts);

public sealed record CompetitionStartGateSnapshot(
    Guid CompetitionId,
    GameMode Mode,
    CompetitionStatus Status,
    CompetitionModeConfiguration Configuration,
    IReadOnlyList<StartGateChallenge> Challenges,
    int ApprovedTeamCount,
    int MaxConcurrentRuntimeInstancesPerTeam,
    IReadOnlyList<CompetitionTrackDefinition>? Tracks = null,
    IReadOnlyList<string>? ApprovedTeamTrackKeys = null,
    bool TracksEnabled = false);

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
    TrackConfigurationInvalid,
    TeamTrackInvalid,
    ExperimentalFeatureDisabled
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
    IExperimentalFeatureReader? experimentalFeatures = null)
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
        var hasPatchVerification = snapshot.Mode == GameMode.Ctf
            && snapshot.Challenges.Any(challenge => challenge.Published
                && challenge.Definition is CtfChallengeDefinition
                    { InteractionKind: CtfInteractionKind.PatchVerification });
        if (hasPatchVerification
            && !(await IsPatchVerificationEnabledAsync(ct)))
        {
            errors.Add(new(
                StartGateFailureCode.ExperimentalFeatureDisabled,
                null,
                "CTF PatchVerification is disabled in platform settings."));
        }
        var publishedChallengeConfigurations = snapshot.Challenges
            .Where(challenge => challenge.Published)
            .Select(challenge => new ChallengeConfigurationSections(
                challenge.Rules,
                challenge.Definition))
            .ToArray();
        foreach (var message in competitionConfigurations.ValidateForStart(
                     snapshot.Mode,
                     snapshot.Configuration,
                     snapshot.ApprovedTeamCount,
                     publishedChallengeConfigurations))
            errors.Add(new(StartGateFailureCode.CompetitionConfigurationInvalid, null, message));
        CompetitionTrackConfiguration trackConfiguration;
        try
        {
            trackConfiguration = CompetitionTrackConfiguration.FromPersisted(
                snapshot.Mode,
                snapshot.Tracks);
            foreach (var message in CompetitionTrackPolicy.Validate(snapshot.Mode, trackConfiguration))
                errors.Add(new(StartGateFailureCode.TrackConfigurationInvalid, null, message));
            foreach (var trackKey in snapshot.TracksEnabled
                         ? snapshot.ApprovedTeamTrackKeys ?? []
                         : [])
            {
                if (trackConfiguration.Find(trackKey) is null)
                {
                    errors.Add(new(
                        StartGateFailureCode.TeamTrackInvalid,
                        null,
                        $"Approved team references missing track '{trackKey}'."));
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            errors.Add(new(
                StartGateFailureCode.TrackConfigurationInvalid,
                null,
                exception.Message));
        }
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
            if (challenge.HintCosts.Any(cost =>
                    cost is < 0 or > ScoreValueLimits.MaximumConfiguredValue))
            {
                errors.Add(new(
                    StartGateFailureCode.ChallengeRulesInvalid,
                    challenge.CompetitionChallengeId,
                    $"Hint cost must be between zero and {ScoreValueLimits.MaximumConfiguredValue}."));
            }
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
                         challenge.Rules,
                         snapshot.Configuration,
                         snapshot.ApprovedTeamCount))
                errors.Add(new(
                    StartGateFailureCode.ChallengeRulesInvalid,
                    challenge.CompetitionChallengeId,
                    message));
            foreach (var message in challengeConfigurations.ValidateDefinitionForStart(
                         snapshot.Mode,
                         challenge.Definition))
                errors.Add(new(
                    StartGateFailureCode.RuntimeDefinitionInvalid,
                    challenge.CompetitionChallengeId,
                    message));
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

    private Task<bool> IsPatchVerificationEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);
}
