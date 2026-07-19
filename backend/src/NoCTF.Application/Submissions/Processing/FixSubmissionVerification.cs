using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Common;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public enum FixVerificationStatus
{
    Valid,
    TeamFailure,
    PlatformFailed
}

public sealed record FixVerificationResult(FixVerificationStatus Status, ScoringFailureCode? ErrorCode = null);

public sealed record FixVerificationContext(
    Guid SubmissionId,
    FixSubmissionReceived Submission,
    long RowVersion);

public interface IFixVerificationStore
{
    Task<FixVerificationContext?> BeginVerifyingAsync(
        Guid submissionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid submissionId,
        long expectedRowVersion,
        FixVerificationResult result,
        string verifierVersion,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<int> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Executes the mode-specific Fix script/runtime/checker behind an infrastructure port.</summary>
public interface IFixSubmissionVerifier
{
    Task<FixVerificationResult> VerifyAsync(
        FixSubmissionReceived submission,
        CancellationToken cancellationToken);
}

public sealed class VerifyFixSubmission(
    IFixVerificationStore store,
    IFixSubmissionVerifier verifier)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid submissionId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var context = await store.BeginVerifyingAsync(submissionId, now, ct);
        if (context is null)
            return OperationResult.Failure("fix_not_verifiable", "The Fix submission is not ready for verification.");

        FixVerificationResult result;
        try
        {
            result = await verifier.VerifyAsync(context.Submission, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = new(FixVerificationStatus.PlatformFailed, ScoringFailureCode.StorageUnavailable);
        }

        var completed = await store.CompleteAsync(
            submissionId,
            context.RowVersion,
            result,
            "fix-verifier-v1",
            now,
            ct);
        return completed
            ? OperationResult.Success()
            : OperationResult.Failure("fix_concurrency", "Fix verification state changed concurrently.");
    }
}
