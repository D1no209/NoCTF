using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetCompetitionTheoryRequest { public Guid CompetitionId { get; set; } [QueryParam] public bool Staff { get; set; } }
public sealed class GetCompetitionTheoryEndpoint(ITheoryActivityStore store, IUserContext user)
    : Endpoint<GetCompetitionTheoryRequest, Ok<IReadOnlyList<TheoryActivitySummary>>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/theory"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("GetCompetitionTheory"));
    }
    public override async Task<Ok<IReadOnlyList<TheoryActivitySummary>>> ExecuteAsync(GetCompetitionTheoryRequest request, CancellationToken ct)
        => TypedResults.Ok((await store.ListAsync(user.UserId, request.Staff, request.CompetitionId, ct)).Items);
}
