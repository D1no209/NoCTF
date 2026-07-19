using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using ApplicationFixVerificationStatus = NoCTF.Application.Submissions.Processing.FixVerificationDecision;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>Explicitly fails Fix verification until a configured Runner verifier is available.</summary>
public sealed class UnavailableFixSubmissionVerifier : IFixSubmissionVerifier
{
    public Task<FixVerificationResult> VerifyAsync(
        FixSubmissionReceived submission,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FixVerificationResult(
            ApplicationFixVerificationStatus.PlatformFailed,
            ScoringFailureCode.StorageUnavailable));
}
