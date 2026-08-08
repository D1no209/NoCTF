using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformConfigurationStore(
    NoCtfDbContext db,
    IFusionCacheProvider? cacheProvider = null,
    ITransactionalMessageOutbox? messageOutbox = null,
    FileReferenceLock? fileReferenceLock = null)
    : IPlatformConfigurationStore
{
    private const short SettingsId = 1;
    private const string CacheKey = "platform-configuration";
    private readonly IFusionCache? cache = cacheProvider?.GetCache(NoCtfCacheNames.ReadModels);
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new OpenApiTransactionalMessageOutbox();
    private readonly FileReferenceLock fileLock = fileReferenceLock ?? new FileReferenceLock();

    public Task<PlatformConfigurationView> GetAsync(CancellationToken ct) =>
        cache is null
            ? LoadAsync(ct)
            : cache.GetOrSetAsync<PlatformConfigurationView>(
                CacheKey,
                (_, token) => LoadAsync(token),
                token: ct).AsTask();

    public async Task<PlatformConfigurationView?> UpdateAsync(
        string name,
        string? description,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        if (settings.Revision != expectedRevision)
            return null;

        settings.Name = name;
        settings.Description = description;
        settings.Revision = checked(settings.Revision + 1);
        settings.UpdatedAt = now;
        return await SaveAsync(settings, ct);
    }

    public async Task<PlatformLogoReplacement?> ReplaceLogoAsync(
        Guid fileId,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        if (settings.Revision != expectedRevision)
            return null;
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return null;

        var previousFileId = settings.LogoFileId;
        settings.LogoFileId = fileId;
        settings.Revision = checked(settings.Revision + 1);
        settings.UpdatedAt = now;
        if (previousFileId is { } previous && previous != fileId)
            await outbox.PublishAsync(new CleanupFile(previous));
        var updated = await SaveAsync(settings, ct, updateCache: false);
        if (updated is null)
            return null;
        await transaction.CommitAsync(ct);
        if (cache is not null)
            await cache.SetAsync(CacheKey, updated, token: ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(updated, previousFileId);
    }

    public Task<BusinessFileReference?> GetLogoFileAsync(CancellationToken ct) =>
        db.PlatformSettings.AsNoTracking()
            .Where(settings => settings.Id == SettingsId && settings.LogoFileId != null)
            .Join(db.Files.AsNoTracking(), settings => settings.LogoFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    private async Task<PlatformConfigurationView?> SaveAsync(
        PlatformSettings settings,
        CancellationToken ct,
        bool updateCache = true)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            var view = ToView(settings);
            if (cache is not null && updateCache)
                await cache.SetAsync(CacheKey, view, token: ct);
            return view;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
    }

    private async Task<PlatformConfigurationView> LoadAsync(CancellationToken ct) =>
        ToView(await db.PlatformSettings.AsNoTracking().Include(item => item.LogoFile)
            .SingleAsync(settings => settings.Id == SettingsId, ct));

    private static PlatformConfigurationView ToView(PlatformSettings settings) =>
        new(
            settings.Name,
            settings.Description,
            settings.LogoFileId,
            settings.Revision,
            settings.UpdatedAt);
}

public sealed class OpenApiPlatformConfigurationStore : IPlatformConfigurationStore
{
    private static readonly PlatformConfigurationView Default =
        new("NoCTF", null, null, 1, DateTimeOffset.UnixEpoch);

    public Task<PlatformConfigurationView> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Default);

    public Task<PlatformConfigurationView?> UpdateAsync(
        string name,
        string? description,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlatformConfigurationView?>(Default with
        {
            Name = name,
            Description = description,
            Revision = expectedRevision + 1,
            UpdatedAt = now
        });

    public Task<PlatformLogoReplacement?> ReplaceLogoAsync(
        Guid fileId,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlatformLogoReplacement?>(new(
            Default with
            {
                LogoFileId = fileId,
                Revision = expectedRevision + 1,
                UpdatedAt = now
            },
            null));

    public Task<BusinessFileReference?> GetLogoFileAsync(CancellationToken cancellationToken) =>
        Task.FromResult<BusinessFileReference?>(null);
}
