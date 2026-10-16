using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeForceTerminationPolicyTests
{
    [Test]
    [Arguments(RuntimeState.Provisioning, true)]
    [Arguments(RuntimeState.Stopping, true)]
    [Arguments(RuntimeState.Failed, true)]
    [Arguments(RuntimeState.Queued, false)]
    [Arguments(RuntimeState.Running, false)]
    [Arguments(RuntimeState.Stopped, false)]
    public async Task Explicit_cleanup_is_available_immediately_only_for_owned_recovery_states(
        RuntimeState state, bool allowed)
    {
        var now = DateTimeOffset.UtcNow;
        var runtime = new RuntimeInstanceView(Guid.NewGuid(), null, null, null, null,
            RuntimePurpose.TemplateTest, RuntimeKind.Container, RuntimeProvider.Docker,
            state, null, now, null, null, null, "runner");
        await Assert.That(RuntimeForceTerminationPolicy.CanForceTerminate(runtime, now)).IsEqualTo(allowed);
        await Assert.That(RuntimeForceTerminationPolicy.CanForceTerminate(runtime with { RunnerId = null }, now)).IsFalse();
    }
}
