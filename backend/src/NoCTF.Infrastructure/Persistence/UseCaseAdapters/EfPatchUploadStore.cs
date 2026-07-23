using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfPatchUploadStore(NoCtfDbContext db) : IPatchUploadStore
{
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
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId,
            UploadedByUserId = scope.UserId,
            ObjectKey = objectKey,
            OriginalFileName = fileName,
            ContentType = contentType,
            ByteLength = byteLength,
            Sha256 = sha256,
            UploadedAt = uploadedAt
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
