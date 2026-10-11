using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetTheorySessionRequest { public Guid ActivityId { get; set; } }
public sealed class GetTheorySessionEndpoint(ITheoryPaperStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetTheorySessionRequest, Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/theory/activities/{activityId}/session"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("GetTheorySession"));
    }
    public override async Task<Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>> ExecuteAsync(GetTheorySessionRequest request, CancellationToken ct)
        => TheoryActivityHttp.Paper(await store.ReadAsync(new(user.UserId, request.ActivityId, null, null, clock.GetUtcNow()), ct));
}
