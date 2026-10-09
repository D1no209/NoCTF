using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Adjudication;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class BeginLiveSoloResultCorrectionRequest
{
    public Guid CompetitionId {get;set;} public Guid MatchId {get;set;} public Guid WinnerTeamId {get;set;}
    public int LeftWins {get;set;} public int RightWins {get;set;} public Guid PreviewId {get;set;} public string Reason {get;set;}="";
}
public sealed class BeginLiveSoloResultCorrectionValidator:Validator<BeginLiveSoloResultCorrectionRequest>
{
    public BeginLiveSoloResultCorrectionValidator(){RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.WinnerTeamId).NotEmpty();
        RuleFor(x=>x.PreviewId).NotEmpty();RuleFor(x=>x.Reason).NotEmpty().MaximumLength(4000);RuleFor(x=>x.LeftWins).InclusiveBetween(0,1024);RuleFor(x=>x.RightWins).InclusiveBetween(0,1024);}
}
public sealed class BeginLiveSoloResultCorrectionEndpoint(ManageLiveSoloResultCorrections corrections,IUserContext user)
    :Endpoint<BeginLiveSoloResultCorrectionRequest,Results<Ok<LiveSoloResultCorrectionResponse>,NotFound,ForbidHttpResult,Conflict<LiveSoloFailureResponse>,UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure(){Post("/competitions/{competitionId}/live-solo/matches/{matchId}/corrections");AuthSchemes("Bearer");
        Description(x=>x.WithName("BeginLiveSoloResultCorrection"));}
    public override async Task<Results<Ok<LiveSoloResultCorrectionResponse>,NotFound,ForbidHttpResult,Conflict<LiveSoloFailureResponse>,UnprocessableEntity<LiveSoloFailureResponse>>>
        ExecuteAsync(BeginLiveSoloResultCorrectionRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        return LiveSoloResultCorrectionResponse.Outcome(await corrections.BeginAsync(new(new(req.CompetitionId,req.MatchId,user.UserId,req.WinnerTeamId,
            req.LeftWins,req.RightWins),req.PreviewId,req.Reason),ct));
    }
}
