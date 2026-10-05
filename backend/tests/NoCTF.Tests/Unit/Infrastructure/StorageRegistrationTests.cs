using FluentStorage.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure;
using NoCTF.Persistence.Sqlite;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class StorageRegistrationTests
{
    [Test]
    public async Task Local_provider_resolves_a_singleton_disk_store()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"noctf-fluent-storage-registration-{Guid.NewGuid():N}");
        try
        {
            using var services = BuildServices(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "Local",
                ["Storage:LocalRoot"] = root
            });

            var first = services.GetRequiredService<IStore>();
            var second = services.GetRequiredService<IStore>();
            await first.SetText("health/storage.txt", "ready");

            await Assert.That(ReferenceEquals(second, first)).IsTrue();
            await Assert.That(await second.GetText("health/storage.txt")).IsEqualTo("ready");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task S3_compatible_provider_requires_explicit_credentials()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:ServiceUrl"] = "http://minio:9000",
            ["Storage:S3:Bucket"] = "noctf"
        });

        Func<IStore> resolve = services.GetRequiredService<IStore>;

        await Assert.That(resolve).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task S3_compatible_provider_resolves_with_explicit_credentials()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:ServiceUrl"] = "http://minio:9000",
            ["Storage:S3:Region"] = "us-east-1",
            ["Storage:S3:Bucket"] = "noctf",
            ["Storage:S3:AccessKey"] = "minio",
            ["Storage:S3:SecretKey"] = "minio-secret",
            ["Storage:S3:ForcePathStyle"] = "true"
        });

        var storage = services.GetRequiredService<IStore>();

        await Assert.That(storage).IsNotNull();
    }

    private static ServiceProvider BuildServices(
        IReadOnlyDictionary<string, string?> values)
    {
        var configurationValues = new Dictionary<string, string?>(values)
        {
            ["OpenApi:Exporting"] = "true"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNoCtfSqliteDevelopmentDatabase(
            "storage-registration-" + Guid.NewGuid().ToString("N"));
        services.AddNoCtfInfrastructure(configuration, development: true);
        return services.BuildServiceProvider();
    }
}
