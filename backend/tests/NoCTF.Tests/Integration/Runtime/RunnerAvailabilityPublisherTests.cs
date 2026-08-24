using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RunnerAvailabilityPublisherTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Failed_receipt_assignment_keeps_publisher_offline_until_resource_reconciliation_clears_it(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runner_availability")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));

            var dbOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(dbOptions, cancellationToken);
            var services = new ServiceCollection();
            services.AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention());
            await using var serviceProvider = services.BuildServiceProvider();
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var database = redis.GetDatabase();
            await database.HashSetAsync(
                $"runner-claim:{fixture.RuntimeInstanceId:N}",
                [
                    new HashEntry("runnerId", "runner-a"),
                    new HashEntry("memoryBytes", 128),
                    new HashEntry("nanoCpus", 10),
                    new HashEntry("pidsLimit", 1)
                ]);
            await database.HashSetAsync(
                $"runner-claim:{fixture.FailedWithoutReceiptRuntimeInstanceId:N}",
                [
                    new HashEntry("runnerId", "runner-a"),
                    new HashEntry("memoryBytes", 128),
                    new HashEntry("nanoCpus", 10),
                    new HashEntry("pidsLimit", 1)
                ]);
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var availabilityOptions = Options.Create(new RunnerOptions
            {
                Id = "runner-a",
                Pool = "pool-a",
                Provider = RuntimeProvider.Docker,
                Capacity = new()
                {
                    MemoryBytes = 1024,
                    NanoCpus = 100,
                    PidsLimit = 10
                },
                Heartbeat = new()
                {
                    IntervalSeconds = 1,
                    TtlSeconds = 10
                },
                ProviderFailureHoldSeconds = 120
            });
            var providerHealth = new RunnerProviderHealthState(
                availabilityOptions,
                TimeProvider.System,
                NullLogger<RunnerProviderHealthState>.Instance);
            using var publisher = new RunnerAvailabilityPublisher(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                registry,
                availabilityOptions,
                NullLogger<RunnerAvailabilityPublisher>.Instance,
                TimeProvider.System,
                providerHealth);

            var blocked = await publisher.PublishOnceAsync(cancellationToken);

            await Assert.That(blocked)
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted);
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:capacity")).IsFalse();
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:heartbeat")).IsFalse();
            await Assert.That(await database.KeyExistsAsync(
                $"runner-claim:{fixture.RuntimeInstanceId:N}")).IsTrue();

            var reconciler = new RecordingResourceReconciler();
            var receiptResources = new RecordingReceiptResources();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = "pool-a",
                    ["Runner:Id"] = "runner-a",
                    ["Runner:Provider"] = nameof(RuntimeProvider.Docker)
                })
                .Build();
            await using (var cleanupDb = new NoCtfDbContext(dbOptions))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    cleanupDb,
                    [reconciler],
                    configuration.ToRunnerOptions(),
                    new RedisRunnerCapacityGate(redis),
                    new RecordingReceiptProviderCatalog(receiptResources));
                await handler.Handle(
                    new ReconcileRuntimeResources("runner-a", fixture.Now),
                    cancellationToken);
            }

            await Assert.That(reconciler.Destroyed)
                .IsEquivalentTo([
                    new RuntimeResourceIdentity(
                        fixture.FailedWithoutReceiptRuntimeInstanceId)
                ]);
            await Assert.That(receiptResources.DestroyedContainerIds)
                .IsEquivalentTo([$"container-{fixture.RuntimeInstanceId:N}"]);
            await Assert.That(await database.KeyExistsAsync(
                $"runner-claim:{fixture.RuntimeInstanceId:N}")).IsFalse();
            await Assert.That(await database.KeyExistsAsync(
                $"runner-claim:{fixture.FailedWithoutReceiptRuntimeInstanceId:N}")).IsFalse();
            await Assert.That(await database.HashExistsAsync(
                "runner:runner-a:capacity",
                "registrationSchema")).IsFalse();
            await using (var cleanupVerify = new NoCtfDbContext(dbOptions))
            {
                var cleaned = await cleanupVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(
                        instance => instance.Id == fixture.RuntimeInstanceId,
                        cancellationToken);
                await Assert.That(cleaned.State).IsEqualTo(RuntimeState.Failed);
                await Assert.That(cleaned.FailureCode)
                    .IsEqualTo(RuntimeFailureCode.CleanupFailed);
                await Assert.That(cleaned.ProviderReceiptJson).IsNull();
                await Assert.That(cleaned.RunnerId).IsNull();

                var cleanedWithoutReceipt = await cleanupVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(
                        instance => instance.Id
                            == fixture.FailedWithoutReceiptRuntimeInstanceId,
                        cancellationToken);
                await Assert.That(cleanedWithoutReceipt.State)
                    .IsEqualTo(RuntimeState.Failed);
                await Assert.That(cleanedWithoutReceipt.FailureCode)
                    .IsEqualTo(RuntimeFailureCode.InvalidConfiguration);
                await Assert.That(cleanedWithoutReceipt.ProviderReceiptJson).IsNull();
                await Assert.That(cleanedWithoutReceipt.RunnerId).IsNull();

                var otherProvider = await cleanupVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(
                        instance => instance.Id == fixture.OtherProviderRuntimeInstanceId,
                        cancellationToken);
                await Assert.That(otherProvider.ProviderReceiptJson).IsNotNull();
                await Assert.That(otherProvider.RunnerId).IsEqualTo("runner-a");
            }

            var online = await publisher.PublishOnceAsync(cancellationToken);

            await Assert.That(online).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:heartbeat")).IsTrue();
            await Assert.That((long)(await database.HashGetAsync(
                "runner:runner-a:capacity",
                "availableMemoryBytes"))!).IsEqualTo(1024);

            providerHealth.ReportFailure(
                RuntimeProvider.Docker,
                RunnerProviderFailureKind.ProvisionRejected,
                fixture.RuntimeInstanceId);
            var providerUnavailable = await publisher.PublishOnceAsync(cancellationToken);

            await Assert.That(providerUnavailable)
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineProviderUnavailable);
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:heartbeat")).IsFalse();
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:capacity")).IsTrue();

            providerHealth.ReportSuccess(RuntimeProvider.Docker);
            var recovered = await publisher.PublishOnceAsync(cancellationToken);
            await Assert.That(recovered).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:heartbeat")).IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        // Stages 1-9 intentionally evolve the model before stage 10 replaces the
        // migration history with the new InitialBaseline.
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;
        var now = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var runtimeInstanceId = Guid.CreateVersion7();
        var failedWithoutReceiptRuntimeInstanceId = Guid.CreateVersion7();
        var otherProviderRuntimeInstanceId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Runner availability",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Availability target",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            RulesJson = "{}",
            UpdatedAt = now
        });
        db.RuntimeInstances.AddRange(
            new RuntimeInstance
            {
                Id = runtimeInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerId = "runner-a",
                State = RuntimeState.Failed,
                FailureCode = RuntimeFailureCode.CleanupFailed,
                ProviderReceiptJson = JsonSerializer.Serialize(new ContainerReceipt(
                    runtimeInstanceId,
                    RuntimeProvider.Docker,
                    $"container-{runtimeInstanceId:N}",
                    RuntimeStatus.Running,
                    new Dictionary<int, int>(),
                    null,
                    $"container-{runtimeInstanceId:N}",
                    $"network-{runtimeInstanceId:N}",
                    runtimeInstanceId)),
                CreatedAt = now
            },
            new RuntimeInstance
            {
                Id = failedWithoutReceiptRuntimeInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerId = "runner-a",
                State = RuntimeState.Failed,
                FailureCode = RuntimeFailureCode.InvalidConfiguration,
                CreatedAt = now.AddMilliseconds(500)
            },
            new RuntimeInstance
            {
                Id = otherProviderRuntimeInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Kubernetes,
                RunnerId = "runner-a",
                State = RuntimeState.Failed,
                FailureCode = RuntimeFailureCode.CleanupFailed,
                ProviderReceiptJson = """{"resourceId":"other-provider"}""",
                CreatedAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            runtimeInstanceId,
            failedWithoutReceiptRuntimeInstanceId,
            otherProviderRuntimeInstanceId);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid RuntimeInstanceId,
        Guid FailedWithoutReceiptRuntimeInstanceId,
        Guid OtherProviderRuntimeInstanceId);

    private sealed class RecordingResourceReconciler : IRuntimeManagedResourceReconciler
    {
        public RuntimeProvider Provider => RuntimeProvider.Docker;

        public List<RuntimeResourceIdentity> Destroyed { get; } = [];

        public Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RuntimeResourceIdentity>>([]);

        public Task DestroyByIdentityAsync(
            RuntimeResourceIdentity identity,
            CancellationToken cancellationToken)
        {
            Destroyed.Add(identity);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReceiptProviderCatalog(
        RecordingReceiptResources resources) : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) => resources;
        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) => resources;
        public IComposeRuntime Compose(RuntimeProvider provider) =>
            throw new NotSupportedException();
        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingReceiptResources
        : IContainerLifecycle, IContainerSandboxLifecycle
    {
        public List<string> DestroyedContainerIds { get; } = [];

        public Task<ContainerReceipt> CreateAsync(
            ContainerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ContainerReceipt> EnsureRunningAsync(
            ContainerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DestroyAsync(
            ContainerReceipt receipt,
            CancellationToken cancellationToken)
        {
            DestroyedContainerIds.Add(receipt.ResourceId);
            return Task.CompletedTask;
        }

        public Task<ContainerReceipt?> GetAsync(
            RuntimeProvider provider,
            string resourceId,
            CancellationToken cancellationToken) => Task.FromResult<ContainerReceipt?>(null);

        public Task<string> CreateIsolatedNetworkAsync(
            ContainerNetworkPolicyRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteIsolatedNetworkAsync(
            string networkId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> IsolatedNetworkExistsAsync(
            string networkId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task CopyArchiveAsync(
            ContainerReceipt receipt,
            Stream tarArchive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ContainerExecResult> ExecAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ContainerExecResult> ExecWithInputAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            ReadOnlyMemory<byte> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
