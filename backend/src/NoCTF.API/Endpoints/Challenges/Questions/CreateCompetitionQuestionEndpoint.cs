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
    public Guid? GameplayFactId { get; set; }
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
    }
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionFailureCode>))]
public enum CompetitionQuestionFailureCode
{
    InvalidRequest,
    SpamRejected,
    CompetitionNotAcceptingQuestions,
    TeamNotEligible,
    InvalidChallengeReference,
    TeamActiveQuestionLimitReached,
    ParticipantMessageLimitReached,
    RevisionConflict,
    InvalidTransition,
    QuestionClosed
}

public sealed record CompetitionQuestionFailureResponse(
    CompetitionQuestionFailureCode Code,
    CompetitionQuestionResponse? Current,
    int? Limit);

internal static class CompetitionQuestionFailureMapper
{
    public static CompetitionQuestionFailureResponse ToResponse(
        CompetitionQuestionMutationResult result) =>
        new(ToCode(result.Failure!.Value), result.Question is null
            ? null
            : CompetitionQuestionResponseMapper.ToResponse(result.Question),
            result.Limit);

    public static CompetitionQuestionFailureCode ToCode(CompetitionQuestionFailure failure) =>
        failure switch
        {
            CompetitionQuestionFailure.InvalidRequest => CompetitionQuestionFailureCode.InvalidRequest,
            CompetitionQuestionFailure.SpamRejected => CompetitionQuestionFailureCode.SpamRejected,
            CompetitionQuestionFailure.CompetitionNotAcceptingQuestions =>
                CompetitionQuestionFailureCode.CompetitionNotAcceptingQuestions,
            CompetitionQuestionFailure.TeamNotEligible => CompetitionQuestionFailureCode.TeamNotEligible,
            CompetitionQuestionFailure.InvalidChallengeReference =>
                CompetitionQuestionFailureCode.InvalidChallengeReference,
            CompetitionQuestionFailure.TeamActiveQuestionLimitReached =>
                CompetitionQuestionFailureCode.TeamActiveQuestionLimitReached,
            CompetitionQuestionFailure.ParticipantMessageLimitReached =>
                CompetitionQuestionFailureCode.ParticipantMessageLimitReached,
            CompetitionQuestionFailure.RevisionConflict => CompetitionQuestionFailureCode.RevisionConflict,
            CompetitionQuestionFailure.InvalidTransition => CompetitionQuestionFailureCode.InvalidTransition,
            CompetitionQuestionFailure.QuestionClosed => CompetitionQuestionFailureCode.QuestionClosed,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
}

public sealed class CreateCompetitionQuestionEndpoint(
    CreateCompetitionQuestion create,
    IUserContext user)
    : Endpoint<CreateCompetitionQuestionRequest, Results<
        CreatedAtRoute<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        UnprocessableEntity<CompetitionQuestionFailureResponse>,
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
            summary.Description = "Challenge questions target one published CompetitionChallenge; platform questions target competition management. Attachments are not accepted and an optional GameplayFact is referenced without copying its protected content.";
        });
    }

    public override async Task<Results<
        CreatedAtRoute<CompetitionQuestionResponse>,
        Conflict<CompetitionQuestionFailureResponse>,
        UnprocessableEntity<CompetitionQuestionFailureResponse>,
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
            request.GameplayFactId,
            user.UserId,
            request.Title,
            request.Body,
            DateTimeOffset.UtcNow), ct);
        return result.Failure switch
        {
            null => TypedResults.CreatedAtRoute(
                CompetitionQuestionResponseMapper.ToResponse(result.Question!),
                "GetCompetitionQuestion",
                new
                {
                    competitionId,
                    questionId = result.Question!.Id
                }),
            CompetitionQuestionFailure.NotFound
                or CompetitionQuestionFailure.SubmissionNotFound => TypedResults.NotFound(),
            CompetitionQuestionFailure.Forbidden => TypedResults.Forbid(),
            CompetitionQuestionFailure.CompetitionNotAcceptingQuestions
                or CompetitionQuestionFailure.TeamNotEligible
                or CompetitionQuestionFailure.TeamActiveQuestionLimitReached => TypedResults.Conflict(
                    CompetitionQuestionFailureMapper.ToResponse(result)),
            CompetitionQuestionFailure.InvalidChallengeReference => TypedResults.UnprocessableEntity(
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
