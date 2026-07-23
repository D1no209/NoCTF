using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerNodeQueueNameTests
{
    [Test]
    public async Task FromAssignment_UsesPoolAndRunnerIdentity()
    {
        var queue = RunnerNodeQueueName.FromAssignment("primary", "runner-a");

        await Assert.That(queue.Value)
            .IsEqualTo("noctf-runner-node-2crk1lgfilmyvht6f9hy52");
    }

    [Test]
    public async Task FromAssignment_DistinguishesPoolAndRunnerBoundaries()
    {
        var first = RunnerNodeQueueName.FromAssignment("ab", "c");
        var second = RunnerNodeQueueName.FromAssignment("a", "bc");
        var otherRunner = RunnerNodeQueueName.FromAssignment("ab", "runner-b");

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(first).IsNotEqualTo(otherRunner);
    }

    [Test]
    [Arguments(null, "runner-a")]
    [Arguments("", "runner-a")]
    [Arguments("primary", null)]
    [Arguments("primary", "")]
    public async Task FromAssignment_RejectsMissingOriginalIdentity(string? pool, string? runnerId)
    {
        await Assert.That(() => RunnerNodeQueueName.FromAssignment(pool!, runnerId!))
            .Throws<ArgumentException>();
    }
}
