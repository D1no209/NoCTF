using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Runner.Messages;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeDispatchAllocationTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Resource_edits_during_provisioning_cannot_reuse_an_incompatible_allocation(bool compose, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var id = Guid.NewGuid();
            var challengeId = Guid.NewGuid();
            var initial = new RuntimeResourceLimits(64 * 1024 * 1024, 500_000_000, 64);
            ChallengeDefinition Definition(RuntimeResourceLimits resources) =>
                TestConfigurations.Definition(GameMode.Ctf, JsonSerializer.Serialize(new CtfChallengeConfiguration(
                    null, null, Runtime: new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam,
                        compose ? new ComposeRuntimeDefinition("services:\n  web:\n    image: busybox:1.36.1\n", new Dictionary<string, RuntimeResourceLimits> { ["web"] = resources })
                            : new ContainerRuntimeDefinition(
                                "busybox:1.36.1",
                                Security: new(false, false, false, ["ALL"], [])), resources)), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            db.Challenges.Add(new CtfChallenge
            {
                Id = challengeId, OwnerId = fixture.OwnerId, Title = "Allocation limits",
                Definition = Definition(initial), CreatedAt = fixture.Now, UpdatedAt = fixture.Now
            });
            db.RuntimeInstances.Add(new TemplateTestRuntimeInstance
            {
                Id = id, ChallengeId = challengeId,
                RuntimeKind = compose ? RuntimeKind.Compose : RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Queued, TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired,
                TestFlagState = RuntimeTestFlagState.NotRequired, CreatedAt = fixture.Now
            });
            await db.SaveChangesAsync(ct);
            var registry = new NatsRunnerAvailabilityRegistry(connection, TimeProvider.System);
            await using var owner = await new NatsClusterLeaseManager(connection)
                .TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner"),
                    "runner", ct);
            await Assert.That(owner).IsNotNull();
            await registry.PublishHeartbeatAsync("tests", "runner", RuntimeProvider.Docker,
                TimeSpan.FromMinutes(1), ct);
            await registry.RegisterAsync(
                CurrentRunnerRegistration.Create(
                    "tests",
                    "runner",
                    new(1024L * 1024 * 1024, 4_000_000_000, 2048),
                    resourceDomainFencingToken: owner!.FencingToken),
                ct);
            var outbox = new Outbox();
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels).Services.BuildServiceProvider();
            var capacity = new PersistedRunnerCapacityGate(db, registry,
                cacheServices.GetRequiredService<IFusionCacheProvider>(), outbox,
                new RunnerCapacityLedgerCoordinator());
            var templates = new ChallengeRuntimeTemplateCatalog();
            db.ChangeTracker.Clear();
            await BackendMessageOperations.DispatchRuntimeAsync(new(id), db, templates, new FixedRuntimePlacementPolicy(),
                capacity, outbox, TimeProvider.System, ct);
            var request = outbox.Messages.OfType<IRuntimeProvisionMessage>().Single();
            db.ChangeTracker.Clear();
            var allocation = (await db.RuntimeInstances.SingleAsync(row => row.Id == id, ct)).CapacityAllocations.Items.Single();
            await Assert.That(RuntimeProvisionCapacity.Matches(allocation, request)).IsTrue();
            var reader = new RuntimeNodeWorkReader(new TestDbContextFactory(
                new DbContextOptionsBuilder<NoCtfDbContext>()
                    .UseNpgsql(postgres.GetConnectionString())
                    .UseSnakeCaseNamingConvention()
                    .Options));
            await Assert.That(await reader.ReadProvisionStatusAsync(request, ct)).IsEqualTo(RuntimeProvisionWorkStatus.Current);
            var tampered = request switch
            {
                ProvisionContainerRuntime single => (IRuntimeProvisionMessage)(single with
                { Definition = single.Definition with { Limits = initial with { NanoCpus = 1_000_000_000 } } }),
                ProvisionComposeRuntime group => group with
                { Definition = group.Definition with { ServiceResources = new Dictionary<string, RuntimeResourceLimits> { ["web"] = initial with { NanoCpus = 1_000_000_000 } } } },
                _ => throw new InvalidOperationException()
            };
            await Assert.That(await reader.ReadProvisionStatusAsync(tampered, ct)).IsEqualTo(RuntimeProvisionWorkStatus.AssignmentRetained);
            // Changing a hard limit must never match a previously committed allocation.
            if (compose)
            {
                await db.Set<ComposeServiceResource>()
                    .Where(row => row.ChallengeId == challengeId && row.ServiceName == "web")
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.Limits.NanoCpus, 1_000_000_000), ct);
            }
            else
            {
                await db.Set<ChallengeRuntimeTemplateEntity>()
                    .Where(row => row.ChallengeId == challengeId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.Limits.NanoCpus, 1_000_000_000), ct);
            }
            db.ChangeTracker.Clear();
            await BackendMessageOperations.DispatchRuntimeAsync(new(id), db, templates, new FixedRuntimePlacementPolicy(),
                capacity, outbox, TimeProvider.System, ct, budgets: new());
            db.ChangeTracker.Clear();
            var rejected = await db.RuntimeInstances.SingleAsync(row => row.Id == id, ct);
            await Assert.That(rejected.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(rejected.FailureCode).IsEqualTo(RuntimeFailureCode.InvalidConfiguration);
            await Assert.That(rejected.RunnerId).IsEqualTo("runner");
            await Assert.That(rejected.CapacityAllocations.Items.Single()).IsEqualTo(allocation);
            await Assert.That(outbox.Messages.OfType<IRuntimeProvisionMessage>().Count()).IsEqualTo(1);
            await Assert.That(rejected.CapacityAllocations.Items.Single().Limit.NanoCpus)
                .IsEqualTo(500_000_000);
        });
    }

    private sealed class Outbox : IPostCommitMessagePublisher
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message) { Messages.Add(message!); return ValueTask.CompletedTask; }
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
