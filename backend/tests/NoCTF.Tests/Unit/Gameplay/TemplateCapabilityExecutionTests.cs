using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class TemplateCapabilityExecutionTests
{
    [Test]
    public async Task Template_break_returns_evidence_and_cannot_be_attached_to_a_formal_fact()
    {
        var flagId = Guid.NewGuid();
        const string flag = "flag{template-test}";
        var result = new EvaluateBreak().Execute(new(Guid.NewGuid(), flag,
            [new(flagId, flag, NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash(flag), ChallengeFlagMatchKind.Exact)],
            DateTimeOffset.UtcNow, CapabilityExecutionScope.TemplateTest));
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Match).IsEqualTo(BreakMatchResult.Matched);
        await Assert.That(result.Value.Flags.Single().FlagId).IsEqualTo(flagId);
        await Assert.That(() => result.Value.ToGameplayOutcome()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Template_questionnaire_uses_the_saved_option_identities_without_creating_a_formal_result()
    {
        var questionId = Guid.NewGuid(); var correctId = Guid.NewGuid(); var wrongId = Guid.NewGuid();
        var revision = new QuestionnaireRevision { Id = Guid.NewGuid(), Questions =
            [new() { Id = questionId, Kind = QuestionnaireQuestionKind.SingleChoice, Options =
                [new() { Id = correctId, IsCorrect = true }, new() { Id = wrongId }] }] };
        var result = new EvaluateQuestionnaire().Execute(new(Guid.NewGuid(), revision, [questionId],
            [new(questionId, [correctId])], DateTimeOffset.UtcNow, CapabilityExecutionScope.TemplateTest));
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Questions.Single().Result).IsEqualTo(QuestionnaireAnswerResult.Correct);
        await Assert.That(() => result.Value.ToGameplayOutcome()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Formal_mapping_preserves_the_real_fact_identity()
    {
        var factId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var result = new EvaluateBreak().Execute(new(factId, "flag{wrong}", [], now));
        var outcome = result.Value!.ToGameplayOutcome();
        await Assert.That(outcome.GameplayFactId).IsEqualTo(factId);
        await Assert.That(outcome.Match).IsEqualTo(BreakMatchResult.NotMatched);
        await Assert.That(outcome.ObservedAt).IsEqualTo(now);
    }

    [Test]
    public async Task Template_check_validates_the_complete_server_requirements()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new CheckEvidenceInput(Guid.NewGuid(), Guid.NewGuid(),
            [new(CheckItemKind.Functionality), new(CheckItemKind.Vulnerability)],
            [new(CheckItemKind.Functionality, null, CheckProbeResult.Passed, CheckFailureReason.None)], now.AddSeconds(-1),
            now.AddSeconds(30), now, now, CapabilityExecutionScope.TemplateTest);
        await Assert.That(CheckEvidenceValidation.Evaluate(input).FailureCode).IsEqualTo(CapabilityExecutionFailure.MissingEvidence);
        var valid = CheckEvidenceValidation.Evaluate(input with { Probes = [.. input.Probes,
            new(CheckItemKind.Vulnerability, null, CheckProbeResult.TargetFailure, CheckFailureReason.VulnerabilityPresent)] });
        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(valid.Value!.ExecutionState).IsEqualTo(CapabilityExecutionState.Completed);
        await Assert.That(() => valid.Value.ToGameplayOutcome()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Target_failure_can_explain_an_unexecuted_dependent_probe()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new CheckEvidenceInput(Guid.NewGuid(), Guid.NewGuid(),
            [new(CheckItemKind.Functionality), new(CheckItemKind.Vulnerability)],
            [new(CheckItemKind.Functionality, null, CheckProbeResult.TargetFailure, CheckFailureReason.FunctionalCheckFailed),
                new(CheckItemKind.Vulnerability, null, CheckProbeResult.NotExecuted, CheckFailureReason.FunctionalCheckFailed)],
            now.AddSeconds(-1), now.AddSeconds(30), now, now, CapabilityExecutionScope.TemplateTest);
        var result = CheckEvidenceValidation.Evaluate(input);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.ExecutionState).IsEqualTo(CapabilityExecutionState.Completed);
        await Assert.That(result.Value.Probes[1].Result).IsEqualTo(CheckProbeResult.NotExecuted);
        var unexplained = input with { Probes = [new(CheckItemKind.Functionality, null, CheckProbeResult.Passed, CheckFailureReason.None), input.Probes[1]] };
        await Assert.That(CheckEvidenceValidation.Evaluate(unexplained).FailureCode).IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
        var unrelated = input with { Probes = [
            new(CheckItemKind.Functionality, null, CheckProbeResult.NotExecuted, CheckFailureReason.FunctionalCheckFailed),
            new(CheckItemKind.Vulnerability, null, CheckProbeResult.TargetFailure, CheckFailureReason.FunctionalCheckFailed)] };
        await Assert.That(CheckEvidenceValidation.Evaluate(unrelated).FailureCode).IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
    }

    [Test]
    public async Task Independent_fix_targets_do_not_replace_the_interactive_template_slot()
    {
        var challengeId = Guid.NewGuid();
        var interactive = new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = challengeId, TemplateTestUse = TemplateTestUse.Interactive };
        var first = new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = challengeId, TemplateTestUse = TemplateTestUse.CapabilityVerification };
        var second = new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = challengeId, TemplateTestUse = TemplateTestUse.CapabilityVerification };
        await Assert.That(new[] { interactive, first, second }.Select(ActiveRuntimeSlot.CreateKey).Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Template_checker_capacity_has_no_gameplay_fact_identity()
    {
        var amount = new RuntimeResourceAmount(256 * 1024 * 1024, 250, 128);
        var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.TemplateChecker, Guid.NewGuid(), Guid.NewGuid());
        var allocation = new RuntimeCapacityAllocation(identity, null, "domain", "runner", amount, amount);
        allocation.Validate();
        await Assert.That(identity.IsAuxiliary).IsTrue();
        await Assert.That(() => (allocation with { GameplayFactId = Guid.NewGuid() }).Validate()).Throws<InvalidOperationException>();
        await Assert.That(() => (allocation with { Identity = identity with { Kind = RuntimeWorkloadKind.PatchChecker } }).Validate()).Throws<InvalidOperationException>();
    }
}
