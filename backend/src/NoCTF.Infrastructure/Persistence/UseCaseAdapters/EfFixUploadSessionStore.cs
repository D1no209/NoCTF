using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixUploadSessionStore(NoCtfDbContext db, IObjectStorage storage) : IFixUploadSessionStore
{
    public async Task<FixUploadCreationResult> CreateAsync(
        CreateFixUploadCommand command,
        SubmissionAdmissionSnapshot expectedAdmission,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status != CompetitionStatus.Running)
            return FixUploadCreationResult.Rejected(FixUploadCreationFailure.AdmissionChanged);
        var currentAdmission = await SubmissionAdmissionPersistence.LoadAsync(
            db, command.CompetitionId, command.TeamId, command.CompetitionChallengeId, command.UserId,
            command.RequestedAt, ct);
        if (!SubmissionAdmissionPersistence.Matches(expectedAdmission, currentAdmission))
            return FixUploadCreationResult.Rejected(FixUploadCreationFailure.AdmissionChanged);

        var uploadId = Guid.CreateVersion7(command.RequestedAt);
        var expiresAt = command.RequestedAt.AddMinutes(15);
        var objectKey = $"fix/{command.CompetitionId:N}/{uploadId:N}";
        db.FixSubmissionRecords.Add(new FixSubmissionRecord
        {
            UploadId = uploadId, CompetitionId = command.CompetitionId, TeamId = command.TeamId,
            CompetitionChallengeId = command.CompetitionChallengeId,
            ObjectKey = objectKey, ExpiresAt = expiresAt, VerificationStatus = FixVerificationStatus.AuthorizationPending,
            ObjectMetadata = JsonSerializer.Serialize(new { command.FileName, command.ContentType, command.Length, command.Sha256, command.UserId }),
            CreatedAt = command.RequestedAt, UpdatedAt = command.RequestedAt
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        FixUploadGrant grant;
        try
        {
            grant = await storage.CreateUploadAsync(
                uploadId, objectKey, command.ContentType, command.Length, TimeSpan.FromMinutes(15), ct);
        }
        catch (Exception authorizationException)
        {
            try
            {
                await MarkCleanupPendingAsync(uploadId);
            }
            catch (Exception recoveryException)
            {
                throw new AggregateException(
                    "Fix upload authorization and recovery-state update both failed.",
                    authorizationException,
                    recoveryException);
            }

            throw;
        }

        var finalized = await db.FixSubmissionRecords
            .Where(record => record.UploadId == uploadId
                             && record.VerificationStatus == FixVerificationStatus.AuthorizationPending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(record => record.VerificationStatus, FixVerificationStatus.Created)
                .SetProperty(record => record.UpdatedAt, command.RequestedAt)
                .SetProperty(record => record.RowVersion, 1), ct);
        if (finalized != 1)
        {
            await storage.DeleteAsync(objectKey, CancellationToken.None);
            throw new InvalidOperationException("Fix upload authorization reservation could not be finalized.");
        }
        return FixUploadCreationResult.Created(grant);
    }

    public async Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(Guid uploadId, Guid competitionId, Guid teamId, Guid challengeId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.FixSubmissionRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UploadId == uploadId
            && x.CompetitionId == competitionId && x.TeamId == teamId && x.CompetitionChallengeId == challengeId
            && x.VerificationStatus == FixVerificationStatus.Created
            && x.SubmissionId == null && x.ExpiresAt > now, ct);
        if (item is null || string.IsNullOrWhiteSpace(item.ObjectMetadata)) return null;
        using var document = JsonDocument.Parse(item.ObjectMetadata);
        var root = document.RootElement;
        if (!root.TryGetProperty("UserId", out var owner) || owner.GetGuid() != userId) return null;
        return new(item.ObjectKey, root.GetProperty("FileName").GetString()!, root.GetProperty("ContentType").GetString()!,
            root.GetProperty("Length").GetInt64(), root.GetProperty("Sha256").GetString()!);
    }

    private Task MarkCleanupPendingAsync(Guid uploadId) =>
        db.FixSubmissionRecords
            .Where(record => record.UploadId == uploadId
                             && record.VerificationStatus == FixVerificationStatus.AuthorizationPending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(record => record.VerificationStatus, FixVerificationStatus.CleanupPending)
                .SetProperty(record => record.RowVersion, 1), CancellationToken.None);
}
