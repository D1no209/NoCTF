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
    string? publicBaseUrl = null) : IStorageProvider
{
    private readonly AmazonS3Client _client = new(
        accessKey,
        secretKey,
        new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            RegionEndpoint = region is null ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(region)
        });

    public async Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(bucketName, fileName, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        await _client.DeleteObjectAsync(bucketName, fileName, cancellationToken);
    }

    public async Task<string> GetUrlAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = fileName,
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
            return await _client.GetPreSignedURLAsync(request);

        using var publicClient = new AmazonS3Client(
            accessKey,
            secretKey,
            new AmazonS3Config
            {
                ServiceURL = publicBaseUrl,
                ForcePathStyle = true,
                RegionEndpoint = region is null ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(region)
            });
        return await publicClient.GetPreSignedURLAsync(request);
    }

    public async Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = fileName,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };
        await _client.PutObjectAsync(putRequest, cancellationToken);
        return fileName;
    }
}
