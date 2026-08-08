using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Storage;

public sealed class BusinessFileReferenceStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : IBusinessFileReferenceStore
{
    public async Task<BusinessFileReferenceResult> ReplaceTeamAvatarAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid teamId,
        Guid fileId,
        StoredObject stored,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var team = await db.Teams.SingleOrDefaultAsync(candidate =>
            candidate.Id == teamId
            && candidate.CompetitionId == competitionId
            && candidate.DeletedAt == null, ct);
        if (team is null)
            return new(BusinessFileReferenceState.NotFound);
        if (!isAdministrator && team.CaptainId != actorUserId
            && !await CanManageCompetitionAsync(competitionId, actorUserId, ct))
            return new(BusinessFileReferenceState.Forbidden);

        var previousFileId = team.AvatarFileId;
        var file = CreateFile(fileId, stored, now);
        db.Files.Add(file);
        team.AvatarFileId = file.Id;
        await db.SaveChangesAsync(ct);
        if (previousFileId is { } previous && previous != file.Id)
            await outbox.PublishAsync(new CleanupFile(previous));
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(BusinessFileReferenceState.Updated, Map(file));
    }

    public async Task<BusinessFileReferenceResult> ClearTeamAvatarAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var team = await db.Teams.SingleOrDefaultAsync(candidate =>
            candidate.Id == teamId
            && candidate.CompetitionId == competitionId
            && candidate.DeletedAt == null, ct);
        if (team is null)
            return new(BusinessFileReferenceState.NotFound);
        if (!isAdministrator && team.CaptainId != actorUserId
            && !await CanManageCompetitionAsync(competitionId, actorUserId, ct))
            return new(BusinessFileReferenceState.Forbidden);

        var previousFileId = team.AvatarFileId;
        team.AvatarFileId = null;
        await db.SaveChangesAsync(ct);
        if (previousFileId is { } previous)
            await outbox.PublishAsync(new CleanupFile(previous));
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(BusinessFileReferenceState.Cleared);
    }

    public Task<BusinessFileReference?> GetTeamAvatarAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct) =>
        db.Teams.AsNoTracking()
            .Where(team => team.Id == teamId
                && team.CompetitionId == competitionId
                && team.DeletedAt == null
                && team.AvatarFileId != null)
            .Join(db.Files.AsNoTracking(), team => team.AvatarFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    public async Task<BusinessFileReferenceResult> ReplaceCompetitionPosterAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid fileId,
        StoredObject stored,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competition = await db.Competitions.SingleOrDefaultAsync(candidate =>
            candidate.Id == competitionId && candidate.DeletedAt == null, ct);
        if (competition is null)
            return new(BusinessFileReferenceState.NotFound);
        if (!isAdministrator && competition.OwnerId != actorUserId
            && !competition.ManagerIds.Contains(actorUserId))
            return new(BusinessFileReferenceState.Forbidden);

        var previousFileId = competition.PosterFileId;
        var file = CreateFile(fileId, stored, now);
        db.Files.Add(file);
        competition.PosterFileId = file.Id;
        competition.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (previousFileId is { } previous && previous != file.Id)
            await outbox.PublishAsync(new CleanupFile(previous));
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(BusinessFileReferenceState.Updated, Map(file));
    }

    public async Task<BusinessFileReferenceResult> ClearCompetitionPosterAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competition = await db.Competitions.SingleOrDefaultAsync(candidate =>
            candidate.Id == competitionId && candidate.DeletedAt == null, ct);
        if (competition is null)
            return new(BusinessFileReferenceState.NotFound);
        if (!isAdministrator && competition.OwnerId != actorUserId
            && !competition.ManagerIds.Contains(actorUserId))
            return new(BusinessFileReferenceState.Forbidden);

        var previousFileId = competition.PosterFileId;
        competition.PosterFileId = null;
        competition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (previousFileId is { } previous)
            await outbox.PublishAsync(new CleanupFile(previous));
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(BusinessFileReferenceState.Cleared);
    }

    public Task<BusinessFileReference?> GetCompetitionPosterAsync(
        Guid competitionId,
        CancellationToken ct) =>
        db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId
                && competition.DeletedAt == null
                && competition.PosterFileId != null)
            .Join(db.Files.AsNoTracking(), competition => competition.PosterFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    private Task<bool> CanManageCompetitionAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken ct) =>
        db.Competitions.AsNoTracking().AnyAsync(competition =>
            competition.Id == competitionId
            && competition.DeletedAt == null
            && (competition.OwnerId == actorUserId
                || competition.ManagerIds.Contains(actorUserId)), ct);

    private static StoredFile CreateFile(
        Guid fileId,
        StoredObject stored,
        DateTimeOffset now) =>
        new()
        {
            Id = fileId,
            ObjectKey = stored.ObjectKey,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            ByteLength = stored.Length,
            Sha256 = Convert.FromHexString(stored.Sha256),
            CreatedAt = now
        };

    private static BusinessFileReference Map(StoredFile file) =>
        new(file.Id, file.ObjectKey, file.FileName, file.ContentType);
}
