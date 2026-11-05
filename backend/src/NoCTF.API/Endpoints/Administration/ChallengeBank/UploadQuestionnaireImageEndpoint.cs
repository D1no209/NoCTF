using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Images;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UploadQuestionnaireImageRequest
{
    public Guid ChallengeId { get; set; }
    public Guid QuestionId { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadQuestionnaireImageValidator : Validator<UploadQuestionnaireImageRequest>
{
    public UploadQuestionnaireImageValidator()
    {
        RuleFor(value => value.ChallengeId).NotEmpty();
        RuleFor(value => value.QuestionId).NotEmpty();
        RuleFor(value => value.File).NotNull();
    }
}

public sealed record QuestionnaireImageResponse(Guid FileId, Guid RevisionId);

public sealed class UploadQuestionnaireImageEndpoint(ManageQuestionnaireImages images, IUserContext user, TimeProvider clock)
    : Endpoint<UploadQuestionnaireImageRequest, Results<Ok<QuestionnaireImageResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/questionnaire/questions/{questionId}/images");
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator"); AllowFileUploads();
        MaxRequestBodySize(ManageQuestionnaireImages.MaximumBytes + 65536);
        Description(builder => builder.WithName("AdminChallengeBankUploadQuestionnaireImage"));
        Summary(summary =>
        {
            summary.Summary = "Add an image to a questionnaire question.";
            summary.Description = "Validates management access and bounded image content, then saves a new immutable questionnaire revision.";
        });
    }

    public override async Task<Results<Ok<QuestionnaireImageResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(UploadQuestionnaireImageRequest request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var result = await images.UploadAsync(user.UserId, request.ChallengeId, request.QuestionId, content, clock.GetUtcNow(), ct);
        if (result.FileId is { } fileId && result.RevisionId is { } revisionId) return TypedResults.Ok(new QuestionnaireImageResponse(fileId, revisionId));
        if (result.Failure == QuestionnaireImageFailure.NotFoundOrForbidden) return TypedResults.NotFound();
        return ApiProblems.Problem(statusCode: result.Failure == QuestionnaireImageFailure.Conflict ? 409
            : result.Failure == QuestionnaireImageFailure.TooLarge ? 413 : 400, detail: ApiMessages.For(result.Failure));
    }
}
