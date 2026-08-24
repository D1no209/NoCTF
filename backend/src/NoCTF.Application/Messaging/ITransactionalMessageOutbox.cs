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
}
