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

public sealed class ApplyAwdScoringRulesRequest
{
    public int FirstRound { get; set; }
    public int LastRound { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdRuleChangeTiming>))]
    public AwdRuleChangeTiming Timing { get; set; }
    public required AwdScoringPolicyContract Policy { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PreviewToken { get; set; } = string.Empty;
}

public sealed class ApplyAwdScoringRulesValidator : Validator<ApplyAwdScoringRulesRequest>
{
    public ApplyAwdScoringRulesValidator()
    {
        RuleFor(request => request.FirstRound).GreaterThan(0);
        RuleFor(request => request.LastRound).GreaterThanOrEqualTo(request => request.FirstRound);
        RuleFor(request => request.Timing).Must(timing => timing is AwdRuleChangeTiming.Immediately or AwdRuleChangeTiming.RecalculateHistory);
        RuleFor(request => request.Policy).NotNull();
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(512);
        RuleFor(request => request.PreviewToken).NotEmpty().MaximumLength(2048);
    }
}

public sealed record ApplyAwdScoringRulesResponse(Guid EventId);

public sealed class ApplyAwdScoringRulesEndpoint(ManageAwdScoringRules rules,
    ICompetitionModerationAuthorizer access, IUserContext user, TimeProvider clock)
    : Endpoint<ApplyAwdScoringRulesRequest, Results<Ok<ApplyAwdScoringRulesResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/awd/scoring");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminApplyAwdScoringRules"));
        Summary(summary => summary.Summary = "Applies a reviewed AWD scoring policy to an explicit round range with immutable audit evidence.");
    }

    public override async Task<Results<Ok<ApplyAwdScoringRulesResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(ApplyAwdScoringRulesRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await access.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await rules.ApplyAsync(new(competitionId, Route<Guid>("competitionChallengeId"),
            request.FirstRound, request.LastRound, request.Timing, request.Policy.ToDomain(), user.UserId,
            request.Reason, clock.GetUtcNow()), request.PreviewToken, ct);
        if (result.Failure == AwdScoringRuleFailure.NotFound) return TypedResults.NotFound();
        return result.EventId is { } eventId ? TypedResults.Ok(new ApplyAwdScoringRulesResponse(eventId))
            : AwdScoringRuleProblems.From(result.Failure!.Value);
    }
}
