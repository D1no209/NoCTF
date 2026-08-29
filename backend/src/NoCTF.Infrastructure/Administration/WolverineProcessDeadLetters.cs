using Wolverine.Persistence.Durability;

namespace NoCTF.Infrastructure.Administration;

public interface IProcessDeadLetterStore
{
    Task<IReadOnlyList<DeadLetterEnvelope>> ListAsync(
        int limit,
        CancellationToken cancellationToken);
    Task<DeadLetterEnvelope?> FindAsync(Guid messageId, CancellationToken cancellationToken);
    Task<bool> ReplayAsync(Guid messageId, CancellationToken cancellationToken);
}

public static class WolverinePersistenceSchemas
{
    public const string Api = "nats";
    public const string Worker = "nats";
    public const string Runner = "nats";
    public static IReadOnlyList<string> All { get; } = [Api, Worker, Runner];
}

/// <summary>
/// NATS JetStream owns dead-letter retention and replay. Operators use the NATS
/// monitoring/replay tooling against configured DLQ subjects; no PostgreSQL
/// Wolverine message store is created by the application.
/// </summary>
public sealed class WolverineProcessDeadLetters : IProcessDeadLetterStore, IAsyncDisposable
{
    public WolverineProcessDeadLetters(string connectionString)
    {
        _ = connectionString;
    }

    public async Task<IReadOnlyList<DeadLetterEnvelope>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return [];
    }

    public async Task<DeadLetterEnvelope?> FindAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    public async Task<bool> ReplayAsync(Guid messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
