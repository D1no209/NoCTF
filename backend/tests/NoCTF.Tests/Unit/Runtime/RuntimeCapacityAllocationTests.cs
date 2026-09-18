using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeCapacityAllocationTests
{
    [Test]
    public async Task Primary_and_checker_allocations_have_independent_stable_identity()
    {
        var runtime = Guid.NewGuid();
        var main = Allocation(new(RuntimeWorkloadKind.Runtime, runtime, runtime));
        var checker = Allocation(new(RuntimeWorkloadKind.AwdChecker, runtime, Guid.NewGuid()), Guid.NewGuid());
        var document = RuntimeCapacityAllocations.Empty.Add(main).Add(checker).Add(checker);
        var restored = RuntimeCapacityAllocations.Deserialize(RuntimeCapacityAllocations.Serialize(document));
        await Assert.That(restored.Items.Count).IsEqualTo(2);
        await Assert.That(restored.Items[0]).IsEqualTo(main);
        await Assert.That(restored.Items[1]).IsEqualTo(checker);
        await Assert.That(main.Identity.Key).IsNotEqualTo(checker.Identity.Key);
        await Assert.That(restored.Remove(main.Identity).Remove(main.Identity).Items).IsEquivalentTo([checker]);
    }

    [Test]
    public async Task Replay_cannot_change_an_existing_allocation_budget_or_owner()
    {
        var runtime = Guid.NewGuid();
        var main = Allocation(new(RuntimeWorkloadKind.Runtime, runtime, runtime));
        var document = RuntimeCapacityAllocations.Empty.Add(main);
        await Assert.That(() => document.Add(main with { RunnerId = "other" })).Throws<InvalidOperationException>();
        await Assert.That(() => document.Add(main with { Budget = main.Budget with { NanoCpus = 1 } }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Unknown_document_schema_and_memory_overcommit_fail_closed()
    {
        await Assert.That(() => RuntimeCapacityAllocations.Deserialize("{\"schemaVersion\":2,\"items\":[]}"))
            .Throws<InvalidOperationException>();
        var runtime = Guid.NewGuid();
        var main = Allocation(new(RuntimeWorkloadKind.Runtime, runtime, runtime));
        await Assert.That(() => RuntimeCapacityAllocations.Empty.Add(main with
        {
            Budget = main.Budget with { MemoryBytes = 1 }
        })).Throws<InvalidOperationException>();
    }

    private static RuntimeCapacityAllocation Allocation(RuntimeWorkloadIdentity identity, Guid? fact = null) =>
        new(identity, fact, "docker-domain", "runner", new(256, 250, 128), new(256, 500, 128));
}
