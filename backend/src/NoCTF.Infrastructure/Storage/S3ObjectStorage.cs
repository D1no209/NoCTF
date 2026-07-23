using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Storage;
using System.Security.Cryptography;

namespace NoCTF.Infrastructure.Storage;

public sealed class S3ObjectStorage(
    IAmazonS3 client,
    IConfiguration configuration) : IObjectStorage
{
    private readonly string bucket = configuration["Storage:S3:Bucket"] ?? "noctf";

    public async Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        Stream upload = content;
        if (!content.CanSeek)
        {
            temporaryPath = Path.Combine(Path.GetTempPath(), $"noctf-upload-{Guid.NewGuid():N}");
            await using (var temporaryOutput = File.Create(temporaryPath))
                await content.CopyToAsync(temporaryOutput, cancellationToken);
            upload = File.OpenRead(temporaryPath);
        }
        try
        {
            var position = upload.Position;
            var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(upload, cancellationToken));
            upload.Position = position;
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = objectKey,
                InputStream = upload,
                ContentType = contentType,
                AutoCloseStream = false
            };
            request.Metadata["x-amz-meta-sha256"] = sha256;
            await client.PutObjectAsync(request, cancellationToken);
            return new(objectKey, fileName, contentType, upload.Length - position, sha256);
        }
        finally
        {
            if (!ReferenceEquals(upload, content))
                await upload.DisposeAsync();
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
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
