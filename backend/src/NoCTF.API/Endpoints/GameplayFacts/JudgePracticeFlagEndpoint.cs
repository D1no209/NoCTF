using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Practice;

namespace NoCTF.API.Endpoints.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PracticeFlagJudgementProtocol>))]
public enum PracticeFlagJudgementProtocol
{
    Correct,
    Wrong
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PracticeFlagFailureCodeProtocol>))]
public enum PracticeFlagFailureCodeProtocol
{
    PracticeUnavailable,
    TeamNotEligible,
    RuntimeNotRunning,
    FlagInvalid
}

public sealed class JudgePracticeFlagRequest
{
    public string Flag { get; set; } = string.Empty;
}

public sealed class JudgePracticeFlagValidator : Validator<JudgePracticeFlagRequest>
{
    public JudgePracticeFlagValidator() =>
        RuleFor(request => request.Flag).NotEmpty().MaximumLength(4_096);
}

public sealed record PracticeFlagJudgementResponse(
    PracticeFlagJudgementProtocol Result);

public sealed record PracticeFlagConflictResponse(
    PracticeFlagFailureCodeProtocol Code);

public sealed class JudgePracticeFlagEndpoint(
    JudgePracticeFlag judge,
    IUserContext user)
    : Endpoint<JudgePracticeFlagRequest,
        Results<Ok<PracticeFlagJudgementResponse>, ForbidHttpResult,
            Conflict<PracticeFlagConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/practice-flag");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute("submission")));
        Description(builder => builder.WithName("JudgePracticeFlag"));
        Summary(summary =>
        {
            summary.Summary = "Checks a Flag in CTF practice mode without scoring.";
            summary.Description =
                "Returns only correctness. It never creates GameplayFact, blood awards or leaderboard changes.";
        });
    }

    public override async Task<
        Results<Ok<PracticeFlagJudgementResponse>, ForbidHttpResult,
            Conflict<PracticeFlagConflictResponse>, ProblemHttpResult>>
        ExecuteAsync(JudgePracticeFlagRequest request, CancellationToken ct)
    {
        var result = await judge.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            request.Flag,
            DateTimeOffset.UtcNow), ct);
        if (result.Judgement is PracticeFlagJudgement judgement)
        {
            return TypedResults.Ok(new PracticeFlagJudgementResponse(
                judgement == PracticeFlagJudgement.Correct
                    ? PracticeFlagJudgementProtocol.Correct
                    : PracticeFlagJudgementProtocol.Wrong));
        }

        return result.FailureCode switch
        {
            PracticeFlagFailureCode.TeamNotEligible => TypedResults.Forbid(),
            PracticeFlagFailureCode.PracticeUnavailable =>
                TypedResults.Conflict(new PracticeFlagConflictResponse(
                    PracticeFlagFailureCodeProtocol.PracticeUnavailable)),
            PracticeFlagFailureCode.RuntimeNotRunning =>
                TypedResults.Conflict(new PracticeFlagConflictResponse(
                    PracticeFlagFailureCodeProtocol.RuntimeNotRunning)),
            PracticeFlagFailureCode.FlagInvalid => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Practice Flag was not accepted.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = PracticeFlagFailureCodeProtocol.FlagInvalid
                }),
            _ => throw new InvalidOperationException(
                $"Unsupported practice Flag failure: {result.FailureCode}.")
        };
    }
}
