using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Storage;

namespace NoCTF.Infrastructure.Storage;

internal enum StorageProvider
{
    Local,
    S3
}

internal static class StorageInfrastructure
{
    internal static IServiceCollection AddNoCtfStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var providerText = configuration["Storage:Provider"];
        var provider = string.IsNullOrWhiteSpace(providerText)
            ? StorageProvider.Local
            : Enum.TryParse<StorageProvider>(providerText, true, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Storage:Provider '{providerText}' is not supported.");
        if (provider == StorageProvider.S3)
        {
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL = configuration["Storage:S3:ServiceUrl"],
                ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", true)
            }));
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else if (provider == StorageProvider.Local)
        {
            services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        }

        services.AddScoped<IManagedFileUploadRegistry, ManagedFileUploadRegistry>();
        services.AddScoped<ManagedFileUploads>();
        services.AddScoped<FileReferenceLock>();
        services.AddScoped<IBusinessFileReferenceStore, BusinessFileReferenceStore>();
        services.AddScoped<ManageBusinessImages>();

        return services;
    }
}
