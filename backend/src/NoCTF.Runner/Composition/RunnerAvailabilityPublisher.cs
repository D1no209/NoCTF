using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Persistence;
using Npgsql;
using StackExchange.Redis;

namespace NoCTF.Runner.Composition;

public sealed class RunnerAvailabilityPublisher(
    IServiceScopeFactory scopeFactory,
    RedisRunnerAvailabilityRegistry registry,
    IOptions<RunnerOptions> configuredOptions,
    ILogger<RunnerAvailabilityPublisher> logger,
    TimeProvider timeProvider,
    RunnerProviderHealthState? providerHealth = null) : BackgroundService
{
    private static readonly string Version =
        typeof(RunnerProgramMarker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(RunnerProgramMarker).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private readonly RunnerOptions options = configuredOptions.Value;
    private RunnerAvailabilityRegistrationOutcome? lastOutcome;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Heartbeat.Interval, timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await PublishOnceAsync(stoppingToken);
                if (outcome != lastOutcome)
                {
                    if (outcome == RunnerAvailabilityRegistrationOutcome.Online)
                    {
                        logger.LogInformation(
                            "Runner {RunnerId} in pool {RunnerPool} is publishing availability.",
                            options.Id,
                            options.Pool);
                    }
                    else if (outcome == RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted)
                    {
                        logger.LogWarning(
                            "Runner {RunnerId} in pool {RunnerPool} remains offline because its Redis capacity is untrusted while assignments may still be active.",
                            options.Id,
                            options.Pool);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Runner {RunnerId} in pool {RunnerPool} is not accepting work because its Runtime provider recently rejected a resource operation.",
                            options.Id,
                            options.Pool);
                    }
                    lastOutcome = outcome;
                }
            }
            catch (RedisException exception)
            {
                logger.LogWarning(
                    exception,
                    "Runner {RunnerId} could not publish availability to Redis.",
                    options.Id);
            }
            catch (NpgsqlException exception)
            {
                logger.LogWarning(
                    exception,
                    "Runner {RunnerId} could not verify active assignments before publishing availability.",
                    options.Id);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    public async Task<RunnerAvailabilityRegistrationOutcome> PublishOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var hasActiveAssignments = await db.RuntimeInstances.AsNoTracking()
            .AnyAsync(
                instance => instance.RunnerId == options.Id
                    && instance.RuntimeProvider == options.Provider!.Value
                    && (instance.State == RuntimeState.Provisioning
                        || instance.State == RuntimeState.Running
                        || instance.State == RuntimeState.Stopping
                        || (instance.State == RuntimeState.Failed
                            && instance.ProviderReceiptJson != null)),
                cancellationToken);

        return await registry.RegisterAsync(
            new RunnerAvailabilityRegistration(
                options.Pool,
                options.Id,
                options.Provider!.Value,
                Version,
                options.ResourceCapacity,
                options.Heartbeat.Ttl,
                hasActiveAssignments,
                providerHealth?.IsReady(options.Provider.Value) ?? true),
            cancellationToken);
    }
}
