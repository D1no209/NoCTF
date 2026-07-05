using Microsoft.Extensions.Configuration;
using NoCTF.Infrastructure.Storage;
using NoCTF.PluginBase;

namespace NoCTF.Infrastructure;

public static class StorageProviderFactory
{
    public static IStorageProvider Create(IConfiguration configuration)
    {
        var providerType = configuration["StorageProvider:Type"] ?? "Local";

        if (providerType.Equals("s3", StringComparison.OrdinalIgnoreCase) ||
            providerType.Equals("minio", StringComparison.OrdinalIgnoreCase))
        {
            return new S3StorageProvider(
                configuration["StorageProvider:S3:Endpoint"]!,
                configuration["StorageProvider:S3:Bucket"]!,
                configuration["StorageProvider:S3:AccessKey"]!,
                configuration["StorageProvider:S3:SecretKey"]!,
                configuration["StorageProvider:S3:Region"],
                configuration["StorageProvider:PublicBaseUrl"]);
        }

        return new LocalFileStorageProvider(
            configuration["StorageProvider:Local:BasePath"] ?? "uploads",
            configuration["StorageProvider:Local:UrlSigningKey"] ?? configuration["JwtSettings:Secret"]);
    }
}
