using NoCTF.Application.Storage;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerFixUploadStore : IFixUploadSessionStore
{
    public Task<FixUploadGrant?> CreateAsync(CreateFixUploadCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<FixUploadGrant?>(null);

    public Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(
        Guid uploadId,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) => Task.FromResult<FixUploadMetadata?>(null);

    public Task<bool> TryConsumeAsync(Guid uploadId, DateTimeOffset consumedAt, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    public Task ReleaseAsync(Guid uploadId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SwaggerObjectStorage : IObjectStorage
{
    public Task<FixUploadGrant> CreateUploadAsync(Guid uploadId, string objectKey, string contentType, long length, TimeSpan lifetime,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<StoredObject?>(null);
    public Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => Task.CompletedTask;
}
