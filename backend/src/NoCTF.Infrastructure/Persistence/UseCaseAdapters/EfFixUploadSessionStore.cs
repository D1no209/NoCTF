using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixUploadSessionStore(
    NoCtfDbContext db,
    IObjectStorage storage) : IFixUploadSessionStore
{
    public async Task<FixUploadGrant?> CreateAsync(
        CreateFixUploadCommand command,
        CancellationToken cancellationToken)
    {
        var scopeExists = await db.Challenges.AnyAsync(item =>
            item.Id == command.ChallengeId && item.CompetitionId == command.CompetitionId,
            cancellationToken);
        if (!scopeExists)
            return null;

        var uploadId = Guid.CreateVersion7(command.RequestedAt);
        var objectKey = $"fix/{command.CompetitionId:N}/{command.TeamId:N}/{uploadId:N}/{Path.GetFileName(command.FileName)}";
        var grant = await storage.CreateUploadAsync(
            uploadId,
            objectKey,
            command.ContentType,
            command.Length,
            TimeSpan.FromMinutes(15),
            cancellationToken);
        db.FixUploadSessions.Add(new FixUploadSession
        {
            Id = uploadId,
            CompetitionId = command.CompetitionId,
            TeamId = command.TeamId,
            ChallengeId = command.ChallengeId,
            UserId = command.UserId,
            ObjectKey = objectKey,
            FileName = Path.GetFileName(command.FileName),
            ContentType = command.ContentType,
            ExpectedLength = command.Length,
            ExpectedSha256 = command.Sha256.ToLowerInvariant(),
            CreatedAt = command.RequestedAt,
            ExpiresAt = grant.ExpiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
        return grant with { UploadId = uploadId };
    }

    public async Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(
        Guid uploadId,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var upload = await db.FixUploadSessions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == uploadId
            && (competitionId == Guid.Empty || item.CompetitionId == competitionId)
            && (teamId == Guid.Empty || item.TeamId == teamId)
            && (challengeId == Guid.Empty || item.ChallengeId == challengeId)
            && item.UserId == userId
            && !item.Consumed
            && item.ExpiresAt > now,
            cancellationToken);
        return upload is null
            ? null
            : new(upload.ObjectKey, upload.FileName, upload.ContentType, upload.ExpectedLength, upload.ExpectedSha256);
    }

    public async Task<bool> TryConsumeAsync(Guid uploadId, DateTimeOffset consumedAt, CancellationToken cancellationToken)
    {
        var updated = await db.FixUploadSessions
            .Where(item => item.Id == uploadId && !item.Consumed && item.ExpiresAt > consumedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Consumed, true), cancellationToken);
        return updated == 1;
    }

    public Task ReleaseAsync(Guid uploadId, CancellationToken cancellationToken) =>
        db.FixUploadSessions
            .Where(item => item.Id == uploadId && item.Consumed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Consumed, false), cancellationToken);
}
