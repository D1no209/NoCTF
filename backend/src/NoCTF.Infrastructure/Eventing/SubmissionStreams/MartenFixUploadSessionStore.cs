using Marten;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

public sealed class MartenFixUploadSessionStore(
    IDocumentSession session,
    NoCtfDbContext db,
    IObjectStorage storage) : IFixUploadSessionStore
{
    public async Task<FixUploadGrant?> CreateAsync(
        CreateFixUploadCommand command,
        CancellationToken cancellationToken)
    {
        var scopeExists = await EF.AnyAsync(db.Challenges, item =>
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
        session.Insert(new FixUploadSession
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
        await session.SaveChangesAsync(cancellationToken);
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
        var upload = await session.LoadAsync<FixUploadSession>(uploadId, cancellationToken);
        if (upload is null
            || upload.CompetitionId != competitionId && competitionId != Guid.Empty
            || upload.TeamId != teamId && teamId != Guid.Empty
            || upload.ChallengeId != challengeId && challengeId != Guid.Empty
            || upload.UserId != userId
            || upload.Consumed
            || upload.ExpiresAt <= now)
            return null;
        return new(upload.ObjectKey, upload.FileName, upload.ContentType, upload.ExpectedLength, upload.ExpectedSha256);
    }

}
