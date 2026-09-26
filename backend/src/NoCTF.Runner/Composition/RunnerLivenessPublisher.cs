using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Runner.Composition;

/// <summary>Keep cleanup routing alive while provider inventory or resource observation is blocked.</summary>
public sealed class RunnerLivenessPublisher(
    NatsRunnerAvailabilityRegistry registry, IOptions<RunnerOptions> options, TimeProvider clock,
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
                await registry.PublishHeartbeatAsync(runner.Pool, runner.Id,
                    runner.Provider!.Value, runner.Heartbeat.Ttl, stoppingToken);
            }
            catch (NatsException exception)
            {
                logger.LogWarning("Runner NATS liveness publication failed after {FailureType}.", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
