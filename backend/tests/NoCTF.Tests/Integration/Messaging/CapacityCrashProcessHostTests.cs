using System.Data.Common;
using System.Text.Json;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Hosting;
using NoCTF.Hosting.Messaging;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.Runtime.Docker;
using NoCTF.Worker;
using Npgsql;
using NSubstitute;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Nats;
using Wolverine.Postgresql;
using Wolverine.Runtime.Agents;

namespace NoCTF.Tests.Integration.Messaging;

public enum CapacityCrashWindow
{
    None, RollbackThenCancel, BeforeAllocationWrite, AfterAllocationCommit, AfterProviderCreate, AfterReleaseCommit
}

/// <summary>Child process for the real-worker/runner crash fixture. Never enabled by an ordinary test run.</summary>
[Category("Integration")]
public sealed class CapacityCrashProcessHostTests
{
    public const string ControlSubject = "noctf.capacity-fixture.control";
    public const string ControlStream = "noctf_capacity_fixture";
    public const string RunnerId = "capacity-fault-runner";

    [Test, Timeout(900_000)]
    public async Task Run(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("NOCTF_CAPACITY_CHILD") != "true")
        {
            Skip.Test("Started only as the isolated capacity crash fixture's Linux child.");
            return;
        }
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var runtimeId = Guid.Parse(configuration["CapacityFixture:RuntimeId"]!);
        var checkpoint = new Checkpoint(configuration, runtimeId);
        using var host = Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder => builder.AddConfiguration(configuration))
            .ConfigureLogging(logging => logging.AddProvider(new FixtureFileLogger()))
            .ConfigureServices(services =>
            {
                services.AddNoCtfStandaloneRunnerPersistence(configuration);
                services.AddNoCtfRunner(configuration, development: false);
                services.AddFusionCache("read-models");
                services.AddDbContext<NoCtfDbContext>(builder => builder.AddInterceptors(
                    new AllocationWriteCheckpoint(checkpoint), new AllocationCommitCheckpoint(checkpoint)));
                services.AddSingleton<IRuntimePlacementPolicy>(new FixedRuntimePlacementPolicy(runnerPool: "fault-tests"));
                services.AddSingleton<IChallengeRuntimeTemplateCatalog, ChallengeRuntimeTemplateCatalog>();
                // These game modes are not used by the template-runtime fixture.
                services.AddSingleton(Substitute.For<IAwdRuntimeProvisioner>());
                services.AddSingleton(Substitute.For<IKohRuntimeProvisioner>());
                services.AddScoped<RuntimeDispatchMessageHandler>();
                services.AddScoped<QueuedRuntimeDispatchHandler>();
                services.AddScoped<ReleaseRunnerCapacityHandler>();
                services.AddSingleton<RuntimeDispatchWakeupGate>();
                services.AddScoped<ICompetitionStartGateStore, CompetitionStartGateStore>();
                services.AddSingleton<ICompetitionConfigurationValidator, GameModeCompetitionConfigurationValidator>();
                services.AddSingleton<IChallengeConfigurationCatalog, GameModeChallengeConfigurationCatalog>();
                services.AddScoped<CompetitionStartGate>();
                services.AddScoped<ICompetitionLifecycleStore, CompetitionLifecycleStore>();
                services.AddScoped<AdvanceCompetitionLifecycleUseCase>();
                services.AddScoped<CompetitionLifecycleMessageHandler>();
                services.AddSingleton<ClusterSchedulingState>();
                services.AddSingleton(new ClusterSchedulerNodeIdentity(Guid.NewGuid().ToString("N")));
                services.AddSingleton<IClusterSchedulerStatusStore, RedisClusterSchedulerStatusStore>();
                services.AddScoped<IClusterScheduleSource, PostgresClusterScheduleSource>();
                services.AddSingleton<IAwdRoundConfigurationCatalog, AwdRoundConfigurationCatalog>();
                services.AddSingleton<IKohProducerConfigurationCatalog, KohProducerConfigurationCatalog>();
                services.AddSingleton<LeaderboardProjectionMergeQueue>();
                services.AddSingularAgent<MaintenanceTickAgent>();
                services.RemoveAll<IRuntimeManagedResourceReconciler>();
                services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
                    new ScopedInventory(provider.GetRequiredService<DockerRuntimeResourceReconciler>(), runtimeId));
                services.Replace(ServiceDescriptor.Singleton<IRuntimeProviderCatalog>(provider =>
                    new CheckpointCatalog(provider.GetRequiredService<RuntimeProviderCatalog>(), checkpoint)));
            }).UseWolverine(options =>
            {
                options.ServiceName = "capacity-fault-host";
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(RuntimeDispatchMessageHandler));
                options.Discovery.IncludeType(typeof(QueuedRuntimeDispatchHandler));
                options.Discovery.IncludeType(typeof(ReleaseRunnerCapacityHandler));
                options.Discovery.IncludeType(typeof(CompetitionLifecycleMessageHandler));
                options.Discovery.IncludeType(typeof(ContainerRuntimeMessageHandler));
                options.Discovery.IncludeType(typeof(RuntimeResourceReconciliationHandler));
                options.Discovery.IncludeType(typeof(RuntimeProvisionWriteBackMessageHandler));
                options.Discovery.IncludeType(typeof(RuntimeStopWriteBackMessageHandler));
                options.Discovery.IncludeType(typeof(UnrelatedScheduleSink));
                options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
                options.Durability.CheckAssignmentPeriod = TimeSpan.FromSeconds(1);
                options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
                options.PersistMessagesWithPostgresql(configuration.GetConnectionString("PostgreSql")!, "capacity_fault_host");
                options.UseEntityFrameworkCoreTransactions();
                options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                options.UseNats(configuration.GetConnectionString("Nats")!).AutoProvision().UseJetStream(_ => { })
                    .DefineWorkQueueStream(ControlStream, stream => stream.WithSubject(ControlSubject), ControlSubject)
                    .DefineWorkQueueStream(NatsSubjects.RunnerStream, stream => stream.WithSubject("noctf.runner.>"), "noctf.runner.>");
                options.ListenToNatsSubject(ControlSubject).UseJetStream(ControlStream, "capacity-control").UseDurableInbox();
                options.ListenToNatsSubject(NatsSubjects.Runner(RunnerNodeQueueName.FromRunnerId(RunnerId).Value))
                    .UseJetStream(NatsSubjects.RunnerStream, "capacity-runner").UseDurableInbox();
                options.Policies.Add(new DurableRunnerCommandPolicy());
                Route<DispatchRuntime>(options); Route<DispatchQueuedRuntimes>(options); Route<StopRuntime>(options);
                Route<ReconcileRunnerAssignments>(options); Route<AdvanceCompetitionLifecycle>(options);
                Route<ReleaseRunnerCapacity>(options); Route<RuntimeProvisioned>(options); Route<RuntimeProvisionFailed>(options);
                Route<RuntimeProvisionCanceled>(options); Route<RuntimeProvisionTerminated>(options);
                Route<RuntimeStopped>(options); Route<RuntimeStopFailed>(options);
                Route<CompetitionEventCommitted>(options); Route<DispatchAwdCheckers>(options); Route<ExpireAccountSourceAddresses>(options);
            }).Build();
        await host.StartAsync(ct);
        await File.WriteAllTextAsync("/checkpoints/ready", Environment.ProcessId.ToString(), ct);
        await host.WaitForShutdownAsync(ct);
    }

    private static void Route<T>(WolverineOptions options) => options.PublishMessage<T>().ToNatsSubject(ControlSubject)
        .UseJetStream(ControlStream).UseDurableOutbox();

    private sealed class FixtureFileLogger : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Writer(categoryName);
        public void Dispose() { }
        private sealed class Writer(string category) : ILogger
        {
            private static readonly Lock Sync = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Information && level != LogLevel.None;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(level)) return;
                lock (Sync) File.AppendAllText("/checkpoints/host-events.log", $"{DateTimeOffset.UtcNow:O} {level} {category}: {formatter(state, exception)} {exception}\n");
            }
        }
    }

    public sealed class UnrelatedScheduleSink
    {
        public void Handle(DispatchAwdCheckers message) { }
        public void Handle(ExpireAccountSourceAddresses message) { }
        public void Handle(CompetitionEventCommitted message) { }
    }

    private sealed class Checkpoint(IConfiguration configuration, Guid runtimeId)
    {
        private readonly CapacityCrashWindow window = Enum.Parse<CapacityCrashWindow>(configuration["CapacityFixture:Window"]!);
        private int hit;
        private bool sawAllocation;
        public async Task BeforeWriteAsync(string sql, CancellationToken ct)
        {
            if (!sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) || !sql.Contains("runtime_instances", StringComparison.Ordinal)
                || !sql.Contains("capacity_allocations", StringComparison.Ordinal)) return;
            if (window == CapacityCrashWindow.RollbackThenCancel)
            {
                await File.WriteAllTextAsync("/checkpoints/fault", window.ToString(), ct);
                throw new InvalidOperationException("Injected rollback after Redis claim, before allocation write.");
            }
            await BlockAsync(CapacityCrashWindow.BeforeAllocationWrite, ct);
        }

        public async Task AfterCommitAsync(CancellationToken ct)
        {
            if (window is not (CapacityCrashWindow.AfterAllocationCommit or CapacityCrashWindow.AfterReleaseCommit)) return;
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("PostgreSql"));
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT jsonb_array_length(capacity_allocations->'items'), state FROM runtime_instances WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", runtimeId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return;
            var count = reader.GetInt32(0);
            var state = (RuntimeState)reader.GetInt16(1);
            if (count > 0) sawAllocation = true;
            if (count > 0 && state == RuntimeState.Provisioning) await BlockAsync(CapacityCrashWindow.AfterAllocationCommit, ct);
            if (count == 0 && sawAllocation && state is RuntimeState.Stopping or RuntimeState.Stopped or RuntimeState.Failed)
                await BlockAsync(CapacityCrashWindow.AfterReleaseCommit, ct);
        }

        public async Task BlockAsync(CapacityCrashWindow at, CancellationToken ct)
        {
            if (window != at || Interlocked.Exchange(ref hit, 1) != 0) return;
            await File.WriteAllTextAsync("/checkpoints/fault", at.ToString(), ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class AllocationWriteCheckpoint(Checkpoint checkpoint) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            await checkpoint.BeforeWriteAsync(command.CommandText, ct);
            return result;
        }
    }

    private sealed class AllocationCommitCheckpoint(Checkpoint checkpoint) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default) =>
            checkpoint.AfterCommitAsync(ct);
    }

    private sealed class CheckpointCatalog(RuntimeProviderCatalog inner, Checkpoint checkpoint) : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) => new CheckpointLifecycle(inner.Containers(provider), checkpoint);
        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) => inner.Sandbox(provider);
        public IComposeRuntime Compose(RuntimeProvider provider) => inner.Compose(provider);
        public IOvaRuntime Appliance(RuntimeProvider provider) => inner.Appliance(provider);
    }

    private sealed class CheckpointLifecycle(IContainerLifecycle inner, Checkpoint checkpoint) : IContainerLifecycle
    {
        public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken ct)
        {
            var result = await inner.CreateAsync(request, ct);
            await checkpoint.BlockAsync(CapacityCrashWindow.AfterProviderCreate, ct);
            return result;
        }
        public async Task<ContainerReceipt> EnsureRunningAsync(ContainerRequest request, CancellationToken ct)
        {
            var result = await inner.EnsureRunningAsync(request, ct);
            await checkpoint.BlockAsync(CapacityCrashWindow.AfterProviderCreate, ct);
            return result;
        }
        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken ct) => inner.DestroyAsync(receipt, ct);
        public Task DestroyAsync(ContainerReceipt receipt, RuntimeTerminationMode mode, RuntimeTerminationPolicy policy, CancellationToken ct) =>
            inner.DestroyAsync(receipt, mode, policy, ct);
        public Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken ct) => inner.GetAsync(provider, resourceId, ct);
    }

    private sealed class ScopedInventory(IRuntimeManagedResourceReconciler inner, Guid runtimeId) : IRuntimeManagedResourceReconciler
    {
        public RuntimeProvider Provider => inner.Provider;
        public Task<bool?> WorkloadExistsAsync(RuntimeWorkloadIdentity identity, CancellationToken ct) => inner.WorkloadExistsAsync(identity, ct);
        public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(CancellationToken ct) =>
            (await inner.ListManagedAsync(ct)).Where(item => item.RuntimeInstanceId == runtimeId).ToArray();
        public Task DestroyByIdentityAsync(RuntimeResourceIdentity identity, CancellationToken ct) => identity.RuntimeInstanceId == runtimeId
            ? inner.DestroyByIdentityAsync(identity, ct) : throw new InvalidOperationException("Resource outside crash-fixture scope.");
    }
}
