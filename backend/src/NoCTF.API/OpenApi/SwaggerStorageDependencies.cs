using NoCTF.Application.Storage;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerObjectStorage : IObjectStorage
{
    public Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<StoredObject?>(null);
    public Task<StoredObject> PutAsync(
        string objectKey,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => Task.CompletedTask;
}
