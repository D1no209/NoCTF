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

public interface IFixUploadSessionStore
{
    Task<FixUploadGrant?> CreateAsync(CreateFixUploadCommand command, CancellationToken cancellationToken);
    Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(
        Guid uploadId,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> TryConsumeAsync(Guid uploadId, DateTimeOffset consumedAt, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid uploadId, CancellationToken cancellationToken);
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
