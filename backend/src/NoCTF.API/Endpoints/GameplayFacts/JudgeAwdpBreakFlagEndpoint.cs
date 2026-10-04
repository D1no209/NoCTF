using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Admission;

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
    /// <summary>One-time verification token; optional when platform policy disables verification. Maximum 4096 characters.</summary>
    [FromHeader("X-NoCTF-Human-Verification", IsRequired = false, RemoveFromSchema = true)]
    public string? HumanVerificationToken { get; set; }

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
    AwdpBreakFlagJudgementFailureCodeProtocol Code,
    string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class JudgeAwdpBreakFlagEndpoint(
    JudgeAwdpBreakFlag judge,
    IUserContext user)
    : Endpoint<JudgeAwdpBreakFlagRequest,
        Results<Ok<AwdpBreakFlagJudgementResponse>, ForbidHttpResult,
            Conflict<AwdpBreakFlagJudgementConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Description(builder => builder
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable));

        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute("submission")));
        Options(options => options.WithMetadata(
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
        Description(builder => builder.WithName("JudgeAwdpBreakFlag"));
        Summary(summary =>
        {
            summary.Params["X-NoCTF-Human-Verification"] = "One-time verification token, at most 4096 characters. Required only when the configured platform policy enables verification for this operation.";
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
                    AwdpBreakFlagJudgementFailureCodeProtocol.JudgementUnavailable,
                    "This published AWDP challenge is not available for Break Flag judgement.")),
            AwdpBreakFlagJudgementFailureCode.AchievementNotSucceeded =>
                TypedResults.Conflict(new AwdpBreakFlagJudgementConflictResponse(
                    AwdpBreakFlagJudgementFailureCodeProtocol.AchievementNotSucceeded,
                    "The team must complete a successful Break on this challenge before its Flag can be checked.")),
            AwdpBreakFlagJudgementFailureCode.FlagInvalid => ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.JudgeAwdpBreakFlagTitleAwdpBreakFlagWas),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = AwdpBreakFlagJudgementFailureCodeProtocol.FlagInvalid
                }),
            _ => throw new InvalidOperationException(
                $"Unsupported AWDP Break Flag judgement failure: {result.FailureCode}.")
        };
    }
}
