using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetAwdScoringRulesRequest
{
    [QueryParam] public int? RoundNumber { get; set; }
}
public sealed class GetAwdScoringRulesValidator : Validator<GetAwdScoringRulesRequest>
{
    public GetAwdScoringRulesValidator() => RuleFor(request => request.RoundNumber).GreaterThan(0);
}
public sealed record AwdRoundScoringPolicyResponse(Guid RulesId, int Revision, DateTimeOffset EnabledAt,
    AwdScoringPolicyContract IssuedPolicy, AwdScoringPolicyContract EffectivePolicy, Guid? AppliedChangeEventId);
public sealed record AwdScoringRuleAuditResponse(Guid EventId, Guid? ActorUserId, string? Reason, int FirstRound,
    int LastRound, [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdRuleChangeTiming>))] AwdRuleChangeTiming Timing,
    DateTimeOffset AppliedAt, AwdScoringPolicyContract Policy);
public sealed record AwdScoringRulesResponse(int RoundNumber, Guid RoundId,
    IReadOnlyList<AwdRoundScoringPolicyResponse> Policies, IReadOnlyList<AwdScoringRuleAuditResponse> RecentChanges);

public sealed class GetAwdScoringRulesEndpoint(IAwdScoringRuleStore store,
    ICompetitionModerationAuthorizer access, IUserContext user)
    : Endpoint<GetAwdScoringRulesRequest, Results<Ok<AwdScoringRulesResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/awd/scoring-rules");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetAwdScoringRules"));
        Summary(summary => summary.Summary = "Reads issued and effective AWD scoring rules for a round and the latest 50 audited changes.");
    }

    public override async Task<Results<Ok<AwdScoringRulesResponse>, NotFound, ForbidHttpResult>>
        ExecuteAsync(GetAwdScoringRulesRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await access.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var view = await store.ReadAsync(competitionId, Route<Guid>("competitionChallengeId"), request.RoundNumber, ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(new AwdScoringRulesResponse(view.RoundNumber,
            view.RoundId, view.Policies.Select(policy => new AwdRoundScoringPolicyResponse(policy.RulesId, policy.Revision,
                policy.EnabledAt, AwdScoringPolicyContract.From(policy.IssuedPolicy),
                AwdScoringPolicyContract.From(policy.EffectivePolicy), policy.AppliedChangeEventId)).ToArray(),
            view.RecentChanges.Select(change => new AwdScoringRuleAuditResponse(change.EventId, change.ActorUserId,
                change.Reason, change.FirstRound, change.LastRound, change.Timing, change.AppliedAt,
                AwdScoringPolicyContract.From(change.Policy))).ToArray()));
    }
}
