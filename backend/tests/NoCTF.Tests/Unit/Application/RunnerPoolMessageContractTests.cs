using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerPoolMessageContractTests
{
    [Test]
    public async Task Claim_work_preserves_original_pool_without_preassigning_a_node()
    {
        await Assert.That(typeof(IRunnerPoolMessage).IsAssignableFrom(typeof(ClaimContainerRuntime)))
            .IsTrue();
        await Assert.That(typeof(ClaimContainerRuntime).GetProperty(nameof(IRunnerPoolMessage.RunnerPool)))
            .IsNotNull();
        await Assert.That(typeof(ClaimContainerRuntime).GetProperty(nameof(IRunnerNodeMessage.RunnerId)))
            .IsNull();
    }
}
