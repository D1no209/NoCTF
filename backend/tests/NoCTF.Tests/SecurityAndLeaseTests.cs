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
    [InlineData("replace-with-a-long-random-secret-value")]
    public void SecretValueValidator_RejectsMissingWeakOrPlaceholderValues(string? value)
        => Assert.True(SecretValueValidator.IsUnsafe(value, 24));

    [Fact]
    public void SecretValueValidator_AcceptsLongRandomValue()
        => Assert.False(SecretValueValidator.IsUnsafe("4rT8-1mQ9-xV2p-Z7cN-5sK6-yH3w", 24));

    [Theory]
    [InlineData("")]
    [InlineData("Host=db;Database=noctf;Username=noctf")]
    [InlineData("Host=db;Database=noctf;Username=noctf;Password=noctf_password")]
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

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
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
