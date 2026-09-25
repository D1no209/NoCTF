using NoCTF.Application.Messaging;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdFlagInjectionConfigurationTests
{
    [Test]
    public async Task Validate_FlagInjectionTimeoutOutsideExecutionBudget_ReturnsExplicitError()
    {
        var configuration = new AwdChallengeConfiguration(
            FlagInjection: new(
                "set-flag ${FLAG}",
                AwdFlagInjectionExecutionBudget.MaximumCommandTimeoutSeconds + 1));

        var errors = AwdConfigurationValidator.Validate(configuration);

        await Assert.That(errors).Contains(
            $"FlagInjection.TimeoutSeconds must be between 1 and {AwdFlagInjectionExecutionBudget.MaximumCommandTimeoutSeconds}.");
    }
}
