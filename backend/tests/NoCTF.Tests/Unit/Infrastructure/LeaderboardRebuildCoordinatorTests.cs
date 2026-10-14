using Microsoft.Extensions.Time.Testing;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NSubstitute;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class LeaderboardRebuildCoordinatorTests
{
    [Test, Timeout(10_000)]
    public async Task Renewal_failure_cancels_computation_and_remains_a_retryable_publication_failure(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var leases = Substitute.For<IClusterLeaseManager>();
        var ownership = Substitute.For<IClusterLease>();
        leases.TryAcquireAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ownership);
        ownership.RenewAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("Synthetic lease loss")));
        await using var lease = await new LeaderboardRebuildCoordinator(leases, clock).AcquireAsync(Guid.NewGuid(), ct);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lease.Token.Register(() => cancelled.TrySetResult());

        clock.Advance(NatsClusterLeaseManager.RenewalInterval);
        await cancelled.Task.WaitAsync(ct);

        await Assert.That(lease.Token.IsCancellationRequested).IsTrue();
        await Assert.That(async () => await lease.ConfirmOwnershipAsync()).Throws<TimeoutException>();
    }

    [Test, Timeout(10_000)]
    public async Task Explicit_publication_check_and_timer_renewal_do_not_race_the_CAS_revision(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var leases = Substitute.For<IClusterLeaseManager>();
        var ownership = Substitute.For<IClusterLease>();
        leases.TryAcquireAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ownership);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var active = 0;
        var maximum = 0;
        ownership.RenewAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            if (Interlocked.Increment(ref calls) == 1) await release.Task;
            else second.TrySetResult();
            Interlocked.Decrement(ref active);
        });
        await using var lease = await new LeaderboardRebuildCoordinator(leases, clock).AcquireAsync(Guid.NewGuid(), ct);
        var check = lease.ConfirmOwnershipAsync();
        clock.Advance(NatsClusterLeaseManager.RenewalInterval);
        release.TrySetResult();
        await check;
        await second.Task.WaitAsync(ct);
        await Assert.That(maximum).IsEqualTo(1);
        await Assert.That(lease.Token.IsCancellationRequested).IsFalse();
    }
}
