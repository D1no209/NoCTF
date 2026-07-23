using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineTransactionalMessageOutbox(
    IDbContextOutbox<NoCtfDbContext> outbox) : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => outbox.PublishAsync(message);
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        outbox.ScheduleAsync(message, scheduledAt);

    public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage
    {
        var queue = RunnerQueueName.FromPool(message.RunnerPool);
        return outbox.EndpointFor(ToPostgresqlQueueUri(queue.Value)).SendAsync(message);
    }

    public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerPoolMessage
    {
        var queue = RunnerQueueName.FromPool(message.RunnerPool);
        return outbox.EndpointFor(ToPostgresqlQueueUri(queue.Value)).SendAsync(
            message,
            new DeliveryOptions { ScheduledTime = scheduledAt });
    }

    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromAssignment(message.RunnerPool, message.RunnerId);
        return outbox.EndpointFor(ToPostgresqlQueueUri(queue.Value)).SendAsync(message);
    }

    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromAssignment(message.RunnerPool, message.RunnerId);
        return outbox.EndpointFor(ToPostgresqlQueueUri(queue.Value)).SendAsync(
            message,
            new DeliveryOptions { ScheduledTime = scheduledAt });
    }

    public Task FlushOutgoingMessagesAsync() => outbox.FlushOutgoingMessagesAsync();

    private static Uri ToPostgresqlQueueUri(string queueName) =>
        new($"postgresql://{queueName}", UriKind.Absolute);
}

public sealed class OpenApiTransactionalMessageOutbox : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        ValueTask.CompletedTask;
    public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
        ValueTask.CompletedTask;
    public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerPoolMessage => ValueTask.CompletedTask;
    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
        ValueTask.CompletedTask;
    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage => ValueTask.CompletedTask;
    public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
}
