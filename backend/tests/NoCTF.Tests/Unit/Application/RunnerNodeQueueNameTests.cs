using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerNodeQueueNameTests
{
    [Test]
    public async Task FromRunnerId_UsesConcreteRunnerIdentity()
    {
        var queue = RunnerNodeQueueName.FromRunnerId("runner-a");

        await Assert.That(queue.Value).StartsWith("noctf-runner-node-");
        await Assert.That(queue).IsEqualTo(RunnerNodeQueueName.FromRunnerId("runner-a"));
    }

    [Test]
    public async Task FromRunnerId_DistinguishesRunnerBoundaries()
    {
        var first = RunnerNodeQueueName.FromRunnerId("c");
        var second = RunnerNodeQueueName.FromRunnerId("bc");
        var otherRunner = RunnerNodeQueueName.FromRunnerId("runner-b");

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(first).IsNotEqualTo(otherRunner);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task FromRunnerId_RejectsMissingRunnerIdentity(string? runnerId)
    {
        await Assert.That(() => RunnerNodeQueueName.FromRunnerId(runnerId!))
            .Throws<ArgumentException>();
    }
}
