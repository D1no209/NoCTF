using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class ListTheoryReviewsRequest : NoCTF.API.Pagination.PaginationRequest { public Guid ActivityId { get; set; } }
public sealed class ListTheoryReviewsValidator : Validator<ListTheoryReviewsRequest>
{ public ListTheoryReviewsValidator() { NoCTF.API.Pagination.PaginationRules.Add(this); RuleFor(request => request.ActivityId).NotEmpty(); } }

public sealed class ListTheoryReviewsEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock) : Endpoint<ListTheoryReviewsRequest, Results<Ok<TheoryReviewPage>, NotFound>>
{
    public override void Configure() { Get("/theory/activities/{activityId}/reviews"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         MaxRequestBodySize(65536); Description(builder => builder.WithName("ListTheoryReviews")); }
    public override async Task<Results<Ok<TheoryReviewPage>, NotFound>> ExecuteAsync(ListTheoryReviewsRequest request, CancellationToken ct)
        => await store.ListAsync(user.UserId, request.ActivityId, request.Offset, request.Limit, clock.GetUtcNow(), ct) is { } page ? TypedResults.Ok(page) : TypedResults.NotFound();
}
