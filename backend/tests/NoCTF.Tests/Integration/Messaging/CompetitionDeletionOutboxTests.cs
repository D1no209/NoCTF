using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Hosting;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Nats;
using Wolverine.Postgresql;
using Wolverine.Persistence.Durability;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class CompetitionDeletionOutboxTests
{
    [Test, Timeout(300_000)]
    [Arguments(false)] [Arguments(true)]
    public async Task Deletion_audit_and_real_outbox_are_atomic_and_recover_without_request_flush(
        bool failBeforeCommit, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var connection = postgres.GetConnectionString();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(connection)
                .UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(ct);
                await fixture.SeedAsync(db, ct);
            }
            var natsUrl = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
            var probe = new DeliveryProbe();
            using (var producer = BuildHost(connection, natsUrl, probe, false, failBeforeCommit))
            {
                await producer.Services.GetRequiredService<IMessageStore>().Admin.MigrateAsync();
                await producer.StartAsync(ct);
                using (var scope = producer.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    // Real Wolverine Publish/Save integration; only the post-commit flush is interrupted.
                    var outbox = new InterruptFlushOutbox(scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>());
                    var store = new AdminCompetitionStore(db, messageOutbox: outbox);
                    if (failBeforeCommit)
                        await Assert.That(async () => await store.ForceDeleteAsync(fixture.Command, true, ct)).Throws<InvalidOperationException>();
                    else
                        await Assert.That((await store.ForceDeleteAsync(fixture.Command, true, ct)).State).IsEqualTo(CompetitionForceDeleteState.Deleted);
                }
                await using (var verification = new NoCtfDbContext(options))
                {
                    await Assert.That(await verification.Competitions.AnyAsync(x => x.Id == fixture.Id, ct)).IsEqualTo(failBeforeCommit);
                    await Assert.That(await verification.Notifications.CountAsync(x => x.Kind == NotificationKind.CompetitionForceDeleted, ct))
                        .IsEqualTo(failBeforeCommit ? 0 : 1);
                    // Independent connection, so this observes committed PostgreSQL rows, not a recording fake.
                    var count = await verification.Database.SqlQueryRaw<int>(
                        "SELECT count(*)::integer AS \"Value\" FROM wolverine_deletion.wolverine_outgoing_envelopes").SingleAsync(ct);
                    await Assert.That(count).IsEqualTo(failBeforeCommit ? 0 : 5);
                }
                await Assert.That(probe.Messages.Count).IsEqualTo(0);
                await producer.StopAsync(ct);
            }
            if (failBeforeCommit) return;

            // Start a fresh host: no scoped outbox/envelope objects survive from the deleting request.
            using var recovery = BuildHost(connection, natsUrl, probe, true, false);
            await recovery.StartAsync(ct);
            try
            {
                await probe.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(40), ct);
                await Assert.That(probe.Messages.OfType<CleanupFile>().Select(x => x.FileId).Distinct())
                    .IsEquivalentTo(fixture.CleanupFileIds);
                await Assert.That(probe.Messages.OfType<InvalidateDeletedCompetitionReadModels>().Select(x => x.CompetitionId).Distinct())
                    .IsEquivalentTo([fixture.Id]);
                await Assert.That(probe.InvalidationAttempts).IsEqualTo(2);
            }
            finally { await recovery.StopAsync(ct); }
        });
    }

    private static IHost BuildHost(string connection, string nats, DeliveryProbe probe, bool recover, bool failBeforeCommit) =>
        Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(probe);
            services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(options =>
            {
                options.UseNpgsql(connection).UseSnakeCaseNamingConvention();
                if (failBeforeCommit) options.AddInterceptors(new FailAfterAuditSave());
            });
            services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        }).UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(DeliveryHandler));
            options.PersistMessagesWithPostgresql(connection, "wolverine_deletion");
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
            options.Durability.Mode = DurabilityMode.Solo;
            options.Durability.DurabilityAgentEnabled = recover;
            options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
            options.Durability.CheckAssignmentPeriod = TimeSpan.FromSeconds(1);
            var subject = NatsSubjects.Subject(WorkerQueue.Background);
            options.UseNats(nats).AutoProvision().UseJetStream(_ => { })
                .DefineWorkQueueStream(NatsSubjects.BackgroundStream, stream => stream.WithSubjects(subject), subject);
            if (recover) options.ListenToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream, "deletion-worker")
                .UseDurableInbox();
            // Exercise the application's actual sender routing, not a more durable test-only approximation.
            options.ConfigureNoCtfMessageRouting(new ConfigurationBuilder().Build(), HostRoles.Only(HostRole.Worker));
        }).Build();

    public sealed class DeliveryProbe
    {
        public int InvalidationAttempts;
        public ConcurrentBag<object> Messages { get; } = [];
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Record(object message)
        {
            Messages.Add(message);
            if (Messages.OfType<CleanupFile>().Select(x => x.FileId).Distinct().Count() == 4
                && Messages.OfType<InvalidateDeletedCompetitionReadModels>().Any()) Delivered.TrySetResult();
        }
    }
    public sealed class DeliveryHandler(DeliveryProbe probe)
    {
        public void Handle(CleanupFile message) => probe.Record(message);
        public void Handle(InvalidateDeletedCompetitionReadModels message)
        {
            if (Interlocked.Increment(ref probe.InvalidationAttempts) == 1)
                throw new IOException("Simulated post-commit cache infrastructure failure.");
            probe.Record(message);
        }
    }
    private sealed class InterruptFlushOutbox(ITransactionalMessageOutbox inner) : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => inner.PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => inner.ScheduleAsync(message, at);
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => inner.PublishToRunnerNodeAsync(message);
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => inner.ScheduleToRunnerNodeAsync(message, at);
        public Task FlushOutgoingMessagesAsync() => throw new IOException("Simulated interruption before outgoing dispatch.");
    }
    private sealed class FailAfterAuditSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<NoCTF.Domain.Notifications.Notification>()
                .Any(x => x.Entity.Kind == NotificationKind.CompetitionForceDeleted))
                throw new InvalidOperationException("Simulated failure after SaveChanges and before Commit.");
            return ValueTask.FromResult(result);
        }
    }
}
