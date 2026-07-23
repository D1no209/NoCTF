using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public interface ITransactionalMessageOutbox
{
    ValueTask PublishAsync<T>(T message);
    ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt);
    ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage;
    ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerPoolMessage;
    ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage;
    Task FlushOutgoingMessagesAsync();
}
