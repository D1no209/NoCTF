using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
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
public enum AwdAttackRewardModeProtocol { FixedPerAttack, SplitVictimDefensePool }
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<EvaluationDispatchModeProtocol>))]
public enum EvaluationDispatchModeProtocol { Automatic, Manual }

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
public sealed record AwdCompetitionModeConfigurationContract(
    int HardeningDurationSeconds,
    int RoundDurationSeconds,
    AwdAttackRewardModeProtocol AttackRewardMode,
    long AttackPoints,
    long VictimDefensePoolPoints,
    int CheckerIntervalSeconds,
    long ServiceHealthyPoints,
    long ServiceUnhealthyPenalty);
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
            GameModeProtocol.Awd => contract.Awd is not null,
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
                    awd.ServiceHealthyPoints, awd.ServiceUnhealthyPenalty);
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
                ServiceUnhealthyPenalty = value.Awd.ServiceUnhealthyPenalty
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
