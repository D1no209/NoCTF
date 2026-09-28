using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Competitions.Progression;

public sealed class CompetitionBadgeStore(
    NoCtfDbContext db, IPostCommitMessagePublisher publisher,
    ICompetitionEventRecorder? eventRecorder = null,
    ProgressionGraphReadCache? readCache = null)
    : ICompetitionBadgeStore
{
    public async Task<IReadOnlyList<CompetitionBadgeView>?> ListAsync(
        Guid competitionId, CancellationToken ct)
    {
        if (!await IsCtfCompetitionAsync(competitionId, ct)) return null;
        if (readCache is not null)
            return await readCache.ReadBadgesAsync(db, competitionId, ct);
        return await db.CompetitionBadges.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.DeletedAt == null)
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new CompetitionBadgeView(
                item.Id, item.CompetitionId, item.Name, item.Description,
                item.ImageFileId, item.CreatedAt, item.UpdatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<CompetitionBadgeResult> CreateAsync(
        Guid competitionId, Guid badgeId, string name, string? description,
        Guid imageFileId, DateTimeOffset now, CancellationToken ct)
    {
        if (!ValidName(name)) return new(null, CompetitionBadgeFailure.InvalidName);
        if (!await IsCtfCompetitionAsync(competitionId, ct))
            return new(null, CompetitionBadgeFailure.NotFound);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        if (!await db.Files.AsNoTracking().AnyAsync(file => file.Id == imageFileId
                && ImageContentTypes.Contains(file.ContentType), ct))
            return new(null, CompetitionBadgeFailure.InvalidImage);
        var badge = new CompetitionBadge
        {
            Id = badgeId, CompetitionId = competitionId,
            Name = name.Trim(), Description = description?.Trim(),
            ImageFileId = imageFileId, CreatedAt = now, UpdatedAt = now
        };
        db.CompetitionBadges.Add(badge);
        await NotifyChangedAsync(competitionId, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        try
        {
            if (readCache is not null)
                await readCache.InvalidateBadgesAsync(competitionId, ct);
        }
        finally
        {
            await publisher.FlushCommittedMessagesAsync();
        }
        return new(ToView(badge));
    }

    public async Task<CompetitionBadgeResult> ReplaceAsync(
        Guid competitionId, Guid badgeId, string? name, string? description,
        Guid? imageFileId, DateTimeOffset now, CancellationToken ct)
    {
        if (!ValidName(name)) return new(null, CompetitionBadgeFailure.InvalidName);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var badge = await db.CompetitionBadges.SingleOrDefaultAsync(item =>
            item.Id == badgeId && item.CompetitionId == competitionId
            && item.DeletedAt == null, ct);
        if (badge is null) return new(null, CompetitionBadgeFailure.NotFound);
        if (imageFileId is { } newImage
            && !await db.Files.AsNoTracking().AnyAsync(file => file.Id == newImage
                && ImageContentTypes.Contains(file.ContentType), ct))
            return new(null, CompetitionBadgeFailure.InvalidImage);
        var previousImage = badge.ImageFileId;
        badge.Name = name!.Trim();
        badge.Description = description?.Trim();
        badge.ImageFileId = imageFileId ?? previousImage;
        badge.UpdatedAt = now;
        await NotifyChangedAsync(competitionId, now, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (imageFileId is not null && imageFileId != previousImage)
                await publisher.PublishAsync(new CleanupFile(previousImage));
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            publisher.DiscardPendingMessages();
            return new(null, CompetitionBadgeFailure.ConcurrencyConflict);
        }
        try
        {
            if (readCache is not null)
                await readCache.InvalidateBadgesAsync(competitionId, ct);
        }
        finally
        {
            await publisher.FlushCommittedMessagesAsync();
        }
        return new(ToView(badge));
    }

    public async Task<CompetitionBadgeFailure?> DeleteAsync(
        Guid competitionId, Guid badgeId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var badge = await db.CompetitionBadges.SingleOrDefaultAsync(item =>
            item.Id == badgeId && item.CompetitionId == competitionId
            && item.DeletedAt == null, ct);
        if (badge is null) return CompetitionBadgeFailure.NotFound;
        if (await db.ProgressionNodes.OfType<BadgeProgressionNode>()
            .AnyAsync(node => node.CompetitionBadgeId == badgeId, ct))
            return CompetitionBadgeFailure.InUse;
        badge.DeletedAt = now;
        badge.UpdatedAt = now;
        await NotifyChangedAsync(competitionId, now, ct);
        await db.SaveChangesAsync(ct);
        await publisher.PublishAsync(new CleanupFile(badge.ImageFileId));
        await transaction.CommitAsync(ct);
        try
        {
            if (readCache is not null)
                await readCache.InvalidateBadgesAsync(competitionId, ct);
        }
        finally
        {
            await publisher.FlushCommittedMessagesAsync();
        }
        return null;
    }

    public Task<BusinessFileReference?> GetImageAsync(
        Guid competitionId, Guid badgeId, CancellationToken ct) =>
        db.CompetitionBadges.AsNoTracking()
            .Where(badge => badge.Id == badgeId
                && badge.CompetitionId == competitionId
                && badge.DeletedAt == null)
            .Join(db.Files.AsNoTracking(), badge => badge.ImageFileId,
                file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    private Task<bool> IsCtfCompetitionAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().AnyAsync(item =>
            item.Id == competitionId && item.Mode == GameMode.Ctf
            && item.DeletedAt == null, ct);

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 160;

    private async Task NotifyChangedAsync(
        Guid competitionId, DateTimeOffset now, CancellationToken ct)
    {
        if (eventRecorder is null) return;
        await eventRecorder.RecordAsync(new(
            competitionId, CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff, now,
            Reason: "Competition badge catalog updated."), ct);
    }

    private static readonly string[] ImageContentTypes =
        ["image/png", "image/jpeg", "image/webp"];

    private static CompetitionBadgeView ToView(CompetitionBadge badge) => new(
        badge.Id, badge.CompetitionId, badge.Name, badge.Description,
        badge.ImageFileId, badge.CreatedAt, badge.UpdatedAt);
}
