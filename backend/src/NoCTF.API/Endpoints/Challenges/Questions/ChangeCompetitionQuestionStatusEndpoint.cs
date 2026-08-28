using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

public sealed class ChangeCompetitionQuestionStatusRequest
{
    public CompetitionQuestionStatusCode? Status { get; set; }
}
public sealed class ChangeCompetitionQuestionStatusValidator
    : Validator<ChangeCompetitionQuestionStatusRequest>
{
    public ChangeCompetitionQuestionStatusValidator()
    {
        RuleFor(request => request.Status).NotNull().IsInEnum();
    }
}

public sealed class ChangeCompetitionQuestionStatusEndpoint(
    ChangeCompetitionQuestionStatus change,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ChangeCompetitionQuestionStatusRequest, Results<
        Ok<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/questions/{threadRootId}/status");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ChangeCompetitionQuestionStatus"));
        Summary(summary =>
        {
            summary.Summary = "Transitions a competition question state.";
            summary.Description = "Every accepted transition creates an immutable audit entry. The asker may resolve a replied question; handlers may resolve or irreversibly close it.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>> ExecuteAsync(
        ChangeCompetitionQuestionStatusRequest request,
        CancellationToken ct)
    {
        if (!user.IsHuman)
            return TypedResults.Forbid();
        var result = await change.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            Route<Guid>("threadRootId"),
            user.UserId,
            CompetitionQuestionResponseMapper.ToDomain(request.Status!.Value),
            timeProvider.GetUtcNow()), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(CompetitionQuestionResponseMapper.ToResponse(result.Question!)),
            CompetitionQuestionFailure.NotFound => TypedResults.NotFound(),
            CompetitionQuestionFailure.Forbidden => TypedResults.Forbid(),
            CompetitionQuestionFailure.InvalidTransition
                or CompetitionQuestionFailure.QuestionClosed => TypedResults.Conflict(
                    CompetitionQuestionFailureMapper.ToResponse(result)),
            CompetitionQuestionFailure.InvalidRequest => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Competition question status is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = CompetitionQuestionFailureMapper.ToCode(result.Failure.Value)
                }),
            _ => throw new InvalidOperationException(
                $"Unsupported question status result: {result.Failure}.")
        };
    }
}
