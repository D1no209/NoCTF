using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using ApplicationFixVerificationStatus = NoCTF.Application.Submissions.Processing.FixVerificationDecision;
using DomainFixVerificationStatus = NoCTF.Domain.Submissions.FixVerificationStatus;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixVerificationStore(NoCtfDbContext db) : IFixVerificationStore
{
    public async Task<FixVerificationContext?> BeginVerifyingAsync(
        Guid submissionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var record = await db.FixSubmissionRecords
            .Include(item => item.Submission)
            .SingleOrDefaultAsync(item => item.SubmissionId == submissionId, ct);
        if (record?.Submission is null)
            return null;
        if (record.ExpiresAt <= now)
        {
            if (FixVerificationStateMachine.CanTransition(record.VerificationStatus, DomainFixVerificationStatus.Expired))
            {
                record.VerificationStatus = DomainFixVerificationStatus.Expired;
                record.UpdatedAt = now;
                record.RowVersion++;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return null;
        }
        if (!FixVerificationStateMachine.CanTransition(record.VerificationStatus, DomainFixVerificationStatus.Verifying))
            return null;

        record.VerificationStatus = DomainFixVerificationStatus.Verifying;
        record.UpdatedAt = now;
        record.RowVersion++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new(
            submissionId,
            ToReceived(record),
            record.RowVersion);
    }

    public async Task<bool> CompleteAsync(
        Guid submissionId,
        long expectedRowVersion,
        FixVerificationResult result,
        string verifierVersion,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        var record = await db.FixSubmissionRecords.SingleOrDefaultAsync(
            item => item.SubmissionId == submissionId,
            ct);
        if (record is null
            || record.RowVersion != expectedRowVersion
            || record.VerificationStatus != DomainFixVerificationStatus.Verifying)
            return false;

        var target = result.Status switch
        {
            ApplicationFixVerificationStatus.Valid => DomainFixVerificationStatus.Valid,
            ApplicationFixVerificationStatus.TeamFailure => DomainFixVerificationStatus.TeamFailure,
            ApplicationFixVerificationStatus.PlatformFailed => DomainFixVerificationStatus.PlatformFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
        if (!FixVerificationStateMachine.CanTransition(record.VerificationStatus, target))
            return false;

        record.VerificationStatus = target;
        record.FailureCategory = result.ErrorCode;
        record.VerifiedAt = completedAt;
        record.VerifierVersion = verifierVersion;
        record.UpdatedAt = completedAt;
        record.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<int> ExpireAsync(DateTimeOffset now, CancellationToken ct)
    {
        var records = await db.FixSubmissionRecords
            .Where(item => item.ExpiresAt <= now
            && (item.VerificationStatus == DomainFixVerificationStatus.Created
                || item.VerificationStatus == DomainFixVerificationStatus.Claimed))
            .ToListAsync(ct);
        foreach (var record in records)
        {
            record.VerificationStatus = DomainFixVerificationStatus.Expired;
            record.UpdatedAt = now;
            record.RowVersion++;
        }
        await db.SaveChangesAsync(ct);
        return records.Count;
    }

    private static FixSubmissionReceived ToReceived(FixSubmissionRecord record)
    {
        var submission = record.Submission!;
        var metadata = string.IsNullOrWhiteSpace(record.ObjectMetadata)
            ? null
            : JsonSerializer.Deserialize<FixMetadata>(record.ObjectMetadata);
        var archive = new FixArchiveReference(
            record.ObjectKey,
            metadata?.FileName ?? "archive",
            metadata?.ContentType ?? "application/octet-stream",
            metadata?.Length ?? 0,
            metadata?.Sha256 ?? string.Empty);
        return new(
            submission.Id,
            submission.CompetitionId,
            submission.TeamId!.Value,
            submission.ChallengeId!.Value,
            submission.UserId!.Value,
            record.UploadId,
            submission.IdempotencyKey ?? string.Empty,
            archive,
            "redacted",
            submission.ReceivedAt);
    }

    private sealed record FixMetadata(string FileName, string ContentType, long Length, string Sha256);
}
