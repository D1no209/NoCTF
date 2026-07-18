using System.Security.Cryptography;
using Amazon.S3;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

public sealed class FixArchiveValidator(IObjectStorage storage)
{
    public async Task<FixArchiveEvidence> ValidateAsync(
        FixArchiveReference expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var actual = await storage.InspectAsync(expected.ObjectKey, cancellationToken);
            if (actual is null)
                return new(ArchiveValidationStatus.Missing, SubmissionErrorCode.FixArchiveMissing);
            if (actual.Length != expected.Length)
                return new(ArchiveValidationStatus.LengthMismatch, SubmissionErrorCode.FixArchiveLengthMismatch);
            if (!string.Equals(actual.ContentType, expected.ContentType, StringComparison.OrdinalIgnoreCase))
                return new(ArchiveValidationStatus.ContentTypeMismatch, SubmissionErrorCode.FixArchiveContentTypeMismatch);

            var actualHash = actual.Sha256;
            if (string.IsNullOrWhiteSpace(actualHash))
            {
                await using var content = await storage.OpenReadAsync(expected.ObjectKey, cancellationToken);
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken));
            }
            return string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase)
                ? new(ArchiveValidationStatus.Valid)
                : new(ArchiveValidationStatus.HashMismatch, SubmissionErrorCode.FixArchiveHashMismatch);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ArchiveValidationStatus.PlatformFailed, SubmissionErrorCode.StorageTimeout);
        }
        catch (IOException)
        {
            return new(ArchiveValidationStatus.PlatformFailed, SubmissionErrorCode.StorageUnavailable);
        }
        catch (AmazonS3Exception)
        {
            return new(ArchiveValidationStatus.PlatformFailed, SubmissionErrorCode.StorageUnavailable);
        }
        catch (HttpRequestException)
        {
            return new(ArchiveValidationStatus.PlatformFailed, SubmissionErrorCode.StorageUnavailable);
        }
        catch (TimeoutException)
        {
            return new(ArchiveValidationStatus.PlatformFailed, SubmissionErrorCode.StorageTimeout);
        }
    }
}
