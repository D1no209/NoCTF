using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerNodeMessageContractTests
{
    [Test]
    [Arguments(typeof(ProvisionContainerRuntime))]
    [Arguments(typeof(StopContainerRuntime))]
    [Arguments(typeof(InjectAwdFlag))]
    [Arguments(typeof(RunAwdChecker))]
    [Arguments(typeof(RunAwdpFixVerification))]
    [Arguments(typeof(CleanupAwdpTarget))]
    public async Task Node_work_preserves_original_assignment(Type messageType)
    {
        await Assert.That(typeof(IRunnerNodeMessage).IsAssignableFrom(messageType)).IsTrue();
        await Assert.That(messageType.GetProperty(nameof(IRunnerNodeMessage.RunnerPool))).IsNotNull();
        await Assert.That(messageType.GetProperty(nameof(IRunnerNodeMessage.RunnerId))).IsNotNull();
    }

    [Test]
    public async Task Stop_work_does_not_persist_the_provider_receipt()
    {
        await Assert.That(typeof(StopContainerRuntime).GetProperty("ProviderReceiptJson")).IsNull();
    }
}
