using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class VoidQuestionnaireSessionRequest { public Guid ActivityId { get; set; } public Guid SessionId { get; set; } public required string Reason { get; set; } }
public sealed class VoidQuestionnaireSessionValidator : Validator<VoidQuestionnaireSessionRequest>
{
    public VoidQuestionnaireSessionValidator()
    { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.SessionId).NotEmpty(); RuleFor(request => request.Reason).NotEmpty().MaximumLength(2000); }
}
public sealed class VoidQuestionnaireSessionEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<VoidQuestionnaireSessionRequest, Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities/{activityId}/reviews/{sessionId}/void"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); MaxRequestBodySize(65536);
        Description(builder => builder.WithName("VoidQuestionnaireSession"));
    }
    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(VoidQuestionnaireSessionRequest request, CancellationToken ct)
    {
        var failure = await store.VoidSessionAsync(user.UserId, request.ActivityId, request.SessionId, request.Reason, clock.GetUtcNow(), ct);
        return failure is null ? TypedResults.NoContent() : failure == QuestionnaireReviewFailure.NotFoundOrForbidden ? TypedResults.NotFound() : QuestionnaireActivityHttp.Problem(failure);
    }
}
