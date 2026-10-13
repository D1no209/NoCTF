using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Checking;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdCheckPlanTests
{
    [Test]
    public async Task DefaultPlanMergesFinalSampleAndEndsExecutionAtRoundBoundary()
    {
        var round = new AwdRound { ActiveEndTicks = TimeSpan.FromSeconds(180).Ticks };
        var samples = AwdCheckPlan.Create(round, new() { CheckerIntervalSeconds = 30 }, 0);
        await Assert.That(samples.Select(sample => sample.ActiveTicks / TimeSpan.TicksPerSecond).ToArray())
            .IsEquivalentTo(new long[] { 0, 30, 60, 90, 120, 150 });
        await Assert.That(samples.Count(sample => sample.Final)).IsEqualTo(1);
        await Assert.That(samples[^1].DeadlineActiveTicks).IsEqualTo(round.ActiveEndTicks);
    }

    [Test]
    public async Task LateActivationDoesNotBackfillSamplesAndPartialRoundDoesNotClaimFinalCheck()
    {
        var samples = AwdCheckPlan.Create(new() { ActiveEndTicks = TimeSpan.FromSeconds(95).Ticks, Partial = true },
            new() { CheckerIntervalSeconds = 30 }, TimeSpan.FromSeconds(70).Ticks);
        await Assert.That(samples.Count).IsEqualTo(1);
        await Assert.That(samples[0].ActiveTicks).IsEqualTo(TimeSpan.FromSeconds(70).Ticks);
        await Assert.That(samples[0].Final).IsFalse();
    }

    [Test]
    public async Task InternalErrorsRetryOnlyInsideOriginalExecutionBudget()
    {
        var at = DateTimeOffset.Parse("2026-10-13T00:00:00Z");
        var details = new AwdFactDetails { Health = AwdHealth.InternalError,
            Execution = AwdCheckExecution.FailedToDetermine, Attempt = 1,
            ReceivedAt = at, Deadline = at.AddSeconds(30) };
        await Assert.That(AwdCheckPlan.CanRetry(details, new(), at.AddSeconds(4))).IsFalse();
        await Assert.That(AwdCheckPlan.CanRetry(details, new(), at.AddSeconds(5))).IsTrue();
        details.Attempt = 3;
        await Assert.That(AwdCheckPlan.CanRetry(details, new(), at.AddSeconds(10))).IsFalse();
        details.Attempt = 1;
        await Assert.That(AwdCheckPlan.CanRetry(details, new(), at.AddSeconds(30))).IsFalse();
    }
}
