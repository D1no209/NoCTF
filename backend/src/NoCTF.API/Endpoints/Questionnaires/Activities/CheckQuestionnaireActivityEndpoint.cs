using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class CheckQuestionnaireActivityRequest { public Guid ActivityId { get; set; } }
public sealed class CheckQuestionnaireActivityValidator : Validator<CheckQuestionnaireActivityRequest>
{ public CheckQuestionnaireActivityValidator() => RuleFor(request => request.ActivityId).NotEmpty(); }
public sealed class CheckQuestionnaireActivityEndpoint(IQuestionnaireActivityStore store, IUserContext user, TimeProvider clock)
    : Endpoint<CheckQuestionnaireActivityRequest, Results<Ok<QuestionnaireActivityCheck>, NotFound>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/check"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); Description(builder => builder.WithName("CheckQuestionnaireActivity"));
    }
    public override async Task<Results<Ok<QuestionnaireActivityCheck>, NotFound>> ExecuteAsync(CheckQuestionnaireActivityRequest request, CancellationToken ct)
        => await store.CheckAsync(user.UserId, request.ActivityId, clock.GetUtcNow(), ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
}
