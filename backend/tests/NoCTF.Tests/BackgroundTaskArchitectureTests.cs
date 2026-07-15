using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Worker;
using Npgsql;

namespace NoCTF.Tests;

public sealed class BackgroundTaskArchitectureTests
{
    [Fact]
    public void JobRegistry_UsesCanonicalCaseInsensitiveLookupAndExposesWorkloads()
    {
        var standard = new StubJobHandler("score.rebuild", CompetitionJobWorkload.Standard);
        var longRunning = new StubJobHandler("patch.validate", CompetitionJobWorkload.LongRunning);
        var registry = new CompetitionJobRegistry([standard, longRunning]);

        Assert.Same(standard, registry.GetRequiredHandler(" SCORE.REBUILD "));
        Assert.Same(longRunning, registry.GetRequiredHandler("Patch.Validate"));
        Assert.Contains(
            registry.Jobs,
            job => job.JobKey == "patch.validate" && job.Workload == CompetitionJobWorkload.LongRunning);
    }

    [Fact]
    public void JobRegistry_RejectsDuplicateKeysBeforeWorkIsClaimed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new CompetitionJobRegistry(
        [
            new StubJobHandler("patch.validate", CompetitionJobWorkload.LongRunning),
            new StubJobHandler("PATCH.VALIDATE", CompetitionJobWorkload.Standard)
        ]));

        Assert.Contains("Duplicate background task job key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("patch.validate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JobRegistry_RejectsEmptyKeysBeforeWorkIsClaimed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new CompetitionJobRegistry(
        [
            new StubJobHandler("  ", CompetitionJobWorkload.Standard)
        ]));

        Assert.Contains("empty job key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkerStartupValidator_RejectsDuplicatePluginJobKeys()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICompetitionJobRegistry, CompetitionJobRegistry>();
        services.AddScoped<ICompetitionJobHandler>(_ =>
            new StubJobHandler("plugin.job", CompetitionJobWorkload.Standard));
        services.AddScoped<ICompetitionJobHandler>(_ =>
            new StubJobHandler("PLUGIN.JOB", CompetitionJobWorkload.LongRunning));
        await using var provider = services.BuildServiceProvider();
        var validator = new CompetitionJobRegistryStartupValidator(
            provider.GetRequiredService<IServiceScopeFactory>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None));

        Assert.Contains("Duplicate background task job key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgresLease_CancelsLostTokenWhenItsSessionConnectionBreaks()
    {
        var connection = new StateChangeConnection();
        var lease = new CompetitionExecutionLease.PostgresLease(
            connection,
            key: 42,
            closeWhenReleased: false);
        using var registration = lease.LostToken.Register(
            static () => throw new InvalidOperationException("consumer callback failure"));

        var exception = Record.Exception(connection.Break);

        Assert.Null(exception);
        Assert.True(lease.LostToken.IsCancellationRequested);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task BackgroundTaskQueue_NormalizesTypesAndRejectsInvalidDurations()
    {
        var competitionId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var tenant = new TenantContext();
        tenant.SetCompetitionId(competitionId);
        await using var db = new ApplicationDbContext(options, tenant);
        var queue = new BackgroundTaskQueue(db);

        var taskId = await queue.EnqueueAsync(competitionId, "  plugin.job  ", new { value = 1 });

        var persisted = await db.BackgroundTasks.IgnoreQueryFilters().SingleAsync(task => task.Id == taskId);
        Assert.Equal("plugin.job", persisted.Type);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.TryAcquireNextAsync(TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.RenewLeaseAsync(taskId, "owner", TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.RecoverExpiredRunningTasksAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task BackgroundTaskQueue_PostgresClaimsCompetingWorkloadsAtomically()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_POSTGRES_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var competitionId = Guid.NewGuid();
        var schemaName = $"noctf_test_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(connectionString);
        await adminConnection.OpenAsync();
        await using (var createSchema = adminConnection.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schemaName)};";
            await createSchema.ExecuteNonQueryAsync();
        }

        var isolatedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schemaName
        }.ConnectionString;
        await using var setup = CreatePostgresDb(isolatedConnectionString, competitionId);
        try
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "BackgroundTasks" (
                    "Id" uuid PRIMARY KEY,
                    "CompetitionId" uuid NOT NULL,
                    "Type" text NOT NULL,
                    "Status" integer NOT NULL,
                    "PayloadJson" text NOT NULL,
                    "AttemptCount" integer NOT NULL,
                    "MaxAttempts" integer NOT NULL,
                    "LastError" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "LockedUntil" timestamp with time zone NULL,
                    "LockOwner" text NULL
                );
                """);

            var setupQueue = new BackgroundTaskQueue(setup);
            var longRunningId = await setupQueue.EnqueueAsync(
                competitionId,
                "long.job",
                new { value = 1 });
            var standardId = await setupQueue.EnqueueAsync(
                competitionId,
                "standard.job",
                new { value = 2 });

            await using var standardDb = CreatePostgresDb(isolatedConnectionString, competitionId);
            await using var longRunningDb = CreatePostgresDb(isolatedConnectionString, competitionId);
            var standardQueue = new BackgroundTaskQueue(standardDb);
            var longRunningQueue = new BackgroundTaskQueue(longRunningDb);

            var claims = await Task.WhenAll(
                standardQueue.TryAcquireNextAsync(
                    TimeSpan.FromMinutes(1),
                    includedTypes: null,
                    excludedTypes: ["long.job"]),
                longRunningQueue.TryAcquireNextAsync(
                    TimeSpan.FromMinutes(1),
                    includedTypes: ["long.job"],
                    excludedTypes: null));

            Assert.Equal(standardId, claims[0]!.Id);
            Assert.Equal(longRunningId, claims[1]!.Id);
            Assert.All(claims, claim =>
            {
                Assert.Equal(BackgroundTaskStatus.Running, claim!.Status);
                Assert.Equal(1, claim.AttemptCount);
                Assert.False(string.IsNullOrWhiteSpace(claim.LockOwner));
                Assert.NotNull(claim.LockedUntil);
            });
            Assert.Null(await standardQueue.TryAcquireNextAsync(TimeSpan.FromMinutes(1)));
        }
        finally
        {
            await setup.Database.CloseConnectionAsync();
            await using var dropSchema = adminConnection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task CtfScoreRebuildLock_JoinsExistingPostgresTransactionAndReleasesLockOnRollback()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_POSTGRES_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var competitionId = Guid.NewGuid();
        var advisoryLockKey = CtfRebuildAdvisoryLockKey(competitionId);
        await using var ownerDb = CreatePostgresDb(connectionString, competitionId);
        await ownerDb.Database.OpenConnectionAsync();
        await using var outerTransaction = await ownerDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var originalTransaction = ownerDb.Database.CurrentTransaction;
        Assert.NotNull(originalTransaction);

        await using var contenderDb = CreatePostgresDb(connectionString, competitionId);
        await contenderDb.Database.OpenConnectionAsync();
        await using var contenderTransaction = await contenderDb.Database.BeginTransactionAsync();

        await using (var rebuildLock = await CtfScoreRebuildLock.AcquireAsync(
                         ownerDb,
                         competitionId,
                         [],
                         CancellationToken.None))
        {
            Assert.Same(originalTransaction, ownerDb.Database.CurrentTransaction);

            await rebuildLock.CommitAsync(CancellationToken.None);

            Assert.Same(originalTransaction, ownerDb.Database.CurrentTransaction);
            Assert.False(await TryAcquirePostgresAdvisoryLockAsync(contenderDb, advisoryLockKey));
        }

        Assert.Same(originalTransaction, ownerDb.Database.CurrentTransaction);
        Assert.Equal(1, await ExecuteScalarIntAsync(ownerDb, "SELECT 1"));
        Assert.False(await TryAcquirePostgresAdvisoryLockAsync(contenderDb, advisoryLockKey));

        await outerTransaction.RollbackAsync();

        Assert.True(await TryAcquirePostgresAdvisoryLockAsync(contenderDb, advisoryLockKey));
        await contenderTransaction.RollbackAsync();
    }

    private static ApplicationDbContext CreatePostgresDb(
        string connectionString,
        Guid competitionId)
    {
        var tenant = new TenantContext();
        tenant.SetCompetitionId(competitionId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ApplicationDbContext(options, tenant);
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static long CtfRebuildAdvisoryLockKey(Guid competitionId)
    {
        var competitionPart = BitConverter.ToInt32(competitionId.ToByteArray(), 0);
        return (long)competitionPart << 32;
    }

    private static async Task<bool> TryAcquirePostgresAdvisoryLockAsync(
        ApplicationDbContext db,
        long lockKey)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_try_advisory_xact_lock(@lock_key);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        return Assert.IsType<bool>(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ExecuteScalarIntAsync(
        ApplicationDbContext db,
        string commandText)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = commandText;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class StubJobHandler(
        string jobKey,
        CompetitionJobWorkload workload) : ICompetitionJobHandler
    {
        public string JobKey { get; } = jobKey;
        public CompetitionJobWorkload Workload { get; } = workload;

        public Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StateChangeConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;

        public void Break()
        {
            var original = _state;
            _state = ConnectionState.Broken;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        public override void ChangeDatabase(string databaseName)
            => throw new NotSupportedException();

        public override void Close()
        {
            var original = _state;
            _state = ConnectionState.Closed;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        public override void Open()
        {
            var original = _state;
            _state = ConnectionState.Open;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
            => throw new NotSupportedException();
    }
}
