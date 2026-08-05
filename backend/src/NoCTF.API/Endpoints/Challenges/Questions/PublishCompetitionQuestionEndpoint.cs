using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

public sealed class PublishCompetitionQuestionRequest
{
    public Guid? ReplyEntryId { get; set; }
    public int? ExpectedRevision { get; set; }
}
public sealed class PublishCompetitionQuestionValidator
    : Validator<PublishCompetitionQuestionRequest>
{
    public PublishCompetitionQuestionValidator()
    {
        RuleFor(request => request.ReplyEntryId).NotNull().NotEqual(Guid.Empty);
        RuleFor(request => request.ExpectedRevision).NotNull().GreaterThanOrEqualTo(0);
    }
}

public sealed class PublishCompetitionQuestionEndpoint(
    PublishCompetitionQuestion publish,
    IUserContext user)
    : Endpoint<PublishCompetitionQuestionRequest, Results<
        Ok<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/questions/{questionId}/publication");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("PublishCompetitionQuestion"));
        Summary(summary =>
        {
            summary.Summary = "Explicitly publishes one reviewed question and handler reply.";
            summary.Description = "This exceptional workflow anonymizes the asker in participant projections. Hint or competition announcement should normally be preferred for broadly useful guidance.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>> ExecuteAsync(
        PublishCompetitionQuestionRequest request,
        CancellationToken ct)
    {
        if (!user.IsHuman)
            return TypedResults.Forbid();
        var result = await publish.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            Route<Guid>("questionId"),
            request.ReplyEntryId!.Value,
            user.UserId,
            request.ExpectedRevision!.Value,
            DateTimeOffset.UtcNow), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(CompetitionQuestionResponseMapper.ToResponse(result.Question!)),
            CompetitionQuestionFailure.NotFound
                or CompetitionQuestionFailure.EntryNotFound => TypedResults.NotFound(),
            CompetitionQuestionFailure.Forbidden => TypedResults.Forbid(),
            CompetitionQuestionFailure.RevisionConflict
                or CompetitionQuestionFailure.ReplyNotPublishable => TypedResults.Conflict(
                    CompetitionQuestionFailureMapper.ToResponse(result)),
            CompetitionQuestionFailure.InvalidRequest => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Competition question publication is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = CompetitionQuestionFailureMapper.ToCode(result.Failure.Value)
                }),
            _ => throw new InvalidOperationException(
                $"Unsupported question publication result: {result.Failure}.")
        };
    }
}
