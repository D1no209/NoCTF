using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.GameplayFact;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdAttackEvaluationTests
{
    [Test]
    public async Task Accepts_issued_flag_across_rounds_without_delivery_acknowledgement()
    {
        var (fact, flag) = Pair();
        var decision = Evaluate(fact, flag);
        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(decision.AwdAttack!.FlagId).IsEqualTo(flag.Id);
        await Assert.That(decision.AwdAttack.GenerationRoundId).IsEqualTo(flag.AwdIssue!.RoundId);
        fact.Awd!.ReceivedRoundNumber = 15;
        await Assert.That(Evaluate(fact, flag).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.Expired);
    }

    [Test]
    public async Task Distinguishes_format_unknown_own_revoked_future_and_unissued_flags()
    {
        var (fact, flag) = Pair();
        fact.Value += " ";
        await Assert.That(Evaluate(fact, flag).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.Invalid);
        fact.Value = "flag{different}";
        await Assert.That(Evaluate(fact, flag).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.UnknownFlag);
        fact.Value = flag.Flag;
        fact.TeamId = flag.TeamId;
        await Assert.That(Evaluate(fact, flag).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.OwnFlag);
        fact.TeamId = Guid.NewGuid();
        flag.AwdIssue!.State = AwdFlagIssueState.Revoked;
        await Assert.That(Evaluate(fact, flag).FailureCode).IsEqualTo(GameplayFactFailureCode.FlagRevoked);
        flag.AwdIssue.State = AwdFlagIssueState.Issued;
        flag.AwdIssue.IssuedAt = fact.OccurredAt.AddTicks(1);
        await Assert.That(Evaluate(fact, flag).FailureCode).IsEqualTo(GameplayFactFailureCode.FlagNotYetActive);
        flag.AwdIssue = null;
        await Assert.That(Evaluate(fact, flag).FailureCode).IsEqualTo(GameplayFactFailureCode.FlagNotIssued);
    }

    [Test]
    public async Task Missing_receipt_is_retryable_system_failure_and_closed_scoring_is_not_a_capture()
    {
        var (fact, flag) = Pair();
        fact.Awd!.ReceiptScorable = false;
        await Assert.That(Evaluate(fact, flag).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.NotScorable);
        fact.Awd = null;
        var failure = Evaluate(fact, flag);
        await Assert.That(failure.Result).IsNull();
        await Assert.That(failure.AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.SystemError);
    }

    [Test]
    public async Task Issuance_qualification_is_required_for_both_attacker_and_defender()
    {
        var (fact, flag) = Pair();
        var rules = IssuanceRules(fact, flag);
        rules.Teams[0].Competitive = false;
        await Assert.That(Evaluate(fact, flag, rules).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.NotScorable);
        rules.Teams[0].Competitive = true;
        rules.Teams[1].Competitive = false;
        await Assert.That(Evaluate(fact, flag, rules).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.NotScorable);
        rules.Teams.RemoveAt(1);
        await Assert.That(Evaluate(fact, flag, rules).AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.NotScorable);
        var missing = new AwdGameplayFactEvaluator().Evaluate(new(fact, [], [flag], null,
            new AwdCompetitionModeConfiguration(), new AwdCompetitionChallengeRules()));
        await Assert.That(missing.AwdAttack!.Outcome).IsEqualTo(AwdAttackOutcome.SystemError);
    }

    private static AwdRoundRules IssuanceRules(FlagAttemptGameplayFact fact, ChallengeFlag flag) => new()
    {
        Id = flag.AwdIssue?.RulesId ?? Guid.NewGuid(), CompetitionId = fact.CompetitionId,
        CompetitionChallengeId = fact.CompetitionChallengeId!.Value,
        Teams = [new() { TeamId = flag.TeamId!.Value }, new() { TeamId = fact.TeamId!.Value }]
    };

    private static GameplayFactDecision Evaluate(FlagAttemptGameplayFact fact, ChallengeFlag flag, AwdRoundRules? rules = null) =>
        new AwdGameplayFactEvaluator().Evaluate(new(fact, [], [flag], null,
            new AwdCompetitionModeConfiguration(), new AwdCompetitionChallengeRules(),
            AwdIssuanceRules: [rules ?? IssuanceRules(fact, flag)]));

    private static (FlagAttemptGameplayFact Fact, ChallengeFlag Flag) Pair()
    {
        var now = DateTimeOffset.UtcNow;
        var competition = Guid.NewGuid();
        var question = Guid.NewGuid();
        var defender = Guid.NewGuid();
        var flag = new AwdRoundChallengeFlag
        {
            Id = Guid.NewGuid(), CompetitionChallengeId = question, TeamId = defender,
            Flag = "flag{unconfirmed-but-issued}", ValidStart = now.AddMinutes(-6), ValidUntil = now.AddMinutes(-3),
            AwdIssue = new() { CompetitionId = competition, CompetitionChallengeId = question, TeamId = defender,
                RoundId = Guid.NewGuid(), RoundNumber = 10, ExpiryRound = 15, RulesId = Guid.NewGuid(),
                State = AwdFlagIssueState.Issued, DeliveryState = AwdFlagDeliveryState.Unconfirmed,
                IssuedAt = now.AddMinutes(-6), ActiveStartTicks = 0 }
        };
        var fact = new FlagAttemptGameplayFact
        {
            Id = Guid.NewGuid(), CompetitionId = competition, CompetitionChallengeId = question,
            TeamId = Guid.NewGuid(), Value = flag.Flag, OccurredAt = now,
            Awd = new() { ReceivedRoundId = Guid.NewGuid(), ReceivedRoundNumber = 12,
                ReceivedActiveTicks = TimeSpan.FromMinutes(6).Ticks, ReceiptScorable = true }
        };
        return (fact, flag);
    }
}
