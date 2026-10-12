using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Challenges;

public sealed record AwdPublicStoreRuleResponse(Guid StoreId, string Name, decimal Weight, int FlagsPerIssue);
public sealed record AwdPublicRulePolicyResponse(Guid? RulesId, int? Revision, DateTimeOffset? EffectiveAt,
    AwdAttackRewardModeProtocol AttackMode, long AttackPoints, long DefensePoints, long SlaPoints,
    int CheckerIntervalSeconds, AwdRuleOptionsContract Options, IReadOnlyList<AwdPublicStoreRuleResponse> Stores,
    AwdScoringPolicyContract Scoring, Guid? ScoringChangeEventId)
{
    internal static AwdPublicRulePolicyResponse From(AwdPublicRulePolicy policy) => new(policy.RulesId,
        policy.Revision, policy.EffectiveAt, (AwdAttackRewardModeProtocol)policy.AttackMode,
        policy.AttackPoints, policy.DefensePoints, policy.SlaPoints, policy.CheckerIntervalSeconds,
        AwdRuleOptionsContract.FromDomain(policy.Options), policy.Stores.Select(store =>
            new AwdPublicStoreRuleResponse(store.StoreId, store.Name, store.Weight, store.FlagsPerIssue)).ToArray(),
        AwdScoringPolicyContract.From(policy.Scoring), policy.ScoringChangeEventId);
}
public sealed record AwdPublicRulesResponse(DateTimeOffset ServerTime, DateTimeOffset EndsAt,
    DateTimeOffset AcceptanceEndsAt, bool Paused, bool AcceptingSubmissions, int? RoundNumber,
    Guid? RoundId, DateTimeOffset? RoundStartsAt, DateTimeOffset? RoundEndsAt,
    int RoundDurationSeconds, int HardeningDurationSeconds, AwdRuleOptionsContract CompetitionDefaults,
    AwdRuleOverridesContract QuestionOverrides, bool AttackPointsOverridden, bool DefensePointsOverridden,
    bool SlaPointsOverridden, bool CheckerIntervalOverridden, bool AttackModeOverridden,
    IReadOnlyList<AwdPublicRulePolicyResponse> IssuedPolicies, AwdPublicRulePolicyResponse NextRound);

public sealed class GetAwdRulesEndpoint(IAwdPublicRulesReader reader, ICompetitionChallengeReadAccess access,
    IUserContext user, TimeProvider time) : EndpointWithoutRequest<Results<Ok<AwdPublicRulesResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/awd/rules");
        AllowAnonymous();
        Description(builder => builder.WithName("GetAwdRules"));
        Summary(summary => summary.Summary = "Reads safe issued, effective scoring and next-round AWD policies. Paused deadlines are provisional until resumption.");
    }

    public override async Task<Results<Ok<AwdPublicRulesResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private,no-store";
        var competitionId = Route<Guid>("competitionId");
        var now = time.GetUtcNow();
        var decision = await access.ResolveAsync(user.UserId, competitionId, now, ct);
        if (decision is null || decision.Visibility.GameMode != GameMode.Awd
            || !ParticipantChallengeVisibilityPolicy.CanView(decision.Visibility.CompetitionStatus)) return TypedResults.NotFound();
        var view = await reader.ReadAsync(competitionId, Route<Guid>("competitionChallengeId"), now, ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(new AwdPublicRulesResponse(
            view.ServerTime, view.EndsAt, view.AcceptanceEndsAt, view.Paused, view.AcceptingSubmissions,
            view.RoundNumber, view.RoundId, view.RoundStartsAt, view.RoundEndsAt, view.RoundDurationSeconds,
            view.HardeningDurationSeconds, AwdRuleOptionsContract.FromDomain(view.CompetitionDefaults),
            AwdRuleOverridesContract.FromDomain(view.QuestionOverrides), view.AttackPointsOverridden,
            view.DefensePointsOverridden, view.SlaPointsOverridden, view.CheckerIntervalOverridden, view.AttackModeOverridden,
            view.IssuedPolicies.Select(AwdPublicRulePolicyResponse.From).ToArray(), AwdPublicRulePolicyResponse.From(view.NextRound)));
    }
}
