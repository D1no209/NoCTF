using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public class OrphanRuntimeCleanerTests
{
    [Test]
    public async Task ExecuteAsync_DestroysOrphansAndMarksStopped()
    {
        var targets = new[] { Target("old-revision"), Target("expired") };
        var store = new Store(targets);
        var runtime = new Runtime();
        var now = DateTimeOffset.UtcNow;

        var result = await new OrphanRuntimeCleaner(store, runtime).ExecuteAsync(now);

        await Assert.That(result.StoppedCount).IsEqualTo(2);
        await Assert.That(runtime.Destroyed).IsEquivalentTo(new[] { "old-revision", "expired" });
        await Assert.That(store.Stopped).IsEquivalentTo(targets.Select(target => target.InstanceId));
        await Assert.That(store.StoppedAt).IsEqualTo(now);
    }

    [Test]
    public async Task ExecuteAsync_PersistsSuccessfulCleanupBeforeReportingFailure()
    {
        var successful = Target("success");
        var failed = Target("failed");
        var store = new Store([successful, failed]);
        var runtime = new Runtime { FailedResourceId = "failed" };

        var cleanup = async () => await new OrphanRuntimeCleaner(store, runtime)
            .ExecuteAsync(DateTimeOffset.UtcNow);

        await Assert.That(cleanup).Throws<InvalidOperationException>();
        await Assert.That(store.Stopped).IsEquivalentTo(new[] { successful.InstanceId });
    }

    private static RuntimeCleanupTarget Target(string resourceId) => new(
        Guid.NewGuid(),
        new ContainerReceipt(
            Guid.NewGuid(),
            RuntimeProvider.Docker,
            resourceId,
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            null,
            null));

    private sealed class Store(IReadOnlyList<RuntimeCleanupTarget> targets) : IOrphanRuntimeStore
    {
        public IReadOnlyCollection<Guid> Stopped { get; private set; } = [];
        public DateTimeOffset? StoppedAt { get; private set; }

        public Task<IReadOnlyList<RuntimeCleanupTarget>> ListOrphansAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(targets);

        public Task MarkStoppedAsync(
            IReadOnlyCollection<Guid> instanceIds,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Stopped = instanceIds;
            StoppedAt = now;
            return Task.CompletedTask;
        }
    }

    private sealed class Runtime : IContainerLifecycle
    {
        public string? FailedResourceId { get; init; }
        public List<string> Destroyed { get; } = [];
        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
        {
            if (receipt.ResourceId == FailedResourceId) throw new InvalidOperationException("destroy failed");
            Destroyed.Add(receipt.ResourceId);
            return Task.CompletedTask;
        }
        public Task<ContainerReceipt?> GetAsync(
            RuntimeProvider provider,
            string resourceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
