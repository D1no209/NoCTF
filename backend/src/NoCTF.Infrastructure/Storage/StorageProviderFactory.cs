using Microsoft.Extensions.Configuration;
using NoCTF.Infrastructure.Storage;
using NoCTF.PluginBase;

namespace NoCTF.Infrastructure;

public static class StorageProviderFactory
{
    public static bool UsesLocalStorage(IConfiguration configuration)
    {
        var providerType = configuration["StorageProvider:Type"] ?? "Local";
        return !providerType.Equals("s3", StringComparison.OrdinalIgnoreCase) &&
               !providerType.Equals("minio", StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateLocalUrlSigningKey(IConfiguration configuration)
    {
        if (!UsesLocalStorage(configuration))
        {
            RequireConfigured("StorageProvider:S3:Endpoint", configuration["StorageProvider:S3:Endpoint"]);
            RequireConfigured("StorageProvider:S3:Bucket", configuration["StorageProvider:S3:Bucket"]);
            SecretValueValidator.RequireSafe(
                "StorageProvider:S3:AccessKey",
                configuration["StorageProvider:S3:AccessKey"],
                8);
            SecretValueValidator.RequireSafe(
                "StorageProvider:S3:SecretKey",
                configuration["StorageProvider:S3:SecretKey"],
                16);
            return;
        }

        SecretValueValidator.RequireSafe(
            "StorageProvider:Local:UrlSigningKey",
            configuration["StorageProvider:Local:UrlSigningKey"],
            32);
    }

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

    private static void RequireConfigured(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} must be configured.");
    }
}
