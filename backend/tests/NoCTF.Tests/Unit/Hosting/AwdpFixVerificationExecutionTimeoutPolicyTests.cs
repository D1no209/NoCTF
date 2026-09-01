using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Messaging;
using NoCTF.Hosting.Messaging;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class AwdpFixVerificationExecutionTimeoutPolicyTests
{
    [Test]
    public async Task Policy_only_assigns_the_dedicated_Fix_handler_timeout()
    {
        await Assert.That(AwdpFixVerificationExecutionTimeoutPolicy.TimeoutFor(
                typeof(RunAwdpFixVerification)))
            .IsEqualTo(AwdpFixExecutionBudget.HandlerExecutionTimeoutSeconds);
        await Assert.That(AwdpFixVerificationExecutionTimeoutPolicy.TimeoutFor(
                typeof(StartAwdpFixVerification)))
            .IsNull();
    }
}
