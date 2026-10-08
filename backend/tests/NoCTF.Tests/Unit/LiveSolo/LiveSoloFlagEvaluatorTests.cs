using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.LiveSolo.Gameplay;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloFlagEvaluatorTests
{
    [Test]
    [Arguments(FlagAcquisitionResource.Container, GameplayFactFailureCode.StaticFlagWithoutContainer)]
    [Arguments(FlagAcquisitionResource.Attachment, GameplayFactFailureCode.StaticFlagWithoutAttachment)]
    [Arguments(FlagAcquisitionResource.Container | FlagAcquisitionResource.Attachment, GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment)]
    public async Task A_correct_static_flag_requires_the_fixed_scope_evidence_but_a_wrong_answer_remains_wrong(
        FlagAcquisitionResource required, GameplayFactFailureCode failure)
    {
        var now = DateTimeOffset.UtcNow;
        var fact = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), TeamId = Guid.NewGuid(), Value = "flag{correct}", OccurredAt = now,
            AcquisitionEvidence = new() { Scope = FlagAcquisitionScope.FormalStaticExecution, Required = required } };
        var flag = new TemplateChallengeFlag { Flag = "flag{correct}", FlagSha256 = ManageChallengeFlags.Hash("flag{correct}") };
        var evaluator = new LiveSoloFlagEvaluator();
        var correct = evaluator.Evaluate(new(fact, [], [flag], null, new LiveSoloCompetitionModeConfiguration(), new LiveSoloCompetitionChallengeRules()));
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.Rejected); await Assert.That(correct.FailureCode).IsEqualTo(failure);
        fact.Value = "flag{wrong}";
        var wrong = evaluator.Evaluate(new(fact, [], [flag], null, new LiveSoloCompetitionModeConfiguration(), new LiveSoloCompetitionChallengeRules()));
        await Assert.That(wrong.Result).IsEqualTo(GameplayFactResult.Wrong); await Assert.That(wrong.FailureCode).IsNull();
    }
    [Test]
    public async Task Foreign_ownership_is_rejected_before_acquisition_checks_and_dynamic_flags_do_not_require_static_evidence()
    {
        var team = Guid.NewGuid(); var other = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var fact = new FlagAttemptGameplayFact { Value = "flag{dynamic}", TeamId = team, OccurredAt = now,
            AcquisitionEvidence = new() { Scope = FlagAcquisitionScope.NotApplicable, Required = FlagAcquisitionResource.Container } };
        var flag = new RuntimeInstanceChallengeFlag { TeamId = other, Flag = "flag{dynamic}", FlagSha256 = ManageChallengeFlags.Hash("flag{dynamic}") };
        var evaluator = new LiveSoloFlagEvaluator();
        var foreign = evaluator.Evaluate(new(fact, [], [flag], null, new LiveSoloCompetitionModeConfiguration(), new LiveSoloCompetitionChallengeRules()));
        await Assert.That(foreign.FailureCode).IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
        await Assert.That(foreign.VictimTeamId).IsEqualTo(other);
        flag.TeamId = team;
        var own = evaluator.Evaluate(new(fact, [], [flag], null, new LiveSoloCompetitionModeConfiguration(), new LiveSoloCompetitionChallengeRules()));
        await Assert.That(own.Result).IsEqualTo(GameplayFactResult.Correct);
    }
}
