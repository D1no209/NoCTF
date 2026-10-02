using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class PersistedRunnerCapacityGateTests
{
    [Test, Timeout(300_000)]
    public async Task Parallel_claims_never_exceed_startup_or_observed_capacity(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            var registry = new NatsRunnerAvailabilityRegistry(connection, TimeProvider.System);
            await using var owner = await new NatsClusterLeaseManager(connection)
                .TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner"),
                    "runner", ct);
            await Assert.That(owner).IsNotNull();
            var registration = CurrentRunnerRegistration.Create("parallel", "runner",
                new(1024, 100, 100),
                resourceDomainFencingToken: owner!.FencingToken) with
            {
                AdmissionOptions = new RunnerAdmissionOptions
                {
                    MainStartupConcurrency = 8
                }
            };
            await registry.PublishHeartbeatAsync("parallel", "runner",
                RuntimeProvider.Docker, TimeSpan.FromMinutes(1), ct);
            await registry.RegisterAsync(registration, ct);
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels)
                .Services.BuildServiceProvider();
            var caches = cacheServices.GetRequiredService<IFusionCacheProvider>();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            var ids = Enumerable.Range(0, 64).Select(_ => Guid.NewGuid()).ToArray();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(ct);
                await fixture.SeedAsync(setup, ct);
                for (var index = 0; index < ids.Length; index++)
                {
                    var challengeId = Guid.NewGuid();
                    setup.Challenges.Add(new AwdpChallenge
                    {
                        Id = challengeId,
                        OwnerId = fixture.OwnerId,
                        Title = "Capacity " + index,
                        Direction = "Pwn",
                        Definition = TestConfigurations.Definition(GameMode.Awdp),
                        CreatedAt = fixture.Now,
                        UpdatedAt = fixture.Now
                    });
                    setup.RuntimeInstances.Add(new TemplateTestRuntimeInstance
                    {
                        Id = ids[index], ChallengeId = challengeId,
                        RuntimeKind = RuntimeKind.Container,
                        RuntimeProvider = RuntimeProvider.Docker,
                        State = RuntimeState.Queued,
                        TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired,
                        TestFlagState = RuntimeTestFlagState.NotRequired,
                        CreatedAt = fixture.Now
                    });
                }
                await setup.SaveChangesAsync(ct);
            }
            var coordinator = new RunnerCapacityLedgerCoordinator();
            var results = await Task.WhenAll(ids.Select(async id =>
            {
                await using var db = new NoCtfDbContext(options);
                var gate = new PersistedRunnerCapacityGate(db, registry,
                    caches, new CapturedOutbox(), coordinator);
                return await gate.TryClaimAsync(
                    new(id, "parallel", 128, 10, 1), ct);
            }));
            await using var verify = new NoCtfDbContext(options);
            var allocated = await verify.RuntimeInstances.AsNoTracking()
                .Where(item => ids.Contains(item.Id))
                .SelectMany(item => item.CapacityAllocationEntries)
                .CountAsync(ct);
            await Assert.That(allocated).IsLessThanOrEqualTo(8);
            await Assert.That(allocated).IsEqualTo(results.Count(result =>
                result.Availability == RunnerCapacityAvailability.Claimed));
            await Assert.That(allocated * 128).IsLessThanOrEqualTo(1024);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Rollback_leaves_no_claim_and_restart_recovers_from_EF_allocations(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            var registry = new NatsRunnerAvailabilityRegistry(connection, TimeProvider.System);
            await using var owner = await new NatsClusterLeaseManager(connection)
                .TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner"),
                    "runner", ct);
            await Assert.That(owner).IsNotNull();
            await registry.PublishHeartbeatAsync("test", "runner", RuntimeProvider.Docker,
                TimeSpan.FromMinutes(1), ct);
            await registry.RegisterAsync(CurrentRunnerRegistration.Create(
                "test", "runner", new(1024, 100, 10),
                resourceDomainFencingToken: owner!.FencingToken), ct);
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels)
                .Services.BuildServiceProvider();
            var caches = cacheServices.GetRequiredService<IFusionCacheProvider>();
            var outbox = new CapturedOutbox();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(ct);
                await fixture.SeedAsync(setup, ct);
                await setup.RuntimeInstances.Where(item => item.Id == fixture.RuntimeIds[0])
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.State, RuntimeState.Queued)
                        .SetProperty(item => item.StoppedAt, (DateTimeOffset?)null), ct);
            }
            var id = fixture.RuntimeIds[0];
            await using (var db = new NoCtfDbContext(options))
            {
                var gate = new PersistedRunnerCapacityGate(db, registry, caches, outbox,
                    new RunnerCapacityLedgerCoordinator());
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var claim = await gate.TryClaimAsync(new(id, "test", 512, 40, 2), ct);
                await Assert.That(claim.Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                await transaction.RollbackAsync(ct);
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var runtime = await db.RuntimeInstances.AsNoTracking()
                    .Include(item => item.CapacityAllocationEntries)
                    .SingleAsync(item => item.Id == id, ct);
                await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
                var gate = new PersistedRunnerCapacityGate(db, registry, caches, outbox,
                    new RunnerCapacityLedgerCoordinator());
                var claim = await gate.TryClaimAsync(new(id, "test", 512, 40, 2), ct);
                await Assert.That(claim.Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                await Assert.That(claim.RunnerId).IsEqualTo("runner");
                var replay = await gate.TryClaimAsync(new(id, "test", 900, 90, 9), ct);
                await Assert.That(replay.State)
                    .IsEqualTo(RunnerCapacityClaimState.AlreadyOwned);
                await db.RuntimeInstances.Where(item => item.Id == id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.State, RuntimeState.Provisioning)
                        .SetProperty(item => item.RunnerId, "runner"), ct);
                await Assert.That(await gate.CanCreateAsync(id, "runner", ct)).IsTrue();
                await Assert.That(await gate.ReleaseAsync(id, "wrong-runner", ct))
                    .IsEqualTo(RunnerCapacityReleaseOutcome.OwnerMismatch);
                await Assert.That(await gate.ReleaseAsync(id, "runner", ct))
                    .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
                var publishedAfterRelease = outbox.Messages.Count;
                await Assert.That(await gate.ReleaseAsync(id, "runner", ct))
                    .IsEqualTo(RunnerCapacityReleaseOutcome.AlreadyReleased);
                await Assert.That(outbox.Messages.Count).IsEqualTo(publishedAfterRelease);
                await Assert.That(await gate.ReleaseAsync(Guid.NewGuid(), "runner", ct))
                    .IsEqualTo(RunnerCapacityReleaseOutcome.RecoveryRequired);
            }
            await using (var verify = new NoCtfDbContext(options))
            {
                var runtime = await verify.RuntimeInstances.AsNoTracking()
                    .Include(item => item.CapacityAllocationEntries)
                    .SingleAsync(item => item.Id == id, ct);
                await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
                var factId = runtime.GameplayFactId!.Value;
                await verify.RuntimeInstances.Where(item => item.Id == id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.State, RuntimeState.Running), ct);
                await verify.GameplayFacts.Where(fact => fact.Id == factId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(fact => fact.State, GameplayFactState.Processing), ct);
                var gate = new PersistedRunnerCapacityGate(verify, registry, caches, outbox,
                    new RunnerCapacityLedgerCoordinator());
                var firstIdentity = new RuntimeWorkloadIdentity(
                    RuntimeWorkloadKind.PatchChecker, id, Guid.NewGuid());
                var secondIdentity = new RuntimeWorkloadIdentity(
                    RuntimeWorkloadKind.PatchChecker, id, Guid.NewGuid());
                var firstRequest = new RunnerCapacityRequest(id, "test", 128, 10, 1,
                    firstIdentity, factId);
                var secondRequest = firstRequest with { Workload = secondIdentity };
                await Assert.That((await gate.TryClaimForRunnerAsync(
                    firstRequest, "runner", ct)).Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                await Assert.That((await gate.TryClaimForRunnerAsync(
                    firstRequest, "runner", ct)).State)
                    .IsEqualTo(RunnerCapacityClaimState.AlreadyOwned);
                await Assert.That((await gate.TryClaimForRunnerAsync(
                    secondRequest, "runner", ct)).Failure)
                    .IsEqualTo(RunnerAdmissionFailure.StartupConcurrencyLimited);
                await Assert.That(await gate.CanCreateWorkloadAsync(
                    firstIdentity, factId, "runner", ct)).IsTrue();
                await Assert.That(await gate.ReleaseWorkloadAsync(
                    firstIdentity, "runner", ct))
                    .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
                await Assert.That((await gate.TryClaimForRunnerAsync(
                    secondRequest, "runner", ct)).Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                await verify.GameplayFacts.Where(fact => fact.Id == factId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(fact => fact.State, GameplayFactState.Completed), ct);
                await Assert.That(await gate.CanCreateWorkloadAsync(
                    secondIdentity, factId, "runner", ct)).IsFalse();
            }
        });
    }

    private sealed class CapturedOutbox : IPostCommitMessagePublisher
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) =>
            throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at)
            where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
