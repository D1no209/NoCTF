using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class PreviewQuestionnaireCorrectionRequest { public Guid ActivityId { get; set; } public required QuestionnaireCorrectionInput Input { get; set; } }
public sealed class PreviewQuestionnaireCorrectionValidator : Validator<PreviewQuestionnaireCorrectionRequest>
{
    public PreviewQuestionnaireCorrectionValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.Input).NotNull();
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new QuestionnaireCorrectionValidator()));
    }
}
public sealed class QuestionnaireCorrectionValidator : AbstractValidator<QuestionnaireCorrectionInput>
{
    public QuestionnaireCorrectionValidator()
    { RuleFor(input => input.RevisionId).NotEmpty(); RuleFor(input => input.Mode).IsInEnum(); RuleFor(input => input.CorrectOptions).NotNull(); }
}
public sealed class PreviewQuestionnaireCorrectionEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<PreviewQuestionnaireCorrectionRequest, Results<Ok<QuestionnaireCorrectionPreview>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/corrections/preview"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(65536);
        Description(builder => builder.WithName("PreviewQuestionnaireCorrection"));
    }
    public override async Task<Results<Ok<QuestionnaireCorrectionPreview>, NotFound, ProblemHttpResult>> ExecuteAsync(PreviewQuestionnaireCorrectionRequest request, CancellationToken ct)
    {
        var result = await store.PreviewAsync(user.UserId, request.ActivityId, request.Input, clock.GetUtcNow(), ct);
        return result.Preview is { } preview ? TypedResults.Ok(preview) : result.Failure == QuestionnaireReviewFailure.NotFoundOrForbidden
            ? TypedResults.NotFound() : QuestionnaireActivityHttp.Problem(result.Failure!);
    }
}
