using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class ContainerRuntimeHandlerTests
{
    [Test]
    public async Task Container_configuration_rejection_has_a_stable_failure_code()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var lifecycle = new RecordingContainerLifecycle
        {
            ProvisionFailure = new RuntimeConfigurationException("Invalid image user.")
        };
        var capacity = new RecordingCapacity(RunnerCapacityReleaseOutcome.Released);
        var reconciler = new RecordingResourceReconciler();
        var handler = CreateHandler(
            lifecycle,
            new RecordingSandboxLifecycle(),
            capacity,
            new FixedWorkReader(null),
            reconciler);
        var message = new ProvisionContainerRuntime(
            runtimeInstanceId,
            RunnerId: "runner-a",
            Definition: new ContainerRequest(
                runtimeInstanceId,
                RuntimeProvider.Docker,
                "challenge:latest",
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<int, int>(),
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(false, false, true, [], []),
                Ttl: null));

        var result = await handler.ProvisionContainerAsync(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisionTerminated>();
        await Assert.That(((RuntimeProvisionTerminated)result!).FailureCode)
            .IsEqualTo(RuntimeFailureCode.InvalidConfiguration);
        await Assert.That(lifecycle.EnsureRunningCalls).IsEqualTo(1);
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(runtimeInstanceId)]);
        await Assert.That(reconciler.Modes).IsEquivalentTo([RuntimeTerminationMode.Force]);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEquivalentTo([runtimeInstanceId]);
    }

    [Test]
    public async Task Stop_capacity_owner_mismatch_fails_closed_after_provider_cleanup()
    {
        var lifecycle = new RecordingContainerLifecycle();
        var sandbox = new RecordingSandboxLifecycle();
        var capacity = new RecordingCapacity(
            RunnerCapacityReleaseOutcome.OwnerMismatch);
        var reconciler = new RecordingResourceReconciler();
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var receipt = new ContainerReceipt(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "container-1",
            RuntimeStatus.Running,
            new Dictionary<int, int> { [8080] = 32000 },
            "runner.example",
            "container-1",
            "network-1",
            runtimeInstanceId);
        var handler = CreateHandler(
            lifecycle,
            sandbox,
            capacity,
            new FixedWorkReader(new(
                RuntimeProvider.Docker,
                ContainerRuntimeReceiptData.From(receipt))),
            reconciler);
        var message = new StopContainerRuntime(runtimeInstanceId, "runner-a");

        var result = await handler.StopContainerAsync(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopFailed>();
        await Assert.That(result).IsNotTypeOf<RuntimeStopped>();
        var failure = (RuntimeStopFailed)result;
        await Assert.That(failure.RunnerId).IsEqualTo(message.RunnerId);
        await Assert.That(lifecycle.Destroyed).IsEquivalentTo([receipt]);
        await Assert.That(lifecycle.DestroyModes)
            .IsEquivalentTo([RuntimeTerminationMode.GracefulThenForce]);
        await Assert.That(sandbox.DeletedNetworks).IsEquivalentTo(["network-1"]);
        await Assert.That(lifecycle.ReadResourceIds).IsEquivalentTo(["container-1"]);
        await Assert.That(sandbox.CheckedNetworks).IsEquivalentTo(["network-1"]);
        await Assert.That(reconciler.Destroyed).IsEmpty();
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    [Test]
    public async Task Provider_rejection_marks_the_runner_temporarily_unavailable()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var lifecycle = new RecordingContainerLifecycle
        {
            ProvisionFailure = new InvalidOperationException(
                "all predefined address pools have been fully subnetted")
        };
        var health = new RunnerProviderHealthState(
            Options.Create(new RunnerOptions
            {
                ProviderFailureHoldSeconds = 120
            }),
            TimeProvider.System,
            NullLogger<RunnerProviderHealthState>.Instance);
        var handler = CreateHandler(
            lifecycle,
            new RecordingSandboxLifecycle(),
            new RecordingCapacity(RunnerCapacityReleaseOutcome.Released),
            new FixedWorkReader(null),
            providerHealth: health);
        var message = new ProvisionContainerRuntime(
            runtimeInstanceId,
            RunnerId: "runner-a",
            Definition: new ContainerRequest(
                runtimeInstanceId,
                RuntimeProvider.Docker,
                "challenge:latest",
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<int, int>(),
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(false, false, false, [], []),
                Ttl: null));

        var result = await handler.ProvisionContainerAsync(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisionTerminated>();
        await Assert.That(((RuntimeProvisionTerminated)result!).FailureCode)
            .IsEqualTo(RuntimeFailureCode.ProviderRejected);
        await Assert.That(health.IsReady(RuntimeProvider.Docker)).IsFalse();
    }

    [Test]
    public async Task Stop_rejects_a_receipt_for_another_runtime_without_deleting_resources()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var receipt = new ContainerReceipt(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "container-1",
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            null,
            "container-1",
            "network-1",
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var lifecycle = new RecordingContainerLifecycle();
        var sandbox = new RecordingSandboxLifecycle();
        var capacity = new RecordingCapacity(RunnerCapacityReleaseOutcome.Released);
        var handler = CreateHandler(
            lifecycle,
            sandbox,
            capacity,
            new FixedWorkReader(new(
                RuntimeProvider.Docker,
                ContainerRuntimeReceiptData.From(receipt))));

        var result = await handler.StopContainerAsync(
            new StopContainerRuntime(runtimeInstanceId, "runner-a"),
            CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopFailed>();
        await Assert.That(lifecycle.Destroyed).IsEmpty();
        await Assert.That(sandbox.DeletedNetworks).IsEmpty();
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Stop_without_receipt_cleans_by_runtime_identity_before_releasing_capacity()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reconciler = new RecordingResourceReconciler();
        var capacity = new RecordingCapacity(RunnerCapacityReleaseOutcome.Released);
        var lifecycle = new RecordingContainerLifecycle();
        var handler = CreateHandler(
            lifecycle,
            new RecordingSandboxLifecycle(),
            capacity,
            new FixedWorkReader(new(
                RuntimeProvider.Docker,
                ProviderReceipt: null)),
            reconciler);
        var message = new StopContainerRuntime(runtimeInstanceId, "runner-a");

        var result = await handler.StopContainerAsync(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopped>();
        var acknowledgement = (RuntimeStopped)result;
        await Assert.That(acknowledgement.RunnerId).IsEqualTo(message.RunnerId);
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(runtimeInstanceId)]);
        await Assert.That(reconciler.Modes)
            .IsEquivalentTo([RuntimeTerminationMode.GracefulThenForce]);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEquivalentTo([runtimeInstanceId]);
    }

    [Test]
    public async Task Stop_does_not_release_capacity_while_provider_resources_remain()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var identity = new RuntimeResourceIdentity(runtimeInstanceId);
        var reconciler = new RecordingResourceReconciler
        {
            Remaining = [identity]
        };
        var capacity = new RecordingCapacity(RunnerCapacityReleaseOutcome.Released);
        var handler = CreateHandler(
            new RecordingContainerLifecycle(),
            new RecordingSandboxLifecycle(),
            capacity,
            new FixedWorkReader(new(
                RuntimeProvider.Docker,
                ProviderReceipt: null)),
            reconciler);

        var result = await handler.StopContainerAsync(
            new StopContainerRuntime(runtimeInstanceId, "runner-a"),
            CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopFailed>();
        await Assert.That(reconciler.Destroyed).IsEquivalentTo([identity]);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Force_termination_uses_identity_cleanup_and_confirms_resources_are_absent()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reconciler = new RecordingResourceReconciler();
        var capacity = new RecordingCapacity(RunnerCapacityReleaseOutcome.Released);
        var lifecycle = new RecordingContainerLifecycle();
        var receipt = new ContainerReceipt(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "container-1",
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            null,
            "container-1",
            "network-1",
            runtimeInstanceId);
        var handler = CreateHandler(
            lifecycle,
            new RecordingSandboxLifecycle(),
            capacity,
            new FixedWorkReader(new(
                RuntimeProvider.Docker,
                ContainerRuntimeReceiptData.From(receipt),
                RuntimeKind.Container)),
            reconciler);
        var message = new ForceTerminateRuntime(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "runner-a",
            Guid.NewGuid(),
            "The runtime exceeded the cleanup timeout.",
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await handler.ForceTerminateAsync(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeForceTerminated>();
        await Assert.That(((RuntimeForceTerminated)result).CleanupResult)
            .IsEqualTo(RuntimeCleanupResult.ResourcesAbsent);
        await Assert.That(lifecycle.DestroyModes).IsEquivalentTo([RuntimeTerminationMode.Force]);
        await Assert.That(reconciler.Destroyed).IsEmpty();
        await Assert.That(capacity.ReleasedRuntimeIds).IsEquivalentTo([runtimeInstanceId]);
    }

    [Test]
    public async Task Force_termination_without_current_work_still_reconciles_provider_identity()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reconciler = new RecordingResourceReconciler();
        var capacity = new RecordingCapacity(RunnerCapacityReleaseOutcome.AlreadyReleased);
        var handler = CreateHandler(
            new RecordingContainerLifecycle(),
            new RecordingSandboxLifecycle(),
            capacity,
            new FixedWorkReader(null),
            reconciler);
        var message = new ForceTerminateRuntime(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "runner-a",
            Guid.NewGuid(),
            "Retry an idempotent force-termination request.",
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await handler.ForceTerminateAsync(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeForceTerminated>();
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(runtimeInstanceId)]);
        await Assert.That(reconciler.Modes).IsEquivalentTo([RuntimeTerminationMode.Force]);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEquivalentTo([runtimeInstanceId]);
    }

    private static RuntimeProviderHandler CreateHandler(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox,
        IRunnerCapacityGate capacity,
        IRuntimeNodeWorkReader reader,
        IRuntimeManagedResourceReconciler? reconciler = null,
        RunnerProviderHealthState? providerHealth = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runner:Pool"] = "default",
                ["Runner:Id"] = "runner-a"
            })
            .Build();
        return new(
            new RecordingProviderCatalog(lifecycle, sandbox),
            [reconciler ?? new RecordingResourceReconciler()],
            configuration.ToRunnerOptions(),
            capacity,
            reader,
            providerHealth: providerHealth);
    }

    private sealed class RecordingContainerLifecycle : IContainerLifecycle
    {
        public List<ContainerReceipt> Destroyed { get; } = [];
        public List<RuntimeTerminationMode> DestroyModes { get; } = [];
        public List<string> ReadResourceIds { get; } = [];
        public Exception? ProvisionFailure { get; init; }
        public int EnsureRunningCalls { get; private set; }

        public Task<ContainerReceipt> CreateAsync(
            ContainerRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ContainerReceipt> EnsureRunningAsync(
            ContainerRequest request,
            CancellationToken cancellationToken)
        {
            EnsureRunningCalls++;
            return ProvisionFailure is null
                ? throw new NotSupportedException()
                : Task.FromException<ContainerReceipt>(ProvisionFailure);
        }

        public Task DestroyAsync(
            ContainerReceipt receipt,
            CancellationToken cancellationToken)
        {
            Destroyed.Add(receipt);
            return Task.CompletedTask;
        }

        public Task DestroyAsync(
            ContainerReceipt receipt,
            RuntimeTerminationMode mode,
            RuntimeTerminationPolicy policy,
            CancellationToken cancellationToken)
        {
            DestroyModes.Add(mode);
            return DestroyAsync(receipt, cancellationToken);
        }

        public Task<ContainerReceipt?> GetAsync(
            RuntimeProvider provider,
            string resourceId,
            CancellationToken cancellationToken)
        {
            ReadResourceIds.Add(resourceId);
            return Task.FromResult<ContainerReceipt?>(null);
        }
    }

    private sealed class RecordingSandboxLifecycle : IContainerSandboxLifecycle
    {
        public List<string> DeletedNetworks { get; } = [];
        public List<string> CheckedNetworks { get; } = [];

        public Task<string> CreateIsolatedNetworkAsync(
            ContainerNetworkPolicyRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteIsolatedNetworkAsync(
            string networkId,
            CancellationToken cancellationToken)
        {
            DeletedNetworks.Add(networkId);
            return Task.CompletedTask;
        }

        public Task<bool> IsolatedNetworkExistsAsync(
            string networkId,
            CancellationToken cancellationToken)
        {
            CheckedNetworks.Add(networkId);
            return Task.FromResult(false);
        }

        public Task CopyArchiveAsync(
            ContainerReceipt receipt,
            Stream tarArchive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ContainerExecResult> ExecAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ContainerExecResult> ExecWithInputAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            ReadOnlyMemory<byte> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProviderCatalog(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox) : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) => lifecycle;

        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) => sandbox;

        public IComposeRuntime Compose(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new NotSupportedException();
    }

    private sealed class FixedWorkReader(RuntimeStopWork? stop) : IRuntimeNodeWorkReader
    {
        public Task<RuntimeProvisionWorkStatus> ReadProvisionStatusAsync(
            IRuntimeProvisionMessage message,
            CancellationToken cancellationToken) =>
            Task.FromResult(RuntimeProvisionWorkStatus.Current);

        public Task<RuntimeStopWork?> ReadStopAsync(
            IRuntimeStopMessage message,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeStopWork?>(stop);
    }

    private sealed class RecordingCapacity(
        RunnerCapacityReleaseOutcome releaseOutcome) : IRunnerCapacityGate
    {
        public List<Guid> ReleasedRuntimeIds { get; } = [];

        public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
            string runnerPool,
            string runnerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunnerPoolInventory> GetPoolInventoryAsync(
            string runnerPool,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunnerCapacityClaim> TryClaimAsync(
            RunnerCapacityRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
            RunnerCapacityRequest request,
            string runnerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
            Guid runtimeInstanceId,
            string runnerId,
            CancellationToken cancellationToken)
        {
            ReleasedRuntimeIds.Add(runtimeInstanceId);
            return Task.FromResult(releaseOutcome);
        }
    }

    private sealed class RecordingResourceReconciler : IRuntimeManagedResourceReconciler
    {
        public List<RuntimeResourceIdentity> Destroyed { get; } = [];
        public List<RuntimeTerminationMode> Modes { get; } = [];
        public IReadOnlyList<RuntimeResourceIdentity> Remaining { get; init; } = [];
        public RuntimeProvider Provider => RuntimeProvider.Docker;

        public Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Remaining);

        public Task DestroyByIdentityAsync(
            RuntimeResourceIdentity identity,
            CancellationToken cancellationToken)
        {
            Destroyed.Add(identity);
            return Task.CompletedTask;
        }

        public Task DestroyByIdentityAsync(
            RuntimeResourceIdentity identity,
            RuntimeTerminationMode mode,
            RuntimeTerminationPolicy policy,
            CancellationToken cancellationToken)
        {
            Modes.Add(mode);
            return DestroyByIdentityAsync(identity, cancellationToken);
        }
    }
}
