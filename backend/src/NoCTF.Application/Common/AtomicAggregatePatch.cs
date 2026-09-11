namespace NoCTF.Application.Common;

public readonly record struct AtomicAggregatePatchDecision<TResult>(
    TResult Result,
    bool ShouldCommit)
{
    public static AtomicAggregatePatchDecision<TResult> Commit(TResult result) =>
        new(result, true);

    public static AtomicAggregatePatchDecision<TResult> Rollback(TResult result) =>
        new(result, false);
}

/// <summary>
/// Runs all persistence operations that implement one aggregate PATCH as a single
/// transaction. A rejected section rolls back earlier section writes and detaches
/// the partially changed graph before control returns to the endpoint.
/// </summary>
public interface IAtomicAggregatePatch
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<AtomicAggregatePatchDecision<TResult>>> operation,
        CancellationToken cancellationToken = default);
}
