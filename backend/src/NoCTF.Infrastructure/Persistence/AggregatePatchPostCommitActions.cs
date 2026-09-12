namespace NoCTF.Infrastructure.Persistence;

/// <summary>
/// Defers cache invalidation until an aggregate PATCH transaction has committed.
/// This keeps read-model invalidation ahead of Outbox dispatch without exposing
/// uncommitted state to a concurrent cache refill.
/// </summary>
public sealed class AggregatePatchPostCommitActions
{
    private readonly List<Func<CancellationToken, Task>> actions = [];
    private bool active;

    internal void Begin()
    {
        if (active)
            throw new InvalidOperationException("An aggregate PATCH is already active in this scope.");
        active = true;
    }

    public Task RunOrDeferAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (!active)
            return action(cancellationToken);
        actions.Add(action);
        return Task.CompletedTask;
    }

    internal async Task CompleteAsync(CancellationToken cancellationToken)
    {
        active = false;
        var committedActions = actions.ToArray();
        actions.Clear();
        foreach (var action in committedActions)
            await action(cancellationToken);
    }

    internal void Discard()
    {
        active = false;
        actions.Clear();
    }
}
