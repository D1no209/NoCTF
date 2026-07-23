using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
public sealed class WolverineTransactionalOutboxTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Consumer_observes_business_fact_committed_with_outbox(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_wolverine_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            using var host = BuildHost(postgres.GetConnectionString());
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE outbox_business_probe (id uuid PRIMARY KEY)",
                    cancellationToken);
            }

            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var observation = OutboxProbeObservation.Expect(id);
                var bus = host.Services.GetRequiredService<IMessageBus>();

                await bus.SendAsync(new WriteOutboxBusinessProbe(id));

                await Assert.That(await observation.WaitAsync(cancellationToken)).IsTrue();
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Scheduled_outbox_message_is_not_visible_before_its_due_time(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            using var host = BuildHost(postgres.GetConnectionString());
            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var dueAt = DateTimeOffset.UtcNow.AddSeconds(1);
                var observation = ScheduledProbeObservation.Expect(id);

                await host.Services.GetRequiredService<IMessageBus>()
                    .SendAsync(new WriteScheduledOutboxProbe(id, dueAt));

                var observedAt = await observation.WaitAsync(cancellationToken);
                await Assert.That(observedAt).IsGreaterThanOrEqualTo(dueAt);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_business_transaction_rolls_back_and_dead_letter_can_be_replayed(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            using var host = BuildHost(postgres.GetConnectionString());
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE rollback_business_probe (id uuid PRIMARY KEY)",
                    cancellationToken);
            }

            RollbackOutboxProbeHandler.Fail = true;
            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var replayed = RollbackProbeObservation.Expect(id);
                await host.Services.GetRequiredService<IMessageBus>()
                    .SendAsync(new WriteRollbackOutboxProbe(id));

                var deadLetters = host.Services.GetRequiredService<IDeadLetters>();
                var deadLetter = await WaitForDeadLetterAsync(deadLetters, id, cancellationToken);
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    var count = await db.Database.SqlQuery<int>(
                            $"SELECT count(*)::int AS value FROM rollback_business_probe WHERE id = {id}")
                        .SingleAsync(cancellationToken);
                    await Assert.That(count).IsEqualTo(0);
                }

                RollbackOutboxProbeHandler.Fail = false;
                await deadLetters.ReplayAsync(
                    new DeadLetterEnvelopeQuery([deadLetter.Id]),
                    cancellationToken);

                await Assert.That(await replayed.WaitAsync(cancellationToken)).IsTrue();
            }
            finally
            {
                RollbackOutboxProbeHandler.Fail = true;
                await host.StopAsync(cancellationToken);
            }
        });
    }

    private static PostgreSqlContainer CreatePostgres() =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_wolverine_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static async Task<DeadLetterEnvelope> WaitForDeadLetterAsync(
        IDeadLetters deadLetters,
        Guid probeId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var result = await deadLetters.QueryAsync(
                new DeadLetterEnvelopeQuery { PageSize = 100 },
                cancellationToken);
            var envelope = result.Envelopes.FirstOrDefault(candidate =>
                candidate.Message is WriteRollbackOutboxProbe probe && probe.Id == probeId);
            if (envelope is not null)
                return envelope;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        throw new TimeoutException("The rollback probe did not reach Wolverine dead-letter storage.");
    }

    private static IHost BuildHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        builder.UseWolverine(options =>
        {
            options.Discovery.IncludeType<OutboxBusinessProbeHandler>();
            options.Discovery.IncludeType<ObserveOutboxBusinessProbeHandler>();
            options.Discovery.IncludeType<ScheduledOutboxProbeHandler>();
            options.Discovery.IncludeType<ObserveScheduledOutboxProbeHandler>();
            options.Discovery.IncludeType<RollbackOutboxProbeHandler>();
            options.Discovery.IncludeType<ObserveRollbackOutboxProbeHandler>();
            options.PersistMessagesWithPostgresql(connectionString, "wolverine_test");
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.All;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
            options.Policies.OnException<RollbackProbeException>().MoveToErrorQueue();
            options.ListenToPostgresqlQueue("outbox-probe").UseDurableInbox();
            options.PublishMessage<WriteOutboxBusinessProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveOutboxBusinessProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<WriteScheduledOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveScheduledOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<WriteRollbackOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveRollbackOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
        });
        return builder.Build();
    }
}

public sealed record WriteOutboxBusinessProbe(Guid Id);
public sealed record ObserveOutboxBusinessProbe(Guid Id);
public sealed record WriteScheduledOutboxProbe(Guid Id, DateTimeOffset DueAt);
public sealed record ObserveScheduledOutboxProbe(Guid Id);
public sealed record WriteRollbackOutboxProbe(Guid Id);
public sealed record ObserveRollbackOutboxProbe(Guid Id);

public sealed class OutboxBusinessProbeHandler
{
    public static async Task Handle(
        WriteOutboxBusinessProbe message,
        NoCtfDbContext db,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO outbox_business_probe (id) VALUES ({message.Id})",
            cancellationToken);
        await outbox.PublishAsync(new ObserveOutboxBusinessProbe(message.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}

public sealed class ScheduledOutboxProbeHandler
{
    public static async Task Handle(
        WriteScheduledOutboxProbe message,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await outbox.ScheduleAsync(new ObserveScheduledOutboxProbe(message.Id), message.DueAt);
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}

public sealed class ObserveScheduledOutboxProbeHandler
{
    public static void Handle(ObserveScheduledOutboxProbe message) =>
        ScheduledProbeObservation.Complete(message.Id, DateTimeOffset.UtcNow);
}

public sealed class RollbackOutboxProbeHandler
{
    public static volatile bool Fail = true;

    public static async Task Handle(
        WriteRollbackOutboxProbe message,
        NoCtfDbContext db,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO rollback_business_probe (id) VALUES ({message.Id})",
            cancellationToken);
        await outbox.PublishAsync(new ObserveRollbackOutboxProbe(message.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        if (Fail)
            throw new RollbackProbeException();
    }
}

public sealed class ObserveRollbackOutboxProbeHandler
{
    public static void Handle(ObserveRollbackOutboxProbe message) =>
        RollbackProbeObservation.Complete(message.Id, true);
}

public sealed class RollbackProbeException : Exception;

public sealed class ObserveOutboxBusinessProbeHandler
{
    public static async Task Handle(
        ObserveOutboxBusinessProbe message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS value FROM outbox_business_probe WHERE id = {message.Id}")
            .SingleAsync(cancellationToken);
        OutboxProbeObservation.Complete(message.Id, count == 1);
    }
}

internal static class OutboxProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<bool>> Pending = [];

    public static Task<bool> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, bool factWasVisible)
    {
        TaskCompletionSource<bool>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(factWasVisible);
    }
}

internal static class ScheduledProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<DateTimeOffset>> Pending = [];

    public static Task<DateTimeOffset> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<DateTimeOffset>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, DateTimeOffset observedAt)
    {
        TaskCompletionSource<DateTimeOffset>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(observedAt);
    }
}

internal static class RollbackProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<bool>> Pending = [];

    public static Task<bool> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, bool applied)
    {
        TaskCompletionSource<bool>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(applied);
    }
}
