using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using Npgsql;
using StackExchange.Redis;

namespace NoCTF.Runner.Composition;

public sealed class RunnerAvailabilityPublisher(
    IServiceScopeFactory scopeFactory,
    RedisRunnerAvailabilityRegistry registry,
    IOptions<RunnerAvailabilityOptions> configuredOptions,
    ILogger<RunnerAvailabilityPublisher> logger) : BackgroundService
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
                    else
                    {
                        logger.LogWarning(
                            "Runner {RunnerId} in pool {RunnerPool} remains offline because its Redis capacity is untrusted while assignments may still be active.",
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
                    && (instance.State == RuntimeState.Provisioning
                        || instance.State == RuntimeState.Running
                        || instance.State == RuntimeState.Stopping),
                cancellationToken);

        return await registry.RegisterAsync(
            new RunnerAvailabilityRegistration(
                options.RunnerPool,
                options.RunnerId,
                options.Provider!.Value,
                Version,
                options.Capacity,
                options.HeartbeatTtl,
                hasActiveAssignments),
            cancellationToken);
    }
}
