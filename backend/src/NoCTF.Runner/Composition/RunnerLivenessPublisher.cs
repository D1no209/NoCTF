using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace NoCTF.Runner.Composition;

/// <summary>Keep cleanup routing alive while provider inventory or resource observation is blocked.</summary>
public sealed class RunnerLivenessPublisher(
    IConnectionMultiplexer redis, IOptions<RunnerOptions> options, TimeProvider clock,
    ILogger<RunnerLivenessPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Heartbeat.Interval, clock);
        do
        {
            try
            {
                var runner = options.Value;
                await redis.GetDatabase().StringSetAsync($"runner:{runner.Id}:heartbeat",
                    $"provider={runner.Provider};version=capacity-v2", runner.Heartbeat.Ttl);
            }
            catch (RedisException exception)
            {
                logger.LogWarning("Runner liveness publication failed after {FailureType}.", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
