using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RunnerResourceMutationCoordinatorTests
{
    [Test]
    public async Task Recovery_waits_for_mutations_and_blocks_new_creates_until_inventory_is_complete(CancellationToken ct)
    {
        using var coordinator = new RunnerResourceMutationCoordinator();
        var running = await coordinator.EnterAsync(ct);
        var recovery = coordinator.ReconcileAsync(ct);
        await Assert.That(recovery.IsCompleted).IsFalse();
        running.Dispose();
        var inventory = await recovery.WaitAsync(TimeSpan.FromSeconds(2), ct);
        var create = coordinator.EnterAsync(ct);
        await Assert.That(create.IsCompleted).IsFalse();
        inventory.Dispose();
        using var next = await create.WaitAsync(TimeSpan.FromSeconds(2), ct);
    }
}
