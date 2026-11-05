using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.API.Endpoints.Questionnaires.Sessions;

public sealed class GetQuestionnaireActivitySessionRequest { public Guid ActivityId { get; set; } }
public sealed class GetQuestionnaireActivitySessionValidator : Validator<GetQuestionnaireActivitySessionRequest>
{ public GetQuestionnaireActivitySessionValidator() => RuleFor(request => request.ActivityId).NotEmpty(); }
public sealed class GetQuestionnaireActivitySessionEndpoint(IQuestionnaireSessionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireActivitySessionRequest, Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/session"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Description(builder => builder.WithName("GetQuestionnaireActivitySession"));
    }
    public override async Task<Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>> ExecuteAsync(GetQuestionnaireActivitySessionRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Session(await store.ReadAsync(new(user.UserId, request.ActivityId, null, null, clock.GetUtcNow()), ct));
}
