using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Provisioning;
using System.Text.Json;
using System.Data.Common;
using NATS.Client.Core;

namespace NoCTF.Runner.Composition;

public sealed class RunnerAvailabilityPublisher(
    IDbContextFactory<NoCtfDbContext> contexts,
    NatsRunnerAvailabilityRegistry registry,
    IOptions<RunnerOptions> configuredOptions,
    ILogger<RunnerAvailabilityPublisher> logger,
    TimeProvider timeProvider,
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
                            "Runner {RunnerId} in pool {RunnerPool} remains offline until EF capacity assignments are reconciled.",
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
            catch (NatsException exception)
            {
                logger.LogWarning(
                    exception,
                    "Runner {RunnerId} could not publish availability to NATS.",
                    options.Id);
                lastOutcome = null;
            }
            catch (Exception exception) when (ContainsDatabaseFailure(exception))
            {
                logger.LogWarning(
                    "Runner {RunnerId} could not verify active assignments after {FailureType}; availability will expire until the next successful sample.",
                    options.Id, exception.GetType().Name);
                lastOutcome = null;
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    private static bool ContainsDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null;
            current = current.InnerException)
        {
            if (current is DbException)
                return true;
        }
        return false;
    }

    public async Task<RunnerAvailabilityRegistrationOutcome> PublishOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var admission = await observer.SampleAsync(cancellationToken);
        var hasActiveAssignments = await db.RuntimeInstances.AsNoTracking()
            .AnyAsync(
                instance => instance.RunnerId == options.Id
                    && instance.RuntimeProvider == options.Provider!.Value
                    && (instance.State == RuntimeState.Provisioning
                        || instance.State == RuntimeState.Running
                        || instance.State == RuntimeState.Stopping
                        || (instance.State == RuntimeState.Failed
                            && instance.ProviderReceipt != null)),
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
                options.Admission,
                initialReconciliationComplete,
                observer.ResourceDomainFencingToken);
        var result = await registry.RegisterAsync(registration, cancellationToken);
        if (result == RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted
            && admission.Capacity is not null)
        {
            using var exclusive = await mutations.ReconcileAsync(cancellationToken);
            var reconciler = reconcilers.Single(x => x.Provider == options.Provider);
            // External inventory reads happen outside the database transaction.
            var managed = await reconciler.ListManagedAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead,
                cancellationToken);
            var runtimes = await db.RuntimeInstances
                .Include(runtime => runtime.CapacityAllocationEntries)
                .Where(runtime => runtime.RuntimeProvider == options.Provider
                    && (runtime.RunnerId == options.Id
                        || runtime.CapacityAllocationEntries.Any(allocation =>
                            allocation.RunnerId == options.Id)))
                .AsNoTracking().AsSplitQuery().ToArrayAsync(cancellationToken);
            if (runtimes.Any(runtime => runtime.CapacityAllocations.Items.Count == 0
                    && (runtime.State is RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping
                        || runtime.State == RuntimeState.Failed && runtime.ProviderReceipt != null)))
                return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
            var known = runtimes.Where(runtime => runtime.CapacityAllocations.Items.Count > 0)
                .Select(runtime => runtime.Id).ToHashSet();
            if (managed.Any(resource => !known.Contains(resource.RuntimeInstanceId)))
                return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
            await transaction.CommitAsync(cancellationToken);
            initialReconciliationComplete = true;
            result = await registry.RegisterAsync(
                registration with { Reconciled = true }, cancellationToken);
        }
        return result;
    }
}
