using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Submissions.Intake;
using NoCTF.Domain.Storage;

namespace NoCTF.Infrastructure.Submissions.PatchUploads;

public sealed class PatchUploadStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    SubmissionAttemptCriticalSection attemptCriticalSection,
    ILogger<PatchUploadStore> logger) : IPatchUploadStore
{
    public PatchUploadStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ILogger<PatchUploadStore> logger)
        : this(
            db,
            outbox,
            new SubmissionAttemptCriticalSection(new LocalCriticalSectionRegistry()),
            logger) { }

    public async Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.DeletedAt == null
                && !item.IsBanned
                && item.RegistrationStatus == TeamRegistrationStatus.Approved
                && item.MemberIds.Contains(userId))
            .Select(item => new { item.Id })
            .SingleOrDefaultAsync(ct);
        if (team is null)
            return null;
        var available = await db.CompetitionChallenges.AsNoTracking()
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { challenge, competition })
            .AnyAsync(item => item.challenge.Id == competitionChallengeId
                && item.challenge.CompetitionId == competitionId
                && item.challenge.DeletedAt == null
                && item.competition.Mode == GameMode.Awdp
                && item.competition.Status == CompetitionStatus.Running,
                ct);
        return available
            ? new(competitionId, competitionChallengeId, team.Id, userId)
            : null;
    }

    public async Task<bool> SaveAsync(
        Guid patchUploadId,
        PatchUploadScope scope,
        string objectKey,
        string fileName,
        string contentType,
        long byteLength,
        byte[] sha256,
        DateTimeOffset uploadedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        await using var attemptLease = await attemptCriticalSection.AcquireAsync(
            db,
            scope.TeamId,
            scope.CompetitionChallengeId,
            SubmissionKind.Fix,
            ct);

        var previous = await db.PatchUploads.AsNoTracking()
            .Where(upload => upload.TeamId == scope.TeamId
                && upload.CompetitionChallengeId == scope.CompetitionChallengeId
                && upload.ConsumedAt == null)
            .Select(upload => new { upload.Id, upload.FileId })
            .SingleOrDefaultAsync(ct);
        if (previous is not null)
        {
            var deleted = await db.PatchUploads
                .Where(upload => upload.Id == previous.Id
                    && upload.ConsumedAt == null)
                .ExecuteDeleteAsync(ct);
            if (deleted != 1)
                return false;
        }

        var file = new StoredFile
        {
            Id = Guid.CreateVersion7(uploadedAt),
            ObjectKey = objectKey,
            FileName = fileName,
            ContentType = contentType,
            ByteLength = byteLength,
            Sha256 = sha256,
            CreatedAt = uploadedAt
        };
        db.Files.Add(file);
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId,
            UploadedByUserId = scope.UserId,
            FileId = file.Id,
            File = file,
            UploadedAt = uploadedAt
        });
        try
        {
            if (previous is not null)
                await outbox.PublishAsync(new CleanupFile(previous.FileId));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            try
            {
                await outbox.FlushOutgoingMessagesAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Patch upload {PatchUploadId} committed, but its durable cleanup message was not flushed immediately.",
                    patchUploadId);
            }
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
