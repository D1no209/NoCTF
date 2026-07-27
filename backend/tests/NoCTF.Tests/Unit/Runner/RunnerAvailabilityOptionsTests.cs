using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RunnerAvailabilityOptionsTests
{
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

        var options = services.GetRequiredService<IOptions<RunnerAvailabilityOptions>>().Value;

        await Assert.That(options.RunnerId).IsEqualTo("docker-1");
        await Assert.That(options.RunnerPool).IsEqualTo("docker");
        await Assert.That(options.Provider).IsEqualTo(RuntimeProvider.Docker);
        await Assert.That(options.Capacity.MemoryBytes).IsEqualTo(4_294_967_296);
        await Assert.That(options.HeartbeatInterval).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(options.HeartbeatTtl).IsEqualTo(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Runner_availability_configuration_rejects_missing_capacity()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Runner:Provider"] = nameof(RuntimeProvider.Docker),
            ["Runner:Pool"] = "docker",
            ["Runner:Id"] = "docker-1",
            ["Runner:Heartbeat:IntervalSeconds"] = "5",
            ["Runner:Heartbeat:TtlSeconds"] = "15"
        });
        Func<RunnerAvailabilityOptions> read = () =>
            services.GetRequiredService<IOptions<RunnerAvailabilityOptions>>().Value;

        await Assert.That(read).Throws<OptionsValidationException>();
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
        Func<RunnerAvailabilityOptions> read = () =>
            services.GetRequiredService<IOptions<RunnerAvailabilityOptions>>().Value;

        await Assert.That(read).Throws<OptionsValidationException>();
    }

    private static ServiceProvider BuildServices(IReadOnlyDictionary<string, string?> values)
    {
        var configurationValues = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        configurationValues["ConnectionStrings:Redis"] = "localhost:6379";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNoCtfRunner(configuration);
        return services.BuildServiceProvider();
    }
}
