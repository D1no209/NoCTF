using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.API.Endpoints.Questionnaires.Sessions;

public sealed class GetQuestionnaireSessionRequest { public Guid ActivityId { get; set; } public Guid SessionId { get; set; } }
public sealed class GetQuestionnaireSessionValidator : Validator<GetQuestionnaireSessionRequest>
{
    public GetQuestionnaireSessionValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.SessionId).NotEmpty(); }
}
public sealed class GetQuestionnaireSessionEndpoint(IQuestionnaireSessionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireSessionRequest, Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/sessions/{sessionId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Description(builder => builder.WithName("GetQuestionnaireSession"));
    }
    public override async Task<Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>> ExecuteAsync(GetQuestionnaireSessionRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Session(await store.ReadAsync(new(user.UserId, request.ActivityId, request.SessionId, null, clock.GetUtcNow()), ct));
}
