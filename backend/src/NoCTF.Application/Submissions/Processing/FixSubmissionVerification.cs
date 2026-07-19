using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public enum FixVerificationStatus
{
    Valid,
    TeamFailure,
    PlatformFailed
}

public sealed record FixVerificationResult(FixVerificationStatus Status, ScoringFailureCode? ErrorCode = null);

/// <summary>Executes the mode-specific Fix script/runtime/checker behind an infrastructure port.</summary>
public interface IFixSubmissionVerifier
{
    Task<FixVerificationResult> VerifyAsync(
        FixSubmissionReceived submission,
        CancellationToken cancellationToken);
}
