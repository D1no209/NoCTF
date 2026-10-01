using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.API;

public sealed class CtfScoreSettlementContractTests
{
    [Test]
    public async Task Custom_at_solve_configuration_validates_the_next_quote_past_the_last_participant()
    {
        var curve = new NoCTF.GameModes.Scoring.ScoreCurveConfiguration(500, 100, 5,
            NoCTF.GameModes.Scoring.ScoreDecayMode.Custom, "initialPoints / (eligibleTeamCount + 1 - solveCount)");
        var configuration = new NoCTF.GameModes.Ctf.Configuration.CtfConfiguration(curve, []);
        await Assert.That(NoCTF.GameModes.Ctf.Configuration.CtfConfigurationValidator.Validate(configuration, 2)).IsEmpty();
        await Assert.That(NoCTF.GameModes.Ctf.Configuration.CtfConfigurationValidator.Validate(
            configuration with { ScoreSettlementMode = CtfScoreSettlementMode.AtSolve }, 2)).IsNotEmpty();
    }

    [Test, Arguments(CtfScoreSettlementMode.DynamicRecalculation), Arguments(CtfScoreSettlementMode.AtSolve)]
    public async Task Competition_and_challenge_contracts_round_trip_the_bounded_mode(CtfScoreSettlementMode mode)
    {
        var competition = CompetitionModeConfigurationContractMapper.FromDomain(new CtfCompetitionModeConfiguration { ScoreSettlementMode = mode });
        var read = CompetitionModeConfigurationContractMapper.ToDomain(Guid.NewGuid(), GameMode.Ctf, competition);
        await Assert.That(((CtfCompetitionModeConfiguration)read).ScoreSettlementMode).IsEqualTo(mode);
        var rule = CompetitionChallengeRulesContractMapper.FromDomain(new CtfCompetitionChallengeRules { ScoreSettlementMode = mode });
        var parsed = CompetitionChallengeRulesContractMapper.ToDomain(Guid.NewGuid(), GameMode.Ctf, rule);
        await Assert.That(((CtfCompetitionChallengeRules)parsed).ScoreSettlementMode).IsEqualTo(mode);
    }

    [Test]
    public async Task Missing_competition_mode_keeps_the_existing_default_and_null_rule_inherits()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<CtfCompetitionModeConfigurationContract>(
            """{"defaultScoreCurve":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":"Linear"},"bloodRewards":[],"wrongSubmissionPenalty":0}""", options)!;
        await Assert.That(legacy.ScoreSettlementMode).IsEqualTo(CtfScoreSettlementModeProtocol.DynamicRecalculation);
        var contract = CompetitionChallengeRulesContractMapper.FromDomain(new CtfCompetitionChallengeRules());
        await Assert.That(contract.Ctf!.ScoreSettlementMode).IsNull();
        await Assert.That(((CtfCompetitionChallengeRules)CompetitionChallengeRulesContractMapper.ToDomain(Guid.NewGuid(), GameMode.Ctf, contract)).ScoreSettlementMode).IsNull();
    }

    [Test]
    public async Task Undefined_modes_are_rejected_by_shape_validation_and_json_binding()
    {
        var contract = CompetitionModeConfigurationContractMapper.FromDomain(new CtfCompetitionModeConfiguration());
        contract.Ctf = contract.Ctf! with { ScoreSettlementMode = (CtfScoreSettlementModeProtocol)99 };
        await Assert.That(CompetitionModeConfigurationContractMapper.HasValidShape(contract)).IsFalse();
        var rule = CompetitionChallengeRulesContractMapper.FromDomain(new CtfCompetitionChallengeRules());
        rule.Ctf!.ScoreSettlementMode = (CtfScoreSettlementModeProtocol)99;
        await Assert.That(CompetitionChallengeRulesContractMapper.HasValidShape(rule)).IsFalse();
        await Assert.That(() => JsonSerializer.Deserialize<CtfScoreSettlementModeProtocol>("\"Unknown\"")).Throws<JsonException>();
    }
}
