using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetTheoryReviewRequest { public Guid ActivityId { get; set; } public Guid PaperId { get; set; } }

public sealed class GetTheoryReviewEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock) : Endpoint<GetTheoryReviewRequest, Results<Ok<TheoryReviewPaper>, NotFound>>
{
    public override void Configure() { Get("/theory/activities/{activityId}/reviews/{paperId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         MaxRequestBodySize(65536); Description(builder => builder.WithName("GetTheoryReview")); }
    public override async Task<Results<Ok<TheoryReviewPaper>, NotFound>> ExecuteAsync(GetTheoryReviewRequest request, CancellationToken ct)
        => await store.ReadAsync(user.UserId, request.ActivityId, request.PaperId, clock.GetUtcNow(), ct) is { } paper ? TypedResults.Ok(paper) : TypedResults.NotFound();
}
