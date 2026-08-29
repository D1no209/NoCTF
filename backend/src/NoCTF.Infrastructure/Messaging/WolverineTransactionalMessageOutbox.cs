using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineTransactionalMessageOutbox(
    IMessageBus bus) : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => bus.PublishAsync(message);
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        bus.ScheduleAsync(message, scheduledAt);

    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        return bus.EndpointFor(ToNatsSubjectUri(queue.Value)).SendAsync(message);
    }

    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        return bus.EndpointFor(ToNatsSubjectUri(queue.Value)).SendAsync(
            message,
            new DeliveryOptions { ScheduledTime = scheduledAt });
    }

    public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;

    private static Uri ToNatsSubjectUri(string queueName)
    {
        return new($"nats://noctf.runner.{queueName.Trim().ToLowerInvariant()}", UriKind.Absolute);
    }
}

public sealed class OpenApiTransactionalMessageOutbox : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        ValueTask.CompletedTask;
    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
        ValueTask.CompletedTask;
    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage => ValueTask.CompletedTask;
    public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
}

public sealed class DevelopmentTransactionalMessageOutbox(IMessageBus bus)
    : ITransactionalMessageOutbox
{
    private readonly List<Func<ValueTask>> pending = [];

    public ValueTask PublishAsync<T>(T message)
    {
        pending.Add(() => bus.PublishAsync(message));
        return ValueTask.CompletedTask;
    }

    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
    {
        pending.Add(() => bus.ScheduleAsync(message, scheduledAt));
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
        PublishAsync(message);

    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage => ScheduleAsync(message, scheduledAt);

    public async Task FlushOutgoingMessagesAsync()
    {
        var batch = pending.ToArray();
        pending.Clear();
        foreach (var publish in batch)
            await publish();
    }
}
