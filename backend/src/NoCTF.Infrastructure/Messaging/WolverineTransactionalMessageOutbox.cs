using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineTransactionalMessageOutbox : ITransactionalMessageOutbox
{
    private readonly IDbContextOutbox<NoCtfDbContext> outbox;
    private readonly Microsoft.Extensions.Logging.ILogger<WolverineTransactionalMessageOutbox>? logger;
    private readonly PostCommitDispatchStatus? dispatchStatus;

    public WolverineTransactionalMessageOutbox(IDbContextOutbox<NoCtfDbContext> outbox,
        Microsoft.Extensions.Logging.ILogger<WolverineTransactionalMessageOutbox>? logger = null,
        PostCommitDispatchStatus? dispatchStatus = null)
    {
        this.outbox = outbox;
        this.logger = logger;
        this.dispatchStatus = dispatchStatus;
        if (outbox is not MessageContext context)
        {
            throw new InvalidOperationException(
                "The Wolverine EF Core outbox must expose its scoped message context.");
        }

        context.MultiFlushMode = MultiFlushMode.AllowMultiples;
    }

    public ValueTask PublishAsync<T>(T message) => outbox.PublishAsync(message);
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        outbox.ScheduleAsync(message, scheduledAt);

    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        return outbox.EndpointFor(ToNatsSubjectUri(queue.Value)).SendAsync(message);
    }

    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        return outbox.EndpointFor(ToNatsSubjectUri(queue.Value)).SendAsync(
            message,
            new DeliveryOptions { ScheduledTime = scheduledAt });
    }

    public Task FlushOutgoingMessagesAsync() =>
        outbox.DbContext.Database.CurrentTransaction is null
            ? outbox.FlushOutgoingMessagesAsync()
            : Task.CompletedTask;

    public async Task FlushCommittedMessagesAsync()
    {
        try { await FlushOutgoingMessagesAsync(); }
        catch (Exception exception)
        {
            // The explicit caller contract is SaveChanges -> Commit -> this method.
            // PostgreSQL Wolverine recovery owns retransmission; never recreate the business operation.
            dispatchStatus?.MarkPending();
            if (logger is not null) Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                "Business transaction committed; durable Outbox delivery is pending after {FailureType}.", exception.GetType().Name);
        }
    }

    private static Uri ToNatsSubjectUri(string queueName)
    {
        return new(
            $"nats://subject/noctf.runner.{queueName.Trim().ToLowerInvariant()}",
            UriKind.Absolute);
    }
}

public sealed class NoOpTransactionalMessageOutbox : ITransactionalMessageOutbox
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
