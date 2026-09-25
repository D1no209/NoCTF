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
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformConfigurationStore(
    NoCtfDbContext db,
    IFusionCacheProvider? cacheProvider = null,
    IPostCommitMessagePublisher? messageOutbox = null,
    FileReferenceLock? fileReferenceLock = null)
    : IPlatformConfigurationStore, IExperimentalFeatureReader
{
    private const short SettingsId = 1;
    private const string CacheKey = "platform-configuration";
    private readonly IFusionCache? cache = cacheProvider?.GetCache(NoCtfCacheNames.ReadModels);
    private readonly IPostCommitMessagePublisher outbox =
        messageOutbox ?? new NoOpPostCommitMessagePublisher();
    private readonly FileReferenceLock fileLock = fileReferenceLock ?? new FileReferenceLock();

    public Task<PlatformConfigurationView> GetAsync(CancellationToken ct) =>
        cache is null
            ? LoadAsync(ct)
            : cache.GetOrSetAsync<PlatformConfigurationView>(
                CacheKey,
                (_, token) => LoadAsync(token),
                token: ct).AsTask();

    public async Task<bool> IsCtfPatchVerificationEnabledAsync(CancellationToken ct) =>
        (await GetAsync(ct)).CtfPatchVerificationEnabled;

    public async Task<PlatformConfigurationView> UpdateAsync(
        string name,
        string? description,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        settings.Name = name;
        settings.Description = description;
        settings.UpdatedAt = now;
        return await SaveAsync(settings, ct);
    }

    public async Task<PlatformConfigurationView> UpdateExperimentalFeaturesAsync(
        bool ctfPatchVerificationEnabled,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        settings.CtfPatchVerificationEnabled = ctfPatchVerificationEnabled;
        settings.UpdatedAt = now;
        return await SaveAsync(settings, ct);
    }

    public async Task<PlatformLogoReplacement> ReplaceLogoAsync(
        Guid fileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            throw new InvalidOperationException("The uploaded platform logo file is unavailable.");

        var previousFileId = settings.LogoFileId;
        settings.LogoFileId = fileId;
        settings.UpdatedAt = now;
        if (previousFileId is { } previous && previous != fileId)
            await outbox.PublishAsync(new CleanupFile(previous));
        var updated = await SaveAsync(settings, ct, updateCache: false);
        await transaction.CommitAsync(ct);
        if (cache is not null)
            await cache.SetAsync(CacheKey, updated, token: ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(updated, previousFileId);
    }

    public Task<BusinessFileReference?> GetLogoFileAsync(CancellationToken ct) =>
        db.PlatformSettings.AsNoTracking()
            .Where(settings => settings.Id == SettingsId && settings.LogoFileId != null)
            .Join(db.Files.AsNoTracking(), settings => settings.LogoFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    private async Task<PlatformConfigurationView> SaveAsync(
        PlatformSettings settings,
        CancellationToken ct,
        bool updateCache = true)
    {
        await db.SaveChangesAsync(ct);
        var view = ToView(settings);
        if (cache is not null && updateCache)
            await cache.SetAsync(CacheKey, view, token: ct);
        return view;
    }

    private async Task<PlatformConfigurationView> LoadAsync(CancellationToken ct) =>
        ToView(await db.PlatformSettings.AsNoTracking().Include(item => item.LogoFile)
            .SingleAsync(settings => settings.Id == SettingsId, ct));

    private static PlatformConfigurationView ToView(PlatformSettings settings) =>
        new(
            settings.Name,
            settings.Description,
            settings.LogoFileId,
            settings.UpdatedAt,
            settings.CtfPatchVerificationEnabled);
}

public sealed class NoOpPlatformConfigurationStore
    : IPlatformConfigurationStore, IExperimentalFeatureReader
{
    private static readonly PlatformConfigurationView Default =
        new("NoCTF", null, null, DateTimeOffset.UnixEpoch);

    public Task<PlatformConfigurationView> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Default);

    public Task<bool> IsCtfPatchVerificationEnabledAsync(CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<PlatformConfigurationView> UpdateAsync(
        string name,
        string? description,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(Default with
        {
            Name = name,
            Description = description,
            UpdatedAt = now
        });

    public Task<PlatformConfigurationView> UpdateExperimentalFeaturesAsync(
        bool ctfPatchVerificationEnabled,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(Default with
        {
            CtfPatchVerificationEnabled = ctfPatchVerificationEnabled,
            UpdatedAt = now
        });

    public Task<PlatformLogoReplacement> ReplaceLogoAsync(
        Guid fileId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PlatformLogoReplacement(
            Default with
            {
                LogoFileId = fileId,
                UpdatedAt = now
            },
            null));

    public Task<BusinessFileReference?> GetLogoFileAsync(CancellationToken cancellationToken) =>
        Task.FromResult<BusinessFileReference?>(null);
}
