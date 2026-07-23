namespace NoCTF.Application.Storage;

public interface IObjectStorage
{
    Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken);
    Task<StoredObject> PutAsync(
        string objectKey,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record StoredObject(
    string ObjectKey,
    string FileName,
    string ContentType,
    long Length,
    string Sha256);
