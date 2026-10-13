using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.API;

public sealed class AwdRuleConfigurationProtocolTests
{
    [Test]
    public async Task Policy_enums_reject_numeric_and_unknown_values_and_preserve_explicit_zero()
    {
        await Assert.That(() => JsonSerializer.Deserialize<AwdRuleOptionsContract>("{\"Preset\":3}"))
            .Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Deserialize<AwdRuleOptionsContract>("{\"PenaltyMode\":\"invented\"}"))
            .Throws<JsonException>();
        var policy = JsonSerializer.Deserialize<AwdRuleOverridesContract>(
            "{\"RecoveringRatio\":0,\"RetryCount\":0,\"SlaSettlement\":\"SampleAverage\"}")!.ToDomain();
        var resolved = policy.Resolve(new() { RecoveringRatio = .75m, RetryCount = 5, FlagLifetime = 8 });
        await Assert.That(resolved.RecoveringRatio).IsEqualTo(0m);
        await Assert.That(resolved.RetryCount).IsEqualTo(0);
        await Assert.That(resolved.FlagLifetime).IsEqualTo(8);
        await Assert.That(resolved.SlaSettlement).IsEqualTo(AwdSlaSettlement.SampleAverage);
    }

    [Test]
    public async Task Scoring_policy_cannot_change_issuance_or_execution_parameters()
    {
        var issued = new AwdRoundRules { Id = Guid.NewGuid(), RoundId = Guid.NewGuid(), Revision = 3,
            FlagBodyTemplate = "[SECURE:20]", CheckerIntervalSeconds = 42, EligibleTeamCount = 7,
            Options = new() { FlagLifetime = 8, HistoricalLifetime = 9, LinkHistoricalWindow = false,
                FinalCheckBudgetSeconds = 19, RetryCount = 4, TotalRounds = 17, MinimumTotalScore = -900 },
            Stores = [new() { StoreId = Guid.NewGuid(), Weight = 2, FlagsPerIssue = 3 }],
            Teams = [new() { TeamId = Guid.NewGuid(), SettledRank = 5 }] };
        var policy = new AwdScoringPolicy { AttackPoints = 80, RecoveringRatio = 0, SlaSettlement = AwdSlaSettlement.SampleAverage };
        var wire = AwdScoringPolicyContract.From(policy);
        await Assert.That(JsonSerializer.Deserialize<AwdScoringPolicyContract>(JsonSerializer.Serialize(wire))!.ToDomain()).IsEqualTo(policy);
        await Assert.That(() => JsonSerializer.Deserialize<AwdScoringPolicyContract>("{\"AttackRewardMode\":3}")).Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Deserialize<AwdScoringPolicyContract>("{\"FlagLifetime\":30}")).Throws<JsonException>();
        var projected = policy.Apply(issued);
        await Assert.That(projected.Id).IsEqualTo(issued.Id);
        await Assert.That(projected.Options.FlagLifetime).IsEqualTo(8);
        await Assert.That(projected.Options.HistoricalLifetime).IsEqualTo(9);
        await Assert.That(projected.Options.FinalCheckBudgetSeconds).IsEqualTo(19);
        await Assert.That(projected.Options.RetryCount).IsEqualTo(4);
        await Assert.That(projected.Options.TotalRounds).IsEqualTo(17);
        await Assert.That(projected.Options.MinimumTotalScore).IsEqualTo(-900L);
        await Assert.That(projected.CheckerIntervalSeconds).IsEqualTo(42);
        await Assert.That(projected.Stores.Single().FlagsPerIssue).IsEqualTo(3);
        await Assert.That(projected.Teams.Single().SettledRank).IsEqualTo(5);
        await Assert.That(projected.Options.RecoveringRatio).IsEqualTo(0m);
        await Assert.That(issued.Options.RecoveringRatio).IsEqualTo(.5m);
        await Assert.That(projected.Options.SlaSettlement).IsEqualTo(AwdSlaSettlement.SampleAverage);
    }

    [Test]
    public async Task Competition_validation_rejects_invalid_advanced_policy_before_any_round_exists()
    {
        var configuration = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid());
        var validator = new GameModeCompetitionConfigurationValidator();
        configuration.AwdOptions = new() { Preset = AwdScoringPreset.Custom, FlagLifetime = 0 };
        await Assert.That(validator.Validate(GameMode.Awd, configuration, 0, []).Count).IsGreaterThan(0);
        configuration.AwdOptions = new() { Preset = (AwdScoringPreset)300 };
        await Assert.That(validator.Validate(GameMode.Awd, configuration, 0, []).Count).IsGreaterThan(0);
        configuration.AwdOptions = new() { Preset = AwdScoringPreset.SharedPool };
        await Assert.That(validator.Validate(GameMode.Awd, configuration, 0, []).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Store_contract_keeps_delivery_and_historical_semantics()
    {
        var id = Guid.NewGuid();
        var source = new NoCTF.Domain.Challenges.AwdChallengeDefinition
        {
            FlagStores = [new() { Id = id, Name = "orders", Position = 0,
                DeliveryMode = AwdFlagDeliveryMode.BusinessChecker, PreservesHistoricalFlags = false }]
        };
        var contract = ChallengeDefinitionContractMapper.FromDomain(source);
        var json = JsonSerializer.Serialize(contract);
        var restored = (NoCTF.Domain.Challenges.AwdChallengeDefinition)ChallengeDefinitionContractMapper.ToDomain(
            Guid.NewGuid(), GameMode.Awd, JsonSerializer.Deserialize<ChallengeDefinitionContract>(json)!);
        await Assert.That(restored.FlagStores.Single().Id).IsEqualTo(id);
        await Assert.That(restored.FlagStores.Single().DeliveryMode).IsEqualTo(AwdFlagDeliveryMode.BusinessChecker);
        await Assert.That(restored.FlagStores.Single().PreservesHistoricalFlags).IsFalse();
        restored.FlagStores.Single().PreservesHistoricalFlags = true;
        await Assert.That(NoCTF.Domain.Challenges.ChallengeDefinitionStructuralComparer.Equals(source, restored)).IsFalse();
    }
}
