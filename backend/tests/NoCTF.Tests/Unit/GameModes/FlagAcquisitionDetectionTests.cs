using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.GameplayFact;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class FlagAcquisitionDetectionTests
{
    [Test]
    [Arguments(FlagAcquisitionResource.Container, GameplayFactFailureCode.StaticFlagWithoutContainer)]
    [Arguments(FlagAcquisitionResource.Attachment, GameplayFactFailureCode.StaticFlagWithoutAttachment)]
    [Arguments(FlagAcquisitionResource.Container | FlagAcquisitionResource.Attachment, GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment)]
    public async Task Correct_static_flag_without_required_access_is_rejected(FlagAcquisitionResource required, GameplayFactFailureCode expected)
    {
        var context = Context(new() { Scope = FlagAcquisitionScope.FormalStaticCtf, Required = required });
        var decision = new CtfGameplayFactEvaluator(new Fixed(GameplayFactResult.Correct)).Evaluate(context);
        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode).IsEqualTo(expected);
        await Assert.That(decision.VictimTeamId).IsNull();
    }

    [Test]
    public async Task Wrong_guesses_and_legacy_or_nonapplicable_attempts_keep_normal_outcomes()
    {
        var missing = new FlagAcquisitionEvidence { Scope = FlagAcquisitionScope.FormalStaticCtf, Required = FlagAcquisitionResource.Container };
        await Assert.That(new CtfGameplayFactEvaluator(new Fixed(GameplayFactResult.Wrong)).Evaluate(Context(missing)).Result)
            .IsEqualTo(GameplayFactResult.Wrong);
        foreach (var evidence in new FlagAcquisitionEvidence?[] { null, new(), new()
        {
            Scope = FlagAcquisitionScope.FormalStaticCtf, Source = FlagAcquisitionEvidenceSource.LegacySubmission,
            Required = FlagAcquisitionResource.Container | FlagAcquisitionResource.Attachment,
            Acquired = FlagAcquisitionResource.Container | FlagAcquisitionResource.Attachment
        } })
            await Assert.That(new CtfGameplayFactEvaluator(new Fixed(GameplayFactResult.Correct)).Evaluate(Context(evidence)).Result)
                .IsEqualTo(GameplayFactResult.Correct);
    }

    [Test]
    public async Task Duplicate_incidents_are_not_emitted_again_but_later_valid_access_can_solve()
    {
        var context = Context(new() { Scope = FlagAcquisitionScope.FormalStaticCtf, Required = FlagAcquisitionResource.Attachment });
        var prior = GameplayFactGeneratedCatalog.Create(GameplayFactKind.FlagAttempt);
        prior.TeamId = context.GameplayFact.TeamId;
        prior.CompetitionChallengeId = context.GameplayFact.CompetitionChallengeId;
        prior.Value = context.GameplayFact.Value;
        prior.FailureCode = GameplayFactFailureCode.StaticFlagWithoutAttachment;
        context = context with { PriorFacts = [prior] };
        var evaluator = new CtfGameplayFactEvaluator(new Fixed(GameplayFactResult.Correct));
        await Assert.That(evaluator.Evaluate(context).Result).IsEqualTo(GameplayFactResult.Duplicate);
        context.GameplayFact.AcquisitionEvidence!.Acquired = FlagAcquisitionResource.Attachment;
        await Assert.That(evaluator.Evaluate(context).Result).IsEqualTo(GameplayFactResult.Correct);
    }

    [Test]
    public async Task Foreign_flag_ownership_takes_priority_over_acquisition_evidence()
    {
        var context = Context(new() { Scope = FlagAcquisitionScope.FormalStaticCtf, Required = FlagAcquisitionResource.Attachment });
        var flag = new TeamChallengeFlag
        {
            Id = Guid.NewGuid(), CompetitionChallengeId = context.GameplayFact.CompetitionChallengeId,
            TeamId = Guid.NewGuid(), Flag = context.GameplayFact.Value!,
            FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash(context.GameplayFact.Value!),
            MatchKind = ChallengeFlagMatchKind.Exact
        };
        context = context with { ApplicableFlags = [flag] };
        var decision = new CtfGameplayFactEvaluator(new Fixed(GameplayFactResult.Wrong)).Evaluate(context);
        await Assert.That(decision.FailureCode).IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
    }

    private static GameplayFactProcessingContext Context(FlagAcquisitionEvidence? evidence)
    {
        var fact = GameplayFactGeneratedCatalog.Create(GameplayFactKind.FlagAttempt);
        fact.Id = Guid.NewGuid(); fact.TeamId = Guid.NewGuid(); fact.CompetitionChallengeId = Guid.NewGuid();
        fact.Value = "flag{unit}"; fact.ValueSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash(fact.Value);
        fact.OccurredAt = DateTimeOffset.UtcNow; fact.AcquisitionEvidence = evidence;
        return new(fact, [], [], null, TestConfigurations.Competition(GameMode.Ctf), TestConfigurations.Rules(GameMode.Ctf),
            ChallengeDefinition: TestConfigurations.Definition(GameMode.Ctf));
    }

    private sealed class Fixed(GameplayFactResult result) : IGameplayFactEvaluator
    {
        public GameplayFactDecision Evaluate(GameplayFactProcessingContext context) => new(result, null, context.GameplayFact.OccurredAt);
    }
}
