using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.API.Endpoints.Questionnaires.Sessions;

public sealed class AnswerQuestionnaireSessionRequest
{
    public Guid ActivityId { get; set; } public Guid SessionId { get; set; } public Guid ItemId { get; set; }
    public Guid ExpectedRevision { get; set; } public Guid RequestId { get; set; }
    public required Guid[] OptionIds { get; set; }
}
public sealed class AnswerQuestionnaireSessionValidator : Validator<AnswerQuestionnaireSessionRequest>
{
    public AnswerQuestionnaireSessionValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.SessionId).NotEmpty();
        RuleFor(request => request.ItemId).NotEmpty(); RuleFor(request => request.ExpectedRevision).NotEmpty();
        RuleFor(request => request.RequestId).NotEmpty(); RuleFor(request => request.OptionIds).NotNull();
        When(request => request.OptionIds is not null, () => RuleFor(request => request.OptionIds.Length).LessThanOrEqualTo(8));
    }
}
public sealed class AnswerQuestionnaireSessionEndpoint(IQuestionnaireSessionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<AnswerQuestionnaireSessionRequest, Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/questionnaires/activities/{activityId}/sessions/{sessionId}/answers/{itemId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("AnswerQuestionnaireSession"));
    }
    public override async Task<Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult>> ExecuteAsync(AnswerQuestionnaireSessionRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Session(await store.AnswerAsync(new(user.UserId, request.ActivityId, request.SessionId, request.ItemId,
            request.ExpectedRevision, request.RequestId, request.OptionIds, clock.GetUtcNow()), ct));
}
