using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

public sealed class CreateCompetitionQuestionRequest
{
    public CompetitionQuestionSubjectCode? Subject { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? SubmissionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
public sealed class CreateCompetitionQuestionValidator
    : Validator<CreateCompetitionQuestionRequest>
{
    public CreateCompetitionQuestionValidator()
    {
        RuleFor(request => request.Subject).NotNull().IsInEnum();
        RuleFor(request => request.Title)
            .NotEmpty()
            .MinimumLength(CompetitionQuestionRules.MinimumTitleLength)
            .MaximumLength(CompetitionQuestionRules.MaximumTitleLength);
        RuleFor(request => request.Body)
            .NotEmpty()
            .MinimumLength(CompetitionQuestionRules.MinimumBodyLength)
            .MaximumLength(CompetitionQuestionRules.MaximumBodyLength);
        RuleFor(request => request)
            .Must(request => request.Subject switch
            {
                CompetitionQuestionSubjectCode.Challenge =>
                    request.CompetitionChallengeId is not null,
                CompetitionQuestionSubjectCode.Platform =>
                    request.CompetitionChallengeId is null,
                _ => true
            })
            .WithMessage("CompetitionChallengeId must be supplied only for Challenge questions.");
    }
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionFailureCode>))]
public enum CompetitionQuestionFailureCode
{
    InvalidRequest,
    SpamRejected,
    LifecycleConflict,
    TeamNotEligible,
    RevisionConflict,
    InvalidTransition,
    QuestionClosed,
    ReplyNotPublishable
}

public sealed record CompetitionQuestionFailureResponse(
    CompetitionQuestionFailureCode Code,
    CompetitionQuestionResponse? Current);

internal static class CompetitionQuestionFailureMapper
{
    public static CompetitionQuestionFailureResponse ToResponse(
        CompetitionQuestionMutationResult result) =>
        new(ToCode(result.Failure!.Value), result.Question is null
            ? null
            : CompetitionQuestionResponseMapper.ToResponse(result.Question));

    public static CompetitionQuestionFailureCode ToCode(CompetitionQuestionFailure failure) =>
        failure switch
        {
            CompetitionQuestionFailure.InvalidRequest => CompetitionQuestionFailureCode.InvalidRequest,
            CompetitionQuestionFailure.SpamRejected => CompetitionQuestionFailureCode.SpamRejected,
            CompetitionQuestionFailure.LifecycleConflict => CompetitionQuestionFailureCode.LifecycleConflict,
            CompetitionQuestionFailure.TeamNotEligible => CompetitionQuestionFailureCode.TeamNotEligible,
            CompetitionQuestionFailure.RevisionConflict => CompetitionQuestionFailureCode.RevisionConflict,
            CompetitionQuestionFailure.InvalidTransition => CompetitionQuestionFailureCode.InvalidTransition,
            CompetitionQuestionFailure.QuestionClosed => CompetitionQuestionFailureCode.QuestionClosed,
            CompetitionQuestionFailure.ReplyNotPublishable => CompetitionQuestionFailureCode.ReplyNotPublishable,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
}

public sealed class CreateCompetitionQuestionEndpoint(
    CreateCompetitionQuestion create,
    IUserContext user)
    : Endpoint<CreateCompetitionQuestionRequest, Results<
        Created<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/questions");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("CreateCompetitionQuestion")
            .WithMetadata(new EnableRateLimitingAttribute("question")));
        Summary(summary =>
        {
            summary.Summary = "Creates a private competition question.";
            summary.Description = "Challenge questions target one published CompetitionChallenge; platform questions target competition management. Attachments are not accepted and an optional Submission is referenced without copying its protected content.";
        });
    }

    public override async Task<Results<
        Created<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>> ExecuteAsync(
        CreateCompetitionQuestionRequest request,
        CancellationToken ct)
    {
        if (!user.IsHuman)
            return TypedResults.Forbid();
        var competitionId = Route<Guid>("competitionId");
        var result = await create.ExecuteAsync(new(
            competitionId,
            CompetitionQuestionResponseMapper.ToDomain(request.Subject!.Value),
            request.CompetitionChallengeId,
            request.SubmissionId,
            user.UserId,
            request.Title,
            request.Body,
            DateTimeOffset.UtcNow), ct);
        return result.Failure switch
        {
            null => TypedResults.Created(
                $"/api/v1/competitions/{competitionId}/questions/{result.Question!.Id}",
                CompetitionQuestionResponseMapper.ToResponse(result.Question)),
            CompetitionQuestionFailure.NotFound
                or CompetitionQuestionFailure.ChallengeNotFound
                or CompetitionQuestionFailure.SubmissionNotFound => TypedResults.NotFound(),
            CompetitionQuestionFailure.Forbidden => TypedResults.Forbid(),
            CompetitionQuestionFailure.LifecycleConflict
                or CompetitionQuestionFailure.TeamNotEligible => TypedResults.Conflict(
                    CompetitionQuestionFailureMapper.ToResponse(result)),
            CompetitionQuestionFailure.InvalidRequest
                or CompetitionQuestionFailure.SpamRejected => TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Competition question was rejected.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = CompetitionQuestionFailureMapper.ToCode(result.Failure.Value)
                    }),
            _ => throw new InvalidOperationException(
                $"Unsupported question creation result: {result.Failure}.")
        };
    }
}
