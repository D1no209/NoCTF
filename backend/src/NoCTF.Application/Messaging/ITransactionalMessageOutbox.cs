using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public interface ITransactionalMessageOutbox
{
    ValueTask PublishAsync<T>(T message);
    ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt);
    ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage;
    ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage;
    Task FlushOutgoingMessagesAsync();
    /// <summary>Persist messages from a nontransactional orchestration handler before dispatch.</summary>
    Task SaveChangesAndFlushAsync(CancellationToken cancellationToken) => FlushOutgoingMessagesAsync();
    /// <summary>Call only after saving messages with business data and confirming the transaction commit.</summary>
    Task FlushCommittedMessagesAsync() => FlushOutgoingMessagesAsync();
}

public sealed class PostCommitDispatchStatus
{
    public bool Pending { get; private set; }
    public void MarkPending() => Pending = true;
}
