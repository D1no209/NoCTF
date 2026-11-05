using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.API.Endpoints.Questionnaires.Sessions;

public sealed class SubmitQuestionnaireSessionRequest
{
    public Guid ActivityId { get; set; } public Guid SessionId { get; set; } public Guid ExpectedRevision { get; set; }
}
public sealed class SubmitQuestionnaireSessionValidator : Validator<SubmitQuestionnaireSessionRequest>
{
    public SubmitQuestionnaireSessionValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.SessionId).NotEmpty(); RuleFor(request => request.ExpectedRevision).NotEmpty();
    }
}
public sealed class SubmitQuestionnaireSessionEndpoint(IQuestionnaireSessionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<SubmitQuestionnaireSessionRequest, Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/sessions/{sessionId}/submit"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("SubmitQuestionnaireSession"));
    }
    public override async Task<Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>> ExecuteAsync(SubmitQuestionnaireSessionRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Session(await store.SubmitAsync(new(user.UserId, request.ActivityId, request.SessionId, request.ExpectedRevision, clock.GetUtcNow()), ct));
}
