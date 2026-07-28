using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Storage;

namespace NoCTF.Infrastructure.Storage;

internal static class StorageInfrastructure
{
    internal static IServiceCollection AddNoCtfStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.Equals(configuration["Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL = configuration["Storage:S3:ServiceUrl"],
                ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", true)
            }));
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        }

        return services;
    }
}
