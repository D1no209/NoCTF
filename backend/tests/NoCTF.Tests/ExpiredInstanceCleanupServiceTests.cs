using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Worker;

namespace NoCTF.Tests;

public class ExpiredInstanceCleanupServiceTests
{
    [Fact]
    public async Task CleanupPersistenceFailure_PreservesRecordAndRetriesAllArtifacts()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FailCleanupSaveDbContext(options, new FixedCleanupTenantContext(competitionId));
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = "expired-container",
            ProviderType = "docker",
            PortMappingsJson = "{\"80\":30080}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        db.CtfDynamicFlags.Add(new CtfDynamicFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            FlagUuid = "flag-id",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var containerManager = new RecordingCleanupContainerManager();
        var services = new ServiceCollection();
        services.AddSingleton<ApplicationDbContext>(db);
        services.AddSingleton<IContainerManager>(containerManager);
        services.AddSingleton<ICompetitionExecutionLease>(new CompetitionExecutionLease());
        await using var provider = services.BuildServiceProvider();
        var service = new ExpiredInstanceCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExpiredInstanceCleanupService>.Instance);

        db.FailNextCleanupSave = true;
        await service.CleanupExpiredInstancesAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var failedBox = await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("expired-container", failedBox.ContainerInstanceId);
        Assert.Null(failedBox.CleanupOwner);
        Assert.Null(failedBox.CleanupLockedUntil);
        Assert.Single(await db.CtfDynamicFlags.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.AuditLogs.IgnoreQueryFilters().ToListAsync());

        await service.CleanupExpiredInstancesAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var completedBox = await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync();
        Assert.Null(completedBox.ContainerInstanceId);
        Assert.Null(completedBox.ExpiresAt);
        Assert.Empty(await db.CtfDynamicFlags.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.AuditLogs.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(2, containerManager.DestroyCount);
    }

    private sealed class FailCleanupSaveDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext) : ApplicationDbContext(options, tenantContext)
    {
        public bool FailNextCleanupSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextCleanupSave &&
                ChangeTracker.Entries<CompetitionLog>().Any(entry => entry.State == EntityState.Added))
            {
                FailNextCleanupSave = false;
                throw new DbUpdateException("Injected cleanup persistence failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class RecordingCleanupContainerManager : IContainerManager
    {
        public int DestroyCount { get; private set; }

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
        {
            DestroyCount++;
            return Task.CompletedTask;
        }

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

file sealed class FixedCleanupTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
