using Microsoft.Extensions.Configuration;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;

namespace NoCTF.Infrastructure.Storage;

/// <summary>
/// Validates the immutable archive facts before allowing the privileged Runner
/// verifier to inspect or execute any user-controlled content.
/// </summary>
public sealed class ValidatingFixSubmissionVerifier(
    IObjectStorage storage,
    UnavailableFixSubmissionVerifier runnerVerifier,
    IConfiguration configuration) : IFixSubmissionVerifier
{
    private readonly TimeSpan inspectionTimeout = TimeSpan.FromSeconds(Math.Max(
        1,
        configuration.GetValue("FixVerification:ArchiveInspectionTimeoutSeconds", 30)));

    public async Task<FixVerificationResult> VerifyAsync(
        FixSubmissionReceived submission,
        CancellationToken cancellationToken)
    {
        var archive = submission.Archive;
        if (!IsSafeObjectKey(archive.ObjectKey))
            return TeamFailure(ScoringFailureCode.FixArchiveMissing);

        using var timeout = new CancellationTokenSource(inspectionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        StoredObject? stored;
        try
        {
            stored = await storage.InspectAsync(archive.ObjectKey, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return PlatformFailure(ScoringFailureCode.StorageTimeout);
        }
        catch
        {
            return PlatformFailure(ScoringFailureCode.StorageUnavailable);
        }

        if (stored is null)
            return TeamFailure(ScoringFailureCode.FixArchiveMissing);
        if (stored.Length != archive.Length)
            return TeamFailure(ScoringFailureCode.FixArchiveLengthMismatch);
        if (!string.Equals(stored.ContentType, archive.ContentType, StringComparison.OrdinalIgnoreCase))
            return TeamFailure(ScoringFailureCode.FixArchiveContentTypeMismatch);
        if (!string.Equals(stored.Sha256, archive.Sha256, StringComparison.OrdinalIgnoreCase))
            return TeamFailure(ScoringFailureCode.FixArchiveHashMismatch);

        return await runnerVerifier.VerifyAsync(submission, cancellationToken);
    }

    private static bool IsSafeObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)
            || objectKey.Length > 1024
            || objectKey[0] is '/' or '\\'
            || objectKey.Contains('\\'))
            return false;

        return objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }

    private static FixVerificationResult TeamFailure(ScoringFailureCode code) =>
        new(FixVerificationDecision.TeamFailure, code);

    private static FixVerificationResult PlatformFailure(ScoringFailureCode code) =>
        new(FixVerificationDecision.PlatformFailed, code);
}
