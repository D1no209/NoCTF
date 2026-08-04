using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformConfigurationStore(NoCtfDbContext db)
    : IPlatformConfigurationStore
{
    private const short SettingsId = 1;

    public async Task<PlatformConfigurationView> GetAsync(CancellationToken ct) =>
        ToView(await db.PlatformSettings.AsNoTracking()
            .SingleAsync(settings => settings.Id == SettingsId, ct));

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
        string objectKey,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        if (settings.Revision != expectedRevision)
            return null;

        var previousObjectKey = settings.LogoObjectKey;
        settings.LogoObjectKey = objectKey;
        settings.Revision = checked(settings.Revision + 1);
        settings.UpdatedAt = now;
        var updated = await SaveAsync(settings, ct);
        return updated is null ? null : new(updated, previousObjectKey);
    }

    private async Task<PlatformConfigurationView?> SaveAsync(
        PlatformSettings settings,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ToView(settings);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
    }

    private static PlatformConfigurationView ToView(PlatformSettings settings) =>
        new(
            settings.Name,
            settings.Description,
            settings.LogoObjectKey,
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
        string objectKey,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlatformLogoReplacement?>(new(
            Default with
            {
                LogoObjectKey = objectKey,
                Revision = expectedRevision + 1,
                UpdatedAt = now
            },
            null));
}
