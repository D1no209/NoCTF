using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class ApplyQuestionnaireCorrectionRequest
{
    public Guid ActivityId { get; set; } public required QuestionnaireCorrectionInput Input { get; set; }
    public Guid Token { get; set; } public DateTimeOffset ExpiresAt { get; set; } public required string Reason { get; set; }
}
public sealed class ApplyQuestionnaireCorrectionValidator : Validator<ApplyQuestionnaireCorrectionRequest>
{
    public ApplyQuestionnaireCorrectionValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.Input).NotNull();
        RuleFor(request => request.Token).NotEmpty(); RuleFor(request => request.Reason).NotEmpty().MaximumLength(2000);
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new QuestionnaireCorrectionValidator()));
    }
}
public sealed record QuestionnaireCorrectionAccepted(Guid TargetRevision, string StatusUrl);
public sealed class ApplyQuestionnaireCorrectionEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<ApplyQuestionnaireCorrectionRequest, Results<Accepted<QuestionnaireCorrectionAccepted>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/corrections"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(65536);
        Description(builder => builder.WithName("ApplyQuestionnaireCorrection"));
    }
    public override async Task<Results<Accepted<QuestionnaireCorrectionAccepted>, NotFound, ProblemHttpResult>> ExecuteAsync(ApplyQuestionnaireCorrectionRequest request, CancellationToken ct)
    {
        var result = await store.CorrectAsync(user.UserId, request.ActivityId, request.Input, request.Token, request.ExpiresAt,
            request.Reason, clock.GetUtcNow(), ct);
        var url = $"/api/v1/questionnaires/activities/{request.ActivityId}/management";
        return result.TargetRevision is { } target ? TypedResults.Accepted(url, new QuestionnaireCorrectionAccepted(target, url))
            : result.Failure == QuestionnaireReviewFailure.NotFoundOrForbidden ? TypedResults.NotFound() : QuestionnaireActivityHttp.Problem(result.Failure!);
    }
}
