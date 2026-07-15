using System.Collections.Concurrent;

namespace NoCTF.Runner;

public sealed class RunnerOperationRejectedException()
    : InvalidOperationException("Runner operation capacity is exhausted.");

public sealed class RunnerOperationConflictException()
    : InvalidOperationException("Runner operation id was reused with a different request.");

public sealed class RunnerOperationTimeoutException()
    : TimeoutException("Runner operation exceeded its configured execution deadline.");

/// <summary>
/// Keeps successful mutation results briefly so a caller can safely retry an
/// operation after an ambiguous HTTP response. Concurrent requests carrying the
/// same operation id share one execution.
/// </summary>
public sealed class RunnerOperationCoordinator
{
    private readonly ConcurrentDictionary<OperationKey, OperationEntry> _operations = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly int _maxEntries;
    private readonly CancellationToken _operationCancellationToken;
    private readonly TimeSpan _maxOperationDuration;
    private readonly SemaphoreSlim _operationLimiter;
    private readonly SemaphoreSlim _admissionLimiter;

    public RunnerOperationCoordinator(
        TimeProvider? timeProvider = null,
        TimeSpan? retention = null,
        int maxEntries = 512,
        int maxConcurrentOperations = 8,
        int maxQueuedOperations = 64,
        CancellationToken operationCancellationToken = default,
        TimeSpan? maxOperationDuration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = retention ?? TimeSpan.FromMinutes(30);
        _maxEntries = Math.Max(128, maxEntries);
        var concurrency = Math.Max(1, maxConcurrentOperations);
        var queueCapacity = Math.Max(0, maxQueuedOperations);
        _operationLimiter = new SemaphoreSlim(concurrency);
        _admissionLimiter = new SemaphoreSlim(concurrency + queueCapacity);
        _operationCancellationToken = operationCancellationToken;
        _maxOperationDuration = maxOperationDuration ?? TimeSpan.FromMinutes(30);
        if (_maxOperationDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxOperationDuration));
    }

    public async Task<T> ExecuteAsync<T>(
        string operationType,
        Guid? operationId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
        => await ExecuteAsync(
            operationType,
            operationId,
            requestFingerprint: null,
            operation,
            cancellationToken);

    public async Task<T> ExecuteAsync<T>(
        string operationType,
        Guid? operationId,
        string? requestFingerprint,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentNullException.ThrowIfNull(operation);

        if (!operationId.HasValue)
            return await ExecuteLimitedAsync(operation, cancellationToken);

        CleanupCompletedEntries();
        var key = new OperationKey(operationType, operationId.Value);
        var candidate = new OperationEntry(
            _timeProvider.GetUtcNow(),
            _timeProvider,
            requestFingerprint,
            () => ExecuteBoxedLimitedAsync(operation, _operationCancellationToken));
        var entry = _operations.GetOrAdd(key, candidate);
        if (!string.Equals(entry.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            throw new RunnerOperationConflictException();

        try
        {
            var result = await entry.Operation.Value.WaitAsync(cancellationToken);
            return result is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"Runner operation '{operationType}' was reused with an incompatible response type.");
        }
        catch
        {
            if (entry.Operation.IsValueCreated &&
                entry.Operation.Value.IsCompleted &&
                !entry.Operation.Value.IsCompletedSuccessfully)
            {
                RemoveIfCurrent(key, entry);
            }

            throw;
        }
    }

    private async Task<T> ExecuteLimitedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!await _admissionLimiter.WaitAsync(TimeSpan.Zero, cancellationToken))
            throw new RunnerOperationRejectedException();
        try
        {
            await _operationLimiter.WaitAsync(cancellationToken);
            try
            {
                using var timeoutCts = new CancellationTokenSource(_maxOperationDuration);
                using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);
                try
                {
                    return await operation(executionCts.Token);
                }
                catch (OperationCanceledException) when (
                    timeoutCts.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
                {
                    throw new RunnerOperationTimeoutException();
                }
            }
            finally
            {
                _operationLimiter.Release();
            }
        }
        finally
        {
            _admissionLimiter.Release();
        }
    }

    private async Task<object> ExecuteBoxedLimitedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
        => (await ExecuteLimitedAsync(operation, cancellationToken))!;

    private void CleanupCompletedEntries()
    {
        var cutoff = (_timeProvider.GetUtcNow() - _retention).ToUnixTimeMilliseconds();
        foreach (var pair in _operations)
        {
            var completedAt = pair.Value.CompletedAtUnixMilliseconds;
            if (completedAt > 0 && completedAt <= cutoff)
                RemoveIfCurrent(pair.Key, pair.Value);
        }

        var overflow = _operations.Count - _maxEntries + 1;
        if (overflow <= 0)
            return;

        foreach (var pair in _operations
                     .Where(pair => pair.Value.CompletedAtUnixMilliseconds > 0)
                     .OrderBy(pair => pair.Value.CompletedAtUnixMilliseconds)
                     .Take(overflow))
        {
            RemoveIfCurrent(pair.Key, pair.Value);
        }
    }

    private void RemoveIfCurrent(OperationKey key, OperationEntry entry)
        => ((ICollection<KeyValuePair<OperationKey, OperationEntry>>)_operations)
            .Remove(new KeyValuePair<OperationKey, OperationEntry>(key, entry));

    private readonly record struct OperationKey(string OperationType, Guid OperationId);
    private sealed class OperationEntry
    {
        private long _completedAtUnixMilliseconds;

        public OperationEntry(
            DateTimeOffset createdAt,
            TimeProvider timeProvider,
            string? requestFingerprint,
            Func<Task<object>> operation)
        {
            CreatedAt = createdAt;
            RequestFingerprint = requestFingerprint;
            Operation = new Lazy<Task<object>>(
                () => ExecuteAndRecordCompletionAsync(operation, timeProvider),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public DateTimeOffset CreatedAt { get; }
        public string? RequestFingerprint { get; }
        public Lazy<Task<object>> Operation { get; }
        public long CompletedAtUnixMilliseconds => Volatile.Read(ref _completedAtUnixMilliseconds);

        private async Task<object> ExecuteAndRecordCompletionAsync(
            Func<Task<object>> operation,
            TimeProvider timeProvider)
        {
            try
            {
                return await operation();
            }
            finally
            {
                Volatile.Write(
                    ref _completedAtUnixMilliseconds,
                    timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            }
        }
    }
}
