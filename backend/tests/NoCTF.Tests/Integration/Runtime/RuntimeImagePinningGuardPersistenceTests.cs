using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Messages;
using NoCTF.Worker;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeImagePinningGuardPersistenceTests
{
    private const string RunnerId = "runner-a";
    private const long TotalMemory = 128 * 1024 * 1024;
    private const long TotalNanoCpus = 200_000_000;
    private const long TotalPids = 128;
    private static readonly RuntimeResourceLimits Limits = new(
        64 * 1024 * 1024,
        100_000_000,
        64);

    [Test]
    [Timeout(300_000)]
    public async Task Mutable_and_pinned_claims_serialize_capacity_recovery_by_database_ownership(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_runtime_image_guard")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redisContainer = new RedisBuilder("redis:7-alpine").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            var connectionString = postgres.GetConnectionString();
            var seedOptions = CreateOptions(connectionString, "image-guard-seed");
            await using (var migrationDb = new NoCtfDbContext(seedOptions))
                await migrationDb.Database.MigrateAsync(cancellationToken);
            var pinnedWinsRuntimeId = await SeedAsync(
                seedOptions,
                "pinned-wins",
                cancellationToken);
            var mutableWinsRuntimeId = await SeedAsync(
                seedOptions,
                "mutable-wins",
                cancellationToken);

            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var realCapacity = new RedisRunnerCapacityGate(redis);
            await InitializeRunnerAsync(redis.GetDatabase(), cancellationToken);

            await AssertPinnedWinnerDoesNotReleaseLiveClaimAsync(
                connectionString,
                redis.GetDatabase(),
                realCapacity,
                pinnedWinsRuntimeId,
                cancellationToken);
            await AssertMutableWinnerReleasesStaleClaimOnceAsync(
                connectionString,
                redis.GetDatabase(),
                realCapacity,
                mutableWinsRuntimeId,
                cancellationToken);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Reconciliation_completes_capacity_recovery_after_mutable_claim_crash(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_runtime_image_recovery")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redisContainer = new RedisBuilder("redis:7-alpine").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            var options = CreateOptions(
                postgres.GetConnectionString(),
                "image-guard-recovery");
            await using (var migrationDb = new NoCtfDbContext(options))
                await migrationDb.Database.MigrateAsync(cancellationToken);
            var runtimeId = await SeedAsync(options, "recovery", cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var database = redis.GetDatabase();
            await InitializeRunnerAsync(database, cancellationToken);
            var capacity = new RedisRunnerCapacityGate(redis);
            await ClaimAsync(capacity, runtimeId, cancellationToken);

            await using (var crashDb = new NoCtfDbContext(options))
            {
                var runtime = await crashDb.RuntimeInstances.SingleAsync(
                    candidate => candidate.Id == runtimeId,
                    cancellationToken);
                runtime.State = RuntimeState.Provisioning;
                runtime.RunnerId = RunnerId;
                runtime.RunnerAssignmentReleaseToken = Guid.CreateVersion7();
                runtime.ProcessingVersion = 1;
                await crashDb.SaveChangesAsync(cancellationToken);
            }

            var outbox = new RecordingOutbox();
            await using (var reconciliationDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers
                    .ExecuteRunnerAssignmentReconciliationAsync(
                        new ReconcileRunnerAssignments(DateTimeOffset.UtcNow),
                        reconciliationDb,
                        capacity,
                        outbox,
                        cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            await Assert.That(await database.KeyExistsAsync(ClaimKey(runtimeId))).IsFalse();
            await AssertCapacityAvailableAsync(database);
            var failure = outbox.Published.OfType<RuntimeProvisionFailed>().Single();
            await Assert.That(failure.FailureCode)
                .IsEqualTo(RuntimeFailureCode.InvalidConfiguration);
            await using (var writeBackDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    failure,
                    writeBackDb,
                    cancellationToken);
            await using var verify = new NoCtfDbContext(options);
            var failed = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                candidate => candidate.Id == runtimeId,
                cancellationToken);
            await Assert.That(failed.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(failed.RunnerAssignmentReleaseToken).IsNull();
        });
    }

    private static async Task AssertPinnedWinnerDoesNotReleaseLiveClaimAsync(
        string connectionString,
        IDatabase redis,
        RedisRunnerCapacityGate realCapacity,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        var capacity = new RecordingCapacity(realCapacity);
        var pinnedOutbox = new RecordingOutbox();
        var mutableOutbox = new RecordingOutbox();
        await using var rowLock = await RuntimeRowLock.AcquireAsync(
            connectionString,
            runtimeId,
            cancellationToken);
        await using var pinnedDb = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-pinned-first"));
        var pinnedTask = CreateHandler(pinnedDb, capacity, pinnedOutbox)
            .ExecuteAsync(PinnedMessage(runtimeId), cancellationToken);
        await WaitForBlockedUpdateAsync(
            connectionString,
            "image-guard-pinned-first",
            cancellationToken);

        await using var mutableDb = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-mutable-second"));
        var mutableTask = CreateHandler(mutableDb, capacity, mutableOutbox)
            .ExecuteAsync(MutableMessage(runtimeId), cancellationToken);
        await WaitForBlockedUpdateAsync(
            connectionString,
            "image-guard-mutable-second",
            cancellationToken);
        await rowLock.ReleaseAsync(cancellationToken);

        await Assert.That(await pinnedTask).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(await mutableTask).IsEqualTo(MessageExecutionOutcome.Superseded);
        await Assert.That(capacity.ClaimCalls).IsEqualTo(1);
        await Assert.That(capacity.OrphanReleaseCalls).IsEqualTo(0);
        await Assert.That(capacity.OwnerReleaseCalls).IsEqualTo(0);
        await Assert.That(await redis.KeyExistsAsync(ClaimKey(runtimeId))).IsTrue();
        await Assert.That(pinnedOutbox.RunnerNodeMessages.OfType<ProvisionContainerRuntime>())
            .Count()
            .IsEqualTo(1);
        await Assert.That(mutableOutbox.Published.OfType<RuntimeProvisionFailed>())
            .IsEmpty();
        await using (var verify = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-pinned-verify")))
        {
            var runtime = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                candidate => candidate.Id == runtimeId,
                cancellationToken);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Provisioning);
            await Assert.That(runtime.RunnerAssignmentReleaseToken).IsNull();
        }
        await realCapacity.ReleaseAsync(runtimeId, RunnerId, cancellationToken);
        await AssertCapacityAvailableAsync(redis);
    }

    private static async Task AssertMutableWinnerReleasesStaleClaimOnceAsync(
        string connectionString,
        IDatabase redis,
        RedisRunnerCapacityGate realCapacity,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        await ClaimAsync(realCapacity, runtimeId, cancellationToken);
        var capacity = new RecordingCapacity(realCapacity);
        var mutableOutbox = new RecordingOutbox();
        var pinnedOutbox = new RecordingOutbox();
        await using var rowLock = await RuntimeRowLock.AcquireAsync(
            connectionString,
            runtimeId,
            cancellationToken);
        await using var mutableDb = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-mutable-first"));
        var mutableTask = CreateHandler(mutableDb, capacity, mutableOutbox)
            .ExecuteAsync(MutableMessage(runtimeId), cancellationToken);
        await WaitForBlockedUpdateAsync(
            connectionString,
            "image-guard-mutable-first",
            cancellationToken);

        await using var pinnedDb = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-pinned-second"));
        var pinnedTask = CreateHandler(pinnedDb, capacity, pinnedOutbox)
            .ExecuteAsync(PinnedMessage(runtimeId), cancellationToken);
        await WaitForBlockedUpdateAsync(
            connectionString,
            "image-guard-pinned-second",
            cancellationToken);
        await rowLock.ReleaseAsync(cancellationToken);

        await Assert.That(await mutableTask).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(await pinnedTask).IsEqualTo(MessageExecutionOutcome.Superseded);
        await Assert.That(capacity.ClaimCalls).IsEqualTo(1);
        await Assert.That(capacity.OrphanReleaseCalls).IsEqualTo(1);
        await Assert.That(capacity.OwnerReleaseCalls).IsEqualTo(0);
        await Assert.That(capacity.OrphanReleaseOutcomes.ToArray())
            .IsEquivalentTo([RunnerCapacityReleaseOutcome.Released]);
        await Assert.That(await redis.KeyExistsAsync(ClaimKey(runtimeId))).IsFalse();
        await AssertCapacityAvailableAsync(redis);
        var failure = mutableOutbox.Published.OfType<RuntimeProvisionFailed>().Single();
        await Assert.That(failure.FailureCode)
            .IsEqualTo(RuntimeFailureCode.InvalidConfiguration);
        await Assert.That(pinnedOutbox.RunnerNodeMessages.OfType<ProvisionContainerRuntime>())
            .IsEmpty();

        await using (var writeBackDb = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-mutable-writeback")))
            await RuntimeWriteBackHandler.Handle(failure, writeBackDb, cancellationToken);
        await using var verify = new NoCtfDbContext(CreateOptions(
            connectionString,
            "image-guard-mutable-verify"));
        var runtime = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
            candidate => candidate.Id == runtimeId,
            cancellationToken);
        await Assert.That(runtime.State).IsEqualTo(RuntimeState.Failed);
        await Assert.That(runtime.RunnerAssignmentReleaseToken).IsNull();
    }

    private static RuntimeClaimHandler CreateHandler(
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox) => new(
            db,
            capacity,
            outbox,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = "default",
                    ["Runner:Id"] = RunnerId
                })
                .Build());

    private static ClaimContainerRuntime PinnedMessage(Guid runtimeId) => new(
        runtimeId,
        ProcessingVersion: 0,
        Generation: 1,
        RunnerPool: "default",
        ContainerDefinition(runtimeId, "registry.example/acme/runtime@sha256:"
            + new string('a', 64)));

    private static ClaimContainerRuntime MutableMessage(Guid runtimeId) => new(
        runtimeId,
        ProcessingVersion: 0,
        Generation: 1,
        RunnerPool: "default",
        ContainerDefinition(runtimeId, "registry.example/acme/runtime:latest"));

    private static ContainerRequest ContainerDefinition(Guid runtimeId, string image) => new(
        Guid.CreateVersion7(),
        RuntimeProvider.Docker,
        image,
        [],
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<int, int> { [8080] = 0 },
        Limits,
        new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
        null,
        Generation: 1,
        RuntimeInstanceId: runtimeId);

    private static async Task ClaimAsync(
        IRunnerCapacityGate capacity,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        var claim = await capacity.TryClaimForRunnerAsync(
            new RunnerCapacityRequest(
                runtimeId,
                "default",
                Limits.MemoryBytes,
                Limits.NanoCpus,
                Limits.PidsLimit),
            RunnerId,
            cancellationToken);
        await Assert.That(claim.Availability)
            .IsEqualTo(RunnerCapacityAvailability.Claimed);
    }

    private static async Task InitializeRunnerAsync(
        IDatabase redis,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.SetAddAsync("runner-pool:default:members", RunnerId);
        await redis.StringSetAsync(
            $"runner:{RunnerId}:heartbeat",
            "alive",
            TimeSpan.FromMinutes(5));
        await redis.HashSetAsync(
            $"runner:{RunnerId}:capacity",
            [
                new("availableMemoryBytes", TotalMemory),
                new("availableNanoCpus", TotalNanoCpus),
                new("availablePids", TotalPids),
                new("totalMemoryBytes", TotalMemory),
                new("totalNanoCpus", TotalNanoCpus),
                new("totalPids", TotalPids)
            ]);
    }

    private static async Task AssertCapacityAvailableAsync(IDatabase redis)
    {
        await Assert.That((long)await redis.HashGetAsync(
            $"runner:{RunnerId}:capacity",
            "availableMemoryBytes")).IsEqualTo(TotalMemory);
        await Assert.That((long)await redis.HashGetAsync(
            $"runner:{RunnerId}:capacity",
            "availableNanoCpus")).IsEqualTo(TotalNanoCpus);
        await Assert.That((long)await redis.HashGetAsync(
            $"runner:{RunnerId}:capacity",
            "availablePids")).IsEqualTo(TotalPids);
    }

    private static string ClaimKey(Guid runtimeId) => $"runner-claim:{runtimeId:N}";

    private static DbContextOptions<NoCtfDbContext> CreateOptions(
        string connectionString,
        string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName
        };
        return new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
    }

    private static async Task WaitForBlockedUpdateAsync(
        string connectionString,
        string applicationName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE application_name = @application_name
                      AND wait_event_type = 'Lock')
                """;
            command.Parameters.AddWithValue("application_name", applicationName);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
        throw new TimeoutException(
            $"Runtime update for '{applicationName}' did not block on the test row lock.");
    }

    private static async Task<Guid> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        string suffix,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = $"runtime-image-{suffix}",
            NormalizedUserName = $"RUNTIME-IMAGE-{suffix.ToUpperInvariant()}",
            Email = $"runtime-image-{suffix}@example.test",
            NormalizedEmail = $"RUNTIME-IMAGE-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = $"Runtime image guard {suffix}",
            Mode = GameMode.Ctf,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddMinutes(-1),
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
            Title = $"Legacy tag {suffix}",
            Direction = "Web",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            Purpose = RuntimePurpose.Player,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "default",
            State = RuntimeState.Queued,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return runtimeId;
    }

    private sealed class RuntimeRowLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection connection;
        private readonly NpgsqlTransaction transaction;
        private bool released;

        private RuntimeRowLock(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        public static async Task<RuntimeRowLock> AcquireAsync(
            string connectionString,
            Guid runtimeId,
            CancellationToken cancellationToken)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM runtime_instances WHERE id = @id FOR UPDATE";
            command.Parameters.AddWithValue("id", runtimeId);
            await command.ExecuteScalarAsync(cancellationToken);
            return new RuntimeRowLock(connection, transaction);
        }

        public async Task ReleaseAsync(CancellationToken cancellationToken)
        {
            if (released)
                return;
            await transaction.CommitAsync(cancellationToken);
            released = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!released)
                await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingCapacity(IRunnerCapacityGate inner)
        : IRunnerCapacityGate
    {
        private int claimCalls;
        private int ownerReleaseCalls;
        private int orphanReleaseCalls;

        public int ClaimCalls => Volatile.Read(ref claimCalls);
        public int OwnerReleaseCalls => Volatile.Read(ref ownerReleaseCalls);
        public int OrphanReleaseCalls => Volatile.Read(ref orphanReleaseCalls);
        public ConcurrentQueue<RunnerCapacityReleaseOutcome> OrphanReleaseOutcomes { get; } = [];

        public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
            string runnerPool,
            string runnerId,
            CancellationToken cancellationToken) =>
            inner.GetHeartbeatAsync(runnerPool, runnerId, cancellationToken);

        public Task<RunnerPoolInventory> GetPoolInventoryAsync(
            string runnerPool,
            CancellationToken cancellationToken) =>
            inner.GetPoolInventoryAsync(runnerPool, cancellationToken);

        public Task<RunnerCapacityClaim> TryClaimAsync(
            RunnerCapacityRequest request,
            CancellationToken cancellationToken) =>
            inner.TryClaimAsync(request, cancellationToken);

        public async Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
            RunnerCapacityRequest request,
            string runnerId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref claimCalls);
            return await inner.TryClaimForRunnerAsync(request, runnerId, cancellationToken);
        }

        public async Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
            Guid runtimeInstanceId,
            string runnerId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ownerReleaseCalls);
            return await inner.ReleaseAsync(runtimeInstanceId, runnerId, cancellationToken);
        }

        public async Task<RunnerCapacityReleaseOutcome> ReleaseOrphanedAsync(
            Guid runtimeInstanceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref orphanReleaseCalls);
            var outcome = await inner.ReleaseOrphanedAsync(runtimeInstanceId, cancellationToken);
            OrphanReleaseOutcomes.Enqueue(outcome);
            return outcome;
        }
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerNodeMessages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage
        {
            RunnerNodeMessages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
