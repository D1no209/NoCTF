using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloProgramDecisionsRequest{public Guid CompetitionId{get;set;}public Guid MatchId{get;set;}}
public sealed record LiveSoloProgramDecisionResponse(Guid Id,Guid ProgramId,
    [property:JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloProgramAction>))]LiveSoloProgramAction Action,
    string Reason,DateTimeOffset OccurredAt,
    [property:JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloCaptureState>))]LiveSoloCaptureState PreviousState,
    [property:JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloCaptureState>))]LiveSoloCaptureState State);
public sealed class ListLiveSoloProgramDecisionsEndpoint(ILiveSoloProgramRecovery programs,IUserContext user)
    :Endpoint<ListLiveSoloProgramDecisionsRequest,Results<Ok<IReadOnlyList<LiveSoloProgramDecisionResponse>>,NotFound>>
{
    public override void Configure(){Get("/competitions/{competitionId}/live-solo/matches/{matchId}/media/program/decisions");AuthSchemes("Bearer");
        Description(x=>x.WithName("ListLiveSoloProgramDecisions"));}
    public override async Task<Results<Ok<IReadOnlyList<LiveSoloProgramDecisionResponse>>,NotFound>> ExecuteAsync(ListLiveSoloProgramDecisionsRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await programs.ProgramDecisionsAsync(req.CompetitionId,req.MatchId,user.UserId,ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok<IReadOnlyList<LiveSoloProgramDecisionResponse>>(result.Select(x=>
            new LiveSoloProgramDecisionResponse(x.Id,x.ProgramId,x.Action,x.Reason,x.OccurredAt,x.PreviousState,x.State)).ToArray());
    }
}
