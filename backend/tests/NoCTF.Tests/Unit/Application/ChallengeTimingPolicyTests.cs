using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeTimingPolicyTests
{
    [Test]
    public async Task Every_schedule_kind_has_a_telemetry_boundary_name()
    {
        foreach (var kind in Enum.GetValues<NoCTF.Infrastructure.Messaging.ClusterScheduleKind>())
            await Assert.That(NoCTF.Infrastructure.Messaging.MaintenanceTickAgent.ScheduleKind(kind)).IsNotEmpty();
    }

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
    [Test]
    public async Task Boundary_instants_distinguish_scoring_judgement_and_closed_submissions()
    {
        var policy = new ChallengeTiming(Opening, Opening.AddHours(1), Opening.AddHours(2));
        await Assert.That(policy.Eligibility(Opening.AddTicks(-1))).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(policy.CanScore(Opening)).IsTrue();
        await Assert.That(policy.Classify(GameplayFactResult.Correct, Opening.AddHours(1))).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(policy.Eligibility(Opening.AddHours(2))).IsEqualTo(GameplayFactTimeEligibility.SubmissionClosed);
        await Assert.That(policy.CanScoreInterval(Opening, Opening.AddHours(1))).IsTrue();
        await Assert.That(policy.CanScoreInterval(Opening, Opening.AddHours(1).AddTicks(1))).IsFalse();
        await Assert.That(policy.CanScoreCheckpoint(Opening.AddHours(1))).IsFalse();
    }
    [Test]
    public async Task Latest_policy_restores_correctness_classification_without_changing_wrong_or_cheat_results()
    {
        var at = Opening.AddMinutes(90);
        var before = new ChallengeTiming(Opening, Opening.AddHours(1), Opening.AddHours(3));
        var latest = before with { ScoringEndsAt = Opening.AddHours(2) };
        var due = before.Classify(GameplayFactResult.Correct, at);
        await Assert.That(latest.Classify(due, at)).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(latest.Classify(GameplayFactResult.Wrong, at)).IsEqualTo(GameplayFactResult.Wrong);
        await Assert.That(latest.Classify(GameplayFactResult.Rejected, at)).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(GameplayFactCompletion.IsSuccessful(due, GameplayFactTimeEligibility.Valid)).IsTrue();
        await Assert.That(GameplayFactCompletion.IsSuccessful(due, GameplayFactTimeEligibility.NotOpened)).IsFalse();
    }
    [Test]
    public async Task Practice_ignores_submission_deadline_but_not_opening_and_never_scores()
    {
        var policy = new ChallengeTiming(Opening, Opening.AddHours(1), Opening.AddHours(2));
        await Assert.That(policy.Eligibility(Opening.AddHours(3), practice: true)).IsEqualTo(GameplayFactTimeEligibility.Valid);
        await Assert.That(policy.Classify(GameplayFactResult.Correct, Opening.AddHours(3), practice: true)).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(policy.Eligibility(Opening.AddTicks(-1), practice: true)).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
    }
    [Test]
    public async Task Nullable_and_equal_times_are_valid_but_reversed_pairs_are_not()
    {
        await Assert.That(new ChallengeTiming().IsValid).IsTrue();
        await Assert.That(new ChallengeTiming(Opening, Opening, Opening).IsValid).IsTrue();
        await Assert.That(new ChallengeTiming(Opening, Opening.AddSeconds(-1)).IsValid).IsFalse();
        await Assert.That(new ChallengeTiming(null, Opening, Opening.AddSeconds(-1)).IsValid).IsFalse();
        await Assert.That(new ChallengeTiming(Opening, null, Opening.AddSeconds(-1)).IsValid).IsFalse();
    }
}
