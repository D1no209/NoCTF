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
    IOptions<RunnerAvailabilityOptions> configuredOptions,
    ILogger<RunnerAvailabilityPublisher> logger,
    RunnerProviderHealthState? providerHealth = null) : BackgroundService
{
    private static readonly string Version =
        typeof(RunnerProgramMarker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(RunnerProgramMarker).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private readonly RunnerAvailabilityOptions options = configuredOptions.Value;
    private RunnerAvailabilityRegistrationOutcome? lastOutcome;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                            options.RunnerId,
                            options.RunnerPool);
                    }
                    else if (outcome == RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted)
                    {
                        logger.LogWarning(
                            "Runner {RunnerId} in pool {RunnerPool} remains offline because its Redis capacity is untrusted while assignments may still be active.",
                            options.RunnerId,
                            options.RunnerPool);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Runner {RunnerId} in pool {RunnerPool} is not accepting work because its Runtime provider recently rejected a resource operation.",
                            options.RunnerId,
                            options.RunnerPool);
                    }
                    lastOutcome = outcome;
                }
            }
            catch (RedisException exception)
            {
                logger.LogWarning(
                    exception,
                    "Runner {RunnerId} could not publish availability to Redis.",
                    options.RunnerId);
            }
            catch (NpgsqlException exception)
            {
                logger.LogWarning(
                    exception,
                    "Runner {RunnerId} could not verify active assignments before publishing availability.",
                    options.RunnerId);
            }

            try
            {
                await Task.Delay(options.HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<RunnerAvailabilityRegistrationOutcome> PublishOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var hasActiveAssignments = await db.RuntimeInstances.AsNoTracking()
            .AnyAsync(
                instance => instance.RunnerPool == options.RunnerPool
                    && instance.RunnerId == options.RunnerId
                    && instance.RuntimeProvider == options.Provider!.Value
                    && (instance.State == RuntimeState.Provisioning
                        || instance.State == RuntimeState.Running
                        || instance.State == RuntimeState.Stopping
                        || (instance.State == RuntimeState.Failed
                            && instance.ProviderReceiptJson != null)),
                cancellationToken);

        return await registry.RegisterAsync(
            new RunnerAvailabilityRegistration(
                options.RunnerPool,
                options.RunnerId,
                options.Provider!.Value,
                Version,
                options.Capacity,
                options.HeartbeatTtl,
                hasActiveAssignments,
                providerHealth?.IsReady(options.Provider.Value) ?? true),
            cancellationToken);
    }
}
