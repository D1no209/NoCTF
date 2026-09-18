using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Nats;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration"), NotInParallel]
public sealed class QueuedRuntimeRecoveryTests
{
    [Test, Timeout(300_000)]
    public async Task Queued_fact_is_dispatched_after_capacity_returns_without_any_saved_retry(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var connection = postgres.GetConnectionString();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(ct);
                await fixture.SeedAsync(db, ct);
                var runtime = await db.RuntimeInstances.SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct);
                runtime.State = RuntimeState.Queued;
                runtime.StoppedAt = null;
                runtime.CreatedAt = fixture.Now.AddHours(-2);
                await db.SaveChangesAsync(ct);
            }
            var probe = new RecoveryProbe(fixture.RuntimeIds[0]);
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(probe);
                    services.AddSingleton(new NoCTF.Infrastructure.Runtime.Capacity.RuntimeDispatchWakeupGate(null));
                    services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(builder =>
                        builder.UseNpgsql(connection).UseSnakeCaseNamingConvention());
                    services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
                })
                .UseWolverine(wolverine =>
                {
                    wolverine.Discovery.DisableConventionalDiscovery();
                    wolverine.Discovery.IncludeType(typeof(QueuedRuntimeDispatchHandler));
                    wolverine.Discovery.IncludeType(typeof(RecoveryDispatchHandler));
                    wolverine.PersistMessagesWithPostgresql(connection, "wolverine_queued_recovery");
                    wolverine.UseEntityFrameworkCoreTransactions();
                    wolverine.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    wolverine.Durability.Mode = DurabilityMode.Solo;
                    wolverine.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}")
                        .AutoProvision().UseJetStream(_ => { })
                        .DefineWorkQueueStream("RECOVERY", stream => stream.WithSubjects("recovery.control"), "recovery.control");
                    wolverine.ListenToNatsSubject("recovery.control").UseJetStream("RECOVERY", "recovery")
                        .UseDurableInbox();
                    wolverine.PublishMessage<DispatchQueuedRuntimes>().ToNatsSubject("recovery.control")
                        .UseJetStream("RECOVERY").UseDurableOutbox();
                    wolverine.PublishMessage<DispatchRuntime>().ToNatsSubject("recovery.control")
                        .UseJetStream("RECOVERY").UseDurableOutbox();
                }).Build();
            await host.StartAsync(ct);
            try
            {
                var bus = host.Services.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new DispatchQueuedRuntimes(fixture.Now));
                await probe.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                // No retry was scheduled by the capacity-blocked consumer.
                probe.CapacityAvailable = true;
                await bus.PublishAsync(new DispatchQueuedRuntimes(fixture.Now.AddSeconds(5)));
                await probe.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                await using var observer = new NoCtfDbContext(options);
                await Assert.That(await observer.RuntimeInstances.Where(x => x.Id == probe.RuntimeId)
                    .Select(x => x.State).SingleAsync(ct)).IsEqualTo(RuntimeState.Provisioning);
                await Assert.That(probe.UnexpectedDispatches).IsEqualTo(0);
                await Assert.That(await observer.RuntimeInstances.CountAsync(x => x.State == RuntimeState.Stopped, ct))
                    .IsEqualTo(2);
            }
            finally { await host.StopAsync(ct); }
        });
    }

    public sealed class RecoveryProbe(Guid runtimeId)
    {
        public Guid RuntimeId { get; } = runtimeId;
        public volatile bool CapacityAvailable;
        public int UnexpectedDispatches;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Recovered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class RecoveryDispatchHandler(NoCtfDbContext db, RecoveryProbe probe)
    {
        public async Task Handle(DispatchRuntime message, CancellationToken ct)
        {
            if (message.RuntimeInstanceId != probe.RuntimeId)
            {
                Interlocked.Increment(ref probe.UnexpectedDispatches);
                return;
            }
            if (!probe.CapacityAvailable)
            {
                probe.Blocked.TrySetResult();
                return;
            }
            await db.RuntimeInstances.Where(x => x.Id == message.RuntimeInstanceId && x.State == RuntimeState.Queued)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, RuntimeState.Provisioning), ct);
            probe.Recovered.TrySetResult();
        }
    }
}
