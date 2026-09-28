using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RunnerAwdpFixRegistrationTests
{
    [Test]
    public async Task Standalone_runner_resolves_the_AWDP_fix_work_reader_dependencies()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] =
                    "Host=localhost;Database=noctf;Username=noctf;Password=test",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["RunnerScoring:CallbackBaseUrl"] = "https://api.internal",
                ["RunnerScoring:SigningKey"] = "runner-scoring-test-signing-key-32-bytes",
                ["Runner:Provider"] = "Docker",
                ["Runner:Pool"] = "runner-pool",
                ["Runner:Id"] = "runner-1",
                ["Runner:Heartbeat:IntervalSeconds"] = "5",
                ["Runner:Heartbeat:TtlSeconds"] = "15"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<NoCtfDbContext>(options =>
                options.UseInMemoryDatabase("runner-awdp-fix-registration"),
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<NoCtfDbContext>(options =>
            options.UseInMemoryDatabase("runner-awdp-fix-registration"));
        services.AddNoCtfRunner(configuration);
        services.AddNoCtfStandaloneRunnerPersistence(configuration);
        services.AddScoped(_ => Substitute.For<IPostCommitMessagePublisher>());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var fence = scope.ServiceProvider.GetRequiredService<IAwdpFixExecutionFence>();
        var reader = scope.ServiceProvider.GetRequiredService<IAwdpFixWorkReader>();

        await Assert.That(fence).IsTypeOf<AwdpFixExecutionFence>();
        await Assert.That(reader).IsTypeOf<AwdpFixWorkReader>();
    }
}
