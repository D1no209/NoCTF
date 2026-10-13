using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdEffectiveRulesTests
{
    [Test, Arguments(1, false), Arguments(4, false), Arguments(5, true), Arguments(7, true)]
    public async Task Active_generation_interval_matches_scheduler_capacity(int interval, bool supported)
    {
        var defaults = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid());
        defaults.AwdOptions = defaults.AwdOptions with { GenerationPolicy = AwdGenerationPolicy.ActiveInterval, GenerationInterval = interval };
        await Assert.That(AwdEffectiveRules.ValidateConfiguration(defaults, new()).Count == 0).IsEqualTo(supported);
        defaults.AwdOptions = defaults.AwdOptions with { GenerationPolicy = AwdGenerationPolicy.EveryNRounds };
        await Assert.That(AwdEffectiveRules.ValidateConfiguration(defaults, new()).Count).IsEqualTo(0);
    }
    [Test]
    [Arguments(AwdSlaSettlement.FinalScheduledCheck, true)]
    [Arguments(AwdSlaSettlement.SampleAverage, true)]
    [Arguments(AwdSlaSettlement.TimeWeighted, false)]
    public async Task Configuration_only_accepts_available_settlement_modes(AwdSlaSettlement settlement, bool supported)
    {
        var defaults = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid());
        defaults.AwdOptions = defaults.AwdOptions with { SlaSettlement = settlement };
        await Assert.That(AwdEffectiveRules.ValidateConfiguration(defaults, new()).Count == 0).IsEqualTo(supported);
        defaults.AwdOptions = defaults.AwdOptions with { SlaSettlement = AwdSlaSettlement.FinalScheduledCheck };
        await Assert.That(AwdEffectiveRules.ValidateConfiguration(defaults,
            new() { AwdOverrides = new() { SlaSettlement = settlement } }).Count == 0).IsEqualTo(supported);
    }

    [Test]
    [Arguments(-1_000_001L, false)]
    [Arguments(-1_000_000L, true)]
    [Arguments(0L, true)]
    [Arguments(1_000_000L, true)]
    [Arguments(1_000_001L, false)]
    public async Task Total_score_floor_has_explicit_signed_bounds(long floor, bool valid)
    {
        var rules = new AwdEffectiveRules(AwdAttackRewardMode.FixedPerAttack, 50, 100, 100, 30,
            new() { MinimumTotalScore = floor });
        await Assert.That(rules.Validate().Count == 0).IsEqualTo(valid);
    }

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
