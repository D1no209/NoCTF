using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class ListTheoryAuditRequest : NoCTF.API.Pagination.PaginationRequest { public Guid ActivityId { get; set; } }
public sealed class ListTheoryAuditValidator : Validator<ListTheoryAuditRequest>
{ public ListTheoryAuditValidator() { NoCTF.API.Pagination.PaginationRules.Add(this); RuleFor(request => request.ActivityId).NotEmpty(); } }

public sealed class ListTheoryAuditEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock) : Endpoint<ListTheoryAuditRequest, Results<Ok<TheoryAuditPage>, NotFound>>
{
    public override void Configure() { Get("/theory/activities/{activityId}/audit"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         MaxRequestBodySize(65536); Description(builder => builder.WithName("ListTheoryAudit")); }
    public override async Task<Results<Ok<TheoryAuditPage>, NotFound>> ExecuteAsync(ListTheoryAuditRequest request, CancellationToken ct)
        => await store.AuditAsync(user.UserId, request.ActivityId, request.Offset, request.Limit, clock.GetUtcNow(), ct) is { } page ? TypedResults.Ok(page) : TypedResults.NotFound();
}
