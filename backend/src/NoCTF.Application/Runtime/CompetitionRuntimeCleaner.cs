using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Application.Runtime;

public sealed record RuntimeCleanupTarget(Guid InstanceId, ContainerReceipt? Receipt);

public interface IRuntimeCleanupStore
{
    Task<IReadOnlyList<RuntimeCleanupTarget>> ListActiveAsync(Guid competitionId, CancellationToken cancellationToken);
    Task MarkStoppedAsync(IReadOnlyCollection<Guid> instanceIds, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record CompetitionRuntimeCleanupResult(int StoppedCount);

public sealed class CompetitionRuntimeCleaner(
    IRuntimeCleanupStore store,
    IContainerLifecycle runtime)
{
    public async Task<CompetitionRuntimeCleanupResult> ExecuteAsync(
        Guid competitionId,
        CancellationToken cancellationToken = default)
    {
        var targets = await store.ListActiveAsync(competitionId, cancellationToken);
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
            await store.MarkStoppedAsync(stopped, DateTimeOffset.UtcNow, cancellationToken);
        if (failure is not null)
            throw new InvalidOperationException("One or more competition runtimes could not be cleaned up.", failure);
        return new(stopped.Count);
    }
}
