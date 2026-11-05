using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class ListQuestionnaireAuditRequest : NoCTF.API.Pagination.PaginationRequest { public Guid ActivityId { get; set; } }
public sealed class ListQuestionnaireAuditValidator : Validator<ListQuestionnaireAuditRequest>
{ public ListQuestionnaireAuditValidator() { NoCTF.API.Pagination.PaginationRules.Add(this); RuleFor(request => request.ActivityId).NotEmpty(); } }
public sealed class ListQuestionnaireAuditEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<ListQuestionnaireAuditRequest, Results<Ok<QuestionnaireAuditPage>, NotFound>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/audit"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); Description(builder => builder.WithName("ListQuestionnaireAudit"));
    }
    public override async Task<Results<Ok<QuestionnaireAuditPage>, NotFound>> ExecuteAsync(ListQuestionnaireAuditRequest request, CancellationToken ct)
        => await store.AuditAsync(user.UserId, request.ActivityId, request.Offset, request.Limit, clock.GetUtcNow(), ct) is { } page ? TypedResults.Ok(page) : TypedResults.NotFound();
}
