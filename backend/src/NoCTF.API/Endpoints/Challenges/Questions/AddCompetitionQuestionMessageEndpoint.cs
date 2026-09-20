using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

public sealed class AddCompetitionQuestionMessageRequest
{
    public string Body { get; set; } = string.Empty;
}
public sealed class AddCompetitionQuestionMessageValidator
    : Validator<AddCompetitionQuestionMessageRequest>
{
    public AddCompetitionQuestionMessageValidator()
    {
        RuleFor(request => request.Body)
            .NotEmpty()
            .MinimumLength(CompetitionQuestionRules.MinimumBodyLength)
            .MaximumLength(CompetitionQuestionRules.MaximumBodyLength);
    }
}

public sealed class AddCompetitionQuestionMessageEndpoint(
    AddCompetitionQuestionMessage add,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<AddCompetitionQuestionMessageRequest, Results<
        Ok<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/questions/{threadRootId}/messages");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("AddCompetitionQuestionMessage")
            .WithMetadata(new EnableRateLimitingAttribute("question")));
        Summary(summary =>
        {
            summary.Summary = "Adds a private message to a competition question.";
            summary.Description = "Asker follow-ups return Replied or Resolved questions to Pending; handler replies move Pending questions to Replied. Closed questions are terminal.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>> ExecuteAsync(
        AddCompetitionQuestionMessageRequest request,
        CancellationToken ct)
    {
        var result = await add.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            Route<Guid>("threadRootId"),
            user.UserId,
            request.Body,
            timeProvider.GetUtcNow()), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(CompetitionQuestionResponseMapper.ToResponse(result.Question!)),
            CompetitionQuestionFailure.NotFound => TypedResults.NotFound(),
            CompetitionQuestionFailure.Forbidden => TypedResults.Forbid(),
            CompetitionQuestionFailure.InvalidTransition
                or CompetitionQuestionFailure.QuestionClosed
                or CompetitionQuestionFailure.TeamActiveQuestionLimitReached
                or CompetitionQuestionFailure.ParticipantMessageLimitReached => TypedResults.Conflict(
                    CompetitionQuestionFailureMapper.ToResponse(result)),
            CompetitionQuestionFailure.InvalidRequest
                or CompetitionQuestionFailure.SpamRejected => TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Competition question message was rejected.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = CompetitionQuestionFailureMapper.ToCode(result.Failure.Value)
                    }),
            _ => throw new InvalidOperationException(
                $"Unsupported question message result: {result.Failure}.")
        };
    }
}
