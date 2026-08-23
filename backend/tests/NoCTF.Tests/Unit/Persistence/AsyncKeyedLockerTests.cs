using AsyncKeyedLock;

namespace NoCTF.Tests.Unit.Persistence;

public sealed class AsyncKeyedLockerTests
{
    [Test]
    public async Task Same_key_is_serialized_and_cancellation_wins()
    {
        using var locker = new AsyncKeyedLocker<string>();
        using var owner = await locker.LockAsync("team:one", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> contend = async () =>
        {
            using var _ = await locker.LockAsync("team:one", cancellation.Token);
        };

        await Assert.That(contend).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Different_keys_are_independent_and_released_keys_can_be_reused()
    {
        using var locker = new AsyncKeyedLocker<string>();
        using (await locker.LockAsync("team:one", CancellationToken.None))
        {
            using var other = await locker.LockOrNullAsync(
                "team:two",
                TimeSpan.Zero,
                CancellationToken.None);
            await Assert.That(other).IsNotNull();
        }

        using var reacquired = await locker.LockOrNullAsync(
            "team:one",
            TimeSpan.Zero,
            CancellationToken.None);
        await Assert.That(reacquired).IsNotNull();
    }
}
