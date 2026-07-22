using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public class RuntimeHealthCheckerTests
{
    [Test]
    public async Task HealthCheck_UnexpectedlyStoppedRuntime_MarksInstanceFailedForReprovision()
    {
        var instanceId = Guid.NewGuid();
        var receipt = new ContainerReceipt(Guid.NewGuid(), RuntimeProvider.Docker, "container-1",
            RuntimeStatus.Running, new Dictionary<int, int>(), "localhost", null);
        var store = new Store([new RuntimeHealthTarget(instanceId, receipt.ResourceId, receipt, RuntimeStatus.Running)]);
        var runtime = new Runtime(receipt with { Status = RuntimeStatus.Stopped });

        var changed = await new ChallengeRuntimeHealthChecker(store, runtime).ExecuteAsync();

        await Assert.That(changed).IsEqualTo(1);
        await Assert.That(store.Statuses[instanceId]).IsEqualTo(RuntimeStatus.Failed);
        await Assert.That(runtime.QueriedProvider).IsEqualTo(RuntimeProvider.Docker);
    }

    [Test]
    public async Task HealthCheck_MissingRuntime_MarksInstanceFailed()
    {
        var instanceId = Guid.NewGuid();
        var receipt = new ContainerReceipt(Guid.NewGuid(), RuntimeProvider.Docker, "missing-container",
            RuntimeStatus.Running, new Dictionary<int, int>(), "localhost", null);
        var store = new Store([new RuntimeHealthTarget(instanceId, receipt.ResourceId, receipt, RuntimeStatus.Running)]);

        var changed = await new ChallengeRuntimeHealthChecker(store, new Runtime(null)).ExecuteAsync();

        await Assert.That(changed).IsEqualTo(1);
        await Assert.That(store.Statuses[instanceId]).IsEqualTo(RuntimeStatus.Failed);
    }

    [Test]
    public async Task HealthCheck_UsesPersistedStatusInsteadOfCreationReceiptStatus()
    {
        var instanceId = Guid.NewGuid();
        var receipt = new ContainerReceipt(Guid.NewGuid(), RuntimeProvider.Kubernetes, "pod-1",
            RuntimeStatus.Pending, new Dictionary<int, int>(), "localhost", null);
        var store = new Store([new RuntimeHealthTarget(
            instanceId, receipt.ResourceId, receipt, RuntimeStatus.Running)]);

        var changed = await new ChallengeRuntimeHealthChecker(
            store, new Runtime(receipt with { Status = RuntimeStatus.Running })).ExecuteAsync();

        await Assert.That(changed).IsEqualTo(0);
        await Assert.That(store.Statuses).IsEmpty();
    }

    [Test]
    public async Task HealthCheck_StaleObservationDoesNotCountAsStateChange()
    {
        var instanceId = Guid.NewGuid();
        var receipt = new ContainerReceipt(Guid.NewGuid(), RuntimeProvider.Docker, "container-1",
            RuntimeStatus.Running, new Dictionary<int, int>(), "localhost", null);
        var store = new Store([new RuntimeHealthTarget(
            instanceId, receipt.ResourceId, receipt, RuntimeStatus.Running)])
        {
            UpdateSucceeded = false
        };

        var changed = await new ChallengeRuntimeHealthChecker(
            store, new Runtime(receipt with { Status = RuntimeStatus.Stopped })).ExecuteAsync();

        await Assert.That(changed).IsEqualTo(0);
        await Assert.That(store.Statuses).IsEmpty();
    }

    private sealed class Store(IReadOnlyList<RuntimeHealthTarget> targets) : IRuntimeHealthStore
    {
        public Dictionary<Guid, RuntimeStatus> Statuses { get; } = [];
        public bool UpdateSucceeded { get; init; } = true;

        public Task<IReadOnlyList<RuntimeHealthTarget>> ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(targets);

        public Task<bool> UpdateStatusAsync(
            Guid instanceId, RuntimeStatus expectedStatus, RuntimeStatus status,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (UpdateSucceeded) Statuses[instanceId] = status;
            return Task.FromResult(UpdateSucceeded);
        }
    }

    private sealed class Runtime(ContainerReceipt? current) : IContainerLifecycle
    {
        public RuntimeProvider? QueriedProvider { get; private set; }
        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken)
        {
            QueriedProvider = provider;
            return Task.FromResult<ContainerReceipt?>(current);
        }
    }
}
