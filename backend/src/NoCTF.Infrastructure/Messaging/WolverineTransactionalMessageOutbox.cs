using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineTransactionalMessageOutbox(
    IDbContextOutbox<NoCtfDbContext> outbox) : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => outbox.PublishAsync(message);
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        outbox.ScheduleAsync(message, scheduledAt);

    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        return outbox.EndpointFor(ToPostgresqlQueueUri(queue.Value)).SendAsync(message);
    }

    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        return outbox.EndpointFor(ToPostgresqlQueueUri(queue.Value)).SendAsync(
            message,
            new DeliveryOptions { ScheduledTime = scheduledAt });
    }

    public Task FlushOutgoingMessagesAsync() =>
        outbox.DbContext.Database.CurrentTransaction is null
            ? outbox.FlushOutgoingMessagesAsync()
            : Task.CompletedTask;

    private static Uri ToPostgresqlQueueUri(string queueName)
    {
        var postgresQueueName = queueName.Replace('-', '_').ToLowerInvariant();
        return new($"postgresql://{postgresQueueName}", UriKind.Absolute);
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
