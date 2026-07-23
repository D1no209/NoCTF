using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.Domain.Challenges;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdRoundSchedulerTests
{
    [Test]
    public async Task BeforeHardeningEnds_HasNoRound()
    {
        var result = AwdRoundScheduler.ResolveInitialRound(
            effectiveRunningTime: TimeSpan.FromSeconds(59),
            hardeningDuration: TimeSpan.FromMinutes(1),
            roundDuration: TimeSpan.FromMinutes(5));

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AtHardeningBoundary_StartsRoundOneWithHalfOpenWindow()
    {
        var result = AwdRoundScheduler.ResolveInitialRound(
            effectiveRunningTime: TimeSpan.FromMinutes(1),
            hardeningDuration: TimeSpan.FromMinutes(1),
            roundDuration: TimeSpan.FromMinutes(5));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Round).IsEqualTo(1);
        await Assert.That(result.Value.StartOffset).IsEqualTo(TimeSpan.FromMinutes(1));
        await Assert.That(result.Value.EndOffset).IsEqualTo(TimeSpan.FromMinutes(6));
        await Assert.That(AwdRoundScheduler.IsWithinWindow(result.Value, TimeSpan.FromMinutes(6))).IsFalse();
    }

    [Test]
    public async Task LaterEffectiveTime_UsesUnboundedRoundNumber()
    {
        var result = AwdRoundScheduler.ResolveInitialRound(
            effectiveRunningTime: TimeSpan.FromMinutes(16),
            hardeningDuration: TimeSpan.FromMinutes(1),
            roundDuration: TimeSpan.FromMinutes(5));

        await Assert.That(result!.Value.Round).IsEqualTo(4);
    }

    [Test]
    public async Task InjectionRetry_UsesBoundedExponentialDelayAndStopsAtWindowEnd()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var validUntil = now.AddSeconds(20);

        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, validUntil, 0))
            .IsEqualTo(now.AddSeconds(1));
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, validUntil, 4))
            .IsEqualTo(now.AddSeconds(16));
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, validUntil, 5))
            .IsNull();
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, now.AddMinutes(2), 8))
            .IsEqualTo(now.AddSeconds(30));
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(validUntil, validUntil, 0))
            .IsNull();
    }

    [Test]
    public async Task BeyondMaximumEncodableRound_HasNoRound()
    {
        var result = AwdRoundScheduler.ResolveInitialRound(
            effectiveRunningTime: TimeSpan.FromSeconds(AwdRoundSpecificationId.MaximumRound),
            hardeningDuration: TimeSpan.Zero,
            roundDuration: TimeSpan.FromSeconds(1));

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Restart_advances_only_one_round_number_without_backfilling_missed_windows()
    {
        var latestEnd = DateTimeOffset.Parse("2026-07-24T00:02:00Z");
        var dueAt = latestEnd.AddMinutes(7);

        var result = AwdRoundScheduler.ResolvePersistedTimelineRound(
            latestRound: 4,
            latestStart: latestEnd.AddMinutes(-2),
            latestEnd,
            dueAt,
            TimeSpan.FromMinutes(2));

        await Assert.That(result.Round).IsEqualTo(5);
        await Assert.That(result.ValidStart).IsEqualTo(latestEnd.AddMinutes(6));
        await Assert.That(result.ValidUntil).IsEqualTo(latestEnd.AddMinutes(8));
    }

    [Test]
    public async Task Duplicate_delivery_keeps_the_current_persisted_window()
    {
        var start = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var end = start.AddMinutes(2);

        var result = AwdRoundScheduler.ResolvePersistedTimelineRound(
            latestRound: 4,
            start,
            end,
            start.AddMinutes(1),
            TimeSpan.FromMinutes(9));

        await Assert.That(result).IsEqualTo(new AwdPersistedRoundWindow(4, start, end));
    }

    [Test]
    public async Task Round_plan_waits_until_effective_hardening_time_finishes()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:10:00Z");

        var result = AwdRoundScheduler.PlanCurrentRound(
            now,
            now.AddHours(1),
            TimeSpan.FromMinutes(9),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            latest: null);

        await Assert.That(result.Kind).IsEqualTo(AwdRoundPlanKind.WaitingForHardening);
        await Assert.That(result.Window).IsNull();
    }

    [Test]
    public async Task Initial_late_delivery_preserves_the_current_logical_window()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:17:00Z");

        var result = AwdRoundScheduler.PlanCurrentRound(
            now,
            now.AddHours(1),
            TimeSpan.FromMinutes(17),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            latest: null);

        await Assert.That(result.Kind).IsEqualTo(AwdRoundPlanKind.Create);
        await Assert.That(result.Window).IsEqualTo(new AwdPersistedRoundWindow(
            2,
            DateTimeOffset.Parse("2026-07-24T00:15:00Z"),
            DateTimeOffset.Parse("2026-07-24T00:20:00Z")));
    }

    [Test]
    public async Task Existing_current_round_is_idempotent_even_after_duration_changes()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:17:00Z");
        var latest = new AwdPersistedRoundWindow(
            4,
            DateTimeOffset.Parse("2026-07-24T00:15:00Z"),
            DateTimeOffset.Parse("2026-07-24T00:20:00Z"));

        var result = AwdRoundScheduler.PlanCurrentRound(
            now,
            now.AddHours(1),
            TimeSpan.FromMinutes(17),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(30),
            latest);

        await Assert.That(result.Kind).IsEqualTo(AwdRoundPlanKind.Current);
        await Assert.That(result.Window).IsEqualTo(latest);
    }

    [Test]
    public async Task EndAt_prevents_a_successor_whose_start_is_not_before_the_deadline()
    {
        var latest = new AwdPersistedRoundWindow(
            4,
            DateTimeOffset.Parse("2026-07-24T00:15:00Z"),
            DateTimeOffset.Parse("2026-07-24T00:20:00Z"));
        var endAt = DateTimeOffset.Parse("2026-07-24T00:20:00Z");

        var result = AwdRoundScheduler.PlanCurrentRound(
            endAt,
            endAt,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            latest);

        await Assert.That(result.Kind).IsEqualTo(AwdRoundPlanKind.Finished);
        await Assert.That(result.Window).IsNull();
    }
}
