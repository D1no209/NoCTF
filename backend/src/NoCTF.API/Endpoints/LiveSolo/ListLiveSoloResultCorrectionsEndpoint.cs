using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Adjudication;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloResultCorrectionsRequest { public Guid CompetitionId {get;set;} public Guid MatchId {get;set;} }
public sealed class ListLiveSoloResultCorrectionsEndpoint(ILiveSoloResultCorrectionStore corrections,IUserContext user)
    :Endpoint<ListLiveSoloResultCorrectionsRequest,Results<Ok<IReadOnlyList<LiveSoloResultCorrectionResponse>>,NotFound>>
{
    public override void Configure(){Get("/competitions/{competitionId}/live-solo/matches/{matchId}/corrections");AuthSchemes("Bearer");
        Description(x=>x.WithName("ListLiveSoloResultCorrections"));}
    public override async Task<Results<Ok<IReadOnlyList<LiveSoloResultCorrectionResponse>>,NotFound>> ExecuteAsync(ListLiveSoloResultCorrectionsRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await corrections.ListCorrectionsAsync(req.CompetitionId,req.MatchId,user.UserId,ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok<IReadOnlyList<LiveSoloResultCorrectionResponse>>(result.Select(LiveSoloResultCorrectionResponse.From).ToArray());
    }
}
