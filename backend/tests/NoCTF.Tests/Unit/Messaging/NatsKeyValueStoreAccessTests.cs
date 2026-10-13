using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NSubstitute;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Unit.Messaging;

public sealed class NatsKeyValueStoreAccessTests
{
    [Test]
    public async Task Concurrent_first_operations_share_initialization_and_warm_operations_reuse_it()
    {
        var store = Substitute.For<INatsKVStore>();
        var opened = new TaskCompletionSource<INatsKVStore>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var access = new NatsKeyValueStoreAccess(async ct =>
        {
            Interlocked.Increment(ref calls);
            return await opened.Task.WaitAsync(ct);
        });
        var reads = Enumerable.Range(0, 32).Select(_ =>
            access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None)).ToArray();

        await Assert.That(calls).IsEqualTo(1);
        opened.SetResult(store);
        var results = await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(results.All(result => ReferenceEquals(result, store))).IsTrue();
        await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Failed_initialization_can_be_retried()
    {
        var store = Substitute.For<INatsKVStore>();
        var calls = 0;
        var access = new NatsKeyValueStoreAccess(_ =>
            Interlocked.Increment(ref calls) == 1
                ? ValueTask.FromException<INatsKVStore>(new NatsException("offline"))
                : ValueTask.FromResult(store));

        await Assert.ThrowsAsync<NatsException>(async () =>
        {
            await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        });
        await Assert.That(await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None))
            .IsSameReferenceAs(store);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Cancelled_waiter_does_not_cancel_shared_initialization()
    {
        var store = Substitute.For<INatsKVStore>();
        var opened = new TaskCompletionSource<INatsKVStore>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var access = new NatsKeyValueStoreAccess(async ct =>
        {
            Interlocked.Increment(ref calls);
            return await opened.Task.WaitAsync(ct);
        });
        var first = access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiter = access.ExecuteAsync(handle => Task.FromResult(handle), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter);
        opened.SetResult(store);
        await Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(5))).IsSameReferenceAs(store);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Cancelled_initializer_does_not_poison_future_operations()
    {
        var store = Substitute.For<INatsKVStore>();
        var calls = 0;
        var access = new NatsKeyValueStoreAccess(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return store;
        });
        using var cancellation = new CancellationTokenSource();
        var first = access.ExecuteAsync(handle => Task.FromResult(handle), cancellation.Token);
        var second = access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        await Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5))).IsSameReferenceAs(store);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Transport_failure_is_propagated_without_replay_and_reopens_next_time()
    {
        var store = Substitute.For<INatsKVStore>();
        var opens = 0;
        var operations = 0;
        var access = new NatsKeyValueStoreAccess(_ =>
        {
            Interlocked.Increment(ref opens);
            return ValueTask.FromResult(store);
        });

        await Assert.That(() => access.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref operations);
            throw new NatsException("stream unavailable");
        }, CancellationToken.None)).Throws<NatsException>();
        await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        await Assert.That(opens).IsEqualTo(2);
        await Assert.That(operations).IsEqualTo(1);
    }

    [Test]
    public async Task Expected_key_or_cas_failure_does_not_reprovision_the_stream()
    {
        var store = Substitute.For<INatsKVStore>();
        var opens = 0;
        var access = new NatsKeyValueStoreAccess(_ =>
        {
            Interlocked.Increment(ref opens);
            return ValueTask.FromResult(store);
        });

        await Assert.That(() => access.ExecuteAsync<int>(
            _ => throw new NatsKVCreateException(), CancellationToken.None))
            .Throws<NatsKVCreateException>();
        await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        await Assert.That(opens).IsEqualTo(1);
    }

    [Test]
    public async Task Late_failure_from_old_handle_does_not_invalidate_its_replacement()
    {
        var firstStore = Substitute.For<INatsKVStore>();
        var replacement = Substitute.For<INatsKVStore>();
        var opens = 0;
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var access = new NatsKeyValueStoreAccess(_ => ValueTask.FromResult(
            Interlocked.Increment(ref opens) == 1 ? firstStore : replacement));
        var slowFailure = access.ExecuteAsync<int>(async _ =>
        {
            await releaseFailure.Task;
            throw new NatsException("late failure");
        }, CancellationToken.None);
        await Assert.That(() => access.ExecuteAsync<int>(
            _ => throw new NatsException("first failure"), CancellationToken.None))
            .Throws<NatsException>();
        await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None);
        releaseFailure.SetResult();
        await Assert.That(() => slowFailure).Throws<NatsException>();

        await Assert.That(await access.ExecuteAsync(handle => Task.FromResult(handle), CancellationToken.None))
            .IsSameReferenceAs(replacement);
        await Assert.That(opens).IsEqualTo(2);
    }
}
