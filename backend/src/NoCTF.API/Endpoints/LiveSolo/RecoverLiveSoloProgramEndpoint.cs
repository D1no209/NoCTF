using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class RecoverLiveSoloProgramRequest
{
    public Guid CompetitionId{get;set;}public Guid MatchId{get;set;}public Guid ProgramId{get;set;}public Guid ExpectedStamp{get;set;}
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloProgramAction>))]public required LiveSoloProgramAction Action{get;set;}
    public string Reason{get;set;}="";
}
public sealed class RecoverLiveSoloProgramValidator:Validator<RecoverLiveSoloProgramRequest>
{
    public RecoverLiveSoloProgramValidator(){RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.ProgramId).NotEmpty();
        RuleFor(x=>x.ExpectedStamp).NotEmpty();RuleFor(x=>x.Action).IsInEnum();RuleFor(x=>x.Reason).NotEmpty().MaximumLength(4000);}
}
public sealed class RecoverLiveSoloProgramEndpoint(ManageLiveSoloProgram programs,IUserContext user)
    :Endpoint<RecoverLiveSoloProgramRequest,Results<Ok<LiveSoloProgramHealthResponse>,NotFound,ForbidHttpResult,Conflict<LiveSoloFailureResponse>,UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure(){Post("/competitions/{competitionId}/live-solo/matches/{matchId}/media/program/recovery");AuthSchemes("Bearer");
        Description(x=>x.WithName("RecoverLiveSoloProgram"));}
    public override async Task<Results<Ok<LiveSoloProgramHealthResponse>,NotFound,ForbidHttpResult,Conflict<LiveSoloFailureResponse>,UnprocessableEntity<LiveSoloFailureResponse>>>
        ExecuteAsync(RecoverLiveSoloProgramRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await programs.RecoverAsync(new(req.CompetitionId,req.MatchId,user.UserId,req.ProgramId,req.ExpectedStamp,req.Action,req.Reason),ct);
        return result.Failure switch {
            null when result.Program is not null=>TypedResults.Ok(LiveSoloProgramHealthResponse.From(result.Program)),
            LiveSoloFailure.Forbidden=>TypedResults.Forbid(),LiveSoloFailure.NotFound=>TypedResults.NotFound(),
            LiveSoloFailure.InvalidConfiguration=>TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _=>TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure??LiveSoloFailure.Conflict))
        };
    }
}
