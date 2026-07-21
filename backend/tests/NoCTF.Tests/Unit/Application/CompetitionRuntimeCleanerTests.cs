using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionRuntimeCleanerTests
{
    [Test]
    public async Task Cleanup_DestroysAllActiveTargetsAndMarksThemStopped()
    {
        var targets = new[] { Target("one"), Target("two") };
        var store = new CleanupStore(targets);
        var runtime = new Runtime();

        var result = await new CompetitionRuntimeCleaner(store, runtime)
            .ExecuteAsync(Guid.NewGuid());

        await Assert.That(result.StoppedCount).IsEqualTo(2);
        await Assert.That(runtime.Destroyed).IsEquivalentTo(new[] { "one", "two" });
        await Assert.That(store.Stopped).IsEquivalentTo(targets.Select(target => target.InstanceId));
    }

    [Test]
    public async Task Cleanup_PersistsSuccessfulTargetsBeforeRetryingFailure()
    {
        var successful = Target("one");
        var failed = Target("two");
        var store = new CleanupStore([successful, failed]);
        var runtime = new Runtime { FailedResourceId = "two" };

        await Assert.That(async () => await new CompetitionRuntimeCleaner(store, runtime)
            .ExecuteAsync(Guid.NewGuid())).Throws<InvalidOperationException>();

        await Assert.That(store.Stopped).IsEquivalentTo(new[] { successful.InstanceId });
    }

    private static RuntimeCleanupTarget Target(string resourceId) => new(
        Guid.NewGuid(),
        new ContainerReceipt(Guid.NewGuid(), RuntimeProvider.Docker, resourceId,
            RuntimeStatus.Running, new Dictionary<int, int>(), "localhost", null));

    private sealed class CleanupStore(IReadOnlyList<RuntimeCleanupTarget> targets) : IRuntimeCleanupStore
    {
        public IReadOnlyCollection<Guid> Stopped { get; private set; } = [];

        public Task<IReadOnlyList<RuntimeCleanupTarget>> ListActiveAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(targets);

        public Task MarkStoppedAsync(IReadOnlyCollection<Guid> instanceIds, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Stopped = instanceIds;
            return Task.CompletedTask;
        }
    }

    private sealed class Runtime : IContainerLifecycle
    {
        public string? FailedResourceId { get; init; }
        public List<string> Destroyed { get; } = [];

        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
        {
            if (receipt.ResourceId == FailedResourceId)
                throw new InvalidOperationException("destroy failed");
            Destroyed.Add(receipt.ResourceId);
            return Task.CompletedTask;
        }

        public Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
