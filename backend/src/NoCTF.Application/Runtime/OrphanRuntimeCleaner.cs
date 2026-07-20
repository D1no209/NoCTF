using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Application.Runtime;

public interface IOrphanRuntimeStore
{
    Task<IReadOnlyList<RuntimeCleanupTarget>> ListOrphansAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task MarkStoppedAsync(
        IReadOnlyCollection<Guid> instanceIds,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class OrphanRuntimeCleaner(
    IOrphanRuntimeStore store,
    IContainerLifecycle runtime)
{
    public async Task<CompetitionRuntimeCleanupResult> ExecuteAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var targets = await store.ListOrphansAsync(now, cancellationToken);
        var stopped = new List<Guid>(targets.Count);
        Exception? failure = null;
        foreach (var target in targets)
        {
            try
            {
                if (target.Receipt is not null)
                    await runtime.DestroyAsync(target.Receipt, cancellationToken);
                stopped.Add(target.InstanceId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
        if (stopped.Count > 0)
            await store.MarkStoppedAsync(stopped, now, cancellationToken);
        if (failure is not null)
            throw new InvalidOperationException("One or more orphan runtimes could not be cleaned up.", failure);
        return new(stopped.Count);
    }
}
