using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerPoolMessageContractTests
{
    [Test]
    [Arguments(typeof(ClaimContainerRuntime))]
    [Arguments(typeof(ClaimComposeRuntime))]
    [Arguments(typeof(ClaimOvaRuntime))]
    public async Task Claim_work_preserves_original_pool_without_preassigning_a_node(
        Type messageType)
    {
        await Assert.That(typeof(IRunnerPoolMessage).IsAssignableFrom(messageType))
            .IsTrue();
        await Assert.That(messageType.GetProperty(nameof(IRunnerPoolMessage.RunnerPool)))
            .IsNotNull();
        await Assert.That(messageType.GetProperty(nameof(IRunnerNodeMessage.RunnerId)))
            .IsNull();
    }
}
