using NoCTF.Runner;

namespace NoCTF.Tests;

public class RunnerOperationCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_ConcurrentDuplicateOperation_ExecutesOnlyOnce()
    {
        var coordinator = new RunnerOperationCoordinator();
        var operationId = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<int> Execute(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 42;
        }

        var first = coordinator.ExecuteAsync("container.create", operationId, Execute, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = coordinator.ExecuteAsync("container.create", operationId, Execute, TestContext.Current.CancellationToken);
        release.TrySetResult();

        Assert.Equal(42, await first);
        Assert.Equal(42, await second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRetry_ReplaysReceipt()
    {
        var coordinator = new RunnerOperationCoordinator();
        var operationId = Guid.NewGuid();
        var calls = 0;

        Task<string> Execute(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("created");
        }

        Assert.Equal("created", await coordinator.ExecuteAsync(
            "container.create", operationId, Execute, TestContext.Current.CancellationToken));
        Assert.Equal("created", await coordinator.ExecuteAsync(
            "container.create", operationId, Execute, TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_FailedOperation_CanBeRetried()
    {
        var coordinator = new RunnerOperationCoordinator();
        var operationId = Guid.NewGuid();
        var calls = 0;

        Task<int> Execute(CancellationToken _)
        {
            var call = Interlocked.Increment(ref calls);
            return call == 1
                ? Task.FromException<int>(new InvalidOperationException("transient"))
                : Task.FromResult(7);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(
            "container.run", operationId, Execute, TestContext.Current.CancellationToken));
        Assert.Equal(7, await coordinator.ExecuteAsync(
            "container.run", operationId, Execute, TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExecuteAsync_OperationDeadline_CancelsHungWorkAndAllowsRetry()
    {
        var coordinator = new RunnerOperationCoordinator(
            maxOperationDuration: TimeSpan.FromMilliseconds(50));
        var operationId = Guid.NewGuid();
        var calls = 0;

        async Task<int> Execute(CancellationToken ct)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 7;
        }

        await Assert.ThrowsAsync<RunnerOperationTimeoutException>(() => coordinator.ExecuteAsync(
            "container.run",
            operationId,
            Execute,
            TestContext.Current.CancellationToken));

        Assert.Equal(7, await coordinator.ExecuteAsync(
            "container.run",
            operationId,
            Execute,
            TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExecuteAsync_CallerDisconnect_DoesNotCancelSharedMutation()
    {
        var coordinator = new RunnerOperationCoordinator();
        var operationId = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCts = new CancellationTokenSource();
        var calls = 0;

        async Task<int> Execute(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 99;
        }

        var disconnected = coordinator.ExecuteAsync(
            "container.create",
            operationId,
            Execute,
            callerCts.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        callerCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnected);

        var retry = coordinator.ExecuteAsync(
            "container.create",
            operationId,
            Execute,
            TestContext.Current.CancellationToken);
        release.TrySetResult();

        Assert.Equal(99, await retry);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_DisconnectedCallers_DoNotBypassUnderlyingConcurrencyLimit()
    {
        var coordinator = new RunnerOperationCoordinator(maxConcurrentOperations: 1);
        var operationIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = 0;
        var maximum = 0;

        async Task<int> Execute(CancellationToken ct)
        {
            var active = Interlocked.Increment(ref current);
            InterlockedExtensions.Max(ref maximum, active);
            firstStarted.TrySetResult();
            try
            {
                await release.Task.WaitAsync(ct);
                return active;
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }

        var callers = operationIds.Select(_ => new CancellationTokenSource()).ToArray();
        var requests = operationIds
            .Select((id, index) => coordinator.ExecuteAsync("container.create", id, Execute, callers[index].Token))
            .ToArray();

        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        foreach (var caller in callers)
            caller.Cancel();
        foreach (var request in requests)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref maximum));

        release.TrySetResult();
        await Task.WhenAll(operationIds.Select(id => coordinator.ExecuteAsync(
            "container.create",
            id,
            Execute,
            TestContext.Current.CancellationToken)));

        Assert.Equal(1, maximum);
        foreach (var caller in callers)
            caller.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_DisconnectedCallers_CannotCreateUnboundedInternalQueue()
    {
        var coordinator = new RunnerOperationCoordinator(
            maxConcurrentOperations: 1,
            maxQueuedOperations: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Execute(CancellationToken ct)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        }

        var first = coordinator.ExecuteAsync(
            "container.create",
            Guid.NewGuid(),
            Execute,
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var queued = coordinator.ExecuteAsync(
            "container.create",
            Guid.NewGuid(),
            Execute,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RunnerOperationRejectedException>(() => coordinator.ExecuteAsync(
            "container.create",
            Guid.NewGuid(),
            Execute,
            TestContext.Current.CancellationToken));

        release.TrySetResult();
        await Task.WhenAll(first, queued);
    }

    [Fact]
    public async Task ExecuteAsync_RetainsReceiptFromCompletionInsteadOfStartTime()
    {
        var time = new RunnerManualTimeProvider();
        var coordinator = new RunnerOperationCoordinator(
            timeProvider: time,
            retention: TimeSpan.FromMinutes(1));
        var operationId = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<int> Execute(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 7;
        }

        var first = coordinator.ExecuteAsync(
            "container.create",
            operationId,
            Execute,
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(2));
        release.TrySetResult();
        Assert.Equal(7, await first);

        Assert.Equal(7, await coordinator.ExecuteAsync(
            "container.create",
            operationId,
            Execute,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(7, await coordinator.ExecuteAsync(
            "container.create",
            operationId,
            Execute,
            TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOperationIdReusedWithDifferentRequest()
    {
        var coordinator = new RunnerOperationCoordinator();
        var operationId = Guid.NewGuid();
        var calls = 0;

        Assert.Equal(1, await coordinator.ExecuteAsync(
            "container.create",
            operationId,
            "fingerprint-a",
            _ => Task.FromResult(Interlocked.Increment(ref calls)),
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<RunnerOperationConflictException>(() => coordinator.ExecuteAsync(
            "container.create",
            operationId,
            "fingerprint-b",
            _ => Task.FromResult(Interlocked.Increment(ref calls)),
            TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }

    private sealed class RunnerManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
