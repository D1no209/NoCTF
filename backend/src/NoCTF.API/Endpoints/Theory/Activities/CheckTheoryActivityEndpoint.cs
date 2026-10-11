using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class CheckTheoryActivityRequest { public Guid ActivityId { get; set; } }
public sealed class CheckTheoryActivityEndpoint(ITheoryActivityStore store, IUserContext user, TimeProvider clock)
    : Endpoint<CheckTheoryActivityRequest, Results<Ok<TheoryActivityCheck>, NotFound>>
{
    public override void Configure() { Post("/theory/activities/{activityId}/check"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse())); Description(builder => builder.WithName("CheckTheoryActivity")); }
    public override async Task<Results<Ok<TheoryActivityCheck>, NotFound>> ExecuteAsync(CheckTheoryActivityRequest request, CancellationToken ct)
        => await store.CheckAsync(user.UserId, request.ActivityId, clock.GetUtcNow(), ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
}
