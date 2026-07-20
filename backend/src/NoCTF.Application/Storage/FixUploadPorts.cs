using NoCTF.Application.Submissions.Intake;

namespace NoCTF.Application.Storage;

public sealed record CreateFixUploadCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    string FileName,
    string ContentType,
    long Length,
    string Sha256,
    DateTimeOffset RequestedAt);

public sealed record FixUploadGrant(
    Guid UploadId,
    string ObjectKey,
    Uri UploadUrl,
    DateTimeOffset ExpiresAt);

public sealed record FixUploadMetadata(
    string ObjectKey,
    string FileName,
    string ContentType,
    long Length,
    string Sha256);

public enum FixUploadCreationFailure
{
    AdmissionChanged
}

public sealed class FixUploadCreationResult
{
    private FixUploadCreationResult(FixUploadGrant? grant, FixUploadCreationFailure? failure)
    {
        Grant = grant;
        Failure = failure;
    }

    public FixUploadGrant? Grant { get; }
    public FixUploadCreationFailure? Failure { get; }

    public static FixUploadCreationResult Created(FixUploadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new(grant, null);
    }
    public static FixUploadCreationResult Rejected(FixUploadCreationFailure failure) => new(null, failure);
}

public interface IFixUploadSessionStore
{
    Task<FixUploadCreationResult> CreateAsync(
        CreateFixUploadCommand command,
        SubmissionAdmissionSnapshot expectedAdmission,
        CancellationToken cancellationToken);
    Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(
        Guid uploadId,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IObjectStorage
{
    Task<FixUploadGrant> CreateUploadAsync(
        Guid uploadId,
        string objectKey,
        string contentType,
        long length,
        TimeSpan lifetime,
        CancellationToken cancellationToken);
    Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken);
    Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record StoredObject(
    string ObjectKey,
    string FileName,
    string ContentType,
    long Length,
    string Sha256);
