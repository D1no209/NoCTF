using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using System.Security.Cryptography;
using System.Text.Json;

namespace NoCTF.Infrastructure.Messaging;

public class WolverinePostCommitMessagePublisher(
    IMessageBus bus,
    NoCtfDbContext db,
    Microsoft.Extensions.Logging.ILogger<WolverinePostCommitMessagePublisher>? logger = null,
    PostCommitDispatchStatus? dispatchStatus = null) : IPostCommitMessagePublisher
{
    private readonly List<Func<ValueTask>> pending = [];

    public ValueTask PublishAsync<T>(T message)
    {
        pending.Add(() => bus.PublishAsync(message, Delivery(message)));
        return ValueTask.CompletedTask;
    }

    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
    {
        pending.Add(() => bus.ScheduleAsync(message, scheduledAt, Delivery(message)));
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        pending.Add(() => bus.EndpointFor(ToNatsSubjectUri(queue.Value))
            .SendAsync(message, Delivery(message)));
        return ValueTask.CompletedTask;
    }

    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage
    {
        var queue = RunnerNodeQueueName.FromRunnerId(message.RunnerId);
        pending.Add(() => bus.EndpointFor(ToNatsSubjectUri(queue.Value)).SendAsync(
            message,
            Delivery(message, scheduledAt)));
        return ValueTask.CompletedTask;
    }

    public async Task FlushOutgoingMessagesAsync()
    {
        if (db.Database.CurrentTransaction is not null)
            return;

        await FlushPendingAsync();
    }

    private async Task FlushPendingAsync()
    {
        var batch = pending.ToArray();
        pending.Clear();
        foreach (var publish in batch)
            await publish();
    }

    public void DiscardPendingMessages() => pending.Clear();

    public async Task SaveChangesAndFlushAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await FlushCommittedMessagesAsync();
    }

    public async Task FlushCommittedMessagesAsync()
    {
        try { await FlushPendingAsync(); }
        catch (Exception exception)
        {
            // The explicit caller contract is SaveChanges -> Commit -> this method. Critical
            // workflows are reconstructed from durable business state by the maintenance agent.
            dispatchStatus?.MarkPending();
            if (logger is not null) Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                "Business transaction committed; JetStream publication requires reconciliation after {FailureType}.",
                exception.GetType().Name);
        }
    }

    private static Uri ToNatsSubjectUri(string queueName)
    {
        return new(
            $"nats://subject/noctf.v2.runner.{queueName.Trim().ToLowerInvariant()}",
            UriKind.Absolute);
    }

    private static DeliveryOptions Delivery<T>(T message, DateTimeOffset? scheduledAt = null)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        var scheduleIdentity = scheduledAt?.UtcTicks.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "immediate";
        var identity = $"{typeName}:{scheduleIdentity}:{Convert.ToHexString(SHA256.HashData(payload))}";
        var options = new DeliveryOptions
        {
            ScheduledTime = scheduledAt,
            DeduplicationId = identity
        };
        options.WithHeader("Nats-Msg-Id", identity);
        return options;
    }
}

public sealed class NoOpPostCommitMessagePublisher : IPostCommitMessagePublisher
{
    public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
    public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
        ValueTask.CompletedTask;
    public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
        ValueTask.CompletedTask;
    public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
        where T : IRunnerNodeMessage => ValueTask.CompletedTask;
    public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    public void DiscardPendingMessages() { }
}

public sealed class DevelopmentPostCommitMessagePublisher(
    IMessageBus bus,
    NoCtfDbContext db)
    : WolverinePostCommitMessagePublisher(bus, db);
