using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.API.Endpoints.Questionnaires.Sessions;

public sealed class IssueQuestionnaireSessionRequest { public Guid ActivityId { get; set; } public Guid RequestId { get; set; } public int ExpectedIssuedCount { get; set; } }
public sealed class IssueQuestionnaireSessionValidator : Validator<IssueQuestionnaireSessionRequest>
{
    public IssueQuestionnaireSessionValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.RequestId).NotEmpty();
        RuleFor(request => request.ExpectedIssuedCount).GreaterThanOrEqualTo(0);
    }
}
public sealed class IssueQuestionnaireSessionEndpoint(IQuestionnaireSessionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<IssueQuestionnaireSessionRequest, Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/sessions"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("IssueQuestionnaireSession"));
    }
    public override async Task<Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>> ExecuteAsync(IssueQuestionnaireSessionRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Session(await store.IssueAsync(new(user.UserId, request.ActivityId, null, null, clock.GetUtcNow(), request.RequestId, request.ExpectedIssuedCount), ct));
}
