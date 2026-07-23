using NoCTF.Application.Runtime.Ports;
using NoCTF.Infrastructure.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RedisRunnerCapacityGateTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Claim_requires_live_runner_and_release_restores_capacity(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-1";
            await database.SetAddAsync($"runner-pool:{pool}:members", runner);
            await database.StringSetAsync($"runner:{runner}:heartbeat", "alive", TimeSpan.FromMinutes(1));
            await database.HashSetAsync($"runner:{runner}:capacity", new HashEntry[]
            {
                new("availableMemoryBytes", 1024),
                new("availableNanoCpus", 100),
                new("availablePids", 10)
            });

            var gate = new RedisRunnerCapacityGate(redis);
            var runtimeId = Guid.CreateVersion7();
            var request = new RunnerCapacityRequest(runtimeId, pool, 512, 40, 2);
            var claim = await gate.TryClaimAsync(request, cancellationToken);

            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await Assert.That(claim.RunnerId).IsEqualTo(runner);
            await gate.ReleaseAsync(runtimeId, runner, cancellationToken);
            var remaining = await database.HashGetAsync($"runner:{runner}:capacity", "availableMemoryBytes");
            await Assert.That((long)remaining!).IsEqualTo(1024);
        });
    }
}
