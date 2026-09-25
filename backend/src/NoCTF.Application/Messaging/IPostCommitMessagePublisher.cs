using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

/// <summary>
/// Buffers messages produced by a business operation and publishes them only after the
/// owning database transaction has committed. This is deliberately not a transactional
/// outbox: JetStream delivery is repaired from durable business state when publication fails.
/// </summary>
public interface IPostCommitMessagePublisher
{
    ValueTask PublishAsync<T>(T message);
    ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt);
    ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage;
    ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage;
    Task FlushOutgoingMessagesAsync();
    /// <summary>Discard messages buffered by a database unit of work that rolled back.</summary>
    void DiscardPendingMessages() { }
    /// <summary>Save the current EF unit of work, then publish its buffered messages.</summary>
    Task SaveChangesAndFlushAsync(CancellationToken cancellationToken) => FlushOutgoingMessagesAsync();
    /// <summary>Call only after the business transaction has committed.</summary>
    Task FlushCommittedMessagesAsync() => FlushOutgoingMessagesAsync();
}

public sealed class PostCommitDispatchStatus
{
    public bool Pending { get; private set; }
    public void MarkPending() => Pending = true;
}
