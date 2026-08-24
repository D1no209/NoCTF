using NoCTF.Application.Messaging;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerNodeAssignmentGuardTests
{
    [Test]
    public async Task Validate_accepts_exact_original_assignment()
    {
        var message = Create("runner-1");

        await Assert.That(() => RunnerNodeAssignmentGuard.Validate(message, "pool-a", "runner-1"))
            .ThrowsNothing();
    }

    [Test]
    [Arguments("pool-b", "runner-2")]
    [Arguments("pool-a", "runner-2")]
    [Arguments("POOL-A", "runner-2")]
    public async Task Validate_rejects_wrong_pool_or_node(string pool, string runnerId)
    {
        var message = Create(runnerId);

        await Assert.That(() => RunnerNodeAssignmentGuard.Validate(message, "pool-a", "runner-1"))
            .Throws<InvalidOperationException>();
    }

    private static CleanupAwdpTarget Create(string runnerId) =>
        new(Guid.CreateVersion7(), runnerId);
}
