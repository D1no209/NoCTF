using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Messages;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Worker;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

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
            await using var cache = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), cache.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(cache.GetConnectionString());
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var id = Guid.NewGuid();
            var challengeId = Guid.NewGuid();
            var initial = new RuntimeResourceLimits(64 * 1024 * 1024, 500_000_000, 64);
            string Definition(RuntimeResourceLimits resources) => JsonSerializer.Serialize(new CtfChallengeConfiguration(
                CtfChallengeConfiguration.CurrentSchemaVersion, null, null, Runtime: new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam,
                    compose ? new ComposeRuntimeDefinition("services:\n  web:\n    image: busybox:1.36.1\n", new Dictionary<string, RuntimeResourceLimits> { ["web"] = resources })
                        : new ContainerRuntimeDefinition("busybox:1.36.1"), resources)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.Challenges.Add(new Challenge
            {
                Id = challengeId, OwnerId = fixture.OwnerId, Mode = GameMode.Ctf, Title = "Allocation limits",
                DefinitionJson = Definition(initial), CreatedAt = fixture.Now, UpdatedAt = fixture.Now
            });
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = id, ChallengeId = challengeId, Purpose = RuntimePurpose.TemplateTest,
                RuntimeKind = compose ? RuntimeKind.Compose : RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Queued, TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired,
                TestFlagState = RuntimeTestFlagState.NotRequired, CreatedAt = fixture.Now
            });
            await db.SaveChangesAsync(ct);
            await new RedisRunnerAvailabilityRegistry(redis).RegisterAsync(new("tests", "runner", RuntimeProvider.Docker,
                "test", new(1024L * 1024 * 1024, 4_000_000_000, 2048), TimeSpan.FromMinutes(1), false), ct);
            var outbox = new Outbox();
            var raw = new RedisRunnerCapacityGate(redis);
            var capacity = new PersistedRunnerCapacityGate(db, raw, outbox);
            var templates = new ChallengeRuntimeTemplateCatalog();
            db.ChangeTracker.Clear();
            await BackendMessageOperations.DispatchRuntimeAsync(new(id), db, templates, new FixedRuntimePlacementPolicy(),
                capacity, outbox, TimeProvider.System, ct);
            var request = outbox.Messages.OfType<IRuntimeProvisionMessage>().Single();
            db.ChangeTracker.Clear();
            var allocation = (await db.RuntimeInstances.SingleAsync(row => row.Id == id, ct)).CapacityAllocations.Items.Single();
            await Assert.That(RuntimeProvisionCapacity.Matches(allocation, request)).IsTrue();
            var services = new ServiceCollection().AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            await using var provider = services.BuildServiceProvider();
            var reader = new RuntimeNodeWorkReader(provider.GetRequiredService<IServiceScopeFactory>());
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
            // Doubling Limit with factor 2 would reproduce the old Budget-only false match.
            await db.Challenges.Where(row => row.Id == challengeId).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.DefinitionJson, Definition(initial with { NanoCpus = 1_000_000_000 })), ct);
            db.ChangeTracker.Clear();
            await BackendMessageOperations.DispatchRuntimeAsync(new(id), db, templates, new FixedRuntimePlacementPolicy(),
                capacity, outbox, TimeProvider.System, ct, budgets: new(2));
            db.ChangeTracker.Clear();
            var rejected = await db.RuntimeInstances.SingleAsync(row => row.Id == id, ct);
            await Assert.That(rejected.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(rejected.FailureCode).IsEqualTo(RuntimeFailureCode.InvalidConfiguration);
            await Assert.That(rejected.RunnerId).IsEqualTo("runner");
            await Assert.That(rejected.CapacityAllocations.Items.Single()).IsEqualTo(allocation);
            await Assert.That(outbox.Messages.OfType<IRuntimeProvisionMessage>().Count()).IsEqualTo(1);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "availableNanoCpus")).IsEqualTo(3_500_000_000);
        });
    }

    private sealed class Outbox : ITransactionalMessageOutbox
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message) { Messages.Add(message!); return ValueTask.CompletedTask; }
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
