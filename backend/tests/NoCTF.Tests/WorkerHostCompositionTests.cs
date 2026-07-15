using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Notifications;
using NoCTF.Application.Plugins;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;
using NoCTF.Worker;

namespace NoCTF.Tests;

public class WorkerHostCompositionTests
{
    [Theory]
    [InlineData("runner.internal")]
    [InlineData("ftp://runner.internal")]
    [InlineData("https://user:password@runner.internal")]
    public void WorkerComposition_RejectsUnsafeRunnerBaseUrl(string runnerBaseUrl)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=noctf;Username=noctf;Password=development-only-password",
            ["Runner:BaseUrl"] = runnerBaseUrl,
            ["StorageProvider:Type"] = "Local",
            ["StorageProvider:Local:BasePath"] = Path.Combine(Path.GetTempPath(), "noctf-worker-invalid-runner")
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddNoCtfWorkerServices());

        Assert.Contains("absolute HTTP(S)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerComposition_ProductionRequiresRunnerBoundary()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.internal;Database=noctf;Username=noctf;Password=production-database-password-12345",
            ["StorageProvider:Type"] = "Local",
            ["StorageProvider:Local:BasePath"] = Path.Combine(Path.GetTempPath(), "noctf-worker-production-composition"),
            ["StorageProvider:Local:UrlSigningKey"] = "production-local-url-signing-key-123456789"
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddNoCtfWorkerServices());

        Assert.Contains("Runner:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerComposition_ResolvesAllRegisteredJobHandlers()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=noctf;Username=noctf;Password=development-only-password",
            ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false,connectTimeout=50,syncTimeout=50",
            ["Runner:BaseUrl"] = "http://127.0.0.1:59999",
            ["StorageProvider:Type"] = "Local",
            ["StorageProvider:Local:BasePath"] = Path.Combine(Path.GetTempPath(), "noctf-worker-composition")
        });
        builder.AddNoCtfWorkerServices();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<ICompetitionJobHandler>().ToArray();

        Assert.Contains(handlers, handler => handler.JobKey == CtfScoreRebuildJobHandler.JobType);
        Assert.Contains(handlers, handler => handler.JobKey == AwdpBackgroundTaskTypes.PatchValidation);
        Assert.Empty(scope.ServiceProvider.GetServices<IChallengeSubmissionHandler>());

        var catalog = host.Services.GetRequiredService<PluginCatalog>();
        Assert.Equal(6, catalog.Plugins.Count);
        Assert.All(catalog.Plugins, plugin => Assert.True(plugin.IsHostAware));

        var hostedServiceNames = host.Services
            .GetServices<IHostedService>()
            .Select(service => service.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("AwdRoundEngine", hostedServiceNames);
        Assert.Contains("AwdpRoundEngine", hostedServiceNames);
        Assert.Contains("KohPollEngine", hostedServiceNames);
        Assert.IsType<RedisStreamHubNotifier>(
            host.Services.GetRequiredService<IHubNotifierService>());
    }

    [Fact]
    public void WorkerComposition_ProductionRequiresExplicitRedis()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.internal;Database=noctf;Username=noctf;Password=production-database-password-12345",
            ["Runner:BaseUrl"] = "https://runner.internal",
            ["Runner:ApiKey"] = "production-runner-api-key-123456",
            ["StorageProvider:Type"] = "Local",
            ["StorageProvider:Local:BasePath"] = Path.Combine(Path.GetTempPath(), "noctf-worker-production-redis"),
            ["StorageProvider:Local:UrlSigningKey"] = "production-local-url-signing-key-123456789"
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddNoCtfWorkerServices());

        Assert.Contains("ConnectionStrings:Redis", exception.Message, StringComparison.Ordinal);
    }
}
