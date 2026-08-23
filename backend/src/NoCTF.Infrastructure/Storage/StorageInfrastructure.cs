using Amazon.S3;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Storage;

namespace NoCTF.Infrastructure.Storage;

internal enum StorageProvider
{
    Local,
    S3
}

internal sealed class StorageOptions
{
    internal const string SectionName = "Storage";

    public StorageProvider Provider { get; init; } = StorageProvider.Local;
    public string LocalRoot { get; init; } = "storage";
    public S3StorageOptions S3 { get; init; } = new();
}

internal sealed class S3StorageOptions
{
    public string? Bucket { get; init; }
    public string? Region { get; init; }
    public string? ServiceUrl { get; init; }
    public bool ForcePathStyle { get; init; } = true;
    public string? AccessKey { get; init; }
    public string? SecretKey { get; init; }
    public string? SessionToken { get; init; }
}

internal static class StorageInfrastructure
{
    internal static IServiceCollection AddNoCtfStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(StorageOptions.SectionName)
            .Get<StorageOptions>() ?? new StorageOptions();
        services.AddSingleton<IStore>(_ => CreateStore(options, configuration));

        services.AddScoped<IManagedFileUploadRegistry, ManagedFileUploadRegistry>();
        services.AddScoped<ManagedFileUploads>();
        services.AddScoped<FileReferenceLock>();
        services.AddScoped<IBusinessFileReferenceStore, BusinessFileReferenceStore>();
        services.AddScoped<ManageBusinessImages>();

        return services;
    }

    private static IStore CreateStore(
        StorageOptions options,
        IConfiguration configuration) =>
        options.Provider switch
        {
            StorageProvider.Local => CreateDiskStore(options.LocalRoot),
            StorageProvider.S3 => CreateS3Store(options.S3, configuration),
            _ => throw new InvalidOperationException(
                $"Storage:Provider '{options.Provider}' is not supported.")
        };

    private static IStore CreateDiskStore(string localRoot)
    {
        if (string.IsNullOrWhiteSpace(localRoot))
            throw new InvalidOperationException("Storage:LocalRoot is required for Local storage.");
        return StorageFactory.Disk(Path.GetFullPath(localRoot));
    }

    private static IStore CreateS3Store(
        S3StorageOptions options,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket))
            throw new InvalidOperationException("Storage:S3:Bucket is required for S3 storage.");

        var accessKey = options.AccessKey ?? configuration["AWS_ACCESS_KEY_ID"];
        var secretKey = options.SecretKey ?? configuration["AWS_SECRET_ACCESS_KEY"];
        var sessionToken = options.SessionToken ?? configuration["AWS_SESSION_TOKEN"];
        if (string.IsNullOrWhiteSpace(accessKey) != string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "S3 storage credentials require both an access key and a secret key.");
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            if (string.IsNullOrWhiteSpace(accessKey))
            {
                throw new InvalidOperationException(
                    "S3-compatible storage requires explicit access and secret keys.");
            }
            var region = string.IsNullOrWhiteSpace(options.Region)
                ? "us-east-1"
                : options.Region;
            return AwsS3Storage.FromThirdPartyCredentials(
                accessKey,
                secretKey!,
                sessionToken,
                options.Bucket,
                new AmazonS3Config
                {
                    ServiceURL = options.ServiceUrl,
                    ForcePathStyle = options.ForcePathStyle,
                    AuthenticationRegion = region
                });
        }

        if (string.IsNullOrWhiteSpace(options.Region))
            throw new InvalidOperationException("Storage:S3:Region is required for AWS S3 storage.");
        return string.IsNullOrWhiteSpace(accessKey)
            ? AwsS3Storage.FromRole(options.Bucket, options.Region)
            : AwsS3Storage.FromCredentials(
                accessKey,
                secretKey!,
                sessionToken,
                options.Bucket,
                options.Region);
    }
}
