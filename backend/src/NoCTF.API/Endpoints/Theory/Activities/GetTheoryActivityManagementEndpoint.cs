using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetTheoryActivityManagementRequest { public Guid ActivityId { get; set; } }

public sealed class GetTheoryActivityManagementEndpoint(ITheoryActivityStore store, IUserContext user, TimeProvider clock) : Endpoint<GetTheoryActivityManagementRequest, Results<Ok<TheoryActivityManagement>, NotFound>>
{
    public override void Configure()
    {
        Get("/theory/activities/{activityId}/management");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("GetTheoryActivityManagement"));
    }
    public override async Task<Results<Ok<TheoryActivityManagement>, NotFound>> ExecuteAsync(GetTheoryActivityManagementRequest request, CancellationToken ct)
        => await store.ReadManagementAsync(user.UserId, request.ActivityId, clock.GetUtcNow(), ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
}
