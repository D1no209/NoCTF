using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Messaging;
using NoCTF.Hosting.Messaging;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class AwdFlagInjectionExecutionTimeoutPolicyTests
{
    [Test]
    public async Task Policy_only_assigns_the_dedicated_Awd_injection_handler_timeout()
    {
        var expected = AwdFlagInjectionExecutionBudget.HandlerExecutionTimeoutSeconds;

        await Assert.That(AwdFlagInjectionExecutionTimeoutPolicy.TimeoutFor(
                typeof(InjectAwdFlag)))
            .IsEqualTo(expected);
        await Assert.That(AwdFlagInjectionExecutionTimeoutPolicy.TimeoutFor(
                typeof(InjectChallengeTestFlag)))
            .IsEqualTo(expected);
        await Assert.That(AwdFlagInjectionExecutionTimeoutPolicy.TimeoutFor(
                typeof(AdvanceAwdRound)))
            .IsNull();
        await Assert.That(expected)
            .IsGreaterThan(AwdFlagInjectionExecutionBudget.MaximumCommandTimeoutSeconds);
    }
}
