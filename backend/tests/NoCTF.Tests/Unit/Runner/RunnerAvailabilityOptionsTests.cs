using FluentStorage.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Application.Storage;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RunnerOptionsTests
{
    [Test]
    public async Task Docker_runtime_log_limits_are_configurable()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15",
            ["Runtime:Docker:RuntimeLogMaxSizeBytes"] = "8388608",
            ["Runtime:Docker:RuntimeLogMaxFiles"] = "2",
            ["Runtime:Docker:OneShotOutputLimitBytesPerStream"] = "262144"
        });

        var options = services.GetRequiredService<DockerRuntimeOptions>();

        await Assert.That(options.RuntimeLogMaxSizeBytes).IsEqualTo(8_388_608);
        await Assert.That(options.RuntimeLogMaxFiles).IsEqualTo(2);
        await Assert.That(options.OneShotOutputLimitBytesPerStream).IsEqualTo(262_144);
    }

    [Test]
    public async Task Runner_availability_configuration_is_strongly_typed()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Capacity:MemoryBytes"] = "4294967296",
            ["Runner:Capacity:NanoCpus"] = "2000000000",
            ["Runner:Capacity:PidsLimit"] = "2048",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15"
        });

        var options = services.GetRequiredService<IOptions<RunnerOptions>>().Value;

        await Assert.That(options.Id).IsEqualTo("docker-1");
        await Assert.That(options.Pool).IsEqualTo("docker");
        await Assert.That(options.Provider).IsEqualTo(RuntimeProvider.Docker);
        await Assert.That(options.Heartbeat.Interval).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(options.Heartbeat.Ttl).IsEqualTo(TimeSpan.FromSeconds(15));
        var cleanup = options.Cleanup.ToPolicy();
        await Assert.That(cleanup.GracefulStopTimeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(cleanup.ForceDeleteTimeout).IsEqualTo(TimeSpan.FromSeconds(8));
        await Assert.That(cleanup.NetworkCleanupTimeout).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(cleanup.VerificationTimeout).IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task Runner_cleanup_configuration_requires_positive_stage_budgets()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Capacity:MemoryBytes"] = "1024",
            ["Runner:Capacity:NanoCpus"] = "100",
            ["Runner:Capacity:PidsLimit"] = "10",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15",
            ["Runner:Cleanup:VerificationTimeoutSeconds"] = "0"
        });
        Func<RunnerOptions> read = () =>
            services.GetRequiredService<IOptions<RunnerOptions>>().Value;

        await Assert.That(read).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Runner_availability_configuration_uses_observation_without_manual_capacity()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15"
        });
        var options = services.GetRequiredService<IOptions<RunnerOptions>>().Value;

        await Assert.That(options.Id).IsEqualTo("docker-1");
        await Assert.That(options.Capacity.MemoryBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Runner_availability_configuration_requires_ttl_greater_than_interval()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Capacity:MemoryBytes"] = "1024",
            ["Runner:Capacity:NanoCpus"] = "100",
            ["Runner:Capacity:PidsLimit"] = "10",
            ["Runner:Heartbeat:IntervalSeconds"] = "15",
            ["Runner:Heartbeat:TtlSeconds"] = "15"
        });
        Func<RunnerOptions> read = () =>
            services.GetRequiredService<IOptions<RunnerOptions>>().Value;

        await Assert.That(read).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Runner_configuration_rejects_an_unknown_provider()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = "mystery",
            ["Runner:Pool"] = "pool-a",
            ["Runner:Id"] = "runner-a",
            ["Runner:Capacity:MemoryBytes"] = "1024",
            ["Runner:Capacity:NanoCpus"] = "100",
            ["Runner:Capacity:PidsLimit"] = "10",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15"
        });
        Func<RunnerOptions> read = () =>
            services.GetRequiredService<IOptions<RunnerOptions>>().Value;

        await Assert.That(read).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Runner_requires_an_explicit_internal_api_base_url()
    {
        Func<ServiceProvider> build = () => BuildServices(
            new Dictionary<string, string?>(),
            configureCallbackBaseUrl: false);

        await Assert.That(build).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Runner_rejects_a_relative_internal_api_base_url()
    {
        Func<ServiceProvider> build = () => BuildServices(new Dictionary<string, string?>
        {
            ["RunnerScoring:CallbackBaseUrl"] = "backend:8080"
        });

        await Assert.That(build).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Runner_does_not_receive_the_platform_file_store()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Capacity:MemoryBytes"] = "1024",
            ["Runner:Capacity:NanoCpus"] = "100",
            ["Runner:Capacity:PidsLimit"] = "10",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15",
            ["Storage:Provider"] = "S3",
            ["Storage:S3:ServiceUrl"] = "http://minio:9000",
            ["Storage:S3:ForcePathStyle"] = "true",
            ["Storage:S3:Bucket"] = "noctf"
        });

        var storage = services.GetService<IStore>();

        await Assert.That(storage).IsNull();
    }

    [Test]
    public async Task Standalone_runner_registers_its_local_configuration_cache()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Capacity:MemoryBytes"] = "1024",
            ["Runner:Capacity:NanoCpus"] = "100",
            ["Runner:Capacity:PidsLimit"] = "10",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15"
        });

        var caches = services.GetRequiredService<IFusionCacheProvider>();

        await Assert.That(caches.GetCache(NoCtfCacheNames.LocalComputation)).IsNotNull();
        await Assert.That(services
                .GetRequiredService<IAwdFlagInjectionConfigurationCatalog>())
            .IsTypeOf<FusionAwdFlagInjectionConfigurationCatalog>();
    }

    private static ServiceProvider BuildServices(
        IReadOnlyDictionary<string, string?> values,
        bool configureCallbackBaseUrl = true)
    {
        var configurationValues = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        configurationValues["ConnectionStrings:Redis"] = "localhost:6379";
        if (configureCallbackBaseUrl
            && !configurationValues.ContainsKey("RunnerScoring:CallbackBaseUrl"))
            configurationValues["RunnerScoring:CallbackBaseUrl"] = "https://api.internal";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddNoCtfRunner(configuration);
        return services.BuildServiceProvider();
    }
}
