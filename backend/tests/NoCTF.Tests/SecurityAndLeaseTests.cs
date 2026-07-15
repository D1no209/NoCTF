using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class SecurityAndLeaseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("change-me-in-production-with-a-real-secret")]
    [InlineData("change_me_at_least_32_chars_long_secret_key")]
    [InlineData("change.me.at.least.32.chars.long.secret.key")]
    [InlineData("change me at least 32 chars long secret key")]
    [InlineData("replace-with-a-long-random-secret-value")]
    public void SecretValueValidator_RejectsMissingWeakOrPlaceholderValues(string? value)
        => Assert.True(SecretValueValidator.IsUnsafe(value, 24));

    [Theory]
    [InlineData("change_me_strong_password")]
    [InlineData("change_me_at_least_32_chars_long_secret_key")]
    [InlineData("change_me_local_storage_url_signing_key")]
    [InlineData("change_me_admin_password")]
    [InlineData("change_me_minio_password")]
    [InlineData("change_me_runner_internal_api_key")]
    public void SecretValueValidator_RejectsEnvironmentExampleSecrets(string value)
        => Assert.True(SecretValueValidator.IsUnsafe(value, 12));

    [Fact]
    public void SecretValueValidator_AcceptsLongRandomValue()
        => Assert.False(SecretValueValidator.IsUnsafe("4rT8-1mQ9-xV2p-Z7cN-5sK6-yH3w", 24));

    [Theory]
    [InlineData("")]
    [InlineData("Host=db;Database=noctf;Username=noctf")]
    [InlineData("Host=db;Database=noctf;Username=noctf;Password=noctf_password")]
    [InlineData("Host=db;Database=noctf;Username=noctf;Password=change_me_strong_password")]
    [InlineData("not-a-connection-string")]
    public void SecretValueValidator_RejectsUnsafeDatabaseConnections(string connectionString)
        => Assert.True(SecretValueValidator.IsConnectionStringUnsafe(connectionString));

    [Fact]
    public void SecretValueValidator_AcceptsDatabaseConnectionWithStrongPassword()
        => Assert.False(SecretValueValidator.IsConnectionStringUnsafe(
            "Host=db;Database=noctf;Username=noctf;Password=V7mZ4-rQ2x-H9pL6-kT8w"));

    [Fact]
    public async Task CompetitionExecutionLease_AllowsOnlyOneOwnerPerKeyUntilReleased()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var leases = new CompetitionExecutionLease();

        var first = await leases.TryAcquireAsync(db, "engine", competitionId);
        Assert.NotNull(first);
        Assert.Null(await leases.TryAcquireAsync(db, "engine", competitionId));

        await first!.DisposeAsync();
        var next = await leases.TryAcquireAsync(db, "engine", competitionId);
        Assert.NotNull(next);
        await next!.DisposeAsync();
    }

    [Fact]
    public async Task BackgroundTaskQueue_RenewsOnlyCurrentOwnersLease()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var queue = new BackgroundTaskQueue(db);
        var taskId = await queue.EnqueueAsync(competitionId, "test", new { value = 1 });
        var acquired = await queue.TryAcquireNextAsync(TimeSpan.FromMinutes(1));

        Assert.NotNull(acquired);
        Assert.False(await queue.RenewLeaseAsync(taskId, "wrong-owner", TimeSpan.FromMinutes(5)));
        Assert.True(await queue.RenewLeaseAsync(taskId, acquired!.LockOwner!, TimeSpan.FromMinutes(5)));
        var renewed = await db.BackgroundTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == taskId);
        Assert.True(renewed.LockedUntil > DateTime.UtcNow.AddMinutes(4));
    }

    [Fact]
    public async Task BackgroundTaskQueue_RedactsSensitivePersistedErrors()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var queue = new BackgroundTaskQueue(db);
        var taskId = await queue.EnqueueAsync(competitionId, "test", new { value = 1 });
        var acquired = await queue.TryAcquireNextAsync(TimeSpan.FromMinutes(1));

        await queue.MarkFailedAsync(
            taskId,
            acquired!.LockOwner!,
            new InvalidOperationException("runner token=super-secret-token"));

        var failed = await db.BackgroundTasks.IgnoreQueryFilters().SingleAsync(task => task.Id == taskId);
        Assert.Contains("[REDACTED]", failed.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", failed.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackgroundTaskQueue_ClaimsIncludedAndExcludedWorkloadsIndependently()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var queue = new BackgroundTaskQueue(db);
        var longRunningId = await queue.EnqueueAsync(competitionId, "long", new { value = 1 });
        var standardId = await queue.EnqueueAsync(competitionId, "standard", new { value = 2 });

        var standard = await queue.TryAcquireNextAsync(
            TimeSpan.FromMinutes(1),
            includedTypes: null,
            excludedTypes: ["long"]);
        var longRunning = await queue.TryAcquireNextAsync(
            TimeSpan.FromMinutes(1),
            includedTypes: ["long"],
            excludedTypes: null);

        Assert.Equal(standardId, standard!.Id);
        Assert.Equal(longRunningId, longRunning!.Id);
    }

    [Fact]
    public async Task BackgroundTaskQueue_RecoversExpiredTasksInBoundedBatchesAndLeavesActiveLease()
    {
        const int recoveryBatchSize = 100;
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var now = DateTime.UtcNow;
        for (var index = 0; index < recoveryBatchSize + 1; index++)
        {
            db.BackgroundTasks.Add(new BackgroundTaskItem
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                Type = "test",
                Status = BackgroundTaskStatus.Running,
                LockOwner = Guid.NewGuid().ToString("N"),
                LockedUntil = now.AddMinutes(-1),
                AttemptCount = 1,
                MaxAttempts = 3,
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-1)
            });
        }

        var activeId = Guid.NewGuid();
        db.BackgroundTasks.Add(new BackgroundTaskItem
        {
            Id = activeId,
            CompetitionId = competitionId,
            Type = "test",
            Status = BackgroundTaskStatus.Running,
            LockOwner = "active-owner",
            LockedUntil = now.AddMinutes(5),
            AttemptCount = 1,
            MaxAttempts = 3,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var queue = new BackgroundTaskQueue(db);
        Assert.Equal(
            recoveryBatchSize,
            await queue.RecoverExpiredRunningTasksAsync(TimeSpan.FromMinutes(10)));
        Assert.Equal(1, await queue.RecoverExpiredRunningTasksAsync(TimeSpan.FromMinutes(10)));

        var active = await db.BackgroundTasks.IgnoreQueryFilters().SingleAsync(task => task.Id == activeId);
        Assert.Equal(BackgroundTaskStatus.Running, active.Status);
        Assert.Equal("active-owner", active.LockOwner);
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new SecurityTenantContext(competitionId));
    }
}

file sealed class SecurityTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
