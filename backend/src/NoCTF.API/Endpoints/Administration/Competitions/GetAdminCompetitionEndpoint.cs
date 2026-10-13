using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.API.Endpoints.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Authentication.Sso;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.LiveSolo;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<ScoreDecayModeProtocol>))]
public enum ScoreDecayModeProtocol { Fixed, Linear, Quadratic, Exponential, Logarithmic, Custom }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CtfScoreSettlementModeProtocol>))]
public enum CtfScoreSettlementModeProtocol { DynamicRecalculation, AtSolve }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<BloodRewardPolicyProtocol>))]
public enum BloodRewardPolicyProtocol { FixedPoints, InitialPointsPercentage, SolveTimePointsPercentage, CurrentPointsPercentage }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdAttackRewardModeProtocol>))]
public enum AwdAttackRewardModeProtocol { FixedPerAttack, SplitVictimDefensePool, Scarcity, TargetRank }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<EvaluationDispatchModeProtocol>))]
public enum EvaluationDispatchModeProtocol { Automatic, Manual }

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdLifetimeUnitProtocol>))]
public enum AwdLifetimeUnitProtocol { Rounds, ActiveSeconds }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdGenerationPolicyProtocol>))]
public enum AwdGenerationPolicyProtocol { EveryRound, EveryNRounds, ActiveInterval }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdScoringPresetProtocol>))]
public enum AwdScoringPresetProtocol { Fixed, SharedPool, Scarcity, TargetRank, SlaMultiplier, Custom }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdDefenseModeProtocol>))]
public enum AwdDefenseModeProtocol { FirstLeak, PerCapture, SharedPool }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdSlaBasisProtocol>))]
public enum AwdSlaBasisProtocol { StatusRatio, ReadableFlags }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdSlaSettlementProtocol>))]
public enum AwdSlaSettlementProtocol { FinalScheduledCheck, SampleAverage, TimeWeighted }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdPenaltyModeProtocol>))]
public enum AwdPenaltyModeProtocol { SlaRewardOnly, Fixed, Proportional, Combined }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdScoreCompositionProtocol>))]
public enum AwdScoreCompositionProtocol { Additive, Weighted, PositiveSlaMultiplier }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AwdAttackSettlementProtocol>))]
public enum AwdAttackSettlementProtocol { Immediate, RoundEnd, FlagExpiry }

public sealed record AwdRuleOptionsContract
{
    public AwdLifetimeUnitProtocol FlagLifetimeUnit { get; init; }
    public int FlagLifetime { get; init; } = 5;
    public bool LinkHistoricalWindow { get; init; } = true;
    public AwdLifetimeUnitProtocol HistoricalLifetimeUnit { get; init; }
    public int HistoricalLifetime { get; init; } = 5;
    public AwdGenerationPolicyProtocol GenerationPolicy { get; init; }
    public int GenerationInterval { get; init; } = 1;
    public int FlagsPerStore { get; init; } = 1;
    public int RoundGapSeconds { get; init; }
    public int? TotalRounds { get; init; }
    public int SubmissionTailSeconds { get; init; }
    public int FinalCheckBudgetSeconds { get; init; } = 30;
    public int RetryCount { get; init; } = 2;
    public int RetryIntervalSeconds { get; init; } = 5;
    public int CallbackGraceSeconds { get; init; } = 30;
    public AwdSlaSettlementProtocol SlaSettlement { get; init; }
    public AwdScoringPresetProtocol Preset { get; init; }
    public AwdDefenseModeProtocol DefenseMode { get; init; }
    public AwdSlaBasisProtocol SlaBasis { get; init; }
    public decimal OkRatio { get; init; } = 1m;
    public decimal RecoveringRatio { get; init; } = 0.5m;
    public decimal MumbleRatio { get; init; }
    public decimal OfflineRatio { get; init; }
    public AwdPenaltyModeProtocol PenaltyMode { get; init; }
    public long MumblePenalty { get; init; }
    public long OfflinePenalty { get; init; }
    public long RecoveringPenalty { get; init; }
    public decimal MumblePenaltyRatio { get; init; }
    public decimal OfflinePenaltyRatio { get; init; }
    public decimal RecoveringPenaltyRatio { get; init; }
    public decimal ConsecutiveFailureMultiplier { get; init; } = 1m;
    public long? MaximumRoundPenalty { get; init; }
    public long? MinimumTotalScore { get; init; }
    public long ConfidentialityPoints { get; init; }
    public decimal MinimumAttackRatio { get; init; } = 0.1m;
    public decimal ScarcityAlpha { get; init; } = 0.705m;
    public decimal MaximumRankMultiplier { get; init; } = 2m;
    public AwdScoreCompositionProtocol Composition { get; init; }
    public decimal AttackWeight { get; init; } = 1m;
    public decimal DefenseWeight { get; init; } = 1m;
    public decimal SlaWeight { get; init; } = 1m;
    public AwdAttackSettlementProtocol AttackSettlement { get; init; }

    public AwdRuleOptions ToDomain() => new()
    {
        FlagLifetimeUnit = (AwdLifetimeUnit)FlagLifetimeUnit,
        FlagLifetime = FlagLifetime,
        LinkHistoricalWindow = LinkHistoricalWindow,
        HistoricalLifetimeUnit = (AwdLifetimeUnit)HistoricalLifetimeUnit,
        HistoricalLifetime = HistoricalLifetime,
        GenerationPolicy = (AwdGenerationPolicy)GenerationPolicy,
        GenerationInterval = GenerationInterval,
        FlagsPerStore = FlagsPerStore,
        RoundGapSeconds = RoundGapSeconds,
        TotalRounds = TotalRounds,
        SubmissionTailSeconds = SubmissionTailSeconds,
        FinalCheckBudgetSeconds = FinalCheckBudgetSeconds,
        RetryCount = RetryCount,
        RetryIntervalSeconds = RetryIntervalSeconds,
        CallbackGraceSeconds = CallbackGraceSeconds,
        SlaSettlement = (AwdSlaSettlement)SlaSettlement,
        Preset = (AwdScoringPreset)Preset,
        DefenseMode = (AwdDefenseMode)DefenseMode,
        SlaBasis = (AwdSlaBasis)SlaBasis,
        OkRatio = OkRatio,
        RecoveringRatio = RecoveringRatio,
        MumbleRatio = MumbleRatio,
        OfflineRatio = OfflineRatio,
        PenaltyMode = (AwdPenaltyMode)PenaltyMode,
        MumblePenalty = MumblePenalty,
        OfflinePenalty = OfflinePenalty,
        RecoveringPenalty = RecoveringPenalty,
        MumblePenaltyRatio = MumblePenaltyRatio,
        OfflinePenaltyRatio = OfflinePenaltyRatio,
        RecoveringPenaltyRatio = RecoveringPenaltyRatio,
        ConsecutiveFailureMultiplier = ConsecutiveFailureMultiplier,
        MaximumRoundPenalty = MaximumRoundPenalty,
        MinimumTotalScore = MinimumTotalScore,
        ConfidentialityPoints = ConfidentialityPoints,
        MinimumAttackRatio = MinimumAttackRatio,
        ScarcityAlpha = ScarcityAlpha,
        MaximumRankMultiplier = MaximumRankMultiplier,
        Composition = (AwdScoreComposition)Composition,
        AttackWeight = AttackWeight,
        DefenseWeight = DefenseWeight,
        SlaWeight = SlaWeight,
        AttackSettlement = (AwdAttackSettlement)AttackSettlement,
    };

    public static AwdRuleOptionsContract FromDomain(AwdRuleOptions value) => new()
    {
        FlagLifetimeUnit = (AwdLifetimeUnitProtocol)value.FlagLifetimeUnit,
        FlagLifetime = value.FlagLifetime,
        LinkHistoricalWindow = value.LinkHistoricalWindow,
        HistoricalLifetimeUnit = (AwdLifetimeUnitProtocol)value.HistoricalLifetimeUnit,
        HistoricalLifetime = value.HistoricalLifetime,
        GenerationPolicy = (AwdGenerationPolicyProtocol)value.GenerationPolicy,
        GenerationInterval = value.GenerationInterval,
        FlagsPerStore = value.FlagsPerStore,
        RoundGapSeconds = value.RoundGapSeconds,
        TotalRounds = value.TotalRounds,
        SubmissionTailSeconds = value.SubmissionTailSeconds,
        FinalCheckBudgetSeconds = value.FinalCheckBudgetSeconds,
        RetryCount = value.RetryCount,
        RetryIntervalSeconds = value.RetryIntervalSeconds,
        CallbackGraceSeconds = value.CallbackGraceSeconds,
        SlaSettlement = (AwdSlaSettlementProtocol)value.SlaSettlement,
        Preset = (AwdScoringPresetProtocol)value.Preset,
        DefenseMode = (AwdDefenseModeProtocol)value.DefenseMode,
        SlaBasis = (AwdSlaBasisProtocol)value.SlaBasis,
        OkRatio = value.OkRatio,
        RecoveringRatio = value.RecoveringRatio,
        MumbleRatio = value.MumbleRatio,
        OfflineRatio = value.OfflineRatio,
        PenaltyMode = (AwdPenaltyModeProtocol)value.PenaltyMode,
        MumblePenalty = value.MumblePenalty,
        OfflinePenalty = value.OfflinePenalty,
        RecoveringPenalty = value.RecoveringPenalty,
        MumblePenaltyRatio = value.MumblePenaltyRatio,
        OfflinePenaltyRatio = value.OfflinePenaltyRatio,
        RecoveringPenaltyRatio = value.RecoveringPenaltyRatio,
        ConsecutiveFailureMultiplier = value.ConsecutiveFailureMultiplier,
        MaximumRoundPenalty = value.MaximumRoundPenalty,
        MinimumTotalScore = value.MinimumTotalScore,
        ConfidentialityPoints = value.ConfidentialityPoints,
        MinimumAttackRatio = value.MinimumAttackRatio,
        ScarcityAlpha = value.ScarcityAlpha,
        MaximumRankMultiplier = value.MaximumRankMultiplier,
        Composition = (AwdScoreCompositionProtocol)value.Composition,
        AttackWeight = value.AttackWeight,
        DefenseWeight = value.DefenseWeight,
        SlaWeight = value.SlaWeight,
        AttackSettlement = (AwdAttackSettlementProtocol)value.AttackSettlement,
    };
}

public sealed record ScoreCurveContract(
    long InitialPoints,
    long MinimumPoints,
    int DecayTeamCount,
    ScoreDecayModeProtocol DecayMode,
    string? CustomExpression);
public sealed record FlagTemplateContract(string Header, string BodyTemplate, bool LeetLiteralText);
public sealed record BloodRewardContract(BloodRewardPolicyProtocol Policy, decimal Value);

public sealed class CompetitionModeConfigurationContract
{
    public required GameModeProtocol Mode { get; set; }
    public required FlagTemplateContract FlagTemplate { get; set; }
    public CtfCompetitionModeConfigurationContract? Ctf { get; set; }
    public AwdCompetitionModeConfigurationContract? Awd { get; set; }
    public AwdpCompetitionModeConfigurationContract? Awdp { get; set; }
    public KohCompetitionModeConfigurationContract? Koh { get; set; }
    public LiveSoloConfigurationContract? LiveSolo { get; set; }
}
public sealed record CtfCompetitionModeConfigurationContract(
    ScoreCurveContract DefaultScoreCurve,
    IReadOnlyList<BloodRewardContract> BloodRewards,
    long WrongSubmissionPenalty,
    CtfScoreSettlementModeProtocol ScoreSettlementMode = CtfScoreSettlementModeProtocol.DynamicRecalculation);
public sealed record AwdTieBreakContract(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdTieBreakField>))] AwdTieBreakField Field,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdSortDirection>))] AwdSortDirection Direction);

public sealed record AwdCompetitionModeConfigurationContract(
    int HardeningDurationSeconds,
    int RoundDurationSeconds,
    AwdAttackRewardModeProtocol AttackRewardMode,
    long AttackPoints,
    long VictimDefensePoolPoints,
    int CheckerIntervalSeconds,
    long ServiceHealthyPoints,
    long ServiceUnhealthyPenalty,
    AwdRuleOptionsContract? Options = null,
    IReadOnlyList<AwdTieBreakContract>? TieBreakers = null);
public sealed record AwdpCompetitionModeConfigurationContract(
    int RoundDurationSeconds,
    ScoreCurveContract BreakScoreCurve,
    ScoreCurveContract FixScoreCurve,
    long FlagWrongPenalty,
    long ExploitSucceededPenalty,
    long ServiceAbnormalPenalty,
    bool RequireBreakBeforeFix,
    int MaxBreakSubmissions,
    int MaxFixSubmissions,
    EvaluationDispatchModeProtocol EvaluationDispatchMode);
public sealed record KohCompetitionModeConfigurationContract(
    int PollIntervalSeconds,
    long ControlPointsPerInterval);

public sealed record CompetitionConfigurationResponse(
    Guid CompetitionId,
    GameModeProtocol Mode,
    CompetitionModeConfigurationContract Configuration,
    CompetitionStatusProtocol CompetitionStatus,
    DateTimeOffset UpdatedAt);

internal static class CompetitionConfigurationMapping
{
    public static CompetitionConfigurationResponse ToResponse(
        CompetitionConfigurationView view) => new(
        view.CompetitionId,
        CompetitionProtocolMapper.ToProtocol(view.Mode),
        CompetitionModeConfigurationContractMapper.FromDomain(view.Configuration),
        CompetitionProtocolMapper.ToProtocol(view.CompetitionStatus),
        view.UpdatedAt);
}

public static class CompetitionModeConfigurationContractMapper
{
    public static bool HasValidShape(CompetitionModeConfigurationContract? contract)
    {
        if (contract is null || !Enum.IsDefined(contract.Mode)
            || contract.FlagTemplate is not { Header: not null, BodyTemplate: not null })
            return false;
        var count = (contract.Ctf is not null ? 1 : 0)
            + (contract.Awd is not null ? 1 : 0)
            + (contract.Awdp is not null ? 1 : 0)
            + (contract.Koh is not null ? 1 : 0)
            + (contract.LiveSolo is not null ? 1 : 0);
        return count == 1 && (contract.Mode switch
        {
            GameModeProtocol.Ctf => contract.Ctf is
                { DefaultScoreCurve: not null, BloodRewards: not null }
                && Enum.IsDefined(contract.Ctf.ScoreSettlementMode)
                && contract.Ctf.BloodRewards.All(item => item is not null),
            GameModeProtocol.Awd => contract.Awd is not null && (contract.Awd.TieBreakers is null
                || contract.Awd.TieBreakers.All(item => item is not null) && AwdTieBreakPolicy.IsValid(
                    contract.Awd.TieBreakers.Select(item => new AwdTieBreakCriterion(item.Field, item.Direction)).ToArray())),
            GameModeProtocol.Awdp => contract.Awdp is
                { BreakScoreCurve: not null, FixScoreCurve: not null },
            GameModeProtocol.Koh => contract.Koh is not null,
            GameModeProtocol.LiveSolo => contract.LiveSolo is { StageRules: not null } && contract.LiveSolo.StageRules.All(x => x is not null),
            _ => false
        });
    }

    public static CompetitionModeConfiguration ToDomain(
        Guid competitionId,
        GameMode expectedMode,
        CompetitionModeConfigurationContract contract)
    {
        if (!HasValidShape(contract))
            throw new ArgumentException("Configuration mode and branch must match exactly.", nameof(contract));
        var configuration = CreateDomain(competitionId, contract);
        if (configuration.Mode != expectedMode)
            throw new InvalidOperationException(
                $"Configuration mode {configuration.Mode} does not match competition mode {expectedMode}.");
        return configuration;
    }

    public static CompetitionModeConfigurationContract FromDomain(
        CompetitionModeConfiguration value)
    {
        var result = new CompetitionModeConfigurationContract
        {
            Mode = (GameModeProtocol)value.Mode,
            FlagTemplate = Flag(value.FlagTemplate)
        };
        switch (value)
        {
            case CtfCompetitionModeConfiguration ctf:
                result.Ctf = new(
                    Curve(ctf.DefaultScoreCurve),
                    ctf.BloodRewards.OrderBy(item => item.Position)
                        .Select(item => new BloodRewardContract(
                            (BloodRewardPolicyProtocol)item.Policy, item.Value)).ToArray(),
                    ctf.WrongSubmissionPenalty,
                    (CtfScoreSettlementModeProtocol)ctf.ScoreSettlementMode);
                break;
            case AwdCompetitionModeConfiguration awd:
                result.Awd = new(
                    awd.HardeningDurationSeconds, awd.RoundDurationSeconds,
                    (AwdAttackRewardModeProtocol)awd.AttackRewardMode, awd.AttackPoints,
                    awd.VictimDefensePoolPoints, awd.CheckerIntervalSeconds,
                    awd.ServiceHealthyPoints, awd.ServiceUnhealthyPenalty,
                    AwdRuleOptionsContract.FromDomain(awd.AwdOptions),
                    AwdTieBreakPolicy.Resolve(awd.TieBreakers.OrderBy(item => item.Position)
                        .Select(item => new AwdTieBreakCriterion(item.Field, item.Direction)))
                        .Select(item => new AwdTieBreakContract(item.Field, item.Direction)).ToArray());
                break;
            case AwdpCompetitionModeConfiguration awdp:
                result.Awdp = new(
                    awdp.RoundDurationSeconds,
                    Curve(awdp.BreakScoreCurve), Curve(awdp.FixScoreCurve), awdp.FlagWrongPenalty,
                    awdp.ExploitSucceededPenalty, awdp.ServiceAbnormalPenalty,
                    awdp.RequireBreakBeforeFix, awdp.MaxBreakSubmissions,
                    awdp.MaxFixSubmissions,
                    (EvaluationDispatchModeProtocol)awdp.EvaluationDispatchMode);
                break;
            case KohCompetitionModeConfiguration koh:
                result.Koh = new(koh.PollIntervalSeconds, koh.ControlPointsPerInterval);
                break;
            case LiveSoloCompetitionModeConfiguration liveSolo:
                result.LiveSolo = LiveSoloConfigurationMapping.ToContract(liveSolo);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported competition configuration {value.GetType().Name}.");
        }
        return result;
    }

    private static CompetitionModeConfiguration CreateDomain(
        Guid competitionId,
        CompetitionModeConfigurationContract value)
    {
        CompetitionModeConfiguration result = value.Mode switch
        {
            GameModeProtocol.Ctf => new CtfCompetitionModeConfiguration
            {
                ScoreSettlementMode = (CtfScoreSettlementMode)value.Ctf!.ScoreSettlementMode,
                DefaultScoreCurve = Curve(value.Ctf!.DefaultScoreCurve),
                BloodRewards = value.Ctf.BloodRewards.Select((reward, position) =>
                    new CompetitionBloodReward
                    {
                        CompetitionId = competitionId,
                        Position = position,
                        Policy = (CompetitionBloodRewardPolicy)reward.Policy,
                        Value = reward.Value
                    }).ToList(),
                WrongSubmissionPenalty = value.Ctf.WrongSubmissionPenalty
            },
            GameModeProtocol.Awd => new AwdCompetitionModeConfiguration
            {
                HardeningDurationSeconds = value.Awd!.HardeningDurationSeconds,
                RoundDurationSeconds = value.Awd.RoundDurationSeconds,
                AttackRewardMode = (AwdAttackRewardMode)value.Awd.AttackRewardMode,
                AttackPoints = value.Awd.AttackPoints,
                VictimDefensePoolPoints = value.Awd.VictimDefensePoolPoints,
                CheckerIntervalSeconds = value.Awd.CheckerIntervalSeconds,
                ServiceHealthyPoints = value.Awd.ServiceHealthyPoints,
                ServiceUnhealthyPenalty = value.Awd.ServiceUnhealthyPenalty,
                AwdOptions = value.Awd.Options?.ToDomain() ?? new(),
                TieBreakers = (value.Awd.TieBreakers?.Select(item => new AwdTieBreakCriterion(item.Field, item.Direction))
                    ?? AwdTieBreakPolicy.Default).Select((item, position) => new AwdCompetitionTieBreak
                    { CompetitionId = competitionId, Position = position, Field = item.Field, Direction = item.Direction }).ToList()
            },
            GameModeProtocol.Awdp => new AwdpCompetitionModeConfiguration
            {
                RoundDurationSeconds = value.Awdp!.RoundDurationSeconds,
                BreakScoreCurve = Curve(value.Awdp.BreakScoreCurve),
                FixScoreCurve = Curve(value.Awdp.FixScoreCurve),
                FlagWrongPenalty = value.Awdp.FlagWrongPenalty,
                ExploitSucceededPenalty = value.Awdp.ExploitSucceededPenalty,
                ServiceAbnormalPenalty = value.Awdp.ServiceAbnormalPenalty,
                RequireBreakBeforeFix = value.Awdp.RequireBreakBeforeFix,
                MaxBreakSubmissions = value.Awdp.MaxBreakSubmissions,
                MaxFixSubmissions = value.Awdp.MaxFixSubmissions,
                EvaluationDispatchMode = (CompetitionEvaluationDispatchMode)value.Awdp.EvaluationDispatchMode
            },
            GameModeProtocol.Koh => new KohCompetitionModeConfiguration
            {
                PollIntervalSeconds = value.Koh!.PollIntervalSeconds,
                ControlPointsPerInterval = value.Koh.ControlPointsPerInterval
            },
            GameModeProtocol.LiveSolo => LiveSoloConfigurationMapping.ToDomain(competitionId, value.LiveSolo!),
            _ => throw new InvalidOperationException(
                $"Unsupported competition configuration contract {value.GetType().Name}.")
        };
        result.CompetitionId = competitionId;
        result.FlagTemplate = new FlagTemplateValue
        {
            Header = value.FlagTemplate.Header,
            BodyTemplate = value.FlagTemplate.BodyTemplate,
            LeetLiteralText = value.FlagTemplate.LeetLiteralText
        };
        return result;
    }

    private static FlagTemplateContract Flag(FlagTemplateValue value) =>
        new(value.Header, value.BodyTemplate, value.LeetLiteralText);
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

public sealed record CompetitionPermissionsResponse(
    Guid CompetitionId,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds);

internal static class CompetitionPermissionsMapper
{
    public static CompetitionPermissionsResponse ToResponse(
        CompetitionPermissionSnapshot snapshot) => new(
        snapshot.CompetitionId,
        snapshot.OwnerId,
        snapshot.ManagerIds,
        snapshot.JudgeIds,
        snapshot.ObserverIds);
}

public sealed record CompetitionLeaderboardVisibilityResponse(
    Guid CompetitionId,
    LeaderboardVisibilityProtocol EffectiveVisibility,
    DateTimeOffset? FrozenStartAt,
    DateTimeOffset? HiddenStartAt);

internal static class CompetitionLeaderboardVisibilityMapper
{
    public static CompetitionLeaderboardVisibilityResponse ToResponse(
        CompetitionVisibilityConfigurationView view) => new(
        view.CompetitionId,
        CompetitionProtocolMapper.ToProtocol(view.EffectiveVisibility),
        view.FrozenStartAt,
        view.HiddenStartAt);
}

public sealed record AdminCompetitionCapabilitiesResponse(
    bool CanObserve,
    bool CanModerate,
    bool CanManagePermissions);

public sealed record CompetitionSsoProviderResponse(
    Guid Id,
    string Name,
    string? IconUrl,
    PublicSsoProtocol Protocol,
    bool Enabled,
    bool AllowBinding);

public sealed record AdminCompetitionResponse(
    CompetitionResponse Competition,
    CompetitionConfigurationResponse ModeConfiguration,
    CompetitionTrackListResponse Tracks,
    CompetitionPermissionsResponse? Permissions,
    CompetitionLeaderboardVisibilityResponse LeaderboardVisibility,
    IReadOnlyList<CompetitionSsoProviderResponse> SsoProviders,
    AdminCompetitionCapabilitiesResponse Capabilities);

public sealed class GetAdminCompetitionEndpoint(
    GetAdminCompetition get,
    GetCompetitionConfiguration getConfiguration,
    GetCompetitionTracks getTracks,
    GetCompetitionPermissions getPermissions,
    GetCompetitionVisibility getVisibility,
    ManageSsoProviders ssoProviders,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<AdminCompetitionResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Gets an administratively visible competition.";
            summary.Description = "Returns draft or public competition metadata when the caller has resource access.";
        });
    }

    public override async Task<Results<Ok<AdminCompetitionResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            includeDeleted: true,
            ct: ct);
        if (view is null)
            return TypedResults.NotFound();
        var role = await CompetitionAdministrationRoleResolver.ResolveAsync(
            view,
            user,
            authorizer,
            ct,
            accessAlreadyEstablished: true);
        var canModerate = await authorizer.CanModerateAsync(user.UserId, view.Id, ct);
        var configuration = await getConfiguration.ExecuteAsync(view.Id, ct);
        var tracks = await getTracks.ExecuteAsync(
            view.Id,
            user.UserId,
            includeInternal: true,
            includeInvitationCodes: canModerate,
            ct);
        var permissions = await getPermissions.ExecuteAsync(
            view.Id,
            user.UserId,
            user.IsAdministrator,
            ct);
        var visibility = await getVisibility.ExecuteAsync(
            view.Id,
            timeProvider.GetUtcNow(),
            ct);
        var sso = await ssoProviders.GetAsync(ct);
        if (configuration is null || tracks is null || visibility is null)
            return TypedResults.NotFound();
        var competition = CompetitionMapper.ToResponse(
            view,
            timeProvider.GetUtcNow()) with
        {
            AdministrationRole = role
        };
        return TypedResults.Ok(new AdminCompetitionResponse(
            competition,
            CompetitionConfigurationMapping.ToResponse(configuration),
            CompetitionTrackProtocolMapping.ToResponse(tracks),
            permissions.State == CompetitionPermissionSnapshotState.Found
                ? CompetitionPermissionsMapper.ToResponse(permissions.Snapshot!)
                : null,
            CompetitionLeaderboardVisibilityMapper.ToResponse(visibility),
            sso.Providers.Select(provider => new CompetitionSsoProviderResponse(
                provider.Id,
                provider.Name,
                provider.IconUrl,
                provider.Protocol == NoCTF.Domain.Identity.SsoProtocol.Oidc
                    ? PublicSsoProtocol.Oidc
                    : PublicSsoProtocol.Cas,
                provider.Enabled,
                provider.AllowBinding)).ToArray(),
            new(true, canModerate,
                permissions.State == CompetitionPermissionSnapshotState.Found)));
    }
}
