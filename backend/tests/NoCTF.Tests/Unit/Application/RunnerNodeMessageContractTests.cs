using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerNodeMessageContractTests
{
    [Test]
    [Arguments(typeof(ProvisionContainerRuntime))]
    [Arguments(typeof(ProvisionComposeRuntime))]
    [Arguments(typeof(ProvisionOvaRuntime))]
    [Arguments(typeof(StopContainerRuntime))]
    [Arguments(typeof(StopComposeRuntime))]
    [Arguments(typeof(StopOvaRuntime))]
    [Arguments(typeof(ForceTerminateRuntime))]
    [Arguments(typeof(ReconcileRuntimeResources))]
    [Arguments(typeof(InjectAwdFlag))]
    [Arguments(typeof(RunAwdChecker))]
    [Arguments(typeof(RunAwdpFixVerification))]
    [Arguments(typeof(RunPatchVerification))]
    [Arguments(typeof(CleanupAwdpTarget))]
    public async Task Node_work_requires_a_concrete_runner_assignment(Type messageType)
    {
        await Assert.That(typeof(IRunnerNodeMessage).IsAssignableFrom(messageType)).IsTrue();
        await Assert.That(messageType.GetProperty(nameof(IRunnerNodeMessage.RunnerId))).IsNotNull();
        await Assert.That(messageType.GetProperty("RunnerPool")).IsNull();
    }

    [Test]
    [Arguments(typeof(StopContainerRuntime))]
    [Arguments(typeof(StopComposeRuntime))]
    [Arguments(typeof(StopOvaRuntime))]
    [Arguments(typeof(ForceTerminateRuntime))]
    public async Task Stop_work_does_not_persist_the_provider_receipt(Type messageType)
    {
        await Assert.That(messageType.GetProperty("ProviderReceiptJson")).IsNull();
        await Assert.That(messageType.GetProperty("Generation")).IsNull();
    }
}
