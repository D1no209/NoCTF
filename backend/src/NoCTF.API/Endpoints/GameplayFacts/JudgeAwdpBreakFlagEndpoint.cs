using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Awdp;

namespace NoCTF.API.Endpoints.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdpBreakFlagJudgementProtocol>))]
public enum AwdpBreakFlagJudgementProtocol
{
    Correct,
    Wrong
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdpBreakFlagJudgementFailureCodeProtocol>))]
public enum AwdpBreakFlagJudgementFailureCodeProtocol
{
    JudgementUnavailable,
    TeamNotEligible,
    AchievementNotSucceeded,
    FlagInvalid
}

public sealed class JudgeAwdpBreakFlagRequest
{
    public string Flag { get; set; } = string.Empty;
}

public sealed class JudgeAwdpBreakFlagValidator : Validator<JudgeAwdpBreakFlagRequest>
{
    public JudgeAwdpBreakFlagValidator() =>
        RuleFor(request => request.Flag).NotEmpty().MaximumLength(4_096);
}

public sealed record AwdpBreakFlagJudgementResponse(
    AwdpBreakFlagJudgementProtocol Result);

public sealed record AwdpBreakFlagJudgementConflictResponse(
    AwdpBreakFlagJudgementFailureCodeProtocol Code);

public sealed class JudgeAwdpBreakFlagEndpoint(
    JudgeAwdpBreakFlag judge,
    IUserContext user)
    : Endpoint<JudgeAwdpBreakFlagRequest,
        Results<Ok<AwdpBreakFlagJudgementResponse>, ForbidHttpResult,
            Conflict<AwdpBreakFlagJudgementConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute("submission")));
        Description(builder => builder.WithName("JudgeAwdpBreakFlag"));
        Summary(summary =>
        {
            summary.Summary = "Checks an AWDP Break Flag after the attack achievement succeeded.";
            summary.Description =
                "Returns only correctness. It never creates GameplayFact, score, event, notification, cheat incident or Runtime changes.";
        });
    }

    public override async Task<
        Results<Ok<AwdpBreakFlagJudgementResponse>, ForbidHttpResult,
            Conflict<AwdpBreakFlagJudgementConflictResponse>, ProblemHttpResult>>
        ExecuteAsync(JudgeAwdpBreakFlagRequest request, CancellationToken ct)
    {
        var result = await judge.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            request.Flag), ct);
        if (result.Judgement is AwdpBreakFlagJudgement judgement)
        {
            return TypedResults.Ok(new AwdpBreakFlagJudgementResponse(
                judgement == AwdpBreakFlagJudgement.Correct
                    ? AwdpBreakFlagJudgementProtocol.Correct
                    : AwdpBreakFlagJudgementProtocol.Wrong));
        }

        return result.FailureCode switch
        {
            AwdpBreakFlagJudgementFailureCode.TeamNotEligible => TypedResults.Forbid(),
            AwdpBreakFlagJudgementFailureCode.JudgementUnavailable =>
                TypedResults.Conflict(new AwdpBreakFlagJudgementConflictResponse(
                    AwdpBreakFlagJudgementFailureCodeProtocol.JudgementUnavailable)),
            AwdpBreakFlagJudgementFailureCode.AchievementNotSucceeded =>
                TypedResults.Conflict(new AwdpBreakFlagJudgementConflictResponse(
                    AwdpBreakFlagJudgementFailureCodeProtocol.AchievementNotSucceeded)),
            AwdpBreakFlagJudgementFailureCode.FlagInvalid => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "AWDP Break Flag was not accepted.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = AwdpBreakFlagJudgementFailureCodeProtocol.FlagInvalid
                }),
            _ => throw new InvalidOperationException(
                $"Unsupported AWDP Break Flag judgement failure: {result.FailureCode}.")
        };
    }
}
