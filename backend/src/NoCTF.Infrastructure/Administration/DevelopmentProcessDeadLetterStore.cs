using Wolverine.Persistence.Durability;

namespace NoCTF.Infrastructure.Administration;

public sealed class DevelopmentProcessDeadLetterStore : IProcessDeadLetterStore
{
    public Task<IReadOnlyList<DeadLetterEnvelope>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DeadLetterEnvelope>>([]);
    }

    public Task<DeadLetterEnvelope?> FindAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<DeadLetterEnvelope?>(null);
    }

    public Task<bool> ReplayAsync(Guid messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
