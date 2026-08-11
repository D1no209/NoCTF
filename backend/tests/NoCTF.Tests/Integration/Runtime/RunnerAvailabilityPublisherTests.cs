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
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            using var publisher = new RunnerAvailabilityPublisher(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                registry,
                Options.Create(new RunnerAvailabilityOptions
                {
                    RunnerId = "runner-a",
                    RunnerPool = "pool-a",
                    Provider = RuntimeProvider.Docker,
                    MemoryBytes = 1024,
                    NanoCpus = 100,
                    PidsLimit = 10,
                    HeartbeatIntervalSeconds = 1,
                    HeartbeatTtlSeconds = 10
                }),
                NullLogger<RunnerAvailabilityPublisher>.Instance);

            var blocked = await publisher.PublishOnceAsync(cancellationToken);

            await Assert.That(blocked)
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted);
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:capacity")).IsFalse();
            await Assert.That(await database.KeyExistsAsync("runner:runner-a:heartbeat")).IsFalse();
            await Assert.That(await database.KeyExistsAsync(
                $"runner-claim:{fixture.RuntimeInstanceId:N}")).IsTrue();

            var reconciler = new RecordingResourceReconciler();
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
                    configuration,
                    new RedisRunnerCapacityGate(redis));
                await handler.Handle(
                    new ReconcileRuntimeResources("pool-a", "runner-a", fixture.Now),
                    cancellationToken);
            }

            await Assert.That(reconciler.Destroyed)
                .IsEquivalentTo([new RuntimeResourceIdentity(fixture.RuntimeInstanceId, 1)]);
            await Assert.That(await database.KeyExistsAsync(
                $"runner-claim:{fixture.RuntimeInstanceId:N}")).IsFalse();
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
                await Assert.That(cleaned.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(cleaned.RunnerUnavailableAt).IsNull();
                await Assert.That(cleaned.ProcessingVersion).IsEqualTo(8);

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
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;
        var now = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var runtimeInstanceId = Guid.CreateVersion7();
        var otherProviderRuntimeInstanceId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
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
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Availability target",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            UpdatedAt = now
        });
        db.RuntimeInstances.AddRange(
            new RuntimeInstance
            {
                Id = runtimeInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                Generation = 1,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerPool = "pool-a",
                RunnerId = "runner-a",
                RunnerAssignmentReleaseToken = Guid.CreateVersion7(),
                RunnerUnavailableAt = now,
                State = RuntimeState.Failed,
                FailureCode = RuntimeFailureCode.CleanupFailed,
                ProcessingVersion = 7,
                ProviderReceiptJson = """{"resourceId":"failed"}""",
                CreatedAt = now
            },
            new RuntimeInstance
            {
                Id = otherProviderRuntimeInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                Generation = 2,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Kubernetes,
                RunnerPool = "pool-a",
                RunnerId = "runner-a",
                State = RuntimeState.Failed,
                FailureCode = RuntimeFailureCode.CleanupFailed,
                ProviderReceiptJson = """{"resourceId":"other-provider"}""",
                CreatedAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, runtimeInstanceId, otherProviderRuntimeInstanceId);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid RuntimeInstanceId,
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
}
