using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Common;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public enum FixVerificationDecision
{
    Valid,
    TeamFailure,
    PlatformFailed
}

public sealed record FixVerificationResult(FixVerificationDecision Status, ScoringFailureCode? ErrorCode = null);

public enum FixVerificationError
{
    None,
    NotVerifiable,
    Concurrency
}

public sealed record FixVerificationOperationResult(bool Succeeded, FixVerificationError Error = FixVerificationError.None)
{
    public static FixVerificationOperationResult Success() => new(true);
    public static FixVerificationOperationResult Failure(FixVerificationError error) => new(false, error);
}

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
    public async Task<FixVerificationOperationResult> ExecuteAsync(
        Guid submissionId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var context = await store.BeginVerifyingAsync(submissionId, now, ct);
        if (context is null)
            return FixVerificationOperationResult.Failure(FixVerificationError.NotVerifiable);

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
            result = new(FixVerificationDecision.PlatformFailed, ScoringFailureCode.StorageUnavailable);
        }

        var completed = await store.CompleteAsync(
            submissionId,
            context.RowVersion,
            result,
            "fix-verifier-v1",
            now,
            ct);
        return completed
            ? FixVerificationOperationResult.Success()
            : FixVerificationOperationResult.Failure(FixVerificationError.Concurrency);
    }
}
