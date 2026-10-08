using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloMatchesRequest { public Guid CompetitionId { get; set; } public bool Staff { get; set; } }
public sealed record LiveSoloMatchesResponse(IReadOnlyList<LiveSoloMatchResponse> Items);
public sealed class ListLiveSoloMatchesEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<ListLiveSoloMatchesRequest, Results<Ok<LiveSoloMatchesResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloMatches"));
        Summary(x => x.Summary = "Lists the caller's Matches or an authorized staff bracket; public delayed state is separate.");
    }
    public override async Task<Results<Ok<LiveSoloMatchesResponse>, NotFound>> ExecuteAsync(ListLiveSoloMatchesRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await matches.ListAsync(req.CompetitionId, user.UserId, req.Staff, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloMatchesResponse(result.Select(LiveSoloProtocol.Match).ToArray()));
    }
}
