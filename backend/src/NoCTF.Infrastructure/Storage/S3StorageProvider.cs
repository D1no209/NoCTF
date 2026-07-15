using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using NoCTF.PluginBase;

namespace NoCTF.Infrastructure.Storage;

public class S3StorageProvider(
    string endpoint,
    string bucketName,
    string accessKey,
    string secretKey,
    string? region = null,
    string? publicBaseUrl = null) : ITemporaryUrlStorageProvider, IDisposable
{
    private readonly AmazonS3Client _client = CreateClient(endpoint, accessKey, secretKey, region);
    private readonly AmazonS3Client? _publicClient = string.IsNullOrWhiteSpace(publicBaseUrl)
        ? null
        : CreateClient(publicBaseUrl, accessKey, secretKey, region);

    private static AmazonS3Client CreateClient(
        string serviceUrl,
        string clientAccessKey,
        string clientSecretKey,
        string? clientRegion) => new(
        clientAccessKey,
        clientSecretKey,
        new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            RegionEndpoint = clientRegion is null
                ? RegionEndpoint.USEast1
                : RegionEndpoint.GetBySystemName(clientRegion)
        });

    public async Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var key = StorageObjectKey.Normalize(fileName);
        var response = await _client.GetObjectAsync(bucketName, key, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var key = StorageObjectKey.Normalize(fileName);
        await _client.DeleteObjectAsync(bucketName, key, cancellationToken);
    }

    public async Task<string> GetUrlAsync(string fileName, CancellationToken cancellationToken = default)
        => await GetUrlAsync(fileName, TimeSpan.FromHours(1), cancellationToken);

    public async Task<string> GetUrlAsync(
        string fileName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        var key = StorageObjectKey.Normalize(fileName);
        var effectiveLifetime = lifetime <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(1)
            : lifetime > TimeSpan.FromDays(7)
                ? TimeSpan.FromDays(7)
                : lifetime;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(effectiveLifetime),
            Verb = HttpVerb.GET
        };
        return await (_publicClient ?? _client).GetPreSignedURLAsync(request);
    }

    public async Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var key = StorageObjectKey.Normalize(fileName);
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };
        await _client.PutObjectAsync(putRequest, cancellationToken);
        return key;
    }

    public void Dispose()
    {
        _publicClient?.Dispose();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
