using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public class RuntimeHealthCheckerTests
{
    [Test]
    public async Task HealthCheck_UpdatesPersistedStatusWhenRuntimeChanges()
    {
        var instanceId = Guid.NewGuid();
        var receipt = new ContainerReceipt(Guid.NewGuid(), RuntimeProvider.Docker, "container-1",
            RuntimeStatus.Running, new Dictionary<int, int>(), "localhost", null);
        var store = new Store([new RuntimeHealthTarget(instanceId, receipt.ResourceId, receipt)]);
        var runtime = new Runtime(receipt with { Status = RuntimeStatus.Stopped });

        var changed = await new ChallengeRuntimeHealthChecker(store, runtime).ExecuteAsync();

        await Assert.That(changed).IsEqualTo(1);
        await Assert.That(store.Statuses[instanceId]).IsEqualTo(RuntimeStatus.Stopped);
        await Assert.That(runtime.QueriedProvider).IsEqualTo(RuntimeProvider.Docker);
    }

    private sealed class Store(IReadOnlyList<RuntimeHealthTarget> targets) : IRuntimeHealthStore
    {
        public Dictionary<Guid, RuntimeStatus> Statuses { get; } = [];

        public Task<IReadOnlyList<RuntimeHealthTarget>> ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(targets);

        public Task UpdateStatusAsync(Guid instanceId, RuntimeStatus status, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Statuses[instanceId] = status;
            return Task.CompletedTask;
        }
    }

    private sealed class Runtime(ContainerReceipt current) : IContainerLifecycle
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
