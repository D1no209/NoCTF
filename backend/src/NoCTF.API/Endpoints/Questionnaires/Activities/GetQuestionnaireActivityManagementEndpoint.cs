using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class GetQuestionnaireActivityManagementRequest { public Guid ActivityId { get; set; } }
public sealed class GetQuestionnaireActivityManagementValidator : Validator<GetQuestionnaireActivityManagementRequest>
{ public GetQuestionnaireActivityManagementValidator() => RuleFor(request => request.ActivityId).NotEmpty(); }
public sealed class GetQuestionnaireActivityManagementEndpoint(IQuestionnaireActivityStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireActivityManagementRequest, Results<Ok<QuestionnaireActivityManagement>, NotFound>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/management"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Description(builder => builder.WithName("GetQuestionnaireActivityManagement"));
    }
    public override async Task<Results<Ok<QuestionnaireActivityManagement>, NotFound>> ExecuteAsync(GetQuestionnaireActivityManagementRequest request, CancellationToken ct)
        => await store.ReadManagementAsync(user.UserId, request.ActivityId, clock.GetUtcNow(), ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
}
