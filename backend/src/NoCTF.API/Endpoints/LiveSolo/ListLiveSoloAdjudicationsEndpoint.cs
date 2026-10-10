using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Adjudication;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloAdjudicationsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
}
public sealed class ListLiveSoloAdjudicationsEndpoint(ILiveSoloAdjudicationStore store, IUserContext user)
    : Endpoint<ListLiveSoloAdjudicationsRequest, Results<Ok<LiveSoloAdjudicationResponse[]>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/adjudications"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloAdjudications"));
        Summary(x => x.Summary = "Reads immutable Match judge decisions for authorized staff.");
    }
    public override async Task<Results<Ok<LiveSoloAdjudicationResponse[]>, NotFound>> ExecuteAsync(ListLiveSoloAdjudicationsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.ReadAsync(req.CompetitionId, req.MatchId, user.UserId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result.Select(LiveSoloAdjudicationResponse.From).ToArray());
    }
}
