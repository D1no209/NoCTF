using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeReplacementCleanupFailureTests
{
    [Test]
    public async Task Reset_is_modeled_as_a_distinct_runtime_identity()
    {
        var originalRuntimeId = Guid.CreateVersion7();
        var replacementRuntimeId = Guid.CreateVersion7();

        var stopOriginal = new StopRuntime(originalRuntimeId);
        var dispatchReplacement = new DispatchRuntime(replacementRuntimeId);

        await Assert.That(stopOriginal.RuntimeInstanceId).IsNotEqualTo(
            dispatchReplacement.RuntimeInstanceId);
    }

    [Test]
    public async Task Cleanup_callbacks_identify_only_the_runtime_and_runner()
    {
        var runtimeId = Guid.CreateVersion7();
        const string runnerId = "runner-a";
        var stopped = new RuntimeStopped(runtimeId, runnerId);
        var failed = new RuntimeStopFailed(runtimeId, runnerId, RuntimeFailureCode.CleanupFailed);

        await Assert.That(stopped.RuntimeInstanceId).IsEqualTo(runtimeId);
        await Assert.That(stopped.RunnerId).IsEqualTo(runnerId);
        await Assert.That(failed.RuntimeInstanceId).IsEqualTo(runtimeId);
        await Assert.That(failed.RunnerId).IsEqualTo(runnerId);
    }
}
