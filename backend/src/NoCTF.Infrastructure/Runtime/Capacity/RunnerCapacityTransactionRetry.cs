namespace NoCTF.Infrastructure.Runtime.Capacity;

/// <summary>Bounds retries of complete capacity transactions, never provider operations.</summary>
public static class RunnerCapacityTransactionRetry
{
    public const int MaximumAttempts = 8;

    public static bool CanRetry(int failedAttempt) => failedAttempt + 1 < MaximumAttempts;

    public static Task DelayAsync(int failedAttempt, CancellationToken ct)
    {
        // Capacity transactions in different processes can still conflict under
        // Serializable. Fixed millisecond retries synchronize those contenders.
        // Full jitter grows to 256ms; seven delays total at most 1001ms, plus transaction I/O.
        var maximumMilliseconds = Math.Min(256, 16 << Math.Min(failedAttempt, 4));
        return Task.Delay(Random.Shared.Next(1, maximumMilliseconds), ct);
    }
}
