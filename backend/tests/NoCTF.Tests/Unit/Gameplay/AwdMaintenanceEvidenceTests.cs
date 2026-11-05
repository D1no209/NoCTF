using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Competitions.Awd;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class AwdMaintenanceEvidenceTests
{
    [Test]
    public async Task A_late_end_observation_does_not_prove_the_process_blocked_the_deadline()
    {
        var (check, fix) = Waiting();
        fix.FixExecution!.AppliedAt = check.Deadline!.Value.AddSeconds(20);
        fix.FixExecution.LastConfirmedRunningAt = check.Deadline.Value.AddSeconds(-1);
        await Assert.That(AwdMaintenanceEvidence.CoversDeadline(check, fix)).IsFalse();
        fix.FixExecution.LastConfirmedRunningAt = check.Deadline.Value;
        await Assert.That(AwdMaintenanceEvidence.CoversDeadline(check, fix)).IsTrue();
    }

    [Test]
    public async Task An_overlapping_fix_requires_a_real_wait_for_the_same_runtime_and_fact()
    {
        var (check, fix) = Waiting();
        fix.FixExecution!.MutationCompletedAt = check.Deadline!.Value.AddSeconds(1);
        await Assert.That(AwdMaintenanceEvidence.CoversDeadline(check, fix)).IsTrue();
        check.MaintenanceWaitStartedAt = check.Deadline;
        await Assert.That(AwdMaintenanceEvidence.CoversDeadline(check, fix)).IsFalse();
        check.MaintenanceWaitStartedAt = check.Deadline.Value.AddSeconds(-2);
        check.RuntimeId = Guid.NewGuid();
        await Assert.That(AwdMaintenanceEvidence.CoversDeadline(check, fix)).IsFalse();
    }

    private static (AwdFactDetails Check, GameplayFact Fix) Waiting()
    {
        var deadline = DateTimeOffset.Parse("2026-11-05T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var runtimeId = Guid.NewGuid(); var factId = Guid.NewGuid();
        return (new() { Deadline = deadline, RuntimeId = runtimeId, BlockingFixFactId = factId,
            MaintenanceWaitStartedAt = deadline.AddSeconds(-2), MaintenanceWaitEndedAt = deadline.AddSeconds(1) },
            new FixAttemptGameplayFact { Id = factId, State = GameplayFactState.Completed,
                FixExecution = new() { GameplayFactId = factId, RuntimeInstanceId = runtimeId, TargetKind = FixTargetKind.Persistent,
                    MutationStartedAt = deadline.AddSeconds(-1) } });
    }
}
