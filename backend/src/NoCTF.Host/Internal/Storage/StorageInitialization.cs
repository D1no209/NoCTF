using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace NoCTF.Hosting.Storage;

internal static class StorageInitialization
{
    internal static async Task InitializeAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var bucket = configuration["Storage:S3:Bucket"]
            ?? throw new InvalidOperationException("Storage:S3:Bucket is required.");
        var endpoint = configuration["Storage:S3:ServiceUrl"]
            ?? throw new InvalidOperationException("Storage:S3:ServiceUrl is required.");
        var accessKey = configuration["Storage:S3:AccessKey"]
            ?? throw new InvalidOperationException("S3 access key is required.");
        var secretKey = configuration["Storage:S3:SecretKey"]
            ?? throw new InvalidOperationException("S3 secret key is required.");
        using var client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", true),
            AuthenticationRegion = configuration["Storage:S3:Region"] ?? "us-east-1"
        });
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
            await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
    }
}
