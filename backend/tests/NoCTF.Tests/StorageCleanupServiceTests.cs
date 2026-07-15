using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Worker;

namespace NoCTF.Tests;

public class StorageCleanupServiceTests
{
    [Fact]
    public async Task CleanupBatch_DeletesUnreferencedObjectAndOutboxRow()
    {
        var storage = new RecordingStorageProvider();
        await using var provider = BuildServices(storage);
        var id = await SeedCleanupItemAsync(provider, "archives/unused.tar.gz");
        var service = new StorageCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StorageCleanupService>.Instance);

        Assert.Equal(1, await service.CleanupBatchAsync(CancellationToken.None));

        Assert.Equal(["archives/unused.tar.gz"], storage.DeletedKeys);
        await using var scope = provider.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .StorageCleanupItems.AnyAsync(item => item.Id == id));
    }

    [Fact]
    public async Task CleanupBatch_PreservesReferencedObjectAndCancelsStaleOutboxRow()
    {
        var storage = new RecordingStorageProvider();
        await using var provider = BuildServices(storage);
        var competitionId = Guid.NewGuid();
        var key = "challenge-attachments/shared.zip";
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Competitions.Add(CompetitionFor(competitionId));
            db.Challenges.Add(new Challenge
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                Title = "active",
                TypeId = "ctf",
                AttachmentStorageKey = key,
                CreatedAt = DateTime.UtcNow
            });
            db.StorageCleanupItems.Add(CleanupItem(key));
            await db.SaveChangesAsync();
        }
        var service = new StorageCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StorageCleanupService>.Instance);

        Assert.Equal(1, await service.CleanupBatchAsync(CancellationToken.None));

        Assert.Empty(storage.DeletedKeys);
        await using var verificationScope = provider.CreateAsyncScope();
        Assert.Empty(await verificationScope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>().StorageCleanupItems.ToListAsync());
    }

    [Fact]
    public async Task CleanupBatch_FailureIsPersistedForRetry()
    {
        var storage = new RecordingStorageProvider { FailDeletes = true };
        await using var provider = BuildServices(storage);
        var id = await SeedCleanupItemAsync(provider, "archives/retry.tar.gz");
        var service = new StorageCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StorageCleanupService>.Instance);

        Assert.Equal(0, await service.CleanupBatchAsync(CancellationToken.None));

        await using var scope = provider.CreateAsyncScope();
        var item = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .StorageCleanupItems.SingleAsync(candidate => candidate.Id == id);
        Assert.Equal(1, item.AttemptCount);
        Assert.Null(item.LockOwner);
        Assert.Null(item.LockedUntil);
        Assert.True(item.NotBefore > DateTime.UtcNow);
        Assert.Contains("storage unavailable", item.LastError, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildServices(IStorageProvider storage)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddSingleton<ITenantContext, StorageCleanupTenantContext>();
        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseSharedInMemoryServiceProvider()
                .UseInMemoryDatabase(databaseName));
        services.AddSingleton(storage);
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedCleanupItemAsync(ServiceProvider provider, string key)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = CleanupItem(key);
        db.StorageCleanupItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static StorageCleanupItem CleanupItem(string key)
        => new()
        {
            Id = Guid.NewGuid(),
            StorageKey = key,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

    private static Competition CompetitionFor(Guid id)
        => new()
        {
            Id = id,
            CompetitionId = id,
            OwnerId = Guid.NewGuid(),
            Title = "storage-test",
            ModeKey = "ctf",
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
            Status = CompetitionStatus.Running
        };

    private sealed class RecordingStorageProvider : IStorageProvider
    {
        public bool FailDeletes { get; init; }
        public List<string> DeletedKeys { get; } = [];

        public Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
                throw new InvalidOperationException("storage unavailable");
            DeletedKeys.Add(fileName);
            return Task.CompletedTask;
        }

        public Task<string> GetUrlAsync(string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(fileName);

        public Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StorageCleanupTenantContext : ITenantContext
    {
        public Guid? CompetitionId => null;
        public void SetCompetitionId(Guid? id) { }
    }
}
