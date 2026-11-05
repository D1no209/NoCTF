using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class TransitionQuestionnaireActivityRequest
{
    public Guid ActivityId { get; set; } public QuestionnaireActivityAction Action { get; set; }
    public DateTimeOffset? Deadline { get; set; } public string? Reason { get; set; }
}
public sealed class TransitionQuestionnaireActivityValidator : Validator<TransitionQuestionnaireActivityRequest>
{
    public TransitionQuestionnaireActivityValidator()
    { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.Action).IsInEnum(); RuleFor(request => request.Reason).MaximumLength(2000); }
}
public sealed class TransitionQuestionnaireActivityEndpoint(IQuestionnaireActivityStore store, IUserContext user, TimeProvider clock)
    : Endpoint<TransitionQuestionnaireActivityRequest, Results<Ok<QuestionnaireActivitySummary>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/operations"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("TransitionQuestionnaireActivity"));
    }
    public override async Task<Results<Ok<QuestionnaireActivitySummary>, NotFound, ProblemHttpResult>> ExecuteAsync(TransitionQuestionnaireActivityRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Result(await store.TransitionAsync(new(user.UserId, request.ActivityId, request.Action,
            clock.GetUtcNow(), request.Deadline, request.Reason), ct));
}
