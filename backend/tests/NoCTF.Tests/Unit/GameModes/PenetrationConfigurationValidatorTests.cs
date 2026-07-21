using NoCTF.GameModes.Penetration.Configuration;

namespace NoCTF.Tests.Unit.GameModes;

public class PenetrationConfigurationValidatorTests
{
    [Test]
    public async Task Cycles_are_rejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var configuration = new PenetrationChallengeConfiguration(
            1,
            [
                new(a, 1, "A", [b], null),
                new(b, 2, "B", [a], null)
            ]);

        var errors = PenetrationConfigurationValidator.Validate(configuration);

        await Assert.That(errors).Contains("Stage prerequisites contain a cycle.");
    }

    [Test]
    public async Task Branching_without_cycle_is_accepted()
    {
        var root = Guid.NewGuid();
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var configuration = new PenetrationChallengeConfiguration(
            1,
            [
                new(root, 1, "Root", [], null),
                new(left, 2, "Left", [root], null),
                new(right, 3, "Right", [root], null)
            ]);

        await Assert.That(PenetrationConfigurationValidator.Validate(configuration)).IsEmpty();
    }

    [Test]
    public async Task Valid_injection_key_is_accepted()
    {
        var configuration = new PenetrationChallengeConfiguration(
            1,
            [new(Guid.NewGuid(), 1, "Root", [], null, "STAGE_1_FLAG")]);

        await Assert.That(PenetrationConfigurationValidator.Validate(configuration)).IsEmpty();
    }

    [Arguments("1_STAGE_FLAG")]
    [Arguments("STAGE-FLAG")]
    [Arguments("")]
    [Test]
    public async Task Invalid_injection_key_is_rejected(string injectionKey)
    {
        var configuration = new PenetrationChallengeConfiguration(
            1,
            [new(Guid.NewGuid(), 1, "Root", [], null, injectionKey)]);

        await Assert.That(PenetrationConfigurationValidator.Validate(configuration))
            .Contains(item => item.Contains("InjectionKey", StringComparison.Ordinal));
    }
}
