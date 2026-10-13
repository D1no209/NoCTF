using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AwdScoringPolicyContract
{
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdAttackRewardMode>))]
    public AwdAttackRewardMode AttackRewardMode { get; init; }
    public long AttackPoints { get; init; } = 50;
    public long VictimDefensePoolPoints { get; init; } = 100;
    public long ServiceHealthyPoints { get; init; } = 100;
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdSlaSettlement>))]
    public AwdSlaSettlement SlaSettlement { get; init; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdDefenseMode>))]
    public AwdDefenseMode DefenseMode { get; init; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdSlaBasis>))]
    public AwdSlaBasis SlaBasis { get; init; }
    public decimal OkRatio { get; init; } = 1m;
    public decimal RecoveringRatio { get; init; } = 0.5m;
    public decimal MumbleRatio { get; init; }
    public decimal OfflineRatio { get; init; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdPenaltyMode>))]
    public AwdPenaltyMode PenaltyMode { get; init; }
    public long MumblePenalty { get; init; }
    public long OfflinePenalty { get; init; }
    public long RecoveringPenalty { get; init; }
    public decimal MumblePenaltyRatio { get; init; }
    public decimal OfflinePenaltyRatio { get; init; }
    public decimal RecoveringPenaltyRatio { get; init; }
    public decimal ConsecutiveFailureMultiplier { get; init; } = 1m;
    public long? MaximumRoundPenalty { get; init; }
    public long ConfidentialityPoints { get; init; }
    public decimal MinimumAttackRatio { get; init; } = 0.1m;
    public decimal ScarcityAlpha { get; init; } = 0.705m;
    public decimal MaximumRankMultiplier { get; init; } = 2m;
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdScoreComposition>))]
    public AwdScoreComposition Composition { get; init; }
    public decimal AttackWeight { get; init; } = 1m;
    public decimal DefenseWeight { get; init; } = 1m;
    public decimal SlaWeight { get; init; } = 1m;
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdAttackSettlement>))]
    public AwdAttackSettlement AttackSettlement { get; init; }
    public static AwdScoringPolicyContract From(AwdScoringPolicy policy) => new()
    {
        AttackRewardMode = policy.AttackRewardMode,
        AttackPoints = policy.AttackPoints,
        VictimDefensePoolPoints = policy.VictimDefensePoolPoints,
        ServiceHealthyPoints = policy.ServiceHealthyPoints,
        SlaSettlement = policy.SlaSettlement,
        DefenseMode = policy.DefenseMode,
        SlaBasis = policy.SlaBasis,
        OkRatio = policy.OkRatio,
        RecoveringRatio = policy.RecoveringRatio,
        MumbleRatio = policy.MumbleRatio,
        OfflineRatio = policy.OfflineRatio,
        PenaltyMode = policy.PenaltyMode,
        MumblePenalty = policy.MumblePenalty,
        OfflinePenalty = policy.OfflinePenalty,
        RecoveringPenalty = policy.RecoveringPenalty,
        MumblePenaltyRatio = policy.MumblePenaltyRatio,
        OfflinePenaltyRatio = policy.OfflinePenaltyRatio,
        RecoveringPenaltyRatio = policy.RecoveringPenaltyRatio,
        ConsecutiveFailureMultiplier = policy.ConsecutiveFailureMultiplier,
        MaximumRoundPenalty = policy.MaximumRoundPenalty,
        ConfidentialityPoints = policy.ConfidentialityPoints,
        MinimumAttackRatio = policy.MinimumAttackRatio,
        ScarcityAlpha = policy.ScarcityAlpha,
        MaximumRankMultiplier = policy.MaximumRankMultiplier,
        Composition = policy.Composition,
        AttackWeight = policy.AttackWeight,
        DefenseWeight = policy.DefenseWeight,
        SlaWeight = policy.SlaWeight,
        AttackSettlement = policy.AttackSettlement,
    };

    public AwdScoringPolicy ToDomain() => new()
    {
        AttackRewardMode = AttackRewardMode,
        AttackPoints = AttackPoints,
        VictimDefensePoolPoints = VictimDefensePoolPoints,
        ServiceHealthyPoints = ServiceHealthyPoints,
        SlaSettlement = SlaSettlement,
        DefenseMode = DefenseMode,
        SlaBasis = SlaBasis,
        OkRatio = OkRatio,
        RecoveringRatio = RecoveringRatio,
        MumbleRatio = MumbleRatio,
        OfflineRatio = OfflineRatio,
        PenaltyMode = PenaltyMode,
        MumblePenalty = MumblePenalty,
        OfflinePenalty = OfflinePenalty,
        RecoveringPenalty = RecoveringPenalty,
        MumblePenaltyRatio = MumblePenaltyRatio,
        OfflinePenaltyRatio = OfflinePenaltyRatio,
        RecoveringPenaltyRatio = RecoveringPenaltyRatio,
        ConsecutiveFailureMultiplier = ConsecutiveFailureMultiplier,
        MaximumRoundPenalty = MaximumRoundPenalty,
        ConfidentialityPoints = ConfidentialityPoints,
        MinimumAttackRatio = MinimumAttackRatio,
        ScarcityAlpha = ScarcityAlpha,
        MaximumRankMultiplier = MaximumRankMultiplier,
        Composition = Composition,
        AttackWeight = AttackWeight,
        DefenseWeight = DefenseWeight,
        SlaWeight = SlaWeight,
        AttackSettlement = AttackSettlement,
    };
}

public sealed class PreviewAwdScoringRulesRequest
{
    public int FirstRound { get; set; }
    public int LastRound { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdRuleChangeTiming>))]
    public AwdRuleChangeTiming Timing { get; set; }
    public required AwdScoringPolicyContract Policy { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PreviewAwdScoringRulesValidator : Validator<PreviewAwdScoringRulesRequest>
{
    public PreviewAwdScoringRulesValidator()
    {
        RuleFor(request => request.FirstRound).GreaterThan(0);
        RuleFor(request => request.LastRound).GreaterThanOrEqualTo(request => request.FirstRound);
        RuleFor(request => request.Timing).Must(timing => timing is AwdRuleChangeTiming.Immediately or AwdRuleChangeTiming.RecalculateHistory);
        RuleFor(request => request.Policy).NotNull();
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(512);
    }
}

public sealed class PreviewAwdScoringRulesEndpoint(ManageAwdScoringRules rules,
    ICompetitionModerationAuthorizer access, IUserContext user, TimeProvider clock)
    : Endpoint<PreviewAwdScoringRulesRequest, Results<Ok<AwdScoringRulePreview>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/awd/scoring-preview");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPreviewAwdScoringRules"));
        Summary(summary => summary.Summary = "Previews explicit AWD scoring changes for existing rounds, preserving issued flag and check policies.");
    }

    public override async Task<Results<Ok<AwdScoringRulePreview>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(PreviewAwdScoringRulesRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await access.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await rules.PreviewAsync(new(competitionId, Route<Guid>("competitionChallengeId"),
            request.FirstRound, request.LastRound, request.Timing, request.Policy.ToDomain(), user.UserId,
            request.Reason, clock.GetUtcNow()), ct);
        if (result.Failure == AwdScoringRuleFailure.NotFound) return TypedResults.NotFound();
        return result.Preview is { } preview ? TypedResults.Ok(preview) : AwdScoringRuleProblems.From(result.Failure!.Value);
    }
}

internal static class AwdScoringRuleProblems
{
    internal static ProblemHttpResult From(AwdScoringRuleFailure failure) => ApiProblems.Problem(
        statusCode: failure is AwdScoringRuleFailure.Conflict or AwdScoringRuleFailure.PreviewExpired ? 409 : 422,
        detail: ApiMessages.For(failure), extensions: new Dictionary<string, object?> { ["code"] = failure.ToString() });
}
