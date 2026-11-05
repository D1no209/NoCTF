using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Messaging;
using NoCTF.Hosting.Messaging;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class CapabilityExecutionTimeoutPolicyTests
{
    [Test]
    public async Task Policy_only_assigns_the_dedicated_Fix_handler_timeout()
    {
        await Assert.That(CapabilityExecutionTimeoutPolicy.TimeoutFor(
                typeof(RunFixExecution)))
            .IsEqualTo(FixExecutionBudget.HandlerExecutionTimeoutSeconds);
        await Assert.That(CapabilityExecutionTimeoutPolicy.TimeoutFor(
                typeof(StartFixExecution)))
            .IsNull();
    }
}
