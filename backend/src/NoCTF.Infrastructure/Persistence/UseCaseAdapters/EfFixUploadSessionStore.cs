using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Storage;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixUploadSessionStore(NoCtfDbContext db, IObjectStorage storage) : IFixUploadSessionStore
{
    public async Task<FixUploadGrant?> CreateAsync(CreateFixUploadCommand command, CancellationToken ct)
    {
        var uploadId = Guid.CreateVersion7(command.RequestedAt);
        var expiresAt = command.RequestedAt.AddMinutes(15);
        var objectKey = $"fix/{command.CompetitionId:N}/{uploadId:N}";
        var grant = await storage.CreateUploadAsync(uploadId, objectKey, command.ContentType, command.Length, TimeSpan.FromMinutes(15), ct);
        db.FixSubmissionRecords.Add(new FixSubmissionRecord
        {
            UploadId = uploadId, CompetitionId = command.CompetitionId, TeamId = command.TeamId, ChallengeId = command.ChallengeId,
            ObjectKey = objectKey, ExpiresAt = expiresAt, VerificationStatus = FixVerificationStatus.Created,
            ObjectMetadata = JsonSerializer.Serialize(new { command.FileName, command.ContentType, command.Length, command.Sha256, command.UserId }),
            CreatedAt = command.RequestedAt, UpdatedAt = command.RequestedAt
        });
        await db.SaveChangesAsync(ct);
        return grant;
    }

    public async Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(Guid uploadId, Guid competitionId, Guid teamId, Guid challengeId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.FixSubmissionRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UploadId == uploadId
            && x.CompetitionId == competitionId && x.TeamId == teamId && x.ChallengeId == challengeId
            && x.SubmissionId == null && x.ExpiresAt > now, ct);
        if (item is null || string.IsNullOrWhiteSpace(item.ObjectMetadata)) return null;
        using var document = JsonDocument.Parse(item.ObjectMetadata);
        var root = document.RootElement;
        if (!root.TryGetProperty("UserId", out var owner) || owner.GetGuid() != userId) return null;
        return new(item.ObjectKey, root.GetProperty("FileName").GetString()!, root.GetProperty("ContentType").GetString()!,
            root.GetProperty("Length").GetInt64(), root.GetProperty("Sha256").GetString()!);
    }
}
