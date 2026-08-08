using System.Diagnostics;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Persistence;

public sealed class LocalCriticalSectionRegistryTests
{
    [Test]
    [Timeout(10_000)]
    public async Task Contender_times_out_within_the_two_second_budget(
        CancellationToken cancellationToken)
    {
        var registry = new LocalCriticalSectionRegistry();
        await using var owner = await registry.AcquireAsync(
            "runtime-quota", "team:one", cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        Func<Task> contend = async () =>
        {
            await using var _ = await registry.AcquireAsync(
                "runtime-quota", "team:one", CancellationToken.None);
        };

        await Assert.That(contend).Throws<TimeoutException>();
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromMilliseconds(2_750));
    }

    [Test]
    public async Task Released_scope_can_be_acquired_again_and_caller_cancellation_wins()
    {
        var registry = new LocalCriticalSectionRegistry();
        await using (var owner = await registry.AcquireAsync(
            "submission-attempt", "team:two", CancellationToken.None))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            Func<Task> contend = async () =>
            {
                await using var _ = await registry.AcquireAsync(
                    "submission-attempt", "team:two", cancellation.Token);
            };
            await Assert.That(contend).Throws<OperationCanceledException>();
        }

        using var acquisitionBudget = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await using var reacquired = await registry.AcquireAsync(
            "submission-attempt", "team:two", acquisitionBudget.Token);
    }
}
