using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloProgramHealthRequest {public Guid CompetitionId{get;set;}public Guid MatchId{get;set;}}
public sealed record LiveSoloProgramHealthResponse(Guid Id,Guid ConcurrencyStamp,
    [property:JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloCaptureState>))]LiveSoloCaptureState State,
    bool Stalled,DateTimeOffset? LastFragmentImportedAt,bool RotationRequested)
{
    internal static LiveSoloProgramHealthResponse From(LiveSoloProgramHealth x)=>new(x.Id,x.ConcurrencyStamp,x.State,x.Stalled,x.LastFragmentImportedAt,x.RotationRequested);
}
public sealed class GetLiveSoloProgramHealthEndpoint(ILiveSoloProgramRecovery recovery,IUserContext user)
    :Endpoint<GetLiveSoloProgramHealthRequest,Results<Ok<LiveSoloProgramHealthResponse>,NotFound>>
{
    public override void Configure(){Get("/competitions/{competitionId}/live-solo/matches/{matchId}/media/program");AuthSchemes("Bearer");
        Description(x=>x.WithName("GetLiveSoloProgramHealth"));}
    public override async Task<Results<Ok<LiveSoloProgramHealthResponse>,NotFound>> ExecuteAsync(GetLiveSoloProgramHealthRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await recovery.HealthAsync(req.CompetitionId,req.MatchId,user.UserId,ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok(LiveSoloProgramHealthResponse.From(result));
    }
}
