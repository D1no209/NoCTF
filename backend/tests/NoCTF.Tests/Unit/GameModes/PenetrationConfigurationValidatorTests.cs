using NoCTF.GameModes.Penetration.Configuration;
using NoCTF.GameModes.Penetration;
using System.Text.Json;

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

    [Test]
    public async Task Stage_progress_DetectsAllStagesCompleted()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new PenetrationChallengeConfiguration(
            1,
            [new(first, 1, "First", [], null), new(second, 2, "Second", [first], null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var catalog = new PenetrationStageConfigurationCatalog();

        var partial = catalog.GetProgress(json, new HashSet<Guid> { first });
        var complete = catalog.GetProgress(json, new HashSet<Guid> { first, second });

        await Assert.That(partial.AllCompleted).IsFalse();
        await Assert.That(partial.RemainingStageIds).IsEquivalentTo([second]);
        await Assert.That(complete.AllCompleted).IsTrue();
        await Assert.That(complete.RemainingStageIds).IsEmpty();
    }
}
