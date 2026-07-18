using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Storage;

namespace NoCTF.Infrastructure.Storage;

public sealed class S3ObjectStorage(
    IAmazonS3 client,
    IConfiguration configuration) : IObjectStorage
{
    private readonly string bucket = configuration["Storage:S3:Bucket"] ?? "noctf";

    public Task<FixUploadGrant> CreateUploadAsync(Guid uploadId, string objectKey, string contentType, long length, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(lifetime)
        };
        return Task.FromResult(new FixUploadGrant(uploadId, objectKey,
            new Uri(client.GetPreSignedURL(request)), DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public async Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        }, cancellationToken);
        var metadata = await InspectAsync(objectKey, cancellationToken)
            ?? throw new InvalidOperationException("Uploaded object could not be inspected.");
        return metadata with { FileName = fileName, ContentType = contentType };
    }

    public async Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucket,
                Key = objectKey
            }, cancellationToken);
            return new(objectKey, objectKey, response.Headers.ContentType ?? "application/octet-stream",
                response.Headers.ContentLength, response.Metadata["x-amz-meta-sha256"] ?? string.Empty);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        (await client.GetObjectAsync(bucket, objectKey, cancellationToken)).ResponseStream;

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(bucket, objectKey, cancellationToken);
}
