using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Endpoints.Challenges.Questions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed class CreateTeamWriteUpConsultationRequest
{
    public Guid? CompetitionChallengeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class CreateTeamWriteUpConsultationValidator
    : Validator<CreateTeamWriteUpConsultationRequest>
{
    public CreateTeamWriteUpConsultationValidator()
    {
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

public sealed class CreateTeamWriteUpConsultationEndpoint(
    CreateTeamWriteUpConsultation create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateTeamWriteUpConsultationRequest, Results<
        CreatedAtRoute<CompetitionQuestionResponse>,
        NotFound,
        ForbidHttpResult,
        UnprocessableEntity<CompetitionQuestionFailureResponse>,
        ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/teams/{teamId}/writeup/consultations");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("CreateTeamWriteUpConsultation")
            .WithMetadata(new EnableRateLimitingAttribute("question")));
        Summary(summary =>
        {
            summary.Summary = "Starts a private team consultation from WriteUp review.";
            summary.Description = "Platform administrators and competition owners, managers or judges may open the thread. The team receives a durable notification and can continue in the existing consultation workspace.";
        });
    }

    public override async Task<Results<CreatedAtRoute<CompetitionQuestionResponse>,
        NotFound, ForbidHttpResult,
        UnprocessableEntity<CompetitionQuestionFailureResponse>,
        ProblemHttpResult>> ExecuteAsync(
        CreateTeamWriteUpConsultationRequest request,
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(
                user.UserId,
                competitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }
        var result = await create.ExecuteAsync(new(
            competitionId,
            Route<Guid>("teamId"),
            request.CompetitionChallengeId,
            user.UserId,
            request.Title,
            request.Body,
            timeProvider.GetUtcNow()), cancellationToken);
        return result.Failure switch
        {
            null => TypedResults.CreatedAtRoute(
                CompetitionQuestionResponseMapper.ToResponse(result.Question!),
                "GetCompetitionQuestion",
                new
                {
                    competitionId,
                    threadRootId = result.Question!.ThreadRootId
                }),
            CompetitionQuestionFailure.NotFound
                or CompetitionQuestionFailure.SubmissionNotFound => TypedResults.NotFound(),
            CompetitionQuestionFailure.Forbidden => TypedResults.Forbid(),
            CompetitionQuestionFailure.InvalidChallengeReference =>
                TypedResults.UnprocessableEntity(
                    CompetitionQuestionFailureMapper.ToResponse(result)),
            CompetitionQuestionFailure.InvalidRequest
                or CompetitionQuestionFailure.SpamRejected => ApiProblems.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: ApiMessages.Get(ApiMessageId.CreateTeamWriteUpConsultationTitleWriteupConsultationWasRejected),
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = CompetitionQuestionFailureMapper.ToCode(result.Failure.Value)
                    }),
            _ => throw new InvalidOperationException(
                $"Unsupported WriteUp consultation result: {result.Failure}.")
        };
    }
}
