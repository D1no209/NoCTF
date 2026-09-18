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
    RunnerProviderHealthState? providerHealth = null,
    RedisRunnerCapacityLedger? ledger = null,
    RunnerResourceMutationCoordinator? mutations = null,
    IEnumerable<IRuntimeManagedResourceReconciler>? reconcilers = null,
    RunnerResourceObserver? observer = null) : BackgroundService
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
        var admission = observer is null ? null : await observer.SampleAsync(cancellationToken);
        var effectiveCapacity = options.ResourceCapacity;
        if (admission?.Observation is { } observed)
            effectiveCapacity = effectiveCapacity with
            {
                MemoryBytes = Math.Min(effectiveCapacity.MemoryBytes, observed.MemoryTotalBytes),
                NanoCpus = Math.Min(effectiveCapacity.NanoCpus, observed.NanoCpus),
                PidsLimit = Math.Min(effectiveCapacity.PidsLimit, observed.PidsCapacity)
            };
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
                effectiveCapacity,
                options.Heartbeat.Ttl,
                hasActiveAssignments,
                providerHealth?.IsReady(options.Provider.Value) ?? true,
                admission, observer is null ? null : options.Admission);
        if (!initialReconciliationComplete && ledger is not null)
            await ledger.PauseAsync(options.Id, options.Pool, cancellationToken);
        var result = await registry.RegisterAsync(registration, cancellationToken);
        if (result == RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted
            && ledger is not null && mutations is not null && reconcilers is not null)
        {
            await ledger.PauseAsync(options.Id, options.Pool, cancellationToken);
            using var exclusive = await mutations.ReconcileAsync(cancellationToken);
            var reconciler = reconcilers.Single(x => x.Provider == options.Provider);
            // External inventory reads happen outside the database transaction.
            var managed = await reconciler.ListManagedAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await RuntimeCapacityCriticalSection.AcquireAsync(db, cancellationToken);
            var ownerJson = JsonSerializer.Serialize(new { items = new[] { new { runnerId = options.Id } } });
            var runtimes = await db.RuntimeInstances.FromSqlInterpolated(
                    $"SELECT * FROM runtime_instances WHERE runner_id = {options.Id} OR capacity_allocations @> CAST({ownerJson} AS jsonb)")
                .Where(runtime => runtime.RuntimeProvider == options.Provider)
                .AsNoTracking().ToArrayAsync(cancellationToken);
            foreach (var runtime in runtimes.Where(runtime => runtime.CapacityAllocations.Items.Count == 0
                         && (runtime.State is RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping
                             || runtime.State == RuntimeState.Failed && runtime.ProviderReceiptJson != null)))
            {
                var legacy = await ledger.ReadLegacyAsync(runtime, options.Id, cancellationToken);
                if (legacy is null)
                    return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
                runtime.CapacityAllocations = runtime.CapacityAllocations.Add(legacy);
                var document = runtime.CapacityAllocations;
                await db.RuntimeInstances.Where(row => row.Id == runtime.Id).ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.CapacityAllocations, document), cancellationToken);
            }
            var known = runtimes.Where(runtime => runtime.CapacityAllocations.Items.Count > 0)
                .Select(runtime => runtime.Id).ToHashSet();
            if (managed.Any(resource => !known.Contains(resource.RuntimeInstanceId)))
                return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
            // Persist legacy evidence before replacing its Redis keys. A failed commit
            // must leave the old claim intact for the next recovery attempt.
            await transaction.CommitAsync(cancellationToken);
            await using var restoreTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await RuntimeCapacityCriticalSection.AcquireAsync(db, cancellationToken);
            var documents = await db.RuntimeInstances.AsNoTracking().Where(runtime =>
                    runtimes.Select(item => item.Id).Contains(runtime.Id))
                .Select(runtime => runtime.CapacityAllocations).ToArrayAsync(cancellationToken);
            await ledger.RestoreAsync(options.Id, options.Pool, effectiveCapacity,
                documents.SelectMany(document => document.Items)
                    .Where(item => item.RunnerId == options.Id).ToArray(), cancellationToken,
                runtimes.Where(runtime => runtime.State == RuntimeState.Provisioning)
                    .SelectMany(runtime => runtime.CapacityAllocations.Items).Select(item => item.Identity).ToHashSet());
            await restoreTransaction.CommitAsync(cancellationToken);
            initialReconciliationComplete = true;
            result = await registry.RegisterAsync(registration, cancellationToken);
        }
        return result;
    }
}
