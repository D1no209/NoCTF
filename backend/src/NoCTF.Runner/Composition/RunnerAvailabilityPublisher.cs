using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Provisioning;
using System.Text.Json;
using Npgsql;
using StackExchange.Redis;

namespace NoCTF.Runner.Composition;

public sealed class RunnerAvailabilityPublisher(
    IServiceScopeFactory scopeFactory,
    RedisRunnerAvailabilityRegistry registry,
    IOptions<RunnerOptions> configuredOptions,
    ILogger<RunnerAvailabilityPublisher> logger,
    TimeProvider timeProvider,
    RedisRunnerCapacityLedger ledger,
    RunnerResourceMutationCoordinator mutations,
    IEnumerable<IRuntimeManagedResourceReconciler> reconcilers,
    RunnerResourceObserver observer,
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
    private bool initialReconciliationComplete;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Min(options.Heartbeat.IntervalSeconds,
            options.Admission.SampleIntervalSeconds)), timeProvider);
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
                    else if (outcome == RunnerAvailabilityRegistrationOutcome.OfflineAdmissionBlocked)
                    {
                        logger.LogWarning("Runner {RunnerId} is alive but admission is blocked by resource observation or pressure.", options.Id);
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
        var admission = await observer.SampleAsync(cancellationToken);
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

        var registration = new RunnerAvailabilityRegistration(
                options.Pool,
                options.Id,
                options.Provider!.Value,
                Version,
                options.Heartbeat.Ttl,
                hasActiveAssignments,
                providerHealth?.IsReady(options.Provider.Value) ?? true,
                admission,
                options.Admission);
        if (!initialReconciliationComplete)
            await ledger.PauseAsync(options.Id, options.Pool, cancellationToken);
        var result = await registry.RegisterAsync(registration, cancellationToken);
        if (result == RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted
            && admission.Capacity is not null)
        {
            await ledger.PauseAsync(options.Id, options.Pool, cancellationToken);
            using var exclusive = await mutations.ReconcileAsync(cancellationToken);
            var reconciler = reconcilers.Single(x => x.Provider == options.Provider);
            // External inventory reads happen outside the database transaction.
            var managed = await reconciler.ListManagedAsync(cancellationToken);
            var managedRuntimeIds = managed.Select(resource => resource.RuntimeInstanceId).ToHashSet();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await RuntimeCapacityCriticalSection.AcquireAsync(db, cancellationToken);
            var ownerJson = JsonSerializer.Serialize(new { items = new[] { new { runnerId = options.Id } } });
            var runtimes = await db.RuntimeInstances.FromSqlInterpolated(
                    $"SELECT * FROM runtime_instances WHERE runner_id = {options.Id} OR capacity_allocations @> CAST({ownerJson} AS jsonb)")
                .Where(runtime => runtime.RuntimeProvider == options.Provider)
                .AsNoTracking().ToArrayAsync(cancellationToken);
            if (runtimes.Any(runtime => runtime.CapacityAllocations.Items.Count == 0
                    && (runtime.State is RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping
                        || runtime.State == RuntimeState.Failed && runtime.ProviderReceiptJson != null)))
                return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
            var known = runtimes.Where(runtime => runtime.CapacityAllocations.Items.Count > 0)
                .Select(runtime => runtime.Id).ToHashSet();
            if (managed.Any(resource => !known.Contains(resource.RuntimeInstanceId)))
                return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
            await transaction.CommitAsync(cancellationToken);
            var starting = runtimes.Where(runtime => runtime.State == RuntimeState.Provisioning)
                .SelectMany(runtime => runtime.CapacityAllocations.Items)
                .Select(item => item.Identity)
                .ToHashSet();
            foreach (var identity in runtimes.SelectMany(runtime => runtime.CapacityAllocations.Items)
                         .Select(item => item.Identity).Where(identity => identity.IsAuxiliary))
            {
                if (await reconciler.WorkloadExistsAsync(identity, cancellationToken) != true)
                    starting.Add(identity);
            }
            await using var restoreTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await RuntimeCapacityCriticalSection.AcquireAsync(db, cancellationToken);
            var recoveryRows = await db.RuntimeInstances.AsNoTracking().Where(runtime =>
                    runtimes.Select(item => item.Id).Contains(runtime.Id))
                .Select(runtime => new { runtime.Id, runtime.State, runtime.ProviderReceiptJson, runtime.CapacityAllocations })
                .ToArrayAsync(cancellationToken);
            var ownedRows = recoveryRows.Where(runtime => runtime.State == RuntimeState.Provisioning
                    || managedRuntimeIds.Contains(runtime.Id)
                        && (runtime.State is RuntimeState.Running or RuntimeState.Stopping
                            || runtime.State == RuntimeState.Failed && runtime.ProviderReceiptJson != null))
                .ToArray();
            var allocations = ownedRows.SelectMany(runtime => runtime.CapacityAllocations.Items)
                .Where(item => item.RunnerId == options.Id).ToArray();
            var claimKeys = allocations.Select(allocation => $"runner-claim:{allocation.Identity.Key}").ToArray();
            await ledger.RestoreAsync(options.Id, options.Pool, admission.Capacity,
                admission.Observation!.ObservedAt, allocations, cancellationToken, starting, claimKeys);
            await restoreTransaction.CommitAsync(cancellationToken);
            initialReconciliationComplete = true;
            result = await registry.RegisterAsync(registration, cancellationToken);
        }
        return result;
    }
}
