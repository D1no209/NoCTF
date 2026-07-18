using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Safe baseline verifier until a runtime provider supplies script/checker execution.</summary>
public sealed class ArchiveOnlyFixSubmissionVerifier : IFixSubmissionVerifier
{
    public Task<FixVerificationResult> VerifyAsync(FixSubmissionReceived submission, CancellationToken cancellationToken) =>
        Task.FromResult(new FixVerificationResult(FixVerificationStatus.Valid));
}
