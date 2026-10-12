using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdEffectiveRulesTests
{
    [Test]
    public async Task PresetReplacesItsStructuralFormulaAndNeverInheritsOldMultiplier()
    {
        var defaults = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid());
        defaults.AwdOptions = new() { Preset = AwdScoringPreset.SharedPool, Composition = AwdScoreComposition.PositiveSlaMultiplier };
        var shared = AwdEffectiveRules.Resolve(defaults, new());
        await Assert.That(shared.Options.Composition).IsEqualTo(AwdScoreComposition.Additive);
        await Assert.That(shared.Options.DefenseMode).IsEqualTo(AwdDefenseMode.SharedPool);
        defaults.AwdOptions = shared.Options with { Preset = AwdScoringPreset.Fixed };
        var fixedRules = AwdEffectiveRules.Resolve(defaults, new());
        await Assert.That(fixedRules.AttackMode).IsEqualTo(AwdAttackRewardMode.FixedPerAttack);
        await Assert.That(fixedRules.Options.DefenseMode).IsEqualTo(AwdDefenseMode.FirstLeak);
    }

    [Test]
    public async Task ZeroOverridesInheritanceAndUnsupportedPrecisionIsRejected()
    {
        var defaults = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid());
        var resolved = AwdEffectiveRules.Resolve(defaults, new()
        {
            AwdOverrides = new() { RecoveringRatio = 0, MumbleRatio = .3333333m }
        });
        await Assert.That(resolved.Options.RecoveringRatio).IsEqualTo(0m);
        await Assert.That(resolved.Validate().Any(error => error.Contains("six fractional", StringComparison.Ordinal))).IsTrue();
    }
}
