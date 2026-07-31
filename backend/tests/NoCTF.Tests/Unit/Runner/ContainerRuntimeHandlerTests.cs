using Microsoft.Extensions.Configuration;
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
    public async Task Stop_capacity_owner_mismatch_fails_closed_after_provider_cleanup()
    {
        var lifecycle = new RecordingContainerLifecycle();
        var sandbox = new RecordingSandboxLifecycle();
        var capacity = new RecordingCapacity(
            RunnerCapacityReleaseOutcome.OwnerMismatch);
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
            runtimeInstanceId,
            3);
        var handler = CreateHandler(
            lifecycle,
            sandbox,
            capacity,
            new FixedWorkReader(new(
                RuntimeProvider.Docker,
                System.Text.Json.JsonSerializer.Serialize(receipt))));
        var message = new StopContainerRuntime(
            runtimeInstanceId,
            8,
            "default",
            "runner-a");

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopFailed>();
        await Assert.That(result).IsNotTypeOf<RuntimeStopped>();
        await Assert.That(lifecycle.Destroyed).IsEquivalentTo([receipt]);
        await Assert.That(sandbox.DeletedNetworks).IsEquivalentTo(["network-1"]);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    private static RuntimeProviderHandler CreateHandler(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox,
        IRunnerCapacityGate capacity,
        IRuntimeNodeWorkReader reader)
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
            [new RecordingResourceReconciler()],
            configuration,
            capacity,
            reader);
    }

    private sealed class RecordingContainerLifecycle : IContainerLifecycle
    {
        public List<ContainerReceipt> Destroyed { get; } = [];

        public Task<ContainerReceipt> CreateAsync(
            ContainerRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ContainerReceipt> EnsureRunningAsync(
            ContainerRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DestroyAsync(
            ContainerReceipt receipt,
            CancellationToken cancellationToken)
        {
            Destroyed.Add(receipt);
            return Task.CompletedTask;
        }

        public Task<ContainerReceipt?> GetAsync(
            RuntimeProvider provider,
            string resourceId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSandboxLifecycle : IContainerSandboxLifecycle
    {
        public List<string> DeletedNetworks { get; } = [];

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

    private sealed class FixedWorkReader(RuntimeStopWork stop) : IRuntimeNodeWorkReader
    {
        public Task<RuntimeProvisionWorkStatus> ReadProvisionStatusAsync(
            IRuntimeProvisionMessage message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
        public RuntimeProvider Provider => RuntimeProvider.Docker;

        public Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RuntimeResourceIdentity>>([]);

        public Task DestroyByIdentityAsync(
            RuntimeResourceIdentity identity,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
