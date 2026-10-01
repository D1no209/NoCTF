using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed record AdminCompetitionChallengeResponse(
    ChallengeResponse Challenge,
    GameModeProtocol Mode,
    CompetitionStatusProtocol CompetitionStatus,
    CompetitionChallengeRulesContract Rules);

public sealed class CompetitionChallengeRulesContract
{
    public required GameModeProtocol Mode { get; set; }
    public CtfCompetitionChallengeRulesContract? Ctf { get; set; }
    public AwdCompetitionChallengeRulesContract? Awd { get; set; }
    public AwdpCompetitionChallengeRulesContract? Awdp { get; set; }
    public KohCompetitionChallengeRulesContract? Koh { get; set; }
}
public sealed class CtfCompetitionChallengeRulesContract
{
    public CtfScoreSettlementModeProtocol? ScoreSettlementMode { get; set; }
    public ScoreCurveContract? ScoreCurve { get; set; }
    public IReadOnlyList<BloodRewardContract>? BloodRewards { get; set; }
    public int? MaxFlagAttempts { get; set; }
    public int? MaxPatchAttempts { get; set; }
    public long? WrongSubmissionPenalty { get; set; }
    public FlagTemplateContract? FlagTemplate { get; set; }
}
public sealed class AwdCompetitionChallengeRulesContract
{
    public AwdAttackRewardModeProtocol? AttackRewardMode { get; set; }
    public long? AttackPoints { get; set; }
    public long? VictimDefensePoolPoints { get; set; }
    public int? CheckerIntervalSeconds { get; set; }
    public long? ServiceHealthyPoints { get; set; }
    public long? ServiceUnhealthyPenalty { get; set; }
    public FlagTemplateContract? FlagTemplate { get; set; }
}
public sealed class AwdpCompetitionChallengeRulesContract
{
    public ScoreCurveContract? BreakScoreCurve { get; set; }
    public ScoreCurveContract? FixScoreCurve { get; set; }
    public int? MaxBreakSubmissions { get; set; }
    public int? MaxFixSubmissions { get; set; }
    public bool? RequireBreakBeforeFix { get; set; }
    public long? FlagWrongPenalty { get; set; }
    public long? ExploitSucceededPenalty { get; set; }
    public long? ServiceAbnormalPenalty { get; set; }
    public EvaluationDispatchModeProtocol? EvaluationDispatchMode { get; set; }
    public FlagTemplateContract? FlagTemplate { get; set; }
}
public sealed class KohCompetitionChallengeRulesContract
{
    public int? PollIntervalSeconds { get; set; }
    public long? ControlPointsPerInterval { get; set; }
}

public static class CompetitionChallengeRulesContractMapper
{
    public static bool HasValidShape(CompetitionChallengeRulesContract? contract)
    {
        if (contract is null || !Enum.IsDefined(contract.Mode))
            return false;
        var count = (contract.Ctf is not null ? 1 : 0)
            + (contract.Awd is not null ? 1 : 0)
            + (contract.Awdp is not null ? 1 : 0)
            + (contract.Koh is not null ? 1 : 0);
        return count == 1 && (contract.Mode switch
        {
            GameModeProtocol.Ctf => contract.Ctf is not null
                && (contract.Ctf.ScoreSettlementMode is null || Enum.IsDefined(contract.Ctf.ScoreSettlementMode.Value))
                && (contract.Ctf.BloodRewards is null
                    || contract.Ctf.BloodRewards.All(item => item is not null))
                && ValidFlagTemplate(contract.Ctf.FlagTemplate),
            GameModeProtocol.Awd => contract.Awd is not null
                && ValidFlagTemplate(contract.Awd.FlagTemplate),
            GameModeProtocol.Awdp => contract.Awdp is not null
                && ValidFlagTemplate(contract.Awdp.FlagTemplate),
            GameModeProtocol.Koh => contract.Koh is not null,
            _ => false
        });
    }

    private static bool ValidFlagTemplate(FlagTemplateContract? value) =>
        value is null or { Header: not null, BodyTemplate: not null };

    public static CompetitionChallengeRules ToDomain(
        Guid competitionChallengeId,
        GameMode expectedMode,
        CompetitionChallengeRulesContract contract)
    {
        if (!HasValidShape(contract))
            throw new ArgumentException("Rules mode and branch must match exactly.", nameof(contract));
        var rules = CreateDomain(competitionChallengeId, contract);
        if (rules.Mode != expectedMode)
            throw new InvalidOperationException(
                $"Rules mode {rules.Mode} does not match competition mode {expectedMode}.");
        return rules;
    }

    public static CompetitionChallengeRulesContract FromDomain(CompetitionChallengeRules value)
    {
        var result = new CompetitionChallengeRulesContract
        {
            Mode = (GameModeProtocol)value.Mode
        };
        var flagTemplate = value.HasFlagTemplate
            ? new FlagTemplateContract(value.FlagTemplate.Header,
                value.FlagTemplate.BodyTemplate, value.FlagTemplate.LeetLiteralText)
            : null;
        switch (value)
        {
            case CtfCompetitionChallengeRules ctf:
                result.Ctf = new CtfCompetitionChallengeRulesContract
                {
                    ScoreSettlementMode = ctf.ScoreSettlementMode is null ? null : (CtfScoreSettlementModeProtocol)ctf.ScoreSettlementMode.Value,
                    ScoreCurve = value.HasScoreCurve ? Curve(value.ScoreCurve) : null,
                    BloodRewards = value.BloodRewards.Count == 0 ? null : value.BloodRewards
                        .OrderBy(item => item.Position)
                        .Select(item => new BloodRewardContract(
                            (BloodRewardPolicyProtocol)item.Policy, item.Value)).ToArray(),
                    MaxFlagAttempts = value.MaxFlagAttempts,
                    MaxPatchAttempts = value.MaxPatchAttempts,
                    WrongSubmissionPenalty = value.WrongSubmissionPenalty,
                    FlagTemplate = flagTemplate
                };
                break;
            case AwdCompetitionChallengeRules:
                result.Awd = new AwdCompetitionChallengeRulesContract
                {
                    AttackRewardMode = value.AttackRewardMode is null
                        ? null : (AwdAttackRewardModeProtocol)value.AttackRewardMode.Value,
                    AttackPoints = value.AttackPoints,
                    VictimDefensePoolPoints = value.VictimDefensePoolPoints,
                    CheckerIntervalSeconds = value.CheckerIntervalSeconds,
                    ServiceHealthyPoints = value.ServiceHealthyPoints,
                    ServiceUnhealthyPenalty = value.ServiceUnhealthyPenalty,
                    FlagTemplate = flagTemplate
                };
                break;
            case AwdpCompetitionChallengeRules:
                result.Awdp = new AwdpCompetitionChallengeRulesContract
                {
                    BreakScoreCurve = value.HasBreakScoreCurve ? Curve(value.BreakScoreCurve) : null,
                    FixScoreCurve = value.HasFixScoreCurve ? Curve(value.FixScoreCurve) : null,
                    MaxBreakSubmissions = value.MaxBreakSubmissions,
                    MaxFixSubmissions = value.MaxFixSubmissions,
                    RequireBreakBeforeFix = value.RequireBreakBeforeFix,
                    FlagWrongPenalty = value.FlagWrongPenalty,
                    ExploitSucceededPenalty = value.ExploitSucceededPenalty,
                    ServiceAbnormalPenalty = value.ServiceAbnormalPenalty,
                    EvaluationDispatchMode = value.EvaluationDispatchMode is null
                        ? null : (EvaluationDispatchModeProtocol)value.EvaluationDispatchMode.Value,
                    FlagTemplate = flagTemplate
                };
                break;
            case KohCompetitionChallengeRules:
                result.Koh = new KohCompetitionChallengeRulesContract
                {
                    PollIntervalSeconds = value.PollIntervalSeconds,
                    ControlPointsPerInterval = value.ControlPointsPerInterval
                };
                break;
            default:
                throw new InvalidOperationException($"Unsupported rules {value.GetType().Name}.");
        }
        return result;
    }

    private static CompetitionChallengeRules CreateDomain(
        Guid competitionChallengeId,
        CompetitionChallengeRulesContract value)
    {
        CompetitionChallengeRules result = value.Mode switch
        {
            GameModeProtocol.Ctf => new CtfCompetitionChallengeRules
            {
                ScoreSettlementMode = value.Ctf!.ScoreSettlementMode is null ? null : (CtfScoreSettlementMode)value.Ctf.ScoreSettlementMode.Value,
                HasScoreCurve = value.Ctf!.ScoreCurve is not null,
                ScoreCurve = value.Ctf.ScoreCurve is { } curve ? Curve(curve) : new(),
                BloodRewards = (value.Ctf.BloodRewards ?? []).Select((reward, position) =>
                    new CompetitionChallengeBloodReward
                    {
                        CompetitionChallengeId = competitionChallengeId,
                        Position = position,
                        Policy = (CompetitionBloodRewardPolicy)reward.Policy,
                        Value = reward.Value
                    }).ToList(),
                MaxFlagAttempts = value.Ctf.MaxFlagAttempts,
                MaxPatchAttempts = value.Ctf.MaxPatchAttempts,
                WrongSubmissionPenalty = value.Ctf.WrongSubmissionPenalty
            },
            GameModeProtocol.Awd => new AwdCompetitionChallengeRules
            {
                AttackRewardMode = value.Awd!.AttackRewardMode is { } rewardMode
                    ? (AwdAttackRewardMode)rewardMode : null,
                AttackPoints = value.Awd.AttackPoints,
                VictimDefensePoolPoints = value.Awd.VictimDefensePoolPoints,
                CheckerIntervalSeconds = value.Awd.CheckerIntervalSeconds,
                ServiceHealthyPoints = value.Awd.ServiceHealthyPoints,
                ServiceUnhealthyPenalty = value.Awd.ServiceUnhealthyPenalty
            },
            GameModeProtocol.Awdp => new AwdpCompetitionChallengeRules
            {
                HasBreakScoreCurve = value.Awdp!.BreakScoreCurve is not null,
                BreakScoreCurve = value.Awdp.BreakScoreCurve is { } breakCurve
                    ? Curve(breakCurve) : new(),
                HasFixScoreCurve = value.Awdp.FixScoreCurve is not null,
                FixScoreCurve = value.Awdp.FixScoreCurve is { } fixCurve
                    ? Curve(fixCurve) : new(),
                MaxBreakSubmissions = value.Awdp.MaxBreakSubmissions,
                MaxFixSubmissions = value.Awdp.MaxFixSubmissions,
                RequireBreakBeforeFix = value.Awdp.RequireBreakBeforeFix,
                FlagWrongPenalty = value.Awdp.FlagWrongPenalty,
                ExploitSucceededPenalty = value.Awdp.ExploitSucceededPenalty,
                ServiceAbnormalPenalty = value.Awdp.ServiceAbnormalPenalty,
                EvaluationDispatchMode = value.Awdp.EvaluationDispatchMode is { } dispatchMode
                    ? (CompetitionEvaluationDispatchMode)dispatchMode : null
            },
            GameModeProtocol.Koh => new KohCompetitionChallengeRules
            {
                PollIntervalSeconds = value.Koh!.PollIntervalSeconds,
                ControlPointsPerInterval = value.Koh.ControlPointsPerInterval
            },
            _ => throw new InvalidOperationException("Unsupported rules mode.")
        };
        result.CompetitionChallengeId = competitionChallengeId;
        var flagTemplate = value.Mode switch
        {
            GameModeProtocol.Ctf => value.Ctf!.FlagTemplate,
            GameModeProtocol.Awd => value.Awd!.FlagTemplate,
            GameModeProtocol.Awdp => value.Awdp!.FlagTemplate,
            _ => null
        };
        result.HasFlagTemplate = flagTemplate is not null;
        if (flagTemplate is not null)
        {
            result.FlagTemplate = new FlagTemplateValue
            {
                Header = flagTemplate.Header,
                BodyTemplate = flagTemplate.BodyTemplate,
                LeetLiteralText = flagTemplate.LeetLiteralText
            };
        }
        return result;
    }

    private static ScoreCurveContract Curve(ScoreCurveValue value) => new(
        value.InitialPoints, value.MinimumPoints, value.DecayTeamCount,
        (ScoreDecayModeProtocol)value.DecayMode, value.CustomExpression);
    private static ScoreCurveValue Curve(ScoreCurveContract value) => new()
    {
        InitialPoints = value.InitialPoints,
        MinimumPoints = value.MinimumPoints,
        DecayTeamCount = value.DecayTeamCount,
        DecayMode = (PersistedScoreDecayMode)value.DecayMode,
        CustomExpression = value.CustomExpression
    };
}

public sealed class GetAdminChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    GetChallengeConfiguration getConfiguration,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetAdminChallengeRequest,
        Results<Ok<AdminCompetitionChallengeResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionChallenge"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition challenge.";
            summary.Description = "Returns management details for published or unpublished competition challenges.";
        });
    }

    public override async Task<Results<Ok<AdminCompetitionChallengeResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetAdminChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var item = await get.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            includeUnpublished: true,
            request.IncludeDeleted,
            ct);

        var configuration = await getConfiguration.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            ct);
        return item is null || configuration is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new AdminCompetitionChallengeResponse(
                ChallengeMapper.ToResponse(item),
                CompetitionProtocolMapper.ToProtocol(configuration.Mode),
                CompetitionProtocolMapper.ToProtocol(configuration.CompetitionStatus),
                CompetitionChallengeRulesContractMapper.FromDomain(configuration.Rules)));
    }
}
