using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Commands.Idempotency;
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
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.GameModes.Registration;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Ctf.Configuration;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration"), NotInParallel]
public sealed class CommandReceiptOutboxTests
{
    [Test, Timeout(300_000)]
    public async Task Configuration_changes_persist_events_and_fanout_before_request_dispatch(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var connection = postgres.GetConnectionString();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(ct);
                await fixture.SeedAsync(setup, ct);
            }
            var commits = new ConfigurationCommitProbe();
            using var host = BuildHost(connection, $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}", new DeliveryProbe(), false, commits);
            await host.Services.GetRequiredService<IMessageStore>().Admin.MigrateAsync();
            await host.StartAsync(ct);
            try
            {
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>();
                var events = new CompetitionEventStore(db, outbox);
                var store = new NoCTF.Infrastructure.Competitions.Configuration.CompetitionConfigurationStore(db, outbox, events);
                var configuration = await store.FindAsync(fixture.Id, ct);
                var changed = await store.TryUpdateAsync(fixture.Id, configuration!.Json, true, fixture.Now, ct);
                await Assert.That(changed.Failure).IsNull();
                var challengeId = await db.CompetitionChallenges.Where(x => x.CompetitionId == fixture.Id)
                    .Select(x => x.Id).SingleAsync(ct);
                var challenges = new NoCTF.Infrastructure.Challenges.Configuration.ChallengeConfigurationStore(db, outbox, events);
                var rules = await challenges.FindAsync(fixture.Id, challengeId, ct);
                await Assert.That((await challenges.TryUpdateAsync(fixture.Id, challengeId, rules!.Json, fixture.Now, ct)).Failure).IsNull();
                await using var observer = new NoCtfDbContext(options);
                await Assert.That(await observer.CompetitionEvents.CountAsync(x => x.CompetitionId == fixture.Id
                    && (x.Kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.CompetitionUpdated
                        || x.Kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.ChallengeUpdated), ct)).IsEqualTo(2);
                // Observe real database rows inside each transaction, before dispatch can remove them.
                await Assert.That(commits.EnvelopeCounts).IsEquivalentTo([3, 3]);
                commits.FailCommit = true;
                await Assert.That(async () => await store.TryUpdateAsync(fixture.Id,
                    """{"schemaVersion":4,"roundDurationSeconds":99}""", true, fixture.Now.AddSeconds(1), ct))
                    .Throws<InvalidOperationException>();
                await Assert.That(await observer.Competitions.Where(x => x.Id == fixture.Id)
                    .Select(x => x.ConfigurationJson).SingleAsync(ct)).IsEqualTo(configuration.Json);
                await Assert.That(await observer.CompetitionEvents.CountAsync(x => x.CompetitionId == fixture.Id
                    && x.Kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.CompetitionUpdated, ct)).IsEqualTo(1);
            }
            finally { await host.StopAsync(ct); }
        });
    }

    [Test, Timeout(300_000)]
    public async Task Actual_flag_and_adjustment_stores_commit_one_fact_per_request_key(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () => {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var connection = postgres.GetConnectionString();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture(); Guid challengeId; Guid teamId;
            await using (var setup = new NoCtfDbContext(options)) {
                await setup.Database.EnsureCreatedAsync(ct); await fixture.SeedAsync(setup, ct);
                var competition = await setup.Competitions.SingleAsync(x => x.Id == fixture.Id, ct);
                competition.Mode = GameMode.Ctf; competition.Status = CompetitionStatus.Running; competition.EndAt = fixture.Now.AddHours(1);
                var template = await setup.Challenges.SingleAsync(ct); template.Mode = GameMode.Ctf;
                competition.ConfigurationJson = """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""";
                template.DefinitionJson = System.Text.Json.JsonSerializer.Serialize(new CtfChallengeConfiguration(CtfChallengeConfiguration.CurrentSchemaVersion,
                    null, null, Runtime: new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition("isolated-test:v1", FlagEnvironmentVariableName: "FLAG"),
                        new RuntimeResourceLimits(64 * 1024 * 1024, 100_000_000, 32), FlagSource: RuntimeFlagSource.PerTeam)),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                var challenge = await setup.CompetitionChallenges.SingleAsync(x => x.CompetitionId == fixture.Id, ct);
                challenge.RulesJson = System.Text.Json.JsonSerializer.Serialize(new NoCTF.GameModes.Ctf.Configuration.CtfChallengeConfiguration(
                    NoCTF.GameModes.Ctf.Configuration.CtfChallengeConfiguration.CurrentSchemaVersion, null, null), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                challenge.IsPublished = true; challengeId = challenge.Id;
                teamId = await setup.Teams.Where(x => x.CompetitionId == fixture.Id).Select(x => x.Id).SingleAsync(ct);
                await setup.SaveChangesAsync(ct);
            }
            using var host = BuildHost(connection, $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}", new DeliveryProbe(), false);
            await host.Services.GetRequiredService<IMessageStore>().Admin.MigrateAsync(); await host.StartAsync(ct);
            try {
                var key = Guid.NewGuid();
                var requests = await Task.WhenAll(Submit(key, "flag{same}"), Submit(key, "flag{same}"));
                await Assert.That(requests[0]).IsEqualTo(requests[1]);
                await Assert.That(async () => await Submit(key, "flag{different}")).Throws<RequestReplayConflictException>();
                await Assert.That(await Submit(Guid.NewGuid(), "flag{same}")).IsNotEqualTo(requests[0]);
                var adjustmentKey = Guid.NewGuid();
                var adjustments = await Task.WhenAll(Adjust(adjustmentKey), Adjust(adjustmentKey));
                await Assert.That(adjustments[0]).IsEqualTo(adjustments[1]);
                await using var verify = new NoCtfDbContext(options);
                await Assert.That(await verify.GameplayFacts.CountAsync(x => x.Kind == GameplayFactKind.FlagAttempt, ct)).IsEqualTo(2);
                await Assert.That(await verify.GameplayFacts.CountAsync(x => x.Kind == GameplayFactKind.ManualAdjustment, ct)).IsEqualTo(1);
                await Assert.That(await verify.Notifications.CountAsync(x => x.Kind == NotificationKind.HttpCommandReceipt, ct)).IsEqualTo(3);
                var runtimeKey = Guid.NewGuid();
                var starts = await Task.WhenAll(Start(runtimeKey), Start(runtimeKey));
                await Assert.That(starts[0]).IsEqualTo(starts[1]);
                await Assert.That(await verify.RuntimeInstances.CountAsync(x => x.Purpose == RuntimePurpose.Player, ct)).IsEqualTo(1);
            } finally { await host.StopAsync(ct); }
            async Task<Guid> Submit(Guid key, string flag) {
                using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>();
                var replay = new TransactionalRequestReplay(db, new RequestKey(key));
                var store = new GameplayFactIntakeStore(db, outbox, new GameplayFactAttemptCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()), new CompetitionEventStore(db, outbox), replay: replay);
                var result = await new SubmitFlag(store, new GameModeGameplayFactAdmissionPolicy()).ExecuteAsync(new(fixture.Id, challengeId, fixture.OwnerId, flag, fixture.Now), ct);
                await Assert.That(result.Succeeded).IsTrue(); return result.Value!.GameplayFactId;
            }
            async Task<Guid> Adjust(Guid key) {
                using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>();
                var store = new GameplayFactIntakeStore(db, outbox, new GameplayFactAttemptCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
                    new CompetitionEventStore(db, outbox), replay: new TransactionalRequestReplay(db, new RequestKey(key)));
                return (await store.TryAcceptManualAdjustmentAsync(new(Guid.NewGuid(), fixture.Id, teamId, challengeId, fixture.OwnerId, 10, fixture.Now), ct)).GameplayFactId!.Value;
            }
            async Task<Guid> Start(Guid key) {
                using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>();
                var store = new RuntimeInstanceStore(db, new ChallengeRuntimeTemplateCatalog(), new FixedRuntimePlacementPolicy(),
                    new PostgresPerTeamRuntimeFlagStore(db), outbox, new TeamRuntimeQuota(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
                    new CompetitionEventStore(db, outbox), new TransactionalRequestReplay(db, new RequestKey(key)));
                var result = await store.MutatePlayerRuntimeAsync(new(fixture.Id, challengeId, fixture.OwnerId, RuntimeAction.Start, null, fixture.Now), ct);
                await Assert.That(result.Failure).IsNull(); return result.Runtime!.Id;
            }
        });
    }
    [Test, Timeout(300_000), Arguments(false), Arguments(true)]
    public async Task Real_routing_persists_command_receipt_and_message_and_recovers_after_sender_restart(bool rollback, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var connection = postgres.GetConnectionString();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using (var setup = new NoCtfDbContext(options)) { await setup.Database.EnsureCreatedAsync(ct); await fixture.SeedAsync(setup, ct); }
            var address = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
            var probe = new DeliveryProbe(); var key = new RequestKey();
            var commandScope = new ReplayScope(fixture.OwnerId, ReplayOperation.ManualAdjustment, fixture.Id, fixture.TemplateId);
            var factId = Guid.NewGuid();
            using (var producer = BuildHost(connection, address, probe, false))
            {
                await producer.Services.GetRequiredService<IMessageStore>().Admin.MigrateAsync(); await producer.StartAsync(ct);
                using (var scope = producer.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>();
                    var receipt = new TransactionalRequestReplay(db, key);
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    await Assert.That(await receipt.FindAsync<GameplayFactAcceptanceResult>(commandScope, new { Delta = 10 }, ct)).IsNull();
                    var challengeId = await db.CompetitionChallenges.Where(x => x.CompetitionId == fixture.Id).Select(x => x.Id).SingleAsync(ct);
                    var teamId = await db.Teams.Where(x => x.CompetitionId == fixture.Id).Select(x => x.Id).SingleAsync(ct);
                    db.GameplayFacts.Add(new GameplayFact { Id = factId, CompetitionId = fixture.Id, CompetitionChallengeId = challengeId,
                        TeamId = teamId, ActorUserId = fixture.OwnerId, Kind = GameplayFactKind.ManualAdjustment, Value = "10",
                        State = GameplayFactState.Completed, Result = GameplayFactResult.Applied, OccurredAt = fixture.Now, UpdatedAt = fixture.Now });
                    receipt.Store(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.Created, factId, fixture.Now));
                    await outbox.PublishAsync(new GameplayFactStateChanged(factId, GameplayFactState.Completed));
                    await outbox.PublishToRunnerNodeAsync(new ForceTerminateRuntime(factId, RuntimeProvider.Docker, "receipt-runner", fixture.OwnerId,
                        "Isolated transport persistence test", fixture.Now));
                    await db.SaveChangesAsync(ct);
                    await using (var observer = new NoCtfDbContext(options))
                        await Assert.That(await observer.Notifications.AnyAsync(x => x.Kind == NotificationKind.HttpCommandReceipt, ct)).IsFalse();
                    if (!rollback) await transaction.CommitAsync(ct);
                    // Deliberately skip request flush and discard all scoped objects.
                }
                await using (var observer = new NoCtfDbContext(options))
                {
                    await Assert.That(await observer.Notifications.CountAsync(x => x.Kind == NotificationKind.HttpCommandReceipt, ct)).IsEqualTo(rollback ? 0 : 1);
                    await Assert.That(await observer.GameplayFacts.AnyAsync(x => x.Id == factId, ct)).IsEqualTo(!rollback);
                    var persisted = await observer.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM wolverine_command_receipts.wolverine_outgoing_envelopes").SingleAsync(ct);
                    await Assert.That(persisted).IsEqualTo(rollback ? 0 : 2);
                    if (rollback) { await producer.StopAsync(ct); return; }
                    var retry = new TransactionalRequestReplay(observer, key);
                    await Assert.That((await retry.FindAsync<GameplayFactAcceptanceResult>(commandScope, new { Delta = 10 }, ct))!.GameplayFactId).IsEqualTo(factId);
                    await Assert.That(async () => await retry.FindAsync<GameplayFactAcceptanceResult>(commandScope, new { Delta = 20 }, ct)).Throws<RequestReplayConflictException>();
                }
                await producer.StopAsync(ct);
            }
            using var recovery = BuildHost(connection, address, probe, true);
            await recovery.StartAsync(ct);
            try { await probe.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(40), ct); await Assert.That(probe.Count).IsEqualTo(1); await Assert.That(probe.RunnerCount).IsEqualTo(1); }
            finally { await recovery.StopAsync(ct); }
        });
    }
    private static IHost BuildHost(string connection, string nats, DeliveryProbe probe, bool recover,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null) => Host.CreateDefaultBuilder()
        .ConfigureServices(services => {
            services.AddSingleton(probe);
            services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(options => options.UseNpgsql(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(interceptor is null ? [] : [interceptor]));
            services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        }).UseWolverine(options => {
            options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType(typeof(DeliveryHandler));
            options.PersistMessagesWithPostgresql(connection, "wolverine_command_receipts"); options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = AutoCreate.All; options.Durability.Mode = DurabilityMode.Solo;
            options.Durability.DurabilityAgentEnabled = recover; options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
            options.Durability.CheckAssignmentPeriod = TimeSpan.FromSeconds(1);
            options.UseNats(nats).AutoProvision().UseJetStream(_ => { })
                .DefineWorkQueueStream(NatsSubjects.GameplayStream, stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Gameplay)), NatsSubjects.Subject(WorkerQueue.Gameplay))
                .DefineWorkQueueStream(NatsSubjects.BackgroundStream, stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Background)), NatsSubjects.Subject(WorkerQueue.Background))
                .DefineWorkQueueStream(NatsSubjects.ControlStream, stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Control)), NatsSubjects.Subject(WorkerQueue.Control))
                .DefineWorkQueueStream(NatsSubjects.ProjectionStream, stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Projection)), NatsSubjects.Subject(WorkerQueue.Projection))
                .DefineWorkQueueStream(NatsSubjects.WebhookStream, stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Webhook)), NatsSubjects.Subject(WorkerQueue.Webhook))
                .DefineWorkQueueStream(NatsSubjects.EventsStream, stream => stream.WithSubjects(NatsSubjects.RealtimeEvents, NatsSubjects.LeaderboardEvents, NatsSubjects.WebhookEvents), NatsSubjects.RealtimeEvents, NatsSubjects.LeaderboardEvents, NatsSubjects.WebhookEvents)
                .DefineWorkQueueStream(NatsSubjects.RunnerStream, stream => stream.WithSubject("noctf.runner.>"), "noctf.runner.>");
            if (recover) options.ListenToNatsSubject(NatsSubjects.Subject(WorkerQueue.Gameplay)).UseJetStream(NatsSubjects.GameplayStream, "receipt-worker").UseDurableInbox();
            if (recover) options.ListenToNatsSubject(NatsSubjects.Runner(RunnerNodeQueueName.FromRunnerId("receipt-runner").Value)).UseJetStream(NatsSubjects.RunnerStream, "receipt-runner").UseDurableInbox();
            options.ConfigureNoCtfMessageRouting(new ConfigurationBuilder().Build(), HostRoles.Only(HostRole.Worker));
        }).Build();
    private sealed class RequestKey(Guid? key = null) : IRequestCommandKey { public Guid? Key { get; } = key ?? Guid.NewGuid(); }
    private sealed class ConfigurationCommitProbe : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public List<int> EnvelopeCounts { get; } = [];
        public bool FailCommit { get; set; }
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT count(*)::integer FROM wolverine_command_receipts.wolverine_outgoing_envelopes";
            EnvelopeCounts.Add((int)(await command.ExecuteScalarAsync(cancellationToken))!);
            if (FailCommit) throw new InvalidOperationException("Injected configuration commit failure.");
            return result;
        }
    }
    public sealed class DeliveryProbe
    {
        public int Count;
        public int RunnerCount;
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Check() { if (Count == 1 && RunnerCount == 1) Arrived.TrySetResult(); }
    }
    public sealed class DeliveryHandler(DeliveryProbe probe)
    {
        public void Handle(GameplayFactStateChanged message) { Interlocked.Increment(ref probe.Count); probe.Check(); }
        public void Handle(ForceTerminateRuntime message) { Interlocked.Increment(ref probe.RunnerCount); probe.Check(); }
    }
}
