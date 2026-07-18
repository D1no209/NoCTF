namespace NoCTF.Application.Storage;

public sealed record StoredObject(
    string ObjectKey,
    string FileName,
    string ContentType,
    long Length,
    string Sha256);

public interface IObjectStorage
{
    Task<StoredObject> PutAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}
